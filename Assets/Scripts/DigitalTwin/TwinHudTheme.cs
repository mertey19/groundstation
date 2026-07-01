using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// Ortak HUD temasi: tutarli, yuvarlatilmis koyu "cam" paneller.
    /// Unity GUI.DrawTexture'in borderRadius/borderWidth overload'u (2019.3+) ile
    /// kose yumusatma ve ince kenarlik tek cizimde yapilir; ayri texture gerekmez.
    /// Tum YKI panelleri (yorunge, mesh, demo) bu temayi kullanir.
    /// </summary>
    public static class TwinHudTheme
    {
        public static readonly Color Bg = new Color(0.05f, 0.07f, 0.11f, 0.92f);
        public static readonly Color Stroke = new Color(0.40f, 0.62f, 0.88f, 0.40f);
        public static readonly Color TextPrimary = new Color(0.94f, 0.97f, 1f);
        public static readonly Color TextSecondary = new Color(0.64f, 0.73f, 0.85f);
        public static readonly Color Accent = new Color(0.32f, 0.72f, 1f);
        public static readonly Color Gps = new Color(0.24f, 0.58f, 1f);
        public static readonly Color Slam = new Color(1f, 0.56f, 0.16f);
        public static readonly Color Good = new Color(0.40f, 0.92f, 0.50f);
        public static readonly Color Warn = new Color(1f, 0.82f, 0.28f);
        public static readonly Color Bad = new Color(1f, 0.44f, 0.38f);

        private static bool _init;
        private static GUIStyle _title, _label, _small, _value, _badge, _button;

        public static GUIStyle Title { get { Init(); return _title; } }
        public static GUIStyle Label { get { Init(); return _label; } }
        public static GUIStyle Small { get { Init(); return _small; } }
        public static GUIStyle Value { get { Init(); return _value; } }
        public static GUIStyle Badge { get { Init(); return _badge; } }
        public static GUIStyle Button { get { Init(); return _button; } }

        private static void Init()
        {
            if (_init) return;
            var b = (GUI.skin != null && GUI.skin.label != null) ? GUI.skin.label : new GUIStyle();
            _title = new GUIStyle(b) { fontStyle = FontStyle.Bold, fontSize = 13, richText = true, wordWrap = false, clipping = TextClipping.Overflow, normal = { textColor = TextPrimary } };
            _label = new GUIStyle(b) { fontSize = 12, richText = true, wordWrap = false, clipping = TextClipping.Overflow, normal = { textColor = TextPrimary } };
            _small = new GUIStyle(b) { fontSize = 11, richText = true, wordWrap = false, clipping = TextClipping.Overflow, normal = { textColor = TextSecondary } };
            _value = new GUIStyle(b) { fontStyle = FontStyle.Bold, fontSize = 16, richText = true, normal = { textColor = TextPrimary } };
            _badge = new GUIStyle(b) { fontStyle = FontStyle.Bold, fontSize = 10, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _button = (GUI.skin != null && GUI.skin.button != null)
                ? new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold }
                : new GUIStyle(b) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _init = true;
        }

        /// <summary>Yuvarlatilmis cam panel (golge + arka plan + ince kenar).</summary>
        public static void Panel(Rect r)
        {
            var prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(r.x + 2f, r.y + 4f, r.width, r.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(0f, 0f, 0f, 0.32f), 0f, 14f);
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Bg, 0f, 14f);
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Stroke, 1.2f, 14f);
            GUI.color = prev;
        }

        /// <summary>
        /// Surukle-birak panel konumu: panelin ust serit (baslik) alanindan tutulup tasinir.
        /// pos.x &lt; -9000 ise ilk cizimde def kullanilir. Donen Rect ile panel cizilir.
        /// </summary>
        public static Rect Drag(ref Vector2 pos, ref bool dragging, Vector2 def, float w, float h, string prefsKey = null)
        {
            if (pos.x < -9000f)
            {
                // Once kayitli konum (kalici), yoksa varsayilan.
                pos = def;
                if (!string.IsNullOrEmpty(prefsKey) && PlayerPrefs.HasKey(prefsKey + "_x"))
                    pos = new Vector2(PlayerPrefs.GetFloat(prefsKey + "_x"), PlayerPrefs.GetFloat(prefsKey + "_y"));
            }
            var e = Event.current;
            var titleBar = new Rect(pos.x, pos.y, w, 28f);
            if (e != null)
            {
                if (e.type == EventType.MouseDown && e.button == 0 && titleBar.Contains(e.mousePosition)) { dragging = true; e.Use(); }
                else if (e.type == EventType.MouseUp && e.button == 0)
                {
                    if (dragging && !string.IsNullOrEmpty(prefsKey))
                    {
                        // Surukleme bitti -> konumu kalici kaydet.
                        PlayerPrefs.SetFloat(prefsKey + "_x", pos.x);
                        PlayerPrefs.SetFloat(prefsKey + "_y", pos.y);
                        PlayerPrefs.Save();
                    }
                    dragging = false;
                }
                else if (dragging && e.type == EventType.MouseDrag) { pos += e.delta; e.Use(); }
            }
            pos.x = Mathf.Clamp(pos.x, 0f, Mathf.Max(0f, Screen.width - w));
            pos.y = Mathf.Clamp(pos.y, 0f, Mathf.Max(0f, Screen.height - h));
            return new Rect(pos.x, pos.y, w, h);
        }

        public static void Fill(Rect r, Color c, float radius = 4f)
        {
            var prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);
            GUI.color = prev;
        }

        public static void Dot(Rect r, Color c)
        {
            Fill(r, c, Mathf.Min(r.width, r.height) * 0.5f);
        }

        public static void Separator(float x, float y, float w)
        {
            Fill(new Rect(x, y, w, 1f), Stroke, 0f);
        }

        public static void Line(Vector2 a, Vector2 b, Color color, float width)
        {
            var saved = GUI.matrix;
            Vector2 d = b - a;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float len = d.magnitude;
            GUIUtility.RotateAroundPivot(angle, a);
            Fill(new Rect(a.x, a.y - width * 0.5f, len, width), color, width * 0.5f);
            GUI.matrix = saved;
        }

        public static Color Quality(float pct)
        {
            if (pct <= 0f) return new Color(0.55f, 0.60f, 0.68f, 0.9f);
            if (pct >= 70f) return Good;
            if (pct >= 40f) return Warn;
            return Bad;
        }
    }
}
