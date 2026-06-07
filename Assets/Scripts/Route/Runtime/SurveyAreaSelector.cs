using UnityEngine;
using UnityEngine.EventSystems;
using Mapbox.Unity.Map;
using GroundStation.Inputs;
using System.Collections.Generic;
using UnityEngine.UI;
using System.Text;

namespace GroundStation.Routes
{
    /// <summary>
    /// Minimal alan secimi: kullanici haritaya iki nokta tiklar, dikdortgen survey alanini belirler.
    /// </summary>
    public class SurveyAreaSelector : MonoBehaviour
    {
        private enum SelectionMode { Rectangle, Polygon }

        [SerializeField] private Camera mapCamera;
        [SerializeField] private AbstractMap abstractMap;
        [SerializeField] private SurveyMissionPlanner surveyPlanner;
        [SerializeField] private MapClickSpawner mapClickSpawner;
        [SerializeField] private bool showSelectionRectangle = true;
        [Header("Debug")]
        [SerializeField] private bool showRuntimeDebugStatus = true;

        [Header("Preview Style")]
        [SerializeField] private float previewLineWidth = 0.35f;
        [SerializeField] private float minPreviewLineWidth = 0.20f;
        [SerializeField] private float maxPreviewLineWidth = 0.65f;
        [SerializeField] private float previewWidthPerCameraHeight = 0.0015f;
        [SerializeField] private bool forceFixedPreviewWidth = true;
        [SerializeField] private float fixedPreviewLineWidth = 8f;
        [SerializeField] private float previewLiftY = 3f;
        [SerializeField] private Color previewBorderColor = new Color(1f, 1f, 1f, 1f);
        [SerializeField] private Color previewFillColor = new Color(1f, 0.12f, 0.12f, 0.30f);

        private bool _isSelecting;
        private SelectionMode _mode = SelectionMode.Rectangle;
        private bool _hasFirst;
        private Vector3 _firstPoint;
        private LineRenderer _preview;
        private MeshFilter _fillMeshFilter;
        private MeshRenderer _fillMeshRenderer;
        private readonly List<Vector3> _polygonPoints = new List<Vector3>();
        private Text _debugText;
        private string _lastStatus = "Idle";

        // UI butonuna tiklayinca ayni kare icinde map'in de sol tikini yakalarsa ekstra nokta ekleniyor.
        // Bu bayrak bir sonraki sol tiklamayi kisa sureyle engeller.
        private float _suppressNextLeftClickUntil;

        private void Awake()
        {
            if (mapCamera == null) mapCamera = Camera.main;
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (surveyPlanner == null) surveyPlanner = FindObjectOfType<SurveyMissionPlanner>();
            if (mapClickSpawner == null) mapClickSpawner = FindObjectOfType<MapClickSpawner>();
            EnsurePreviewLine();
            EnsureDebugStatusText();
        }

        public void BeginAreaSelection()
        {
            _mode = SelectionMode.Rectangle;
            _isSelecting = true;
            _hasFirst = false;
            if (mapClickSpawner != null) mapClickSpawner.enabled = false;
            if (_preview != null) _preview.positionCount = 0;
            ClearFill();
            _lastStatus = "Rectangle selection started";
            Debug.Log("[SurveyAreaSelector] Rectangle selection started.");
        }

        public void BeginPolygonSelection()
        {
            _mode = SelectionMode.Polygon;
            _isSelecting = true;
            _hasFirst = false;
            _polygonPoints.Clear();
            if (mapClickSpawner != null) mapClickSpawner.enabled = false;
            if (_preview != null) { _preview.positionCount = 0; _preview.loop = false; }
            ClearFill();
            _lastStatus = "Polygon selection started";
            Debug.Log("[SurveyAreaSelector] Polygon selection started.");
        }

        public void FinishPolygonSelection()
        {
            if (_mode != SelectionMode.Polygon) return;
            if (_polygonPoints.Count < 3)
            {
                _lastStatus = "Polygon finish failed (<3 points)";
                Debug.LogWarning("[SurveyAreaSelector] Polygon finish failed: need at least 3 points.");
                CancelAreaSelection();
                return;
            }
            if (surveyPlanner != null)
                surveyPlanner.SetSurveyPolygon(_polygonPoints);
            if (_preview != null)
            {
                _preview.loop = true;
                DrawPreviewPolygon(_polygonPoints);
            }
            _isSelecting = false;
            _hasFirst = false;
            if (mapClickSpawner != null) mapClickSpawner.enabled = true;
            _lastStatus = "Polygon finished";
            Debug.Log($"[SurveyAreaSelector] Polygon finished. Points={_polygonPoints.Count}");
        }

