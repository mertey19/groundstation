using System;
using System.IO;
using UnityEngine;

namespace GroundStation.Routes
{
    /// <summary>
    /// Rota dışa aktarma: JSON string, dosya, ileride ağ üzerinden gönderim için hazır.
    /// RouteSerializer kullanır; MonoBehaviour olabilir (Inspector'tan tetiklemek için).
    /// </summary>
    public class RouteExporter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RouteManager routeManager;

        [Header("Export Settings")]
        [SerializeField] private bool prettyPrint = true;
        [SerializeField] private string defaultFileName = "route_export.json";

        private void Awake()
        {
            if (routeManager == null)
                routeManager = GetComponent<RouteManager>();
        }

        /// <summary>
        /// Bağlı RouteManager'ın rotasını JSON string olarak döner.
        /// </summary>
        public string ExportToJson()
        {
            var data = GetRouteData();
            return data == null ? "{}" : RouteSerializer.ToJson(data, prettyPrint);
        }

        /// <summary>
        /// Verilen RouteData'yı JSON string olarak döner.
        /// </summary>
        public string ExportToJson(RouteData data)
        {
            return RouteSerializer.ToJson(data ?? GetRouteData(), prettyPrint);
        }

        /// <summary>
        /// Rota JSON'ını StreamingAssets veya persistentDataPath'e yazar.
        /// </summary>
        /// <param name="fileName">Dosya adı (örn. route_export.json)</param>
        /// <param name="usePersistentDataPath">true = persistentDataPath, false = StreamingAssets</param>
        public string ExportToFile(string fileName = null, bool usePersistentDataPath = true)
        {
            var data = GetRouteData();
            if (data == null)
            {
                Debug.LogWarning("[RouteExporter] No route data to export.");
                return null;
            }

            string name = string.IsNullOrEmpty(fileName) ? defaultFileName : fileName;
            string dir = usePersistentDataPath ? Application.persistentDataPath : Application.streamingAssetsPath;
            string path = Path.Combine(dir, name);
            string json = RouteSerializer.ToJson(data, prettyPrint);

            try
            {
                File.WriteAllText(path, json);
                Debug.Log($"[RouteExporter] Exported to {path}");
                return path;
            }
            catch (Exception e)
            {
                Debug.LogError($"[RouteExporter] Export failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// JSON string'i panoya kopyalar (editor ortamında).
        /// </summary>
        public void CopyJsonToClipboard()
        {
            string json = ExportToJson();
#if UNITY_EDITOR
            UnityEditor.EditorGUIUtility.systemCopyBuffer = json;
            Debug.Log("[RouteExporter] JSON copied to clipboard.");
#else
            Debug.Log("[RouteExporter] Clipboard not available in build. Use ExportToFile or network.");
#endif
        }

        private RouteData GetRouteData()
        {
            return routeManager != null ? routeManager.GetRouteData() : null;
        }
    }
}
