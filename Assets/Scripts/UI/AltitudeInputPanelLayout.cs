using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.UI
{
    /// <summary>
    /// Waypoint Altitude alanini kontrol paneli icinde konumlandirir; daha buyuk ve okunakli yapar.
    /// Bu scripti AltitudeInputField (MainControlPanel'in child'i) uzerine ekle.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class AltitudeInputPanelLayout : MonoBehaviour
    {
        [Header("Boyut (daha buyuk = daha okunakli)")]
        [SerializeField] private float width = 240f;
        [SerializeField] private float height = 52f;

        [Header("Panel icinde konum (soldan, ortada dikey)")]
        [SerializeField] private float offsetLeft = 16f;

        [Header("Yazi (label + input)")]
        [Tooltip("Child Text/InputField font boyutu (0 = degistirme)")]
        [SerializeField] private int fontSize = 20;
        [Tooltip("Input icindeki padding (sol, sag, ust, alt)")]
        [SerializeField] private int textPaddingLeft = 10;
        [SerializeField] private int textPaddingRight = 10;
        [SerializeField] private int textPaddingTop = 6;
        [SerializeField] private int textPaddingBottom = 6;

        private void Awake()
        {
            ApplyLayout();
        }

        public void ApplyLayout()
        {
            var rt = transform as RectTransform;
            if (rt == null) return;

            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(offsetLeft + width * 0.5f, 0f);
            rt.sizeDelta = new Vector2(width, height);

            if (fontSize > 0)
            {
                foreach (var text in GetComponentsInChildren<Text>(true))
                {
                    text.fontSize = fontSize;
                    text.resizeTextForBestFit = false;
                }
                var input = GetComponentInChildren<InputField>(true);
                if (input != null)
                {
                    if (input.textComponent != null)
                        input.textComponent.fontSize = fontSize;
                    if (input.placeholder != null && input.placeholder is Text phText)
                        phText.fontSize = fontSize;
                    var textRt = input.GetComponent<RectTransform>();
                    if (textRt != null)
                    {
                        textRt.offsetMin = new Vector2(textPaddingLeft, textPaddingBottom);
                        textRt.offsetMax = new Vector2(-textPaddingRight, -textPaddingTop);
                    }
                }
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Apply Layout Now")]
        private void EditorApplyLayout()
        {
            ApplyLayout();
        }
#endif
    }
}