        public void CancelAreaSelection()
        {
            _isSelecting = false;
            _hasFirst = false;
            _polygonPoints.Clear();
            if (mapClickSpawner != null) mapClickSpawner.enabled = true;
            if (_preview != null) { _preview.positionCount = 0; _preview.loop = false; }
            ClearFill();
            _lastStatus = "Selection canceled";
        }

        private void Update()
        {
            UpdateDebugStatusText();
            UpdatePreviewWidthByCamera();
            if (!_isSelecting) return;
            EnsureRuntimeRefs();
            if (mapCamera == null || abstractMap == null || abstractMap.Root == null)
            {
                _lastStatus = "Missing refs (camera/map/root)";
                return;
            }

            if (Time.unscaledTime < _suppressNextLeftClickUntil)
            {
                if (Input.GetMouseButtonDown(0))
                    _lastStatus = "Map click suppressed (UI action)";
                return;
            }

            // Polygon mode: sag tik ile bitir.
            if (_mode == SelectionMode.Polygon && Input.GetMouseButtonDown(1))
            {
                FinishPolygonSelection();
                return;
            }

            if (!Input.GetMouseButtonDown(0)) return;
            // Buton / panel'e tik atinca ayni kare icinde haritaya nokta eklenmesin.
            // Unity input akisi Update -> UI events seklinde olabildigi icin suppress bazen gec kalabiliyor.
            // Bu yuzden seçim acikken UI uzerini her zaman engelliyoruz.
            if (IsPointerOverUI())
            {
                _lastStatus = "Click blocked by UI (panel)";
                return;
            }

            Ray ray = mapCamera.ScreenPointToRay(Input.mousePosition);
            if (!TryGetMapWorldClick(ray, out Vector3 p))
            {
                _lastStatus = "Map click raycast failed";
                return;
            }

            if (_mode == SelectionMode.Polygon)
            {
                _polygonPoints.Add(p);
                DrawPreviewPolygon(_polygonPoints);
                _lastStatus = $"Polygon point added ({_polygonPoints.Count})";
                return;
            }

            if (!_hasFirst)
            {
                _firstPoint = p;
                _hasFirst = true;
                DrawPreviewRect(_firstPoint, _firstPoint);
                _lastStatus = "Rectangle first point selected";
                return;
            }

            float minX = Mathf.Min(_firstPoint.x, p.x);
            float maxX = Mathf.Max(_firstPoint.x, p.x);
            float minZ = Mathf.Min(_firstPoint.z, p.z);
            float maxZ = Mathf.Max(_firstPoint.z, p.z);

            if (surveyPlanner != null)
                surveyPlanner.SetSurveyArea(minX, maxX, minZ, maxZ);
            DrawPreviewRect(_firstPoint, p);
            _isSelecting = false;
            _hasFirst = false;
            if (mapClickSpawner != null) mapClickSpawner.enabled = true;
            _lastStatus = "Rectangle selection completed";
        }

        private void EnsurePreviewLine()
        {
            if (!showSelectionRectangle) return;
            var go = new GameObject("SurveyAreaPreview");
            go.transform.SetParent(transform, false);
            _preview = go.AddComponent<LineRenderer>();
            _preview.useWorldSpace = true;
            _preview.loop = true;
            _preview.positionCount = 0;
            float finalWidth = previewLineWidth;
            _preview.startWidth = finalWidth;
            _preview.endWidth = finalWidth;
            _preview.numCornerVertices = 2;
            _preview.numCapVertices = 0;

            // Unlit renk: zayif aydinlatma/terrain ile gozulmesini azaltir.
            var mat = new Material(Shader.Find("Unlit/Color"));
            _preview.material = mat;

            _preview.startColor = previewBorderColor;
            _preview.endColor = _preview.startColor;

            var fillGo = new GameObject("SurveyAreaFill");
            fillGo.transform.SetParent(transform, false);
            _fillMeshFilter = fillGo.AddComponent<MeshFilter>();
            _fillMeshRenderer = fillGo.AddComponent<MeshRenderer>();
            var fillMat = new Material(Shader.Find("Unlit/Color"));
            fillMat.color = previewFillColor;
            _fillMeshRenderer.material = fillMat;
        }

