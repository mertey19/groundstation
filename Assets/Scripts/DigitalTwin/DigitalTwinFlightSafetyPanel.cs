using System.Globalization;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// PDF 3.3 Fail-Safe / RTL paneli.
    /// Uc fail-safe tetikleyicisini AYRI AYRI izler ve gosterir:
    ///   - Sinyal (mesh link kalitesi)
    ///   - Batarya (telemetry.batteryPercent; %25 uyari, %15 kritik)
    ///   - Operasyon sahasi (DigitalTwinGeofence ihlali)
    /// RTL tespiti tam-kelime eslesmesiyle yapilir (LEVEL/ISLAND gibi sahte pozitif yok).
    /// Operator mudahalesi: EVE DON (RTL) ve ACIL DUR butonlari — yanlislikla basmayi
    /// onlemek icin 1.2 sn BASILI TUTMA ister; komut DigitalTwinCommandEgress ile gider
    /// ve gorev gunlugune "operator_intervention" olarak islenir.
    /// </summary>
    public class DigitalTwinFlightSafetyPanel : MonoBehaviour
    {
        [SerializeField] private DigitalTwinRemoteState remoteState;
        [SerializeField] private DigitalTwinGeofence geofence;
        [SerializeField] private DigitalTwinCommandEgress commandEgress;
        [SerializeField] private float lowLinkThreshold = 35f;
        [SerializeField] private float lowBatteryThreshold = 25f;
        [SerializeField] private float criticalBatteryThreshold = 15f;
        [SerializeField] private float holdToConfirmSeconds = 1.2f;
        [SerializeField] private bool showHud = true;

        private Vector2 _hudPos = new Vector2(-99999f, 0f);
        private bool _drag;
        private float _rtlHold, _stopHold;
        private float _nextResolveAt;
        private GUIStyle _bannerStyle, _rowStyle;

        // Tam-kelime RTL/inis mod adlari (ArduPilot/PX4).
        private static readonly string[] RtlTokens = { "RTL", "QRTL", "RETURN", "LAND", "QLAND" };

        private void Awake()
        {
            ResolveRefs();
        }

        private void Update()
        {
            // Sahne taramasini OnGUI'den cikar: 2 sn'de bir Update'te dene.
            if (Time.unscaledTime >= _nextResolveAt)
            {
                _nextResolveAt = Time.unscaledTime + 2f;
                ResolveRefs();
            }
        }

        private void ResolveRefs()
        {
            if (remoteState == null) remoteState = FindObjectOfType<DigitalTwinRemoteState>();
            if (geofence == null) geofence = FindObjectOfType<DigitalTwinGeofence>();
            if (commandEgress == null) commandEgress = FindObjectOfType<DigitalTwinCommandEgress>();
        }

        private void OnGUI()
        {
            if (!showHud || remoteState == null) return;
            TwinHudTheme.BeginScaledHud();

            float w = TwinHudTheme.RightPanelWidth, h = 236f;
            Rect r = TwinHudTheme.Drag(ref _hudPos, ref _drag, TwinHudTheme.HudColumn.Right, w, h, "twinhud_safety_v4");
            TwinHudTheme.Panel(r);

            float x = r.x + 14f, y = r.y + 12f;
            GUI.Label(new Rect(x, y, w - 28f, 18f), "UÇUŞ GÜVENLİĞİ", TwinHudTheme.Title);
            y += 23f;
            TwinHudTheme.Separator(x, y, w - 28f);
            y += 8f;

            // --- Durum degerlendirmesi ---
            string mode = ExtractMode(remoteState.LastModeText);
            bool rtl = IsRtlMode(mode);
            float link = remoteState.HasMeshStatus ? remoteState.LastMeshStatus.linkQualityPercent : -1f;
            bool lowLink = link >= 0f && link < lowLinkThreshold;
            float batt = remoteState.LastBatteryPercent;
            bool battWarn = batt >= 0f && batt < lowBatteryThreshold;
            bool battCritical = batt >= 0f && batt < criticalBatteryThreshold;
            bool fenceBreach = geofence != null && geofence.AnyBreached;

            string statusTxt; Color statusCol;
            if (rtl) { statusTxt = "EVE DÖNÜŞ (RTL) AKTİF"; statusCol = TwinHudTheme.Bad; }
            else if (fenceBreach) { statusTxt = "SAHA DIŞI — İHLAL"; statusCol = TwinHudTheme.Bad; }
            else if (battCritical) { statusTxt = "BATARYA KRİTİK"; statusCol = TwinHudTheme.Bad; }
            else if (lowLink) { statusTxt = "UYARI · SİNYAL ZAYIF"; statusCol = TwinHudTheme.Warn; }
            else if (battWarn) { statusTxt = "UYARI · BATARYA DÜŞÜK"; statusCol = TwinHudTheme.Warn; }
            else { statusTxt = "NORMAL"; statusCol = TwinHudTheme.Good; }

            var sBar = new Rect(x, y, w - 28f, 26f);
            TwinHudTheme.Fill(sBar, new Color(statusCol.r, statusCol.g, statusCol.b, 0.22f), 6f);
            if (_bannerStyle == null) _bannerStyle = new GUIStyle(TwinHudTheme.Value) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            _bannerStyle.normal.textColor = statusCol;
            GUI.Label(sBar, statusTxt, _bannerStyle);
            y += 32f;

            if (_rowStyle == null) _rowStyle = new GUIStyle(TwinHudTheme.Small);

            // --- Uc tetikleyici ayri satirlar ---
            DrawStatusRow(x, ref y, w, "Sinyal",
                link < 0f ? "—" : string.Format(CultureInfo.InvariantCulture, "%{0:F0}", link),
                link < 0f ? TwinHudTheme.TextSecondary : TwinHudTheme.Quality(link));

            Color battCol = batt < 0f ? TwinHudTheme.TextSecondary : (battCritical ? TwinHudTheme.Bad : (battWarn ? TwinHudTheme.Warn : TwinHudTheme.Good));
            DrawStatusRow(x, ref y, w, "Batarya",
                batt < 0f ? "—" : string.Format(CultureInfo.InvariantCulture, "%{0:F0}{1}", batt,
                    remoteState.LastBatteryVoltage > 0f ? string.Format(CultureInfo.InvariantCulture, " · {0:F1} V", remoteState.LastBatteryVoltage) : ""),
                battCol);
            if (batt >= 0f)
            {
                var bar = new Rect(x + 70f, y - 6f, w - 98f, 5f);
                TwinHudTheme.Fill(bar, new Color(1f, 1f, 1f, 0.10f), 2.5f);
                TwinHudTheme.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(batt / 100f), bar.height), battCol, 2.5f);
                y += 5f;
            }

            string fenceTxt = geofence == null || !geofence.HasCenter ? "tanımsız"
                : (fenceBreach ? "SAHA DIŞI!" : string.Format(CultureInfo.InvariantCulture, "içinde (r={0:F0} m)", geofence.RadiusMeters));
            DrawStatusRow(x, ref y, w, "Saha", fenceTxt,
                geofence == null || !geofence.HasCenter ? TwinHudTheme.TextSecondary : (fenceBreach ? TwinHudTheme.Bad : TwinHudTheme.Good));

            GUI.Label(new Rect(x, y, w - 28f, 16f),
                "Uçuş modu: <b>" + (string.IsNullOrEmpty(mode) ? "-" : mode) + "</b>   ·   Fail-safe: " + ((rtl || lowLink || battCritical || fenceBreach) ? "tetiklendi" : "hazır"),
                TwinHudTheme.Small);
            y += 22f;

            // --- Operator mudahale butonlari (basili tut) ---
            float bw = (w - 28f - 8f) * 0.5f;
            DrawHoldButton(new Rect(x, y, bw, 26f), "EVE DÖN (RTL)", ref _rtlHold, () =>
            {
                EnsureEgress();
                if (commandEgress != null) commandEgress.SendReturnToLaunch();
            });
            DrawHoldButton(new Rect(x + bw + 8f, y, bw, 26f), "ACİL DUR", ref _stopHold, () =>
            {
                EnsureEgress();
                if (commandEgress != null) commandEgress.SendEmergencyStop();
            });
            y += 30f;
            GUI.Label(new Rect(x, y, w - 28f, 14f), "komut için 1,2 sn basılı tut", new GUIStyle(TwinHudTheme.Small) { fontSize = 10 });

            TwinHudTheme.EndScaledHud();
        }

        private void DrawStatusRow(float x, ref float y, float w, string label, string value, Color col)
        {
            TwinHudTheme.Dot(new Rect(x, y + 3f, 9f, 9f), col);
            GUI.Label(new Rect(x + 16f, y, 60f, 16f), label, TwinHudTheme.Small);
            _rowStyle.normal.textColor = col;
            GUI.Label(new Rect(x + 78f, y, w - 106f, 16f), value, _rowStyle);
            y += 19f;
        }

        private void DrawHoldButton(Rect rect, string label, ref float hold, System.Action onConfirm)
        {
            bool pressed = GUI.RepeatButton(rect, label, TwinHudTheme.Button);
            if (Event.current.type == EventType.Repaint)
            {
                if (pressed)
                {
                    hold += Time.unscaledDeltaTime;
                    if (hold >= holdToConfirmSeconds)
                    {
                        hold = 0f;
                        onConfirm?.Invoke();
                    }
                }
                else
                {
                    hold = 0f;
                }
            }
            if (hold > 0.01f)
                TwinHudTheme.Fill(new Rect(rect.x + 2f, rect.yMax - 4f, (rect.width - 4f) * Mathf.Clamp01(hold / holdToConfirmSeconds), 3f), TwinHudTheme.Bad, 1.5f);
        }

        private void EnsureEgress()
        {
            if (commandEgress != null) return;
            commandEgress = FindObjectOfType<DigitalTwinCommandEgress>();
            if (commandEgress == null)
            {
                var go = new GameObject("DigitalTwinCommandEgress");
                commandEgress = go.AddComponent<DigitalTwinCommandEgress>();
            }
        }

        /// <summary>Tam-kelime eslesmesi: "LEVEL" icindeki "EVE", "ISLAND" icindeki "LAND" tetiklemez.</summary>
        private static bool IsRtlMode(string mode)
        {
            if (string.IsNullOrEmpty(mode)) return false;
            var tokens = mode.Split(' ', '_', '(', ')', '/', '-', '.');
            for (int i = 0; i < tokens.Length; i++)
            {
                string t = tokens[i].Trim();
                for (int j = 0; j < RtlTokens.Length; j++)
                    if (t.Equals(RtlTokens[j], System.StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            return false;
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
