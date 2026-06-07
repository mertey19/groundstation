using System.Collections;
using System.Reflection;
using UnityEngine;
using Mapbox.Unity.Map;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// Mapbox'in twin panelinde (DigitalTwin3DView RenderTexture) gosterdigi bolgenin YESIL
    /// alanlarini (cim/park/agac) tespit edip o dunya konumlarina low-poly agac dagitir.
    /// "Mapbox neyi gosteriyorsa onun ikizi" -> dinamik: drone nereye bakarsa orada agac.
    /// DigitalTwinMap3DMode.Enable3DForTwin -> Populate() cagirir.
    /// </summary>
    public class MapboxTreeScatter : MonoBehaviour
    {
        [SerializeField] private int maxTrees = 350;
        [Tooltip("Yesil pikselden agac olma olasiligi (seyreklik).")]
        [SerializeField] private float density = 0.07f;
        [SerializeField] private float treeBaseHeight = 7f;
        [SerializeField] private bool shadows = true;
        [Tooltip("Zemin duzlemi Y (Mapbox tile genelde 0).")]
        [SerializeField] private float groundY = 0f;

        private Transform _root;
        private Mesh _ico;
        private Material[] _green;
        private Material _trunk;

        public void Populate()
        {
            StopAllCoroutines();
            StartCoroutine(PopulateRoutine());
        }

        public void Clear()
        {
            StopAllCoroutines();
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
        }

        private IEnumerator PopulateRoutine()
        {
            yield return null; yield return null;   // twinCamera RT dolsun

            var view = FindObjectOfType<DigitalTwin3DView>();
            if (view == null) { Debug.LogWarning("[MapboxTrees] DigitalTwin3DView yok"); yield break; }
            var t = typeof(DigitalTwin3DView);
            const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;
            var rt = t.GetField("renderTexture", BF)?.GetValue(view) as RenderTexture;
            var cam = t.GetField("twinCamera", BF)?.GetValue(view) as Camera;
            if (rt == null || cam == null) { Debug.LogWarning("[MapboxTrees] RT/cam bulunamadi"); yield break; }

            EnsureAssets();
            if (_root != null) Destroy(_root.gameObject);
            _root = new GameObject("MapboxTrees").transform;

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            int tw = rt.width, th = rt.height;
            Destroy(tex);

            int placed = 0, step = Mathf.Max(1, tw / 140);
            var plane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
            for (int y = 0; y < th && placed < maxTrees; y += step)
                for (int x = 0; x < tw && placed < maxTrees; x += step)
                {
                    var c = px[y * tw + x];
                    // cim/park/agac yesili: yesil belirgin, asiri parlak (bina) veya gri (yol) degil
                    bool green = c.g > c.r + 10 && c.g > c.b + 6 && c.g > 45 && c.g < 235;
                    if (!green) continue;
                    if (Random.value > density) continue;
                    var vray = cam.ViewportPointToRay(new Vector3((float)x / tw, (float)y / th, 0f));
                    if (plane.Raycast(vray, out float dd) && dd > 0f && dd < 6000f)
                    {
                        SpawnTree(vray.GetPoint(dd));
                        placed++;
                    }
                }
            Debug.Log($"[MapboxTrees] {placed} agac (yesil alan) yerlestirildi (RT {tw}x{th}).");
        }

        private void SpawnTree(Vector3 wp)
        {
            float h = treeBaseHeight * Random.Range(0.7f, 1.4f);
            float rad = h * 0.28f;
            var tree = new GameObject("Tree").transform;
            tree.SetParent(_root, false);
            tree.position = wp;
            tree.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.transform.SetParent(tree, false);
            trunk.transform.localScale = new Vector3(rad * 0.3f, h * 0.2f, rad * 0.3f);
            trunk.transform.localPosition = new Vector3(0f, h * 0.2f, 0f);
            var tr = trunk.GetComponent<MeshRenderer>();
            if (tr != null) { tr.sharedMaterial = _trunk; tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }

            var crown = new GameObject("Crown");
            crown.transform.SetParent(tree, false);
            crown.AddComponent<MeshFilter>().sharedMesh = _ico;
            crown.transform.localScale = new Vector3(rad * 2f, h * 0.85f, rad * 2f);
            crown.transform.localPosition = new Vector3(0f, h * 0.55f, 0f);
            crown.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), Random.Range(-8f, 8f));
            var cr = crown.AddComponent<MeshRenderer>();
            cr.sharedMaterial = _green[Random.Range(0, _green.Length)];
            cr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void EnsureAssets()
        {
            if (_ico == null) _ico = IcoMesh();
            if (_green != null) return;
            var sh = Shader.Find("Standard");
            var cols = new[] {
                new Color(0.20f,0.44f,0.16f), new Color(0.27f,0.52f,0.21f),
                new Color(0.16f,0.38f,0.14f), new Color(0.31f,0.55f,0.24f),
            };
            _green = new Material[cols.Length];
            for (int i = 0; i < cols.Length; i++) { _green[i] = new Material(sh) { color = cols[i] }; _green[i].SetFloat("_Glossiness", 0.12f); _green[i].enableInstancing = true; }
            _trunk = new Material(sh) { color = new Color(0.36f, 0.26f, 0.16f) }; _trunk.enableInstancing = true;
        }

        private Mesh IcoMesh()
        {
            float tt = (1f + Mathf.Sqrt(5f)) / 2f;
            Vector3[] b = {
                new Vector3(-1,tt,0), new Vector3(1,tt,0), new Vector3(-1,-tt,0), new Vector3(1,-tt,0),
                new Vector3(0,-1,tt), new Vector3(0,1,tt), new Vector3(0,-1,-tt), new Vector3(0,1,-tt),
                new Vector3(tt,0,-1), new Vector3(tt,0,1), new Vector3(-tt,0,-1), new Vector3(-tt,0,1)
            };
            for (int i = 0; i < b.Length; i++) b[i] = b[i].normalized * 0.5f;
            int[] f = { 0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1 };
            var v = new Vector3[f.Length]; var tri = new int[f.Length];
            for (int i = 0; i < f.Length; i++) { v[i] = b[f[i]]; tri[i] = i; }
            var m = new Mesh { vertices = v, triangles = tri };
            m.RecalculateNormals();
            return m;
        }
    }
}
