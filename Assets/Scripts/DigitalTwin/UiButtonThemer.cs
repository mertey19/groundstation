using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// Sahnedeki tum Unity UI Button'larini ortak temaya ceker: yuvarlatilmis koyu "cam"
    /// arka plan, mavi accent hover/pressed gecisi, acik renk kalin metin.
    /// TwinHudTheme paleti ile uyumlu — boylece eski butonlar (Uydu, +/-, Hiz, Baslat...)
    /// yeni HUD panelleriyle ayni gorunume kavusur.
    ///
    /// OnGUI panellerim (yorunge/mesh/demo) Button olmadigi icin etkilenmez.
    /// Play'de Start'ta otomatik uygular; dinamik eklenen butonlar icin periyodik tekrarlar.
    /// </summary>
    public class UiButtonThemer : MonoBehaviour
    {
        [Header("Davranis")]
        [SerializeField] private bool applyOnStart = true;
        [SerializeField] private bool reapplyPeriodically = true;
        [SerializeField] private float reapplyInterval = 2f;
        [SerializeField] private int cornerRadius = 10;

        [Header("Renkler")]
        [SerializeField] private Color normal = new Color(0.10f, 0.13f, 0.18f, 0.94f);
        [SerializeField] private Color highlighted = new Color(0.20f, 0.46f, 0.72f, 0.98f);
        [SerializeField] private Color pressed = new Color(0.14f, 0.34f, 0.55f, 1f);
        [SerializeField] private Color textColor = new Color(0.94f, 0.97f, 1f);

        private Sprite _rounded;
        private float _nextReapply;
        // Bir kez temalanan butonlar atlanir: periyodik taramada GetComponentsInChildren
        // + reflection maliyeti yalnizca YENI butonlara oder (GC spike onleme).
        private readonly HashSet<int> _themedIds = new HashSet<int>();
        private static System.Type _tmpTextType;
        private static System.Reflection.PropertyInfo _tmpColorProp;
        private static bool _tmpResolved;

        private void Start()
        {
            if (applyOnStart) ApplyAll();
        }

        private void Update()
        {
            if (!reapplyPeriodically) return;
            if (Time.unscaledTime < _nextReapply) return;
            _nextReapply = Time.unscaledTime + Mathf.Max(0.5f, reapplyInterval);
            ApplyAll();
        }

        [ContextMenu("Apply To All Buttons")]
        public void ApplyAll()
        {
            EnsureSprite();
            var buttons = FindObjectsOfType<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
                Theme(buttons[i]);
        }

        private void Theme(Button b)
        {
            if (b == null) return;
            if (!_themedIds.Add(b.GetInstanceID())) return;   // zaten temalandi

            var img = b.targetGraphic as Image;
            if (img == null) img = b.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = _rounded;
                img.type = Image.Type.Sliced;
                img.color = Color.white;          // renk artik ColorBlock'tan gelir
                img.pixelsPerUnitMultiplier = 1f;
            }

            b.transition = Selectable.Transition.ColorTint;
            var cb = b.colors;
            cb.normalColor = normal;
            cb.highlightedColor = highlighted;
            cb.pressedColor = pressed;
            cb.selectedColor = normal;
            cb.disabledColor = new Color(0.18f, 0.20f, 0.24f, 0.55f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.12f;
            b.colors = cb;

            var txt = b.GetComponentInChildren<Text>(true);
            if (txt != null)
            {
                txt.color = textColor;
                if (txt.fontStyle == FontStyle.Normal) txt.fontStyle = FontStyle.Bold;
            }

            // TextMeshPro (varsa) reflection ile renklendir — TMP derleme bagimliligi olmadan.
            // Tip ve property BIR KEZ cozulur (buton basina tip taramasi/boxing yok).
            if (!_tmpResolved)
            {
                _tmpResolved = true;
                _tmpTextType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
                if (_tmpTextType != null)
                    _tmpColorProp = _tmpTextType.GetProperty("color", BindingFlags.Instance | BindingFlags.Public);
            }
            if (_tmpTextType != null && _tmpColorProp != null)
            {
                var tmp = b.GetComponentInChildren(_tmpTextType, true);
                if (tmp != null && _tmpColorProp.CanWrite)
                    _tmpColorProp.SetValue(tmp, textColor);
            }
        }

        private void OnDestroy()
        {
            if (_rounded != null)
            {
                if (_rounded.texture != null) Destroy(_rounded.texture);
                Destroy(_rounded);
            }
        }

        private void EnsureSprite()
        {
            if (_rounded != null) return;
            int s = 48;
            int r = Mathf.Clamp(cornerRadius, 2, s / 2);
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, RoundedAlpha(x, y, s, s, r)));
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            _rounded = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        }

        private static float RoundedAlpha(int x, int y, int w, int h, int r)
        {
            float cx = Mathf.Clamp(x, r, w - 1 - r);
            float cy = Mathf.Clamp(y, r, h - 1 - r);
            float dx = x - cx, dy = y - cy;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01(r - d + 0.5f);
        }
    }
}
