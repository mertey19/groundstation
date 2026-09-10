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
            if (!DigitalTwinHudWorkspace.Shows(DigitalTwinHudWorkspace.Detail.Mesh)) return;
            if (remoteState == null) return;
            bool fresh = remoteState.HasFreshMesh;
            var mesh = fresh ? remoteState.LastMeshStatus : null;
            if (mesh == null && !showWhenNoData && !DigitalTwinHudWorkspace.IsSelected(DigitalTwinHudWorkspace.Detail.Mesh)) return;

            TwinHudTheme.BeginScaledHud();
            float w = TwinHudTheme.LeftPanelWidth;
            float h = fresh ? 330f : 268f;
            Rect panel = TwinHudTheme.Drag(ref _hudPos, ref _drag, TwinHudTheme.HudColumn.Left, w, h, "twinhud_mesh_v5");
            TwinHudTheme.Panel(panel);

            // Local coordinates keep the entire diagram inside its own clipping boundary.
            GUI.BeginGroup(panel);
            try
            {
                GUI.Label(new Rect(16f, 12f, w - 112f, 20f), "MESH AĞI", TwinHudTheme.Title);
                var badge = new Rect(w - 92f, 12f, 76f, 19f);
                TwinHudTheme.Fill(badge, new Color(0.18f, 0.34f, 0.52f, 0.5f), 9f);
                GUI.Label(badge, "802.11s", TwinHudTheme.Badge);
                TwinHudTheme.Separator(16f, 39f, w - 32f);

                var diagram = new Rect(16f, 49f, w - 32f, 144f);
                TwinHudTheme.Fill(diagram, new Color(0.08f, 0.12f, 0.17f, 0.65f), 10f);
                Vector2 uav = new Vector2(w * 0.5f, 81f);
                Vector2 rover = new Vector2(66f, 156f);
                Vector2 station = new Vector2(w - 66f, 156f);
                bool relay = fresh && mesh.relayModeActive;
                Color active = fresh ? TwinHudTheme.Quality(mesh.linkQualityPercent) : new Color(0.36f, 0.44f, 0.55f, 0.5f);
                Color muted = new Color(0.36f, 0.44f, 0.55f, 0.25f);

                Connection(rover, uav, relay ? active : muted, fresh && relay);
                Connection(uav, station, active, fresh);
                Connection(rover, station, relay ? muted : active, fresh && !relay);
                Node(uav, TwinHudTheme.Gps, "İHA");
                Node(rover, TwinHudTheme.Slam, "Rover");
                Node(station, TwinHudTheme.Good, "YKİ");

                var status = new Rect(16f, 203f, w - 32f, h - 219f);
                TwinHudTheme.Fill(status, new Color(0.10f, 0.15f, 0.21f, 0.68f), 8f);
                if (fresh)
                {
                    GUI.Label(new Rect(28f, 211f, 182f, 20f), relay ? "İHA üzerinden aktarım" : "Doğrudan bağlantı", TwinHudTheme.Label);
                    var quality = new Rect(w - 110f, 211f, 80f, 20f);
                    TwinHudTheme.Fill(quality, new Color(active.r, active.g, active.b, 0.2f), 6f);
                    GUI.Label(quality, string.Format("%{0:F0} kalite", mesh.linkQualityPercent), TwinHudTheme.Badge);
                    GUI.Label(new Rect(28f, 238f, w - 56f, 16f), string.Format("{0} hop  ·  RSSI {1:F0} dBm  ·  SNR {2:F0} dB", mesh.hopCount, mesh.signalDbm, mesh.snrDb), TwinHudTheme.Small);
                    GUI.Label(new Rect(28f, 257f, w - 56f, 16f), string.Format("Gecikme {0:F0} ms  ·  Kayıp %{1:F1}", mesh.latencyMs, mesh.packetLossPercent), TwinHudTheme.Small);
                    var bar = new Rect(28f, 279f, w - 56f, 5f);
                    TwinHudTheme.Fill(bar, new Color(1f, 1f, 1f, 0.08f), 2f);
                    float filled = bar.width * Mathf.Clamp01(mesh.linkQualityPercent / 100f);
                    if (filled > 0f) TwinHudTheme.Fill(new Rect(bar.x, bar.y, filled, bar.height), active, 2f);
                    DrawSparkline(new Rect(28f, 291f, w - 56f, 14f));
                }
                else
                {
                    TwinHudTheme.Dot(new Rect(28f, 219f, 6f, 6f), TwinHudTheme.TextSecondary);
                    GUI.Label(new Rect(42f, 210f, w - 70f, 19f), remoteState.HasMeshStatus ? "Mesh verisi güncel değil" : "Mesh verisi bekleniyor", TwinHudTheme.Label);
                    GUI.Label(new Rect(42f, 231f, w - 70f, 15f), "Bağlantılar veri geldiğinde güncellenir.", TwinHudTheme.Small);
                }
            }
            finally
            {
                GUI.EndGroup();
                TwinHudTheme.EndScaledHud();
            }
        }

        private static void Connection(Vector2 from, Vector2 to, Color color, bool animate)
        {
            // Stop at the outside of each node, with equal space around all three links.
            Vector2 direction = (to - from).normalized;
            Vector2 a = from + direction * 25f;
            Vector2 b = to - direction * 25f;
            TwinHudTheme.Line(a, b, color, animate ? 2.5f : 1.5f);
            if (animate) AnimDot(a, b);
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
            for (int i = 0; i < count; i += step)
            {
                var s = hist[i];
                float fx = area.x + 2f + (area.width - 4f) * ((float)i / Mathf.Max(1, count - 1));
                float fy = area.yMax - 2f - (area.height - 4f) * Mathf.Clamp01(s.linkQualityPercent / 100f);
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

        private void Node(Vector2 pos, Color color, string label)
        {
            const float radius = 22f;
            TwinHudTheme.Dot(new Rect(pos.x - 26f, pos.y - 26f, 52f, 52f), new Color(color.r, color.g, color.b, 0.10f));
            TwinHudTheme.Dot(new Rect(pos.x - radius, pos.y - radius, radius * 2f, radius * 2f), color);
            TwinHudTheme.Dot(new Rect(pos.x - 19f, pos.y - 19f, 38f, 38f), new Color(0.055f, 0.085f, 0.13f, 1f));
            if (_nodeStyle == null)
                _nodeStyle = new GUIStyle(TwinHudTheme.Badge) { fontSize = 11, clipping = TextClipping.Clip };
            GUI.Label(new Rect(pos.x - 22f, pos.y - 10f, 44f, 20f), label, _nodeStyle);
        }
    }
}
