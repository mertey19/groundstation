using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// PDF 3.4.2 "canli goruntu aktarimi" paneli.
    /// ImageryService'in son kamera/goruntu texture'ini kucuk bir pencerede gosterir;
    /// kaynak + canli/kapali durumu + adaptif mod (hibrit / dijital ikiz) bilgisi.
    /// Goruntu yoksa "Dijital ikiz modu" / "bekleniyor" mesaji verir.
    /// Baslik seridinden surukle-tasi (TwinHudTheme).
    /// </summary>
    public class DigitalTwinCameraFeedPanel : MonoBehaviour
    {
        [SerializeField] private DigitalTwinImageryService imageryService;
        [SerializeField] private DigitalTwinRemoteState remoteState;
        [SerializeField] private DigitalTwinMissionEngine missionEngine;
        [SerializeField] private bool showHud = true;

        private Vector2 _hudPos = new Vector2(-99999f, 0f);
        private bool _drag;

        private void Awake()
        {
            if (imageryService == null) imageryService = FindObjectOfType<DigitalTwinImageryService>();
            if (remoteState == null) remoteState = FindObjectOfType<DigitalTwinRemoteState>();
            if (missionEngine == null) missionEngine = FindObjectOfType<DigitalTwinMissionEngine>();
        }

        private void OnGUI()
        {
            if (!showHud) return;
            if (imageryService == null) imageryService = FindObjectOfType<DigitalTwinImageryService>();

            float w = 300f, h = 214f;
            Vector2 def = new Vector2(Screen.width - w - 16f, 152f);   // sag, ucus guvenligi panelinin altinda
            Rect r = TwinHudTheme.Drag(ref _hudPos, ref _drag, def, w, h, "twinhud_cam");
            TwinHudTheme.Panel(r);

            float x = r.x + 14f, y = r.y + 12f;
            Texture tex = imageryService != null ? imageryService.CurrentTexture : null;
            bool live = imageryService != null && imageryService.HasActiveImagery && tex != null;
            bool twinOnly = missionEngine != null && missionEngine.TwinOnlyModeRecommended;

            GUI.Label(new Rect(x, y, w - 96f, 18f), "CANLI KAMERA", TwinHudTheme.Title);
            var badge = new Rect(r.x + r.width - 80f, y - 1f, 64f, 17f);
            TwinHudTheme.Fill(badge, live ? new Color(0.82f, 0.22f, 0.22f, 0.92f) : new Color(0.42f, 0.44f, 0.48f, 0.7f), 8f);
            GUI.Label(badge, live ? "CANLI" : "KAPALI", TwinHudTheme.Badge);
            y += 24f;

            var img = new Rect(x, y, w - 28f, h - 74f);
            TwinHudTheme.Fill(img, new Color(0f, 0f, 0f, 0.55f), 6f);
            if (live && tex != null)
            {
                GUI.DrawTexture(img, tex, ScaleMode.ScaleToFit, false);
            }
            else
            {
                var st = new GUIStyle(TwinHudTheme.Small) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(img, twinOnly ? "Sinyal zayıf — Dijital ikiz modu" : "Kamera akışı bekleniyor…", st);
            }

            y = r.y + r.height - 26f;
            string src = (remoteState != null && !string.IsNullOrEmpty(remoteState.LastSourceId)) ? remoteState.LastSourceId : "-";
            string mode = twinOnly ? "<color=#FFD24A>Adaptif: Dijital ikiz</color>" : "<color=#66E68A>Adaptif: Hibrit</color>";
            GUI.Label(new Rect(x, y, w - 28f, 16f), $"Kaynak: {src}   ·   {mode}", TwinHudTheme.Small);
        }
    }
}