        private void UpdatePreviewWidthByCamera()
        {
            if (_preview == null) return;
            float width;
            if (forceFixedPreviewWidth)
            {
                width = Mathf.Max(0.01f, fixedPreviewLineWidth);
            }
            else
            {
                width = previewLineWidth;
                if (mapCamera != null)
                {
                    width = mapCamera.transform.position.y * previewWidthPerCameraHeight;
                }
                width = Mathf.Clamp(width, minPreviewLineWidth, maxPreviewLineWidth);
            }
            _preview.startWidth = width;
            _preview.endWidth = width;
        }

        private void EnsureRuntimeRefs()
        {
            if (mapCamera == null) mapCamera = Camera.main;
            if (mapCamera == null)
            {
                var anyCamera = FindObjectOfType<Camera>();
                if (anyCamera != null) mapCamera = anyCamera;
            }
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (surveyPlanner == null) surveyPlanner = FindObjectOfType<SurveyMissionPlanner>();
            if (mapClickSpawner == null) mapClickSpawner = FindObjectOfType<MapClickSpawner>();
        }

        private bool IsPointerOverUI()
        {
            if (EventSystem.current == null) return false;

            // En deterministik yontem: ayni frame'de UI icin raycast yap.
            // Bu sayede EventSystem state'inin Update sirasi yuzunden gec kalmasi engellenir.
            if (Input.touchCount <= 0)
            {
                var ped = new PointerEventData(EventSystem.current)
                {
                    position = Input.mousePosition
                };
                var results = new List<RaycastResult>(8);
                EventSystem.current.RaycastAll(ped, results);
                return results.Count > 0;
            }

            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                var ped = new PointerEventData(EventSystem.current)
                {
                    position = touch.position,
                };
                var results = new List<RaycastResult>(8);
                EventSystem.current.RaycastAll(ped, results);
                if (results.Count > 0) return true;
            }

            return false;
        }

        private bool TryGetMapWorldClick(Ray ray, out Vector3 worldPoint)
        {
            worldPoint = default;

            // 1) Tercih: map tile collider'ina vur.
            if (Physics.Raycast(ray, out RaycastHit hit, 100000f))
            {
                worldPoint = hit.point;
                return true;
            }

            // 2) Fallback: map root duzlemine projekte et.
            Plane mapPlane = new Plane(Vector3.up, abstractMap.Root.position);
            if (!mapPlane.Raycast(ray, out float enter)) return false;
            worldPoint = ray.GetPoint(enter);
            return true;
        }

        private void DrawPreviewRect(Vector3 a, Vector3 b)
        {
            if (_preview == null) return;
            // Rect preview: tiklanan noktalarin y degerini temel alip biraz yukari kaldiralim.
            float y = ((a.y + b.y) * 0.5f) + previewLiftY;
            Vector3 p0 = new Vector3(Mathf.Min(a.x, b.x), y, Mathf.Min(a.z, b.z));
            Vector3 p1 = new Vector3(Mathf.Max(a.x, b.x), y, Mathf.Min(a.z, b.z));
            Vector3 p2 = new Vector3(Mathf.Max(a.x, b.x), y, Mathf.Max(a.z, b.z));
            Vector3 p3 = new Vector3(Mathf.Min(a.x, b.x), y, Mathf.Max(a.z, b.z));
            _preview.positionCount = 4;
            _preview.SetPosition(0, p0);
            _preview.SetPosition(1, p1);
            _preview.SetPosition(2, p2);
            _preview.SetPosition(3, p3);

            DrawFillFromVertices(new List<Vector3> { p0, p1, p2, p3 });
        }

        private void DrawPreviewPolygon(List<Vector3> points)
        {
            if (_preview == null) return;
            if (points == null || points.Count == 0) { _preview.positionCount = 0; ClearFill(); return; }
            _preview.positionCount = points.Count;

            // 3 noktadan itibaren polygonu kapali goster (loop = true).
            _preview.loop = points.Count >= 3;

            for (int i = 0; i < points.Count; i++)
            {
                // Terrain uzerinde kalsin diye tiklanan noktanin y degerini kullan.
                _preview.SetPosition(i, new Vector3(points[i].x, points[i].y + previewLiftY, points[i].z));
            }

            if (points.Count >= 3)
            {
                var lifted = new List<Vector3>(points.Count);
                for (int i = 0; i < points.Count; i++)
                    lifted.Add(new Vector3(points[i].x, points[i].y + (previewLiftY * 0.5f), points[i].z));
                DrawFillFromVertices(lifted);
            }
            else
            {
                ClearFill();
            }
        }

        private void DrawFillFromVertices(List<Vector3> verts)
        {
            if (_fillMeshFilter == null || verts == null || verts.Count < 3) return;

            int[] tris = TriangulateXZ(verts);
            if (tris == null || tris.Length < 3) return;

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            _fillMeshFilter.sharedMesh = mesh;
            if (_fillMeshRenderer != null) _fillMeshRenderer.enabled = true;
        }

