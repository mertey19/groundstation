using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// digital-twin (Python fotogrametri) sahasini HTML twin.html'e BIREBIR yakin kurar:
    /// orthophoto zemin + 3B bina (yogunluk rengi) + agac + su yuzeyi + sis + gradient gok + golge.
    /// Mapbox'tan bagimsiz kendi koordinati. Twin paneli (DigitalTwin3DView RT) bizim sahneyi gosterir.
    /// Built-in RP, Standard shader.
    /// </summary>
    public class SimurghSiteImporter : MonoBehaviour
    {
        [Header("Kaynak")]
        [SerializeField] private string folder = "SimurghTwin";
        [SerializeField] private float metersPerUnit = 1f;

        [Header("Sahne")]
        [SerializeField] private bool spawnGround = true;
        [SerializeField] private bool spawnBuildings = true;
        [SerializeField] private bool spawnTrees = true;
        [Tooltip("3B su duzlemi. Aukerman'da HTML twin de su gostermiyor (orthophoto'da golet boyasi var) -> kapali. Sentetik/gercek su sahasi icin ac.")]
        [SerializeField] private bool spawnWater = false;
        [SerializeField] private float heightScale = 1.5f;
        [Tooltip("Bina yukseklik carpani (3B belirginlik icin yuksek).")]
        [SerializeField] private float buildingHeightScale = 3.0f;
        [SerializeField] private int maxTrees = 600;
        [SerializeField] private bool shadows = true;
        [SerializeField] private bool fog = true;

        private const float SITE_OFFSET = 10000f;

        private Transform _root;
        private SiteMeta _meta;
        private Camera _twinCam;
        private bool _twinCamWasEnabled;
        private MonoBehaviour _mapCam;
        private bool _mapCamWasEnabled;

        // global ayar yedekleri (Hide'da geri yukle)
        private Material _prevSkybox; private bool _skyboxChanged;
        private bool _prevFog; private Color _prevFogColor; private FogMode _prevFogMode;
        private float _prevFogStart, _prevFogEnd; private bool _fogChanged;

        private System.Random _rng = new System.Random(1234);
        private Material[] _crownMats; private Material _trunkMat; private Material _bldMat; private Material _waterMat;
        private Mesh _icoMesh;

        // HTML twin'deki gibi low-poly kosegen (icosahedron) agac taci mesh'i (flat shading).
        private Mesh IcoMesh()
        {
            if (_icoMesh != null) return _icoMesh;
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            Vector3[] b = {
                new Vector3(-1,t,0), new Vector3(1,t,0), new Vector3(-1,-t,0), new Vector3(1,-t,0),
                new Vector3(0,-1,t), new Vector3(0,1,t), new Vector3(0,-1,-t), new Vector3(0,1,-t),
                new Vector3(t,0,-1), new Vector3(t,0,1), new Vector3(-t,0,-1), new Vector3(-t,0,1)
            };
            for (int i = 0; i < b.Length; i++) b[i] = b[i].normalized * 0.5f;
            int[] f = {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };
            var verts = new Vector3[f.Length];
            var tris = new int[f.Length];
            for (int i = 0; i < f.Length; i++) { verts[i] = b[f[i]]; tris[i] = i; } // flat: her yuze ayri vertex
            var m = new Mesh { vertices = verts, triangles = tris };
            m.RecalculateNormals();
            _icoMesh = m;
            return m;
        }

        [Serializable] private class Georef { public double origin_lat; public double origin_lon; }
        [Serializable] private class SiteMeta
        {
            public string site_name; public double width_m; public double height_m;
            public double min_elev_m; public double max_elev_m; public Georef georef;
        }
        [Serializable] private class Building { public double u, v, su, sv, height_m, density; public string id, type; }
        [Serializable] private class BuildingList { public int count; public Building[] buildings; }
        [Serializable] private class Tree { public double u, v, height_m, radius_m; }
        [Serializable] private class TreeList { public int count; public Tree[] trees; }

        private string SiteDir() => Path.Combine(Application.streamingAssetsPath, folder);

        public void Show()
        {
            // twin acikken Mapbox'in kendi kamera kontrolunu durdur (mouse panel orbit'ine kalsin)
            var mcc = FindObjectOfType<MapCameraController>();
            if (mcc != null) { _mapCam = mcc; _mapCamWasEnabled = mcc.enabled; mcc.enabled = false; }
            Spawn(ResolvePanelRenderTexture());
        }

        public void Hide()
        {
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
            if (_twinCam != null) { _twinCam.enabled = _twinCamWasEnabled; _twinCam = null; }
            if (_mapCam != null) { _mapCam.enabled = _mapCamWasEnabled; _mapCam = null; }
            if (_skyboxChanged) { RenderSettings.skybox = _prevSkybox; _skyboxChanged = false; }
            if (_fogChanged)
            {
                RenderSettings.fog = _prevFog; RenderSettings.fogColor = _prevFogColor; RenderSettings.fogMode = _prevFogMode;
                RenderSettings.fogStartDistance = _prevFogStart; RenderSettings.fogEndDistance = _prevFogEnd; _fogChanged = false;
            }
            DynamicGI.UpdateEnvironment();
        }

        private RenderTexture ResolvePanelRenderTexture()
        {
            var view = FindObjectOfType<DigitalTwin3DView>();
            if (view == null) { Debug.LogWarning("[SimurghSite] DigitalTwin3DView yok; ana ekrana render."); return null; }
            var t = typeof(DigitalTwin3DView);
            const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;
            RenderTexture rt = t.GetField("renderTexture", BF)?.GetValue(view) as RenderTexture;
            _twinCam = t.GetField("twinCamera", BF)?.GetValue(view) as Camera;
            if (rt == null)
            {
                var raw = t.GetField("targetRawImage", BF)?.GetValue(view) as RawImage;
                if (raw != null) rt = raw.texture as RenderTexture;
            }
            if (_twinCam != null) { _twinCamWasEnabled = _twinCam.enabled; _twinCam.enabled = false; }
            Debug.Log($"[SimurghSite] Panel RT {(rt != null ? "bulundu" : "BULUNAMADI")}, twinCam {(_twinCam != null ? "durduruldu" : "yok")}.");
            return rt;
        }

        [ContextMenu("Spawn Site")]
        public void Spawn() => Spawn(ResolvePanelRenderTexture());

        public void Spawn(RenderTexture panelRT)
        {
            var meta = ReadJson<SiteMeta>(Path.Combine(SiteDir(), "site_meta.json"));
            if (meta == null) { Debug.LogError("[SimurghSite] site_meta.json okunamadi"); return; }
            _meta = meta; _rng = new System.Random(1234);

            if (_root != null) Destroy(_root.gameObject);
            _root = new GameObject("SimurghSite").transform;
            _root.SetParent(transform, false);
            _root.position = new Vector3(SITE_OFFSET, 0f, 0f);

            EnsureMaterials();
            SpawnSun();

            float W = (float)(meta.width_m / Mathf.Max(0.0001f, metersPerUnit));
            float H = (float)(meta.height_m / Mathf.Max(0.0001f, metersPerUnit));

            Texture2D ortho = null;
            if (spawnGround) ortho = SpawnGround(meta, W, H);
            if (spawnWater && ortho != null) SpawnWater(ortho, W, H);

            int nb = 0, nt = 0;
            if (spawnBuildings)
            {
                var bl = ReadJson<BuildingList>(Path.Combine(SiteDir(), "buildings.json"));
                if (bl != null && bl.buildings != null) { foreach (var b in bl.buildings) SpawnBuilding(b, W, H); nb = bl.buildings.Length; }
            }
            if (spawnTrees)
            {
                var tl = ReadJson<TreeList>(Path.Combine(SiteDir(), "trees.json"));
                if (tl != null && tl.trees != null)
                {
                    int n = (maxTrees > 0) ? Mathf.Min(maxTrees, tl.trees.Length) : tl.trees.Length;
                    for (int i = 0; i < n; i++) SpawnTree(tl.trees[i], W, H);
                    nt = n;
                }
            }

            Debug.Log($"[SimurghSite] {meta.site_name}: {nb} bina + {nt} agac | saha {W:F0}x{H:F0} u (HTML twin gibi).");
            SetFog(Mathf.Max(W, H));
            PlaceCamera(W, H, panelRT);
        }

        private Vector3 Local(double u, double v, float W, float H)
            => new Vector3((float)((u - 0.5) * W), 0f, (float)((0.5 - v) * H));
        private float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        private void PlaceCamera(float W, float H, RenderTexture panelRT)
        {
            float span = Mathf.Max(W, H);
            float fovv = 52f;
            float height = (span * 0.5f) / Mathf.Tan(fovv * 0.5f * Mathf.Deg2Rad) * 1.15f;
            var camGo = new GameObject("SimurghCamera");
            camGo.transform.SetParent(_root, false);
            camGo.transform.localPosition = new Vector3(0f, height * 1.12f, -H * 0.18f);
            camGo.transform.localRotation = Quaternion.Euler(74f, 0f, 0f);   // daha dik kusbakisi
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = fovv;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.3f; cam.farClipPlane = height * 6f; cam.allowHDR = true;

            // HTML twin OrbitControls gibi mouse kontrolu: sol dondur, tekerlek zoom, sag pan
            var orbit = camGo.AddComponent<SimurghOrbitController>();
            orbit.Init(_root.position, height * 1.05f, 0f, 56f);   // hafif egik: binalarin yuksekligi 3B gorunur

            if (panelRT != null) { cam.targetTexture = panelRT; Debug.Log($"[SimurghSite] Kamera PANEL RT'sine render + mouse orbit (h={height:F0})."); }
            else { cam.depth = 100f; Debug.Log("[SimurghSite] Panel RT yok; ana ekrana render."); }
        }

        private void SpawnSun()
        {
            var sunGo = new GameObject("SimurghSun");
            sunGo.transform.SetParent(_root, false);
            sunGo.transform.localRotation = Quaternion.Euler(46f, -35f, 0f);
            var light = sunGo.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.35f;
            light.color = new Color(1f, 0.95f, 0.85f);
            if (shadows) { light.shadows = LightShadows.Soft; light.shadowStrength = 0.6f; light.shadowBias = 0.05f; }

            var skySh = Shader.Find("Skybox/Procedural");
            if (skySh != null)
            {
                if (!_skyboxChanged) { _prevSkybox = RenderSettings.skybox; _skyboxChanged = true; }
                var sky = new Material(skySh);
                sky.SetFloat("_AtmosphereThickness", 0.9f); sky.SetFloat("_Exposure", 1.3f);
                sky.SetColor("_SkyTint", new Color(0.45f, 0.6f, 0.85f));
                sky.SetColor("_GroundColor", new Color(0.37f, 0.4f, 0.36f));
                RenderSettings.skybox = sky;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.88f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.54f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.34f, 0.36f, 0.33f);
            RenderSettings.ambientIntensity = 1.15f;
            DynamicGI.UpdateEnvironment();
        }

        private void SetFog(float span)
        {
            if (!fog) return;
            if (!_fogChanged)
            {
                _prevFog = RenderSettings.fog; _prevFogColor = RenderSettings.fogColor; _prevFogMode = RenderSettings.fogMode;
                _prevFogStart = RenderSettings.fogStartDistance; _prevFogEnd = RenderSettings.fogEndDistance; _fogChanged = true;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.66f, 0.76f, 0.88f);
            RenderSettings.fogStartDistance = span * 0.6f;
            RenderSettings.fogEndDistance = span * 3.2f;
        }

        private Texture2D SpawnGround(SiteMeta m, float W, float H)
        {
            string path = Path.Combine(SiteDir(), "orthomosaic.jpg");
            if (!File.Exists(path)) { Debug.LogWarning("[SimurghSite] orthomosaic.jpg yok"); return null; }
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, true);
                tex.LoadImage(File.ReadAllBytes(path));
                tex.wrapMode = TextureWrapMode.Clamp; tex.anisoLevel = 16;
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Orthophoto";
                ground.transform.SetParent(_root, false);
                ground.transform.localPosition = Vector3.zero;
                ground.transform.localScale = new Vector3(W / 10f, 1f, H / 10f);
                ground.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var mat = new Material(Shader.Find("Standard")) { mainTexture = tex };
                mat.color = new Color(0.78f, 1.0f, 0.66f);   // cimenleri yesillestir (texture * yesil tint)
                mat.SetFloat("_Glossiness", 0.04f); mat.SetFloat("_Metallic", 0f);
                var mr = ground.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = true;
                Debug.Log("[SimurghSite] Orthophoto zemin olusturuldu.");
                return tex;
            }
            catch (Exception e) { Debug.LogWarning("[SimurghSite] zemin: " + e.Message); return null; }
        }

        // orthophoto'daki mavi (su) bolgeyi tespit edip 3B su yuzeyi koyar (HTML twin gibi).
        private void SpawnWater(Texture2D ortho, float W, float H)
        {
            try
            {
                var px = ortho.GetPixels32();
                int tw = ortho.width, thh = ortho.height;
                int minX = tw, minY = thh, maxX = 0, maxY = 0, count = 0;
                int step = Mathf.Max(1, tw / 256);
                for (int y = 0; y < thh; y += step)
                    for (int x = 0; x < tw; x += step)
                    {
                        var c = px[y * tw + x];
                        // gri-mavi / teal gol: mavi >= kirmizi ve yesilden belirgin dusuk doygunluk
                        bool blueish = (c.b >= c.r + 4) && (c.b + 6 >= c.g) && (c.b > 45) && (c.r < 130);
                        if (blueish)
                        { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; count++; }
                    }
                if (count < 40) { Debug.Log("[SimurghSite] Belirgin su yok."); return; }

                // texture (x,y) -> u,v. ground 180 Y dondu; GetPixels32 y=0 alt.
                float u0 = (float)minX / tw, u1 = (float)maxX / tw;
                float v0 = 1f - (float)maxY / thh, v1 = 1f - (float)minY / thh;
                float cu = (u0 + u1) * 0.5f, cv = (v0 + v1) * 0.5f;
                float wu = Mathf.Max(0.02f, (u1 - u0)), wv = Mathf.Max(0.02f, (v1 - v0));

                var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
                water.name = "Water";
                water.transform.SetParent(_root, false);
                water.transform.localPosition = Local(cu, cv, W, H) + new Vector3(0f, 0.4f, 0f);
                water.transform.localScale = new Vector3(wu * W / 10f, 1f, wv * H / 10f);
                var mr = water.GetComponent<MeshRenderer>();
                mr.sharedMaterial = _waterMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Debug.Log($"[SimurghSite] Su yuzeyi olusturuldu ({count} mavi ornek).");
            }
            catch (Exception e) { Debug.LogWarning("[SimurghSite] su: " + e.Message); }
        }

        private void SpawnBuilding(Building b, float W, float H)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = string.IsNullOrEmpty(b.id) ? "Building" : b.id;
            go.transform.SetParent(_root, false);
            float w = Mathf.Max(2f, (float)(b.su * W));
            float d = Mathf.Max(2f, (float)(b.sv * H));
            float h = Mathf.Max(6f, (float)b.height_m / metersPerUnit * buildingHeightScale);
            go.transform.localPosition = Local(b.u, b.v, W, H) + new Vector3(0f, h * 0.5f, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, Rand(-4f, 4f), 0f);
            go.transform.localScale = new Vector3(w, h, d);
            var r = go.GetComponent<MeshRenderer>();
            if (r != null)
            {
                r.sharedMaterial = _bldMat;   // hepsi ayni belirgin renk
                r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private void SpawnTree(Tree t, float W, float H)
        {
            float sv = Rand(0.7f, 1.35f);
            float h = Mathf.Max(2f, (float)t.height_m / metersPerUnit * heightScale) * sv;
            float rad = Mathf.Max(0.4f, (float)t.radius_m / metersPerUnit) * sv;
            float trunkH = h * 0.42f, crownH = h * 0.58f;
            var tree = new GameObject("Tree").transform;
            tree.SetParent(_root, false);
            tree.localPosition = Local(t.u, t.v, W, H);
            tree.localRotation = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.transform.SetParent(tree, false);
            trunk.transform.localScale = new Vector3(rad * 0.3f, trunkH * 0.5f, rad * 0.3f);
            trunk.transform.localPosition = new Vector3(0f, trunkH * 0.5f, 0f);
            var tr = trunk.GetComponent<MeshRenderer>();
            if (tr != null) { tr.sharedMaterial = _trunkMat; tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            // HTML twin gibi low-poly kosegen tac (icosahedron, flat shading)
            var crown = new GameObject("Crown");
            crown.transform.SetParent(tree, false);
            crown.AddComponent<MeshFilter>().sharedMesh = IcoMesh();
            crown.transform.localScale = new Vector3(rad * 2.1f, crownH * 1.4f, rad * 2.0f);
            crown.transform.localPosition = new Vector3(0f, trunkH + crownH * 0.5f, 0f);
            crown.transform.localRotation = Quaternion.Euler(0f, Rand(0f, 360f), Rand(-8f, 8f));
            var cr = crown.AddComponent<MeshRenderer>();
            cr.sharedMaterial = _crownMats[_rng.Next(_crownMats.Length)];
            cr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void EnsureMaterials()
        {
            var sh = Shader.Find("Standard");
            if (sh == null) return;
            // DAHA KOYU YESIL agac taclari
            var greens = new[] {
                new Color(0.05f,0.16f,0.05f), new Color(0.07f,0.20f,0.07f),
                new Color(0.04f,0.13f,0.04f), new Color(0.08f,0.22f,0.08f),
            };
            _crownMats = new Material[greens.Length];
            for (int i = 0; i < greens.Length; i++) { _crownMats[i] = new Material(sh) { color = greens[i] }; _crownMats[i].SetFloat("_Glossiness", 0.08f); _crownMats[i].enableInstancing = true; }
            _trunkMat = new Material(sh) { color = new Color(0.30f, 0.21f, 0.13f) }; _trunkMat.enableInstancing = true;
            // Binalar: tonu artirildi (daha acik/belirgin), hepsi ayni
            _bldMat = new Material(sh) { color = new Color(0.78f, 0.76f, 0.71f) }; _bldMat.SetFloat("_Glossiness", 0.14f); _bldMat.enableInstancing = true;

            // su: HTML gibi mavi-teal, yari saydam, parlak
            _waterMat = new Material(sh) { color = new Color(0.16f, 0.42f, 0.52f, 0.72f) };
            _waterMat.SetFloat("_Mode", 3f);
            _waterMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _waterMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _waterMat.SetInt("_ZWrite", 0);
            _waterMat.DisableKeyword("_ALPHATEST_ON"); _waterMat.EnableKeyword("_ALPHABLEND_ON");
            _waterMat.renderQueue = 3000;
            _waterMat.SetFloat("_Glossiness", 0.85f); _waterMat.SetFloat("_Metallic", 0.1f);
        }

        private static T ReadJson<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) { Debug.LogWarning("[SimurghSite] yok: " + path); return null; }
                return JsonUtility.FromJson<T>(File.ReadAllText(path));
            }
            catch (Exception e) { Debug.LogError("[SimurghSite] JSON: " + e.Message); return null; }
        }
    }
}
