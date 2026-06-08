using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// PDF 3.4.3 "Mesh Network Mimarisi" gorsel paneli (Sekil 3.4.3.1).
    /// IHA - Rover - YKI 3 dugumlu 802.11s / BATMAN-adv topolojisini canli cizer:
    /// link kalitesi rengi, relay yolu (hop atlamasi), paket akis animasyonu ve
    /// alt satirda hop/RSSI/SNR/gecikme/kayip/link metrikleri. Ortak TwinHudTheme kullanir.
    /// Veri: DigitalTwinRemoteState.LastMeshStatus (JSON koprusunden meshLink).
    /// </summary>
    public class DigitalTwinMeshTopologyPanel : MonoBehaviour
    {
        public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

        [SerializeField] private DigitalTwinRemoteState remoteState;
        [SerializeField] private Corner corner = Corner.BottomLeft;
        [SerializeField] private Vector2 panelSize = new Vector2(326f, 246f);
        [SerializeField] private Vector2 margin = new Vector2(16f, 16f);
        [SerializeField] private bool showWhenNoData = true;

        private Vector2 _hudPos = new Vector2(-99999f, 0f);
        private bool _drag;

        private void Awake()
        {
            if (remoteState == null) remoteState = FindObjectOfType<DigitalTwinRemoteState>();
        }

        private void OnGUI()
        {
            if (remoteState == null)
            {
                remoteState = FindObjectOfType<DigitalTwinRemoteState>();
                if (remoteState == null) return;
            }

            var mesh = remoteState.HasMeshStatus ? remoteState.LastMeshStatus : null;
            if (mesh == null && !showWhenNoData) return;

            float w = 322f, h = 224f;
            // Varsayilan: sol tarafta, biraz yukarida. Baslik seridinden surukle.
            Vector2 def = new Vector2(16f, Screen.height - h - 132f);
            Rect r = TwinHudTheme.Drag(ref _hudPos, ref _drag, def, w, h);
            TwinHudTheme.Panel(r);

            float x = r.x + 16f, y = r.y + 12f;
            GUI.Label(new Rect(x, y, r.width - 96f, 18f), "MESH AĞI", TwinHudTheme.Title);
            var badge = new Rect(r.x + r.width - 92f, y - 1f, 76f, 17f);
            TwinHudTheme.Fill(badge, new Color(0.18f, 0.34f, 0.52f, 0.75f), 8f);
            GUI.Label(badge, "802.11s", TwinHudTheme.Badge);
            y += 23f;
            TwinHudTheme.Separator(x, y, r.width - 32f);
            y += 8f;

            float cx = r.x + r.width * 0.5f;
            Vector2 uav = new Vector2(cx, y + 30f);
            Vector2 rover = new Vector2(r.x + 64f, y + 92f);
            Vector2 yki = new Vector2(r.x + r.width - 64f, y + 92f);

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

            float my = r.y + r.height - 62f;
            if (has)
            {
                string mode = relay ? "<color=#FFD24A>RELAY · İHA üzerinden</color>" : "<color=#66E68A>DIRECT</color>";
                GUI.Label(new Rect(x, my, r.width - 32f, 18f), $"Hop {mesh.hopCount}    ·    Link %{mesh.linkQualityPercent:F0}    ·    {mode}", TwinHudTheme.Label);
                GUI.Label(new Rect(x, my + 20f, r.width - 32f, 16f), $"RSSI {mesh.signalDbm:F0} dBm    SNR {mesh.snrDb:F0} dB    Gecikme {mesh.latencyMs:F0} ms    Kayıp %{mesh.packetLossPercent:F1}", TwinHudTheme.Small);
                var bar = new Rect(x, my + 40f, r.width - 32f, 7f);
                TwinHudTheme.Fill(bar, new Color(1f, 1f, 1f, 0.10f), 3.5f);
                TwinHudTheme.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(link / 100f), bar.height), lc, 3.5f);
            }
            else
            {
                GUI.Label(new Rect(x, my + 14f, r.width - 32f, 18f), "Mesh verisi bekleniyor…", TwinHudTheme.Small);
            }
        }

        private static void AnimDot(Vector2 a, Vector2 b)
        {
            float t = Mathf.Repeat(Time.unscaledTime * 0.55f, 1f);
            Vector2 p = Vector2.Lerp(a, b, t);
            TwinHudTheme.Dot(new Rect(p.x - 3.5f, p.y - 3.5f, 7f, 7f), Color.white);
        }

        private static void Node(Vector2 pos, Color col, string label)
        {
            float rad = 20f;
            TwinHudTheme.Dot(new Rect(pos.x - rad - 3f, pos.y - rad - 3f, (rad + 3f) * 2f, (rad + 3f) * 2f), new Color(col.r, col.g, col.b, 0.22f));
            TwinHudTheme.Dot(new Rect(pos.x - rad, pos.y - rad, rad * 2f, rad * 2f), col);
            TwinHudTheme.Dot(new Rect(pos.x - rad * 0.6f, pos.y - rad * 0.6f, rad * 1.2f, rad * 1.2f), new Color(0.05f, 0.07f, 0.11f, 0.96f));
            var st = new GUIStyle(TwinHudTheme.Badge) { fontSize = 11 };
            GUI.Label(new Rect(pos.x - 34f, pos.y - 8f, 68f, 16f), label, st);
        }

        private Rect ComputeRect()
        {
            // Sol-alt kosede, kompakt ve daha asagida: ekranin altina yapisik (alttaki
            // Yukseklik / Survey gibi butonlari ortmesin). Inspector alanlari yok sayilir.
            float w = 322f, h = 224f;
            return new Rect(16f, Screen.height - h - 10f, w, h);
        }
    }
}