        private void ClearFill()
        {
            if (_fillMeshFilter != null)
                _fillMeshFilter.sharedMesh = null;
            if (_fillMeshRenderer != null)
                _fillMeshRenderer.enabled = false;
        }

        // Ear clipping triangulation on XZ plane.
        private static int[] TriangulateXZ(List<Vector3> input)
        {
            int n = input.Count;
            if (n < 3) return null;

            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);

            if (SignedAreaXZ(input) < 0f) idx.Reverse();

            var triangles = new List<int>((n - 2) * 3);
            int guard = 0;
            while (idx.Count > 2 && guard < 4096)
            {
                guard++;
                bool earFound = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i - 1 + idx.Count) % idx.Count];
                    int i1 = idx[i];
                    int i2 = idx[(i + 1) % idx.Count];

                    if (!IsConvexXZ(input[i0], input[i1], input[i2])) continue;
                    if (AnyPointInsideTriangleXZ(input, idx, i0, i1, i2)) continue;

                    triangles.Add(i0);
                    triangles.Add(i1);
                    triangles.Add(i2);
                    idx.RemoveAt(i);
                    earFound = true;
                    break;
                }

                if (!earFound)
                    break;
            }

            return triangles.ToArray();
        }

        private static float SignedAreaXZ(List<Vector3> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++)
            {
                int j = (i + 1) % p.Count;
                a += p[i].x * p[j].z - p[j].x * p[i].z;
            }
            return 0.5f * a;
        }

        private static bool IsConvexXZ(Vector3 a, Vector3 b, Vector3 c)
        {
            float cross = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
            return cross > 0.0001f;
        }

        private static bool AnyPointInsideTriangleXZ(List<Vector3> points, List<int> active, int a, int b, int c)
        {
            Vector2 A = new Vector2(points[a].x, points[a].z);
            Vector2 B = new Vector2(points[b].x, points[b].z);
            Vector2 C = new Vector2(points[c].x, points[c].z);

            for (int i = 0; i < active.Count; i++)
            {
                int p = active[i];
                if (p == a || p == b || p == c) continue;
                Vector2 P = new Vector2(points[p].x, points[p].z);
                if (PointInTriangle2D(P, A, B, C))
                    return true;
            }
            return false;
        }

        private static bool PointInTriangle2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float s1 = Sign2D(p, a, b);
            float s2 = Sign2D(p, b, c);
            float s3 = Sign2D(p, c, a);
            bool hasNeg = (s1 < 0f) || (s2 < 0f) || (s3 < 0f);
            bool hasPos = (s1 > 0f) || (s2 > 0f) || (s3 > 0f);
            return !(hasNeg && hasPos);
        }

        private static float Sign2D(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        /// <summary>
        /// UI butonlarina tiklandiktan hemen sonra map tarafinda sol tik yakalanmasini engeller.
        /// </summary>
        public void SuppressNextLeftClick(float seconds = 0.25f)
        {
            _suppressNextLeftClickUntil = Time.unscaledTime + Mathf.Max(0.01f, seconds);
        }

        private void EnsureDebugStatusText()
        {
            if (!showRuntimeDebugStatus || _debugText != null) return;
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return;

            var go = new GameObject("SurveySelectorDebug", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(canvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(20f, -250f);
            rt.sizeDelta = new Vector2(560f, 110f);

            _debugText = go.GetComponent<Text>();
            _debugText.fontSize = 18;
            _debugText.fontStyle = FontStyle.Bold;
            _debugText.color = new Color(1f, 0.95f, 0.2f, 1f);
            _debugText.alignment = TextAnchor.UpperLeft;
            _debugText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _debugText.verticalOverflow = VerticalWrapMode.Overflow;
            try { _debugText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            _debugText.raycastTarget = false;
        }

        private void UpdateDebugStatusText()
        {
            if (!showRuntimeDebugStatus) return;
            if (_debugText == null) EnsureDebugStatusText();
            if (_debugText == null) return;

            var sb = new StringBuilder(200);
            sb.Append("Survey Select | ");
            sb.Append(_isSelecting ? "ON" : "OFF");
            sb.Append(" | Mode: ").Append(_mode);
            sb.Append(" | Pts: ").Append(_polygonPoints.Count);
            sb.Append(" | Last: ").Append(_lastStatus);
            _debugText.text = sb.ToString();
        }
    }
}
