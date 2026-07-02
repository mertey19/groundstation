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
        private static Matrix4x4 _savedMatrix = Matrix4x4.identity;

        // --- Cozunurluk olcekleme (4K / projektor okunabilirligi) ---
        // Paneller 1080p "sanal" koordinatlarda cizilir; GUI.matrix hem cizimi hem
        // IMGUI girdisini olcekler. Panel konumlamada Screen.width yerine ScreenW kullan.

        /// <summary>Genel HUD kompaktligi: 1 = tam boy, 0.78 = %22 kucuk paneller (yazilar dahil).</summary>
        public static float HudCompactScale = 0.78f;

        public static float UiScale => Mathf.Max(1f, Screen.height / 1080f) * Mathf.Clamp(HudCompactScale, 0.6f, 1.2f);
        public static float ScreenW => Screen.width / UiScale;
        public static float ScreenH => Screen.height / UiScale;

        /// <summary>OnGUI basinda cagir; donen deger olcek. Sonunda EndScaledHud() cagir.</summary>
        public static float BeginScaledHud()
        {
            _savedMatrix = GUI.matrix;
            float s = UiScale;
            if (Mathf.Abs(s - 1f) > 0.001f)
                GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f)) * _savedMatrix;
            return s;
        }

        public static void EndScaledHud()
        {
            GUI.matrix = _savedMatrix;
        }

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

        // --- Otomatik kolon istifi ---
        // Paneller sabit Y yerine kolon icinde GERCEK yuksekliklerine gore alt alta dizilir:
        // biri gizlenince bosluk kapanir, icerik uzasa da CAKISMA OLMAZ. Kullanici bir paneli
        // surukleyince o panel istiften cikar (serbest konum, PlayerPrefs ile kalici).
        public enum HudColumn { Free, Left, Right }

        public static float LeftColumnStartY = 195f;    // sol ust telemetrinin alti
        public static float RightColumnStartY = 130f;   // zoom +/- butonlarinin alti
        private const float StackGap = 8f;

        private static int _stackFrame = -1;
        private static EventType _stackEvt = EventType.Ignore;
        private static float _stackLeftY, _stackRightY;

        private static void EnsureStackFrame()
        {
            var e = Event.current;
            var t = e != null ? e.type : EventType.Ignore;
            if (Time.frameCount != _stackFrame || t != _stackEvt)
            {
                _stackFrame = Time.frameCount;
                _stackEvt = t;
                _stackLeftY = LeftColumnStartY;
                _stackRightY = RightColumnStartY;
            }
        }

        private static Vector2 StackNext(HudColumn column, float w, float h)
        {
            EnsureStackFrame();
            if (column == HudColumn.Right)
            {
                var p = new Vector2(ScreenW - w - 16f, _stackRightY);
                _stackRightY += h + StackGap;
                return p;
            }
            var l = new Vector2(16f, _stackLeftY);
            _stackLeftY += h + StackGap;
            return l;
        }

        /// <summary>Kolon istifine katilan suruklenebilir panel (onerilen).</summary>
        public static Rect Drag(ref Vector2 pos, ref bool dragging, HudColumn column, float w, float h, string prefsKey = null)
        {
            if (pos.x < -9000f && !string.IsNullOrEmpty(prefsKey) && PlayerPrefs.HasKey(prefsKey + "_x"))
                pos = new Vector2(PlayerPrefs.GetFloat(prefsKey + "_x"), PlayerPrefs.GetFloat(prefsKey + "_y"));

            bool userPlaced = pos.x > -9000f;
            // Tasinmamis panel istifi takip eder (ve istif slotunu yalnizca o zaman tuketir).
            Vector2 basePos = userPlaced ? pos : StackNextIf(column, w, h);
            return DragCore(ref pos, ref dragging, basePos, userPlaced, w, h, prefsKey);
        }

        private static Vector2 StackNextIf(HudColumn column, float w, float h)
        {
            if (column == HudColumn.Free) return new Vector2(16f, 16f);
            return StackNext(column, w, h);
        }

        /// <summary>
        /// Sabit varsayilan konumlu suruklenebilir panel (demo/ozet gibi ortalananlar icin).
        /// </summary>
        public static Rect Drag(ref Vector2 pos, ref bool dragging, Vector2 def, float w, float h, string prefsKey = null)
        {
            if (pos.x < -9000f && !string.IsNullOrEmpty(prefsKey) && PlayerPrefs.HasKey(prefsKey + "_x"))
                pos = new Vector2(PlayerPrefs.GetFloat(prefsKey + "_x"), PlayerPrefs.GetFloat(prefsKey + "_y"));
            bool userPlaced = pos.x > -9000f;
            Vector2 basePos = userPlaced ? pos : def;
            return DragCore(ref pos, ref dragging, basePos, userPlaced, w, h, prefsKey);
        }

        private static Rect DragCore(ref Vector2 pos, ref bool dragging, Vector2 basePos, bool userPlaced, float w, float h, string prefsKey)
        {
            var e = Event.current;
            var titleBar = new Rect(basePos.x, basePos.y, w, 28f);
            if (e != null)
            {
                if (e.type == EventType.MouseDown && e.button == 0 && titleBar.Contains(e.mousePosition))
                {
                    dragging = true;
                    pos = basePos;   // istif/varsayilan konumu kilitle, buradan surukle
                    e.Use();
                }
                else if (e.rawType == EventType.MouseUp && e.button == 0)
                {
                    // rawType: fare pencere DISINDA birakilsa da MouseUp yakalanir.
                    if (dragging && !string.IsNullOrEmpty(prefsKey))
                    {
                        PlayerPrefs.SetFloat(prefsKey + "_x", pos.x);
                        PlayerPrefs.SetFloat(prefsKey + "_y", pos.y);
                    }
                    dragging = false;
                }
                else if (dragging && e.type == EventType.MouseDrag) { pos += e.delta; e.Use(); }
            }

            if (dragging || userPlaced)
            {
                pos.x = Mathf.Clamp(pos.x, 0f, Mathf.Max(0f, ScreenW - w));
                pos.y = Mathf.Clamp(pos.y, 0f, Mathf.Max(0f, ScreenH - h));
                basePos = pos;
            }
            else
            {
                basePos.x = Mathf.Clamp(basePos.x, 0f, Mathf.Max(0f, ScreenW - w));
                basePos.y = Mathf.Clamp(basePos.y, 0f, Mathf.Max(0f, ScreenH - h));
            }
            return new Rect(basePos.x, basePos.y, w, h);
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
