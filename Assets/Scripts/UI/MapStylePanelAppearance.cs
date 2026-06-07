using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.UI
{
    /// <summary>
    /// Map Style Panel (Uydu / Dijital ikiz / Sokak) butonlarina okunakli isimler ve guzel gorunum uygular.
    /// Bu scripti MapStylePanel veya ust bar objesine ekleyin. Butonlar panelin child'i olmali (sira: Uydu, Dijital ikiz, Sokak).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class MapStylePanelAppearance : MonoBehaviour
    {
        [Header("Buton etiketleri (sira onemli: 1=Uydu, 2=Dijital ikiz, 3=Sokak)")]
        [SerializeField] private string[] buttonLabels = new[] { "Uydu", "Dijital ikiz", "Sokak" };

        [Header("Okunaklilik")]
        [SerializeField] private int fontSize = 28;
        [SerializeField] private bool bold = true;
        [Tooltip("Buton yazi rengi (koyu = acik arka planda okunakli)")]
        [SerializeField] private Color textColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        [Tooltip("Buton arka plan rengi (acik gri)")]
        [SerializeField] private Color buttonColor = new Color(0.94f, 0.94f, 0.94f, 1f);

        private void Awake()
        {
            ApplyAppearance();
        }

        public void ApplyAppearance()
        {
            var buttons = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length && i < buttonLabels.Length; i++)
            {
                var btn = buttons[i];
                var text = btn.GetComponentInChildren<Text>(true);
                if (text != null)
                {
                    text.text = buttonLabels[i];
                    text.fontSize = fontSize;
                    text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
                    text.color = textColor;
                    text.alignment = TextAnchor.MiddleCenter;
                    text.horizontalOverflow = HorizontalWrapMode.Overflow;
                    text.verticalOverflow = VerticalWrapMode.Overflow;
                }

                var img = btn.GetComponent<Image>();
                if (img != null && img.color.a > 0.01f)
                    img.color = buttonColor;
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Apply Appearance Now")]
        private void EditorApply()
        {
            ApplyAppearance();
        }
#endif
    }
}
