using System;
using System.IO;
using System.Text;
using Mapbox.Json;
using Mapbox.Json.Linq;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    [Serializable]
    public sealed class LiveMapHeader
    {
        public string type, protocol, authToken, sourceId, mapId, frameId, frameConvention, dataKind;
        public long sequence, capturedAtMs;
        public int pointCount, chunkCount;
        public float voxelSizeM;
    }

    public sealed class LiveMapSnapshot
    {
        public readonly LiveMapHeader Header;
        public readonly Vector3[] Positions;
        public readonly Color32[] Colors;
        public readonly Bounds Bounds;
        public LiveMapSnapshot(LiveMapHeader header, Vector3[] positions, Color32[] colors)
        {
            Header = header; Positions = positions; Colors = colors;
            var bounds = new Bounds(positions[0], Vector3.zero);
            foreach (var point in positions) bounds.Encapsulate(point);
            Bounds = bounds;
        }
    }

    // One complete registered map per TCP connection. An incomplete snapshot never
    // replaces the previous map. The producer owns SLAM and loop-closure corrections.
    public static class LiveMapProtocol
    {
        public const string Version = "simurgh-live-map/1";
        public const int MaxPoints = 50000, ChunkPoints = 256, MaxLineBytes = 65536;
        public const long MaxAgeMs = 5000;
        public static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        public static bool Fresh(long stamp, long now) => stamp > 0 && stamp >= now - MaxAgeMs && stamp <= now + 1000;

        public static string ReadLine(Stream stream, Func<long> clock = null, long deadline = 0)
        {
            clock = clock ?? (() => NowMs);
            if (deadline == 0) deadline = clock() + MaxAgeMs;
            using (var line = new MemoryStream())
            {
                for (int i = 0; i <= MaxLineBytes; i++)
                {
                    if (clock() > deadline) throw new InvalidDataException("Harita aktarımı zaman aşımı.");
                    int value = stream.ReadByte();
                    if (value < 0) throw new EndOfStreamException("Harita aktarımı tamamlanmadı.");
                    if (value == '\n') return new UTF8Encoding(false, true).GetString(line.ToArray());
                    if (i == MaxLineBytes) throw new InvalidDataException("Harita paketi çok büyük.");
                    line.WriteByte((byte)value);
                }
            }
            throw new InvalidDataException("Geçersiz paket.");
        }

        private static JObject Parse(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 12 })
            {
                var value = JObject.Load(reader);
                if (reader.Read()) throw new InvalidDataException("Paket sonrasında ek veri var.");
                return value;
            }
        }

        private static bool Id(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 80) return false;
            foreach (char c in text)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-' || c == '/')) return false;
            return true;
        }

        public static LiveMapSnapshot ReadSnapshot(Stream stream, string token, bool allowTest, Func<long> clock = null)
        {
            clock = clock ?? (() => NowMs);
            long started = clock();
            var h = Parse(ReadLine(stream, clock, started + MaxAgeMs)).ToObject<LiveMapHeader>();
            if (h == null || h.type != "begin" || h.protocol != Version || string.IsNullOrEmpty(token) || h.authToken != token)
                throw new InvalidDataException("Harita protokolü veya erişim anahtarı geçersiz.");
            if (!Id(h.sourceId) || !Id(h.mapId) || !Id(h.frameId) || h.sequence <= 0 || h.frameConvention != "ros_z_up_m")
                throw new InvalidDataException("Harita kimliği veya koordinat sistemi geçersiz.");
            if (h.dataKind != "measured" && !(allowTest && h.dataKind == "test"))
                throw new InvalidDataException("Ölçülmüş harita bekleniyor; test verisi kapalı.");
            if (!Fresh(h.capturedAtMs, started)) throw new InvalidDataException("Harita ölçüm zamanı eski veya saatler eşleşmiyor.");
            if (h.pointCount < 1 || h.pointCount > MaxPoints || h.chunkCount != (h.pointCount + ChunkPoints - 1) / ChunkPoints
                || !DigitalTwinMessageValidation.Finite(h.voxelSizeM) || h.voxelSizeM < 0.02f || h.voxelSizeM > 5)
                throw new InvalidDataException("Harita boyutu veya örnekleme aralığı geçersiz.");
            // Do not retain credentials in snapshots, exports or diagnostic strings.
            h.authToken = null;
            var positions = new Vector3[h.pointCount];
            var colors = new Color32[h.pointCount];
            int offset = 0;
            var values = new double[6];
            for (int chunk = 0; chunk < h.chunkCount; chunk++)
            {
                var block = Parse(ReadLine(stream, clock, started + MaxAgeMs));
                var points = block["points"] as JArray;
                int expected = Math.Min(ChunkPoints, h.pointCount - offset);
                if ((string)block["type"] != "points" || (int?)block["index"] != chunk || points == null || points.Count != expected)
                    throw new InvalidDataException("Harita parçası eksik veya sırası yanlış.");
                foreach (var row in points)
                {
                    var p = row as JArray;
                    if (p == null || p.Count != 6) throw new InvalidDataException("XYZ/RGB noktası geçersiz.");
                    for (int k = 0; k < 6; k++)
                    {
                        if (p[k].Type != JTokenType.Integer && p[k].Type != JTokenType.Float) throw new InvalidDataException("Nokta sayısal olmalı.");
                        values[k] = (double)p[k];
                        if (!DigitalTwinMessageValidation.Finite(values[k])) throw new InvalidDataException("Sonlu olmayan nokta.");
                        if (k < 3 ? Math.Abs(values[k]) > 10000 : values[k] < 0 || values[k] > 255 || values[k] != Math.Floor(values[k]))
                            throw new InvalidDataException("Nokta veya renk sınır dışında.");
                    }
                    // ROS map (x,y,z up) -> Unity (x,z up,y). No GPS alignment is inferred.
                    positions[offset] = new Vector3((float)values[0], (float)values[2], (float)values[1]);
                    colors[offset++] = new Color32((byte)values[3], (byte)values[4], (byte)values[5], 255);
                }
                if (clock() - started > MaxAgeMs) throw new InvalidDataException("Harita aktarımı zaman aşımı.");
            }
            var end = Parse(ReadLine(stream, clock, started + MaxAgeMs));
            if ((string)end["type"] != "end" || (long?)end["sequence"] != h.sequence || !Fresh(h.capturedAtMs, clock()))
                throw new InvalidDataException("Harita tamamlanmadı veya aktarım gecikti.");
            return new LiveMapSnapshot(h, positions, colors);
        }
    }
}
