using System.Collections;
using System.Collections.Generic;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// PDF 2. bolum gorev senaryosunun tam DEMO oynaticisi (hakem sunum modu).
    /// Tek tusla faz akisini canlandirir: scan -> joint_operation -> dynamic_replan -> complete.
    ///
    /// Her frame'de uretir:
    ///   - GPS pose + VI-SLAM pose (golge modu; SLAM'de kucuk drift -> yorunge sapmasi)
    ///   - mesh link metrikleri (faza gore degisir; replan'de link duser, relay aktiflesir)
    ///   - faza ozgu engel / hedef / voksel deltalari (oncu tarama -> dinamik guncelleme)
    ///   - rover pozu (faz 2'den itibaren)
    ///
    /// Boylece GPS-SLAM yorunge karsilastirmasi, mesh topoloji paneli ve dijital ikiz
    /// gorsellerinin tamami UDP/ROS olmadan, dogrudan JsonPoseBridge uzerinden calisir.
    /// </summary>
    public class DigitalTwinMissionDemoPlayer : MonoBehaviour
    {
        [Header("Referanslar (otomatik bulunur)")]
        [SerializeField] private MonoBehaviour ingressBehaviour;
        [SerializeField] private AbstractMap abstractMap;

        [Header("Oynatma")]
        [SerializeField] private bool autoPlayOnStart = false;
        [SerializeField] private float startDelaySeconds = 1.0f;
        [SerializeField] private float frameInterval = 0.2f;
        [SerializeField] private int totalFrames = 170;
        [SerializeField] private bool loop = false;

        [Header("Saha")]
        [SerializeField] private Vector2 fallbackCenterLatLon = new Vector2(39.8719f, 32.7302f);
        [SerializeField] private bool recenterMapOnStart = true;
        [SerializeField] private float areaLatDeg = 0.00060f;
        [SerializeField] private float areaLonDeg = 0.00085f;
        [SerializeField] private float baseAltitudeM = 40f;

        [Header("Kimlik / HUD")]
        [SerializeField] private string authToken = "simurgh-2026";
        [SerializeField] private bool showHud = true;

        private Vector2 _hudPos = new Vector2(-99999f, 0f);
        private bool _drag;
        private GUIStyle _phaseStyle, _pctStyle;

        private IDigitalTwinIngress _ingress;
        private Coroutine _routine;
        private long _seq;
        private Vector2 _center, _targetA, _targetB, _targetC;
        private string _phase = "-";
        private float _progress;
        private bool _playing;

        private void Awake()
        {
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (ingressBehaviour == null) ingressBehaviour = FindObjectOfType<DigitalTwinJsonPoseBridge>();
            _ingress = ingressBehaviour as IDigitalTwinIngress;
            _seq = System.Math.Max(2000L, (long)(Time.realtimeSinceStartup * 1000f));
        }

        private void Start()
        {
            if (autoPlayOnStart) Play(startDelaySeconds);
        }

        [ContextMenu("Play Demo")]
        public void Play() { Play(0f); }

        public void Play(float delay)
        {
            Stop();
            _routine = StartCoroutine(Run(delay));
        }

        [ContextMenu("Stop Demo")]
        public void Stop()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            _playing = false;
        }

        private void OnDisable()
        {
            // Deaktivasyonda Unity coroutine'i sessizce oldurur; durumu deterministik sifirla
            // (aksi halde HUD "Durdur" gosterir ama hicbir sey oynamaz).
            Stop();
        }

        private Vector2 ResolveCenter()
        {
            if (abstractMap != null)
            {
                var c = abstractMap.CenterLatitudeLongitude;
                if (!double.IsNaN(c.x) && !double.IsNaN(c.y) && (c.x != 0.0 || c.y != 0.0))
                    return new Vector2((float)c.x, (float)c.y);
            }
            return fallbackCenterLatLon;
        }

        private IEnumerator Run(float delay)
        {
            if (_ingress == null) _ingress = ingressBehaviour as IDigitalTwinIngress;
            if (_ingress == null) { Debug.LogWarning("[MissionDemo] JsonPoseBridge bulunamadi."); yield break; }
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

            _playing = true;
            do
            {
                _center = ResolveCenter();
                _targetA = new Vector2(_center.x + areaLatDeg * 0.55f, _center.y + areaLonDeg * 0.35f);
                _targetB = new Vector2(_center.x - areaLatDeg * 0.30f, _center.y + areaLonDeg * 0.60f);
                _targetC = new Vector2(_center.x + areaLatDeg * 0.10f, _center.y - areaLonDeg * 0.55f);

                if (recenterMapOnStart && abstractMap != null)
                    try { abstractMap.UpdateMap(new Vector2d(_center.x, _center.y), abstractMap.Zoom); } catch { }

                int n = Mathf.Max(40, totalFrames);
                for (int i = 0; i < n; i++)
                {
                    if (!_playing) break;
                    float t = (float)i / (n - 1);
                    _progress = t;
                    _phase = PhaseFor(t);

                    SendUav(i, t, _phase);
                    if (t >= 0.40f) SendRover(i, t, _phase);

                    yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, frameInterval));
                }
            } while (loop && _playing);

            _playing = false;
            _routine = null;
        }

        private static string PhaseFor(float t)
        {
            if (t < 0.40f) return TwinOperationPhases.Scan;
            if (t < 0.68f) return TwinOperationPhases.JointOperation;
            if (t < 0.86f) return TwinOperationPhases.DynamicReplan;
            return TwinOperationPhases.Complete;
        }

        private void Send(DigitalTwinMessageV1 msg, bool stripGpsPose = false)
        {
            msg.schemaVersion = DigitalTwinJsonSchema.Version1;
            msg.sequenceId = ++_seq;
            msg.timestampMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            msg.authToken = authToken;
            string json = JsonUtility.ToJson(msg);
            // JsonUtility null class alanini bile default degerlerle yazar; GPS kesintisini
            // gercekci simule etmek icin "pose" blogu JSON'dan tamamen cikarilir — bridge'in
            // null-normalizasyonu boylece SLAM fallback'ini ve kaynak rozetini tetikler.
            if (stripGpsPose)
                json = RemoveJsonBlock(json, "pose");
            _ingress.TryApplyDigitalTwinJson(json);
        }

        // Rover'in cektigi karekod fotografini simule eden kucuk uretilmis JPEG:
        // ag uzerinden base64 iletim yolunun (imagery.imageBase64 -> galeri) ucta uca kanitidir.
        private readonly System.Collections.Generic.Dictionary<string, string> _demoPhotoCache =
            new System.Collections.Generic.Dictionary<string, string>();

        private TwinImageryBlock BuildDemoPhoto(string targetId, Color tint)
        {
            string b64;
            if (!_demoPhotoCache.TryGetValue(targetId, out b64))
            {
                var tex = new Texture2D(96, 96, TextureFormat.RGB24, false);
                for (int py = 0; py < 96; py++)
                    for (int px = 0; px < 96; px++)
                    {
                        bool stripe = ((px + py) / 12) % 2 == 0;
                        bool qr = (px / 8 + py / 8) % 3 == 0 && px > 16 && px < 80 && py > 16 && py < 80;
                        tex.SetPixel(px, py, qr ? Color.black : (stripe ? tint : Color.white));
                    }
                tex.Apply();
                b64 = System.Convert.ToBase64String(tex.EncodeToJPG(70));
                Destroy(tex);
                _demoPhotoCache[targetId] = b64;
            }
            return new TwinImageryBlock
            {
                pipeline = "rover_cam",
                mode = "qr_photo",
                label = targetId,
                targetId = targetId,
                imageBase64 = b64,
                overlayAlpha = 0.9f
            };
        }

        private static string RemoveJsonBlock(string json, string key)
        {
            string k = "\"" + key + "\":";
            int i = json.IndexOf(k, System.StringComparison.Ordinal);
            if (i < 0) return json;
            int braceStart = json.IndexOf('{', i + k.Length);
            if (braceStart < 0) return json;
            int depth = 0, j = braceStart;
            for (; j < json.Length; j++)
            {
                if (json[j] == '{') depth++;
                else if (json[j] == '}') { depth--; if (depth == 0) break; }
            }
            if (j >= json.Length) return json;
            int end = j + 1;
            if (end < json.Length && json[end] == ',') end++;
            else if (i > 0 && json[i - 1] == ',') i--;
            return json.Remove(i, end - i);
        }

        private void SendUav(int i, float t, string phase)
        {
            float ang = t * Mathf.PI * 4f;
            float spread = phase == TwinOperationPhases.Scan ? Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(t / 0.40f)) : 1f;
            float gpsLat = _center.x + Mathf.Sin(ang) * areaLatDeg * spread;
            float gpsLon = _center.y + Mathf.Cos(ang) * areaLonDeg * spread;
            float yaw = Mathf.Repeat(ang * Mathf.Rad2Deg + 90f, 360f);

            // VI-SLAM golge izi: GPS'e gore kucuk salinann drift; replan fazinda artar (GPS karistirma).
            float driftScale = phase == TwinOperationPhases.DynamicReplan ? 2.4f : 1f;
            float dLat = ((Mathf.Sin(t * 9f) * 0.5f + 0.5f) * 0.000016f + t * 0.000006f) * driftScale;
            float dLon = (Mathf.Cos(t * 7f) * 0.000012f) * driftScale;

            // GPS kesintisi senaryosu (PDF 2.1 "olasi sinyal kaybinda SLAM devreye alinir"):
            // Faz 2'nin ortasinda GPS bloklari gonderilmez -> YKI kaynak rozeti SLAM'e doner.
            bool gpsLoss = t >= 0.50f && t < 0.62f;

            var msg = new DigitalTwinMessageV1
            {
                sourceId = "demo-uav",
                vehicleType = TwinVehicleTypes.Uav,
                missionPhase = phase,
                pose = new TwinPoseBlock { latitude = gpsLat, longitude = gpsLon, altitudeM = baseAltitudeM + Mathf.Sin(ang * 2f) * 3f, yawDeg = yaw },
                slamPose = new TwinSlamPoseBlock { latitude = gpsLat + dLat, longitude = gpsLon + dLon, altitudeM = baseAltitudeM, yawDeg = yaw, confidence = gpsLoss ? 0.82f : 0.93f },
                telemetry = BuildTelemetry(t, phase),
                meshLink = BuildMesh(phase),
                mission = new TwinMissionDelta
                {
                    phase = phase,
                    status = phase == TwinOperationPhases.Complete ? "complete" : "running",
                    activeVehicle = TwinVehicleTypes.Uav,
                    warning = gpsLoss ? "GPS sinyali kesildi — VI-SLAM kestirimi devrede"
                        : (phase == TwinOperationPhases.DynamicReplan ? "Hareketli engel: rover rotasi guncelleniyor" : ""),
                    note = NoteFor(phase)
                }
            };

            if (i == 0)
            {
                msg.route = BuildScanRoute();
                msg.routeMode = "replace";
                msg.replaceRoute = true;
            }

            AttachScriptedEntities(msg, i, t, phase);
            Send(msg, gpsLoss);
        }

        private void SendRover(int i, float t, string phase)
        {
            float rt = Mathf.InverseLerp(0.40f, 0.92f, t);
            float lat = Mathf.Lerp(_center.x, _targetA.x, rt);
            float lon = Mathf.Lerp(_center.y, _targetA.y, rt);
            if (phase == TwinOperationPhases.DynamicReplan)
                lon += areaLonDeg * 0.12f * Mathf.Sin(t * 20f); // kacinma manevrasi

            // Rover slamPose'u da set edilir (JsonUtility null nesneyi bos 0,0 yazar -> yanlis konum olmasin diye).
            var msg = new DigitalTwinMessageV1
            {
                sourceId = "demo-rover",
                vehicleType = TwinVehicleTypes.Rover,
                missionPhase = phase,
                pose = new TwinPoseBlock { latitude = lat, longitude = lon, altitudeM = 1.5f, yawDeg = Mathf.Repeat(t * 360f, 360f) },
                slamPose = new TwinSlamPoseBlock { latitude = lat, longitude = lon, altitudeM = 1.5f, yawDeg = 0f, confidence = 0.85f },
                telemetry = BuildTelemetry(t, phase),
                meshLink = BuildMesh(phase),
                mission = new TwinMissionDelta { phase = phase, status = "running", activeVehicle = TwinVehicleTypes.Rover, note = NoteFor(phase) }
            };
            Send(msg);
        }

        private TwinTelemetryBlock BuildTelemetry(float t, string phase)
        {
            var m = BuildMesh(phase);
            return new TwinTelemetryBlock
            {
                altitudeM = baseAltitudeM,
                speedMps = phase == TwinOperationPhases.Complete ? 0f : 7.5f,
                mode = phase == TwinOperationPhases.Complete ? "HOLD" : "AUTO",
                waypointIndex = Mathf.Clamp(Mathf.FloorToInt(t * 8f), 0, 99),
                hopCount = m.hopCount,
                signalDbm = m.signalDbm,
                snrDb = m.snrDb,
                latencyMs = m.latencyMs,
                packetLossPercent = m.packetLossPercent,
                // Fail-safe paneli icin gerceklesen batarya tuketimi simulasyonu.
                batteryPercent = Mathf.Lerp(98f, 46f, t),
                batteryVoltage = Mathf.Lerp(25.1f, 22.2f, t)
            };
        }

        private TwinMeshStatus BuildMesh(string phase)
        {
            switch (phase)
            {
                case TwinOperationPhases.DynamicReplan:
                    return new TwinMeshStatus { hopCount = 2, signalDbm = -74f, snrDb = 9f, latencyMs = 95f, packetLossPercent = 4.2f, linkQualityPercent = 34f, relayModeActive = true };
                case TwinOperationPhases.JointOperation:
                    return new TwinMeshStatus { hopCount = 1, signalDbm = -62f, snrDb = 17f, latencyMs = 56f, packetLossPercent = 1.2f, linkQualityPercent = 74f, relayModeActive = false };
                case TwinOperationPhases.Complete:
                    return new TwinMeshStatus { hopCount = 1, signalDbm = -54f, snrDb = 23f, latencyMs = 38f, packetLossPercent = 0.4f, linkQualityPercent = 90f, relayModeActive = false };
                default:
                    return new TwinMeshStatus { hopCount = 1, signalDbm = -55f, snrDb = 22f, latencyMs = 42f, packetLossPercent = 0.6f, linkQualityPercent = 88f, relayModeActive = false };
            }
        }

        private static string NoteFor(string phase)
        {
            switch (phase)
            {
                case TwinOperationPhases.Scan: return "Oncu tarama: saha haritalaniyor";
                case TwinOperationPhases.JointOperation: return "Ortak harekat: IHA + Rover";
                case TwinOperationPhases.DynamicReplan: return "Dinamik yeniden planlama";
                case TwinOperationPhases.Complete: return "Gorev tamamlandi";
                default: return "";
            }
        }

        private TwinRouteBlock BuildScanRoute()
        {
            float la = areaLatDeg * 0.9f, lo = areaLonDeg * 0.9f;
            float alt = baseAltitudeM + 5f;
            return new TwinRouteBlock
            {
                waypoints = new[]
                {
                    new TwinRouteWaypoint { index = 0, operation = "upsert", latitude = _center.x - la, longitude = _center.y - lo, altitudeM = alt },
                    new TwinRouteWaypoint { index = 1, operation = "upsert", latitude = _center.x + la, longitude = _center.y - lo, altitudeM = alt },
                    new TwinRouteWaypoint { index = 2, operation = "upsert", latitude = _center.x + la, longitude = _center.y + lo, altitudeM = alt },
                    new TwinRouteWaypoint { index = 3, operation = "upsert", latitude = _center.x - la, longitude = _center.y + lo, altitudeM = alt }
                }
            };
        }

        private void AttachScriptedEntities(DigitalTwinMessageV1 msg, int i, float t, string phase)
        {
            var obstacles = new List<TwinObstacleDelta>();
            var targets = new List<TwinTargetDelta>();
            var voxels = new List<TwinVoxelCellDelta>();

            if (i == 3)
            {
                targets.Add(new TwinTargetDelta { id = "qr-A", operation = "upsert", kind = "qrcode", latitude = _targetA.x, longitude = _targetA.y, reached = false, confidence = 0.90f });
                targets.Add(new TwinTargetDelta { id = "qr-B", operation = "upsert", kind = "qrcode", latitude = _targetB.x, longitude = _targetB.y, reached = false, confidence = 0.86f });
                targets.Add(new TwinTargetDelta { id = "qr-C", operation = "upsert", kind = "qrcode", latitude = _targetC.x, longitude = _targetC.y, reached = false, confidence = 0.82f });
                obstacles.Add(new TwinObstacleDelta { id = "obs-static", operation = "upsert", kind = "static", latitude = _center.x + areaLatDeg * 0.2f, longitude = _center.y + areaLonDeg * 0.05f, radiusM = 3f, severity = 0.4f });
            }

            if (phase == TwinOperationPhases.Scan && i >= 2 && i % 3 == 0)
            {
                float ang = t * Mathf.PI * 4f;
                voxels.Add(new TwinVoxelCellDelta
                {
                    id = "vox-" + i,
                    operation = "upsert",
                    latitude = _center.x + Mathf.Sin(ang) * areaLatDeg * 0.8f,
                    longitude = _center.y + Mathf.Cos(ang) * areaLonDeg * 0.8f,
                    altitudeM = 4f,
                    sizeM = 2.0f,
                    occupancy = Mathf.Clamp01(0.4f + 0.5f * Mathf.Sin(i))
                });
            }

            if (phase == TwinOperationPhases.DynamicReplan)
            {
                float k = Mathf.InverseLerp(0.68f, 0.86f, t);
                obstacles.Add(new TwinObstacleDelta
                {
                    id = "obs-moving",
                    operation = "upsert",
                    kind = "moving",
                    latitude = Mathf.Lerp(_center.x, _targetA.x, k) + areaLatDeg * 0.05f,
                    longitude = Mathf.Lerp(_center.y, _targetA.y, k),
                    radiusM = 4.5f,
                    severity = 0.9f
                });
            }

            // Rover hedeflere SIRAYLA ulasir; karekodlarin COZULMUS icerigi okunur
            // (hedef listesi panelinde "qr-A OKUNDU: ..." olarak gorunur — hakem kaniti).
            if (phase == TwinOperationPhases.JointOperation && t >= 0.55f)
            {
                targets.Add(new TwinTargetDelta { id = "qr-A", operation = "upsert", kind = "qrcode", latitude = _targetA.x, longitude = _targetA.y, reached = true, confidence = 0.97f, decodedContent = "KESIF-A | magara girisi guvenli" });
                if (t < 0.57f)
                    msg.imagery = BuildDemoPhoto("qr-A", new Color(0.35f, 0.65f, 1f));
            }

            if (phase == TwinOperationPhases.DynamicReplan && t >= 0.80f)
            {
                targets.Add(new TwinTargetDelta { id = "qr-B", operation = "upsert", kind = "qrcode", latitude = _targetB.x, longitude = _targetB.y, reached = true, confidence = 0.95f, decodedContent = "KESIF-B | engel raporlandi" });
                if (t < 0.82f)
                    msg.imagery = BuildDemoPhoto("qr-B", new Color(1f, 0.62f, 0.2f));
            }

            if (phase == TwinOperationPhases.Complete && i % 4 == 0 && msg.imagery == null)
                msg.imagery = BuildDemoPhoto("qr-C", new Color(0.4f, 0.9f, 0.5f));

            if (phase == TwinOperationPhases.Complete && i % 4 == 0)
            {
                targets.Add(new TwinTargetDelta { id = "qr-A", operation = "upsert", kind = "qrcode", latitude = _targetA.x, longitude = _targetA.y, reached = true, confidence = 0.97f, decodedContent = "KESIF-A | magara girisi guvenli" });
                targets.Add(new TwinTargetDelta { id = "qr-B", operation = "upsert", kind = "qrcode", latitude = _targetB.x, longitude = _targetB.y, reached = true, confidence = 0.95f, decodedContent = "KESIF-B | engel raporlandi" });
                targets.Add(new TwinTargetDelta { id = "qr-C", operation = "upsert", kind = "qrcode", latitude = _targetC.x, longitude = _targetC.y, reached = true, confidence = 0.93f, decodedContent = "KESIF-C | gorev tamamlandi" });
            }

            if (obstacles.Count > 0) msg.obstacles = obstacles.ToArray();
            if (targets.Count > 0) msg.targets = targets.ToArray();
            if (voxels.Count > 0) msg.voxelCells = voxels.ToArray();
        }

        private void OnGUI()
        {
            if (!showHud) return;
            TwinHudTheme.BeginScaledHud();
            float w = 344f, h = 78f;
            Vector2 def = new Vector2((TwinHudTheme.ScreenW - w) * 0.5f, TwinHudTheme.ScreenH - h - 60f);
            Rect r = TwinHudTheme.Drag(ref _hudPos, ref _drag, def, w, h, "twinhud_demo");
            TwinHudTheme.Panel(r);

            float x = r.x + 16f, y = r.y + 12f;
            GUI.Label(new Rect(x, y, w - 130f, 18f), "DEMO SENARYO", TwinHudTheme.Title);
            if (_phaseStyle == null) _phaseStyle = new GUIStyle(TwinHudTheme.Small) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold, normal = { textColor = TwinHudTheme.Accent } };
            GUI.Label(new Rect(x, y, w - 32f, 18f), PhaseLabelTr(_phase), _phaseStyle);
            y += 25f;

            var bar = new Rect(x, y, w - 32f, 9f);
            TwinHudTheme.Fill(bar, new Color(1f, 1f, 1f, 0.10f), 4.5f);
            TwinHudTheme.Fill(new Rect(bar.x, bar.y, bar.width * _progress, bar.height), TwinHudTheme.Accent, 4.5f);
            y += 17f;

            if (GUI.Button(new Rect(x, y, 96f, 21f), _playing ? "Durdur" : "Oynat", TwinHudTheme.Button))
            {
                if (_playing) Stop(); else Play();
            }
            if (_pctStyle == null) _pctStyle = new GUIStyle(TwinHudTheme.Small) { alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(x + 106f, y, w - 122f, 21f), $"%{_progress * 100f:F0}", _pctStyle);
            TwinHudTheme.EndScaledHud();
        }

        private static string PhaseLabelTr(string phase)
        {
            switch (phase)
            {
                case TwinOperationPhases.Scan: return "Faz 1 · Öncü Tarama";
                case TwinOperationPhases.JointOperation: return "Faz 2 · Ortak Harekat";
                case TwinOperationPhases.DynamicReplan: return "Dinamik Replan · link düştü";
                case TwinOperationPhases.Complete: return "Görev Tamamlandı";
                default: return phase;
            }
        }
    }
}
