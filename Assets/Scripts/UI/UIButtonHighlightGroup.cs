using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.UI
{
    public class UIButtonHighlightGroup : MonoBehaviour
    {
        [SerializeField] private Color normalColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        [SerializeField] private Color selectedColor = new Color(0.1f, 0.5f, 0.9f, 0.9f);
        [SerializeField] private Color normalTextColor = Color.white;
        [SerializeField] private Color selectedTextColor = Color.white;

        private Button[] _buttons;

        private void Awake()
        {
            _buttons = GetComponentsInChildren<Button>(true);
        }

        public void Select(Button clicked)
        {
            if (_buttons == null) return;

            foreach (var btn in _buttons)
            {
                bool isSelected = (btn == clicked);

                UnityEngine.UI.Image img = btn.GetComponent<UnityEngine.UI.Image>();
                UnityEngine.UI.Text txt = btn.GetComponentInChildren<UnityEngine.UI.Text>();

                if (img != null)
                    img.color = isSelected ? selectedColor : normalColor;

                if (txt != null)
                    txt.color = isSelected ? selectedTextColor : normalTextColor;
            }
        }
    }
}