using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// PDF 3.4.2 "canli goruntu aktarimi" paneli.
    /// ImageryService'in son kamera/goruntu texture'ini gosterir; kaynak + CANLI/KAPALI
    /// durumu + adaptif mod bilgisi. PDF geregi adaptif gecis HEM otomatik (link esigi)
    /// HEM operator inisiyatifiyle olur: "Dijital Ikiz Moduna Gec / Kameraya Don"
    /// dugmesi AdaptiveFlowController.ManualTwinOnly'i degistirir.
    /// Cozunurluk olcekli, baslik seridinden suruklenir.
    /// </summary>
    public class DigitalTwinCameraFeedPanel : MonoBehaviour
    {
        [SerializeField] private DigitalTwinImageryService imageryService;
        [SerializeField] private DigitalTwinRemoteState remoteState;
        [SerializeField] private DigitalTwinMissionEngine missionEngine;
        [SerializeField] private DigitalTwinAdaptiveFlowController adaptiveFlow;
        [SerializeField] private bool showHud = true;

        private Vector2 _hudPos = new Vector2(-99999f, 0f);
        private bool _drag;
        private float _nextResolveAt;
        private GUIStyle _centerStyle;

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
            if (imageryService == null) imageryService = FindObjectOfType<DigitalTwinImageryService>();
            if (remoteState == null) remoteState = FindObjectOfType<DigitalTwinRemoteState>();
            if (missionEngine == null) missionEngine = FindObjectOfType<DigitalTwinMissionEngine>();
            if (adaptiveFlow == null) adaptiveFlow = FindObjectOfType<DigitalTwinAdaptiveFlowController>();
        }

        private void OnGUI()
        {
            if (!showHud) return;
            TwinHudTheme.BeginScaledHud();

            float w = 300f, h = 238f;
            // Sag sutun #2 (ucus guvenliginin altinda).
            Vector2 def = new Vector2(TwinHudTheme.ScreenW - w - 16f, 362f);
            Rect r = TwinHudTheme.Drag(ref _hudPos, ref _drag, def, w, h, "twinhud_cam_v2");
            TwinHudTheme.Panel(r);

            float x = r.x + 14f, y = r.y + 12f;
            Texture tex = imageryService != null ? imageryService.CurrentTexture : null;
            bool manual = adaptiveFlow != null && adaptiveFlow.ManualTwinOnly;
            bool autoTwinOnly = missionEngine != null && missionEngine.TwinOnlyModeRecommended;
            bool twinOnly = manual || autoTwinOnly;
            bool live = !twinOnly && imageryService != null && imageryService.HasActiveImagery && tex != null;

            GUI.Label(new Rect(x, y, w - 96f, 18f), "CANLI KAMERA", TwinHudTheme.Title);
            var badge = new Rect(r.x + r.width - 80f, y - 1f, 64f, 17f);
            TwinHudTheme.Fill(badge, live ? new Color(0.82f, 0.22f, 0.22f, 0.92f) : new Color(0.42f, 0.44f, 0.48f, 0.7f), 8f);
            GUI.Label(badge, live ? "CANLI" : "KAPALI", TwinHudTheme.Badge);
            y += 24f;

            var img = new Rect(x, y, w - 28f, h - 100f);
            TwinHudTheme.Fill(img, new Color(0f, 0f, 0f, 0.55f), 6f);
            if (live)
            {
                GUI.DrawTexture(img, tex, ScaleMode.ScaleToFit, false);
            }
            else
            {
                if (_centerStyle == null) _centerStyle = new GUIStyle(TwinHudTheme.Small) { alignment = TextAnchor.MiddleCenter };
                string msg = manual ? "Operatör: Dijital ikiz modu"
                    : (autoTwinOnly ? "Sinyal zayıf — Dijital ikiz modu" : "Kamera akışı bekleniyor…");
                GUI.Label(img, msg, _centerStyle);
            }

            float by = r.y + h - 54f;
            // Operator inisiyatifli mod gecisi (PDF 3.4.2).
            string btnLabel = manual ? "Kameraya Dön" : "Dijital İkiz Moduna Geç";
            if (GUI.Button(new Rect(x, by, w - 28f, 22f), btnLabel, TwinHudTheme.Button))
            {
                if (adaptiveFlow == null) ResolveRefs();
                if (adaptiveFlow != null)
                {
                    adaptiveFlow.ManualTwinOnly = !manual;
                    if (missionEngine != null)
                        missionEngine.PushExternalEvent("operator_intervention",
                            adaptiveFlow.ManualTwinOnly ? "manuel twin-only ACIK" : "manuel twin-only KAPALI");
                }
            }
            by += 26f;

            string src = (remoteState != null && !string.IsNullOrEmpty(remoteState.LastSourceId)) ? remoteState.LastSourceId : "-";
            string mode = twinOnly
                ? (manual ? "<color=#FFD24A>Adaptif: Dijital ikiz (manuel)</color>" : "<color=#FFD24A>Adaptif: Dijital ikiz</color>")
                : "<color=#66E68A>Adaptif: Hibrit</color>";
            GUI.Label(new Rect(x, by, w - 28f, 16f), "Kaynak: " + src + "   ·   " + mode, TwinHudTheme.Small);

            TwinHudTheme.EndScaledHud();
        }
    }
}
