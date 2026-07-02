using System.Collections.Generic;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// Hedef (karekod) listesi paneli — PDF gorev senaryosunun ana kaniti:
    /// "Rover her hedefe ulastiginda karekodlari tarar" -> okunan ICERIK + basari
    /// sayaci ("2/3") burada canli gosterilir; hakem sunumu dogrudan bu panelden yapilir.
    /// Veri: DigitalTwinMissionEngine.GetTargetInfos (decodedContent + reached).
    /// </summary>
    public class DigitalTwinTargetListPanel : MonoBehaviour
    {
        [SerializeField] private DigitalTwinMissionEngine missionEngine;
        [SerializeField] private bool showWhenEmpty = false;
        [SerializeField] private int maxRows = 6;
        [SerializeField] private float refreshInterval = 0.5f;

        private readonly List<DigitalTwinMissionEngine.TargetInfo> _targets = new List<DigitalTwinMissionEngine.TargetInfo>();
        private Vector2 _hudPos = new Vector2(-99999f, 0f);
        private bool _drag;
        private float _nextRefreshAt, _nextResolveAt;
        private GUIStyle _rowStyle, _contentStyle;

        private void Update()
        {
            if (Time.unscaledTime >= _nextResolveAt)
            {
                _nextResolveAt = Time.unscaledTime + 2f;
                if (missionEngine == null) missionEngine = FindObjectOfType<DigitalTwinMissionEngine>();
            }
            if (missionEngine != null && Time.unscaledTime >= _nextRefreshAt)
            {
                _nextRefreshAt = Time.unscaledTime + Mathf.Max(0.1f, refreshInterval);
                missionEngine.GetTargetInfos(_targets);
            }
        }

        private void OnGUI()
        {
            if (missionEngine == null) return;
            if (_targets.Count == 0 && !showWhenEmpty) return;

            TwinHudTheme.BeginScaledHud();
            int rows = Mathf.Min(maxRows, Mathf.Max(1, _targets.Count));
            float w = 300f, h = 64f + rows * 22f;
            // Sag sutun #3 (kameranin altinda).
            Vector2 def = new Vector2(TwinHudTheme.ScreenW - w - 16f, 606f);
            Rect r = TwinHudTheme.Drag(ref _hudPos, ref _drag, def, w, h, "twinhud_targets_v2");
            TwinHudTheme.Panel(r);

            float x = r.x + 14f, y = r.y + 12f;
            GUI.Label(new Rect(x, y, w - 110f, 18f), "HEDEFLER", TwinHudTheme.Title);
            var badge = new Rect(r.x + r.width - 76f, y - 1f, 60f, 17f);
            bool allDone = _targets.Count > 0 && missionEngine.TargetsReachedCount >= _targets.Count;
            TwinHudTheme.Fill(badge, allDone ? new Color(0.20f, 0.55f, 0.28f, 0.9f) : new Color(0.18f, 0.34f, 0.52f, 0.75f), 8f);
            GUI.Label(badge, missionEngine.TargetsReachedCount + "/" + missionEngine.TargetsTotalCount, TwinHudTheme.Badge);
            y += 23f;
            TwinHudTheme.Separator(x, y, w - 28f);
            y += 8f;

            if (_rowStyle == null) _rowStyle = new GUIStyle(TwinHudTheme.Label) { fontSize = 12 };
            if (_contentStyle == null) _contentStyle = new GUIStyle(TwinHudTheme.Small) { alignment = TextAnchor.MiddleRight };

            int shown = 0;
            for (int i = 0; i < _targets.Count && shown < maxRows; i++, shown++)
            {
                var t = _targets[i];
                Color c = t.reached ? TwinHudTheme.Good : TwinHudTheme.Gps;
                TwinHudTheme.Dot(new Rect(x, y + 5f, 10f, 10f), c);
                _rowStyle.normal.textColor = t.reached ? TwinHudTheme.Good : TwinHudTheme.TextPrimary;
                GUI.Label(new Rect(x + 17f, y, 92f, 18f), t.id, _rowStyle);
                string contentTxt = t.reached
                    ? (string.IsNullOrEmpty(t.content) ? "OKUNDU" : "“" + t.content + "”")
                    : "bekliyor";
                _contentStyle.normal.textColor = t.reached ? TwinHudTheme.TextPrimary : TwinHudTheme.TextSecondary;
                GUI.Label(new Rect(x + 110f, y, w - 138f, 18f), contentTxt, _contentStyle);
                y += 22f;
            }
            if (_targets.Count > maxRows)
                GUI.Label(new Rect(x, y, w - 28f, 14f), "+" + (_targets.Count - maxRows) + " hedef daha…", TwinHudTheme.Small);

            TwinHudTheme.EndScaledHud();
        }
    }
}
