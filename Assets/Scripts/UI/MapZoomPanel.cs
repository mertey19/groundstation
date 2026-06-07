using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.UI
{
    /// <summary>
    /// Sag ustteki + / - butonlariyla haritayi zoom eder.
    /// Bu scripti MapZoomPanel'e ekleyin ve iki buton atayin.
    /// </summary>
    public class MapZoomPanel : MonoBehaviour
    {
        [SerializeField] private MapCameraController mapCameraController;
        [SerializeField] private Button zoomInButton;
        [SerializeField] private Button zoomOutButton;

        private void Awake()
        {
            if (mapCameraController == null)
                mapCameraController = FindObjectOfType<MapCameraController>();

            if (zoomInButton != null)
            {
                SetButtonLabel(zoomInButton, "+");
                zoomInButton.onClick.AddListener(OnZoomInClicked);
            }
            if (zoomOutButton != null)
            {
                SetButtonLabel(zoomOutButton, "-");
                zoomOutButton.onClick.AddListener(OnZoomOutClicked);
            }
        }

        private void OnZoomInClicked()
        {
            if (mapCameraController != null)
                mapCameraController.ZoomIn();
        }

        private void OnZoomOutClicked()
        {
            if (mapCameraController != null)
                mapCameraController.ZoomOut();
        }

        private static void SetButtonLabel(Button button, string label)
        {
            var txt = button.GetComponentInChildren<Text>(true);
            if (txt == null) return;
            txt.text = label;
            txt.fontSize = 34;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
        }
    }
}
