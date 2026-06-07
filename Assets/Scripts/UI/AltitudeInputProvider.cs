using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.UI
{
    /// <summary>
    /// Waypoint eklenirken kullanilacak yukseklik degerini UI InputField'dan okur.
    /// MapClickSpawner bu component'e referans verir; bos/gecersiz input'ta defaultAltitude kullanilir.
    /// </summary>
    public class AltitudeInputProvider : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private InputField altitudeInputField;
        [Tooltip("Input bos veya gecersizse kullanilacak yukseklik (m).")]
        [SerializeField] private float defaultAltitude = 10f;
        [Tooltip("Kabul edilen min/max yukseklik (m).")]
        [SerializeField] private float minAltitude = 0f;
        [SerializeField] private float maxAltitude = 500f;

        private void Awake()
        {
            if (altitudeInputField == null)
                altitudeInputField = GetComponent<InputField>();
            if (altitudeInputField == null)
                altitudeInputField = GetComponentInChildren<InputField>();
        }

        /// <summary>
        /// Su anki altitude degerini doner (input'tan parse veya default). Her zaman [minAltitude, maxAltitude] araliginda.
        /// </summary>
        public float GetAltitude()
        {
            if (altitudeInputField == null)
                altitudeInputField = GetComponentInChildren<InputField>();
            if (altitudeInputField == null || string.IsNullOrWhiteSpace(altitudeInputField.text))
                return Mathf.Clamp(defaultAltitude, minAltitude, maxAltitude);

            if (float.TryParse(altitudeInputField.text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                return Mathf.Clamp(parsed, minAltitude, maxAltitude);

            return Mathf.Clamp(defaultAltitude, minAltitude, maxAltitude);
        }

        /// <summary>
        /// InputField'a varsayilan degeri yazar (opsiyonel, waypoint eklemeden once cagrilabilir).
        /// </summary>
        public void SetDefaultInField()
        {
            if (altitudeInputField != null)
                altitudeInputField.text = defaultAltitude.ToString("F0");
        }
    }
}
