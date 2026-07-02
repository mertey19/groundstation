using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// PDF 3.4.3 "Mesh Network Mimarisi" gorsel paneli (Sekil 3.4.3.1).
    /// IHA - Rover - YKI 3 dugumlu 802.11s / BATMAN-adv topolojisini canli cizer:
    /// link kalitesi rengi, relay yolu (hop atlamasi), paket akis animasyonu,
    /// hop/RSSI/SNR/gecikme/kayip metrikleri ve LINK KALITESI SPARKLINE'i
    /// (MissionEngine.MeshHistory'den son ~180 ornek — operator trendi gorur).
    /// Cozunurluk olcekli (TwinHudTheme.BeginScaledHud), baslik seridinden suruklenir.
    /// </summary>
    public class DigitalTwinMeshTopologyPanel : MonoBehaviour
    {
        [SerializeField] private DigitalTwinRemoteState remoteState;
        [SerializeField] private DigitalTwinMissionEngine missionEngine;
        [SerializeField] private bool showWhenNoData = true;
        [SerializeField] private Vector2 panelSize = new Vector2(286f, 268f);

        private Vector2 _hudPos = new Vector2(-99999f, 0f);
        private bool _drag;
        private float _nextResolveAt;
        private GUIStyle _nodeStyle;

        private void Awake()
        {
            ResolveRefs();
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextResolveAt)
            {
                _nextResolveAt = Time.unscaledTime + 2f;
                ResolveRefs();
            }
        }

        private void ResolveRefs()
        {
            if (remoteState == null) remoteState = FindObjectOfType<DigitalTwinRemoteState>();
            if (missionEngine == null) missionEngine = FindObjectOfType<DigitalTwinMissionEngine>();
        }

        private void OnGUI()
        {
            if (remoteState == null) return;
            var mesh = remoteState.HasMeshStatus ? remoteState.LastMeshStatus : null;
            if (mesh == null && !showWhenNoData) return;

            TwinHudTheme.BeginScaledHud();
            float w = panelSize.x, h = panelSize.y;
            // Sol sutun #2 (yorungenin hemen altinda).
            Vector2 def = new Vector2(16f, 362f);
            Rect r = TwinHudTheme.Drag(ref _hudPos, ref _drag, def, w, h, "twinhud_mesh_v2");
            TwinHudTheme.Panel(r);

            float x = r.x + 16f, y = r.y + 12f;
            GUI.Label(new Rect(x, y, r.width - 96f, 18f), "MESH AĞI", TwinHudTheme.Title);
            var badge = new Rect(r.x + r.width - 92f, y - 1f, 76f, 17f);
            TwinHudTheme.Fill(badge, new Color(0.18f, 0.34f, 0.52f, 0.75f), 8f);
            GUI.Label(badge, "802.11s", TwinHudTheme.Badge);
            y += 23f;
            TwinHudTheme.Separator(x, y, w - 32f);
            y += 8f;

            float cx = r.x + w * 0.5f;
            Vector2 uav = new Vector2(cx, y + 26f);
            Vector2 rover = new Vector2(r.x + 60f, y + 82f);
            Vector2 yki = new Vector2(r.x + w - 60f, y + 82f);

            bool has = mesh != null;
            float link = has ? mesh.linkQualityPercent : 0f;
            bool relay = has && mesh.relayModeActive;
            Color lc = TwinHudTheme.Quality(link);
            Color dim = new Color(1f, 1f, 1f, 0.12f);

            if (relay)
            {
                TwinHudTheme.Line(rover, uav, lc, 3.5f); AnimDot(rover, uav);
                TwinHudTheme.Line(uav, yki, lc, 3.5f); AnimDot(uav, yki);
                TwinHudTheme.Line(rover, yki, new Color(1f, 0.45f, 0.40f, 0.20f), 2f);
            }
            else
            {
                TwinHudTheme.Line(rover, yki, has ? lc : dim, 3.5f); if (has) AnimDot(rover, yki);
                TwinHudTheme.Line(uav, yki, has ? lc : dim, 3.5f); if (has) AnimDot(uav, yki);
                TwinHudTheme.Line(rover, uav, dim, 2f);
            }

            Node(uav, TwinHudTheme.Gps, "İHA");
            Node(rover, TwinHudTheme.Slam, "Rover");
            Node(yki, TwinHudTheme.Good, "YKİ");

            float my = y + 118f;
            if (has)
            {
                string modeTxt = relay ? "<color=#FFD24A>RELAY · İHA üzerinden</color>" : "<color=#66E68A>DIRECT</color>";
                GUI.Label(new Rect(x, my, w - 32f, 18f), string.Format("Hop {0}    ·    Link %{1:F0}    ·    {2}", mesh.hopCount, mesh.linkQualityPercent, modeTxt), TwinHudTheme.Label);
                GUI.Label(new Rect(x, my + 19f, w - 32f, 16f), string.Format("RSSI {0:F0} dBm   SNR {1:F0} dB   Gecikme {2:F0} ms   Kayıp %{3:F1}", mesh.signalDbm, mesh.snrDb, mesh.latencyMs, mesh.packetLossPercent), TwinHudTheme.Small);

                var bar = new Rect(x, my + 38f, w - 32f, 6f);
                TwinHudTheme.Fill(bar, new Color(1f, 1f, 1f, 0.10f), 3f);
                TwinHudTheme.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(link / 100f), bar.height), lc, 3f);

                // Link kalitesi trendi (sparkline) — MissionEngine gecmisinden.
                DrawSparkline(new Rect(x, my + 50f, w - 32f, 26f));
                GUI.Label(new Rect(x, my + 76f, w - 32f, 14f), "link geçmişi", new GUIStyle(TwinHudTheme.Small) { fontSize = 10 });
            }
            else
            {
                GUI.Label(new Rect(x, my + 14f, w - 32f, 18f), "Mesh verisi bekleniyor…", TwinHudTheme.Small);
            }

            TwinHudTheme.EndScaledHud();
        }

        private void DrawSparkline(Rect area)
        {
            TwinHudTheme.Fill(area, new Color(1f, 1f, 1f, 0.06f), 4f);
            if (missionEngine == null) return;
            var hist = missionEngine.MeshHistory;
            if (hist == null || hist.Count < 2) return;

            // En fazla ~64 nokta ciz (adimlayarak) — GC'siz, hizli.
            int count = hist.Count;
            int step = Mathf.Max(1, count / 64);
            Vector2 prev = Vector2.zero;
            bool hasPrev = false;
            int idx = 0;
            for (int i = 0; i < count; i += step, idx++)
            {
                var s = hist[i];
                float fx = area.x + area.width * ((float)i / Mathf.Max(1, count - 1));
                float fy = area.yMax - area.height * Mathf.Clamp01(s.linkQualityPercent / 100f);
                var p = new Vector2(fx, fy);
                if (hasPrev)
                    TwinHudTheme.Line(prev, p, TwinHudTheme.Quality(s.linkQualityPercent), 1.6f);
                prev = p; hasPrev = true;
            }
        }

        private static void AnimDot(Vector2 a, Vector2 b)
        {
            float t = Mathf.Repeat(Time.unscaledTime * 0.55f, 1f);
            Vector2 p = Vector2.Lerp(a, b, t);
            TwinHudTheme.Dot(new Rect(p.x - 3.5f, p.y - 3.5f, 7f, 7f), Color.white);
        }

        private void Node(Vector2 pos, Color col, string label)
        {
            float rad = 20f;
            TwinHudTheme.Dot(new Rect(pos.x - rad - 3f, pos.y - rad - 3f, (rad + 3f) * 2f, (rad + 3f) * 2f), new Color(col.r, col.g, col.b, 0.22f));
            TwinHudTheme.Dot(new Rect(pos.x - rad, pos.y - rad, rad * 2f, rad * 2f), col);
            TwinHudTheme.Dot(new Rect(pos.x - rad * 0.6f, pos.y - rad * 0.6f, rad * 1.2f, rad * 1.2f), new Color(0.05f, 0.07f, 0.11f, 0.96f));
            if (_nodeStyle == null) _nodeStyle = new GUIStyle(TwinHudTheme.Badge) { fontSize = 11 };
            GUI.Label(new Rect(pos.x - 34f, pos.y - 8f, 68f, 16f), label, _nodeStyle);
        }
    }
}
