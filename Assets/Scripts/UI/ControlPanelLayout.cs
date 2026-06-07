using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.UI
{
    [RequireComponent(typeof(RectTransform))]
    public class ControlPanelLayout : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private float spacing = 14f;
        [SerializeField] private int paddingLeft = 14;
        [SerializeField] private int paddingTop = 10;
        [SerializeField] private int paddingRight = 14;
        [SerializeField] private int paddingBottom = 10;
        [SerializeField] private float childHeight = 62f;
        [SerializeField] private float minButtonWidth = 150f;
        [SerializeField] private bool expandChildren = true;
        [SerializeField] private bool fitToContent = false;
        [SerializeField] private bool autoFitButtonWidth = true;

        [Header("Text")]
        [SerializeField] private int buttonFontSize = 26;
        [SerializeField] private bool forceBoldText = true;

        private void Awake()
        {
            ApplyLayout();
        }

        private void OnEnable()
        {
            ApplyLayout();
        }

        private void ApplyLayout()
        {
            var rt = transform as RectTransform;
            if (rt == null) return;

            var hlg = GetComponent<HorizontalLayoutGroup>();
            if (hlg == null)
                hlg = gameObject.AddComponent<HorizontalLayoutGroup>();

            hlg.spacing = spacing;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlHeight = true;
            hlg.childControlWidth = expandChildren;
            hlg.childForceExpandHeight = childHeight <= 0;
            hlg.childForceExpandWidth = expandChildren;
            hlg.padding = new RectOffset(paddingLeft, paddingTop, paddingRight, paddingBottom);

            if (childHeight > 0 || minButtonWidth > 0)
            {
                if (childHeight > 0)
                    hlg.childForceExpandHeight = false;
                int childCount = transform.childCount;
                float computedWidth = minButtonWidth;
                if (autoFitButtonWidth && childCount > 0)
                {
                    float innerWidth = rt.rect.width - paddingLeft - paddingRight - spacing * Mathf.Max(0, childCount - 1);
                    if (innerWidth > 1f)
                        computedWidth = Mathf.Max(minButtonWidth, innerWidth / childCount);
                }
                foreach (Transform child in transform)
                {
                    var crt = child as RectTransform;
                    if (crt == null) continue;

                    if (childHeight > 0)
                        crt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, childHeight);

                    if (minButtonWidth > 0f)
                    {
                        var le = child.GetComponent<LayoutElement>();
                        if (le == null) le = child.gameObject.AddComponent<LayoutElement>();
                        le.minWidth = computedWidth;
                        le.preferredWidth = computedWidth;
                        le.flexibleWidth = 0f;
                    }
                }
            }

            if (buttonFontSize > 0)
            {
                foreach (var btn in GetComponentsInChildren<Button>(true))
                {
                    var text = btn.GetComponentInChildren<Text>(true);
                    if (text != null)
                    {
                        text.fontSize = buttonFontSize;
                        text.resizeTextForBestFit = false;
                        text.alignment = TextAnchor.MiddleCenter;
                        if (forceBoldText) text.fontStyle = FontStyle.Bold;
                    }
                }
            }

            if (fitToContent)
            {
                var csf = GetComponent<ContentSizeFitter>();
                if (csf == null) csf = gameObject.AddComponent<ContentSizeFitter>();
                csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
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
