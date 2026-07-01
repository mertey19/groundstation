using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// PDF 3.3 Fail-Safe / RTL gostergesi.
    /// Ucus modu (AUTO/GUIDED/RTL/HOLD...) + sinyal/link durumu + "Eve Don (RTL)" aktif uyarisi.
    /// Veri: DigitalTwinRemoteState telemetry mode + meshLink (link kalitesi).
    /// Baslik seridinden surukle-tasi (TwinHudTheme).
    /// </summary>
    public class DigitalTwinFlightSafetyPanel : MonoBehaviour
    {
        [SerializeField] private DigitalTwinRemoteState remoteState;
        [SerializeField] private float lowLinkThreshold = 35f;
        [SerializeField] private bool showHud = true;

        private Vector2 _hudPos = new Vector2(-99999f, 0f);
        private bool _drag;

        private void Awake()
        {
            if (remoteState == null) remoteState = FindObjectOfType<DigitalTwinRemoteState>();
        }

        private void OnGUI()
        {
            if (!showHud) return;
            if (remoteState == null)
            {
                remoteState = FindObjectOfType<DigitalTwinRemoteState>();
                if (remoteState == null) return;
            }

            float w = 300f, h = 120f;   // canli kamera paneliyle esit genislik
            Vector2 def = new Vector2(Screen.width - w - 16f, 16f);   // sag ust
            Rect r = TwinHudTheme.Drag(ref _hudPos, ref _drag, def, w, h, "twinhud_safety");
            TwinHudTheme.Panel(r);

            float x = r.x + 14f, y = r.y + 12f;
            string mode = ExtractMode(remoteState.LastModeText);
            bool rtl = Contains(mode, "RTL") || Contains(mode, "RETURN") || Contains(mode, "EVE") || Contains(mode, "LAND");
            float link = remoteState.HasMeshStatus ? remoteState.LastMeshStatus.linkQualityPercent : -1f;
            bool lowLink = link >= 0f && link < lowLinkThreshold;

            GUI.Label(new Rect(x, y, w - 28f, 18f), "UÇUŞ GÜVENLİĞİ", TwinHudTheme.Title);
            y += 23f;
            TwinHudTheme.Separator(x, y, w - 28f);
            y += 8f;

            string statusTxt; Color statusCol;
            if (rtl) { statusTxt = "EVE DÖNÜŞ (RTL) AKTİF"; statusCol = TwinHudTheme.Bad; }
            else if (lowLink) { statusTxt = "UYARI · SİNYAL ZAYIF"; statusCol = TwinHudTheme.Warn; }
            else { statusTxt = "NORMAL"; statusCol = TwinHudTheme.Good; }

            var sBar = new Rect(x, y, w - 28f, 26f);
            TwinHudTheme.Fill(sBar, new Color(statusCol.r, statusCol.g, statusCol.b, 0.22f), 6f);
            var sStyle = new GUIStyle(TwinHudTheme.Value) { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = statusCol } };
            GUI.Label(sBar, statusTxt, sStyle);
            y += 32f;

            GUI.Label(new Rect(x, y, w - 28f, 16f), $"Uçuş modu: <b>{(string.IsNullOrEmpty(mode) ? "-" : mode)}</b>", TwinHudTheme.Label);
            y += 18f;
            string linkTxt = link >= 0f ? $"{link:F0}%" : "-";
            GUI.Label(new Rect(x, y, w - 28f, 16f), $"Bağlantı: {linkTxt}   ·   Fail-safe: {((rtl || lowLink) ? "tetiklendi" : "hazır")}", TwinHudTheme.Small);
        }

        private static bool Contains(string s, string sub)
        {
            return !string.IsNullOrEmpty(s) && s.IndexOf(sub, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ExtractMode(string modeText)
        {
            if (string.IsNullOrEmpty(modeText)) return "";
            int i = modeText.IndexOf(':');
            string m = i >= 0 ? modeText.Substring(i + 1) : modeText;
            int b = m.IndexOf('[');
            if (b >= 0) m = m.Substring(0, b);
            return m.Trim();
        }
    }
}
