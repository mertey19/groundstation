using UnityEngine;
using Mapbox.Unity.Map;
using GroundStation.DigitalTwin;

namespace GroundStation.Map
{
    /// <summary>
    /// Harita saglik kontrolu. Eskiden yalnizca Console'a yazardi — sahada internet
    /// yoksa operator bos haritaya bakar, nedenini goremezdi. Artik sorun ekranda
    /// KIRMIZI banner olarak gosterilir (cevrimdisi / tile provider eksik / imagery yok)
    /// ve baglanti gelince kendiliginden kaybolur.
    /// Not: Mapbox tile'lari cevrimici cekilir; demo alaninda once tum zoom seviyeleri
    /// gezilerek FileCache isitilmali (cache'lenen bolge cevrimdisi da calisir).
    /// </summary>
    public class MapLoadChecker : MonoBehaviour
    {
        [SerializeField] private AbstractMap abstractMap;
        [SerializeField] private float checkDelay = 2f;
        [SerializeField] private float recheckInterval = 5f;
        [SerializeField] private bool showOnScreenWarning = true;

        private string _warning = "";
        private float _nextCheckAt;
        private GUIStyle _warnStyle;

        private void Start()
        {
            if (abstractMap == null) abstractMap = GetComponent<AbstractMap>();
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            _nextCheckAt = Time.unscaledTime + checkDelay;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextCheckAt) return;
            _nextCheckAt = Time.unscaledTime + Mathf.Max(1f, recheckInterval);
            Check();
        }

        private void Check()
        {
            _warning = "";
            if (abstractMap == null)
            {
                abstractMap = FindObjectOfType<AbstractMap>();
                if (abstractMap == null) return;
            }

            if (abstractMap.TileProvider == null)
            {
                _warning = "HARİTA: Tile Provider yok — Extent Type 'Range Around Center' veya 'Camera Bounds' olmalı.";
                Debug.LogWarning("[MapLoadChecker] " + _warning);
                return;
            }

            if (abstractMap.ImageLayer == null)
            {
                _warning = "HARİTA: Imagery katmanı yok — harita görüntüsü çıkmaz.";
                Debug.LogWarning("[MapLoadChecker] " + _warning);
                return;
            }

            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                _warning = "İNTERNET YOK — harita yalnızca önbellekteki bölgelerde görünür (çevrimdışı mod).";
                Debug.LogWarning("[MapLoadChecker] " + _warning);
            }
        }

        private void OnGUI()
        {
            if (!showOnScreenWarning || string.IsNullOrEmpty(_warning)) return;
            TwinHudTheme.BeginScaledHud();
            float w = Mathf.Min(680f, TwinHudTheme.ScreenW - 32f);
            var r = new Rect((TwinHudTheme.ScreenW - w) * 0.5f, 44f, w, 30f);
            TwinHudTheme.Fill(r, new Color(0.62f, 0.12f, 0.10f, 0.92f), 8f);
            if (_warnStyle == null)
                _warnStyle = new GUIStyle(TwinHudTheme.Label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            GUI.Label(r, _warning, _warnStyle);
            TwinHudTheme.EndScaledHud();
        }
    }
}
