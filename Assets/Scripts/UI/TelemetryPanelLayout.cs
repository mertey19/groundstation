using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.UI
{
    /// <summary>
    /// Telemetry panelinde satir hizasi ve metin duzenini saglar.
    /// - VerticalLayoutGroup ile satir araliklari ve hizalama.
    /// - Child Text bilesenlerinde alignment / overflow ayari (taşma önleme).
    /// Bu scripti TelemetryPanel GameObject'ine ekle (DroneTelemetryUI ile birlikte).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class TelemetryPanelLayout : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private float spacing = 6f;
        [SerializeField] private float paddingLeft = 10f;
        [SerializeField] private float paddingRight = 10f;
        [SerializeField] private float paddingTop = 10f;
        [SerializeField] private float paddingBottom = 10f;

        [Header("Text ayarlari (Runtime'da uygulanir)")]
        [SerializeField] private bool applyTextAlignment = true;

        private void Awake()
        {
            ApplyLayout();
        }

        private void ApplyLayout()
        {
            var rt = transform as RectTransform;
            if (rt == null) return;

            // VerticalLayoutGroup: satir araliklari ve ust-sol hizalama
            var vlg = GetComponent<VerticalLayoutGroup>();
            if (vlg == null)
                vlg = gameObject.AddComponent<VerticalLayoutGroup>();

            vlg.spacing = spacing;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.padding = new RectOffset((int)paddingLeft, (int)paddingTop, (int)paddingRight, (int)paddingBottom);

            if (!applyTextAlignment) return;

            foreach (Transform child in transform)
            {
                var text = child.GetComponent<Text>();
                if (text == null) continue;

                text.alignment = TextAnchor.MiddleLeft;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.lineSpacing = 1.1f;
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
