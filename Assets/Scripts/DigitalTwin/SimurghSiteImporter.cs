using System;
using System.Collections.Generic;
using System.IO;
using Mapbox.Utils;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>Georeferenced survey scene with owned assets and isolated rendering.</summary>
    public class SimurghSiteImporter : MonoBehaviour
    {
        [SerializeField] private string folder = "SimurghTwin";
        public const int SiteLayer = 30;
        private Transform _root, _trees, _buildings, _uav, _rover;
        private SiteMeta _meta;
        private DigitalTwin3DView _view;
        private SimurghOrbitController _orbit;
        private MapCameraController _mapControl;
        private bool _mapWasEnabled;
        private int _previousMask;
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private DigitalTwinJsonPoseBridge _bridge;
        private DigitalTwinRoverAdapter _roverAdapter;
        private readonly List<Vector3> _trailPoints = new List<Vector3>();
        private LineRenderer _trail;
        private Camera _camera;
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;
        public bool TreesVisible => _trees != null && _trees.gameObject.activeSelf;
        public bool BuildingsVisible => _buildings != null && _buildings.gameObject.activeSelf;
        public bool UavVisible => _uav != null && _uav.gameObject.activeSelf;
        public bool RoverVisible => _rover != null && _rover.gameObject.activeSelf;
        public string UavStatus { get; private set; } = "Konum bekleniyor";
        public string RoverStatus { get; private set; } = "Konum bekleniyor";
        public string LoadError { get; private set; }
        public string SiteName => _meta != null ? _meta.site_name : "Saha yüklenemedi";
        public float Width => _meta != null ? (float)_meta.width_m : 0f;
        public float Height => _meta != null ? (float)_meta.height_m : 0f;
        public SimurghOrbitController Orbit => _orbit;
        public Camera SiteCamera => _camera;

        [Serializable] private class Raster { public string crs; public double[] bounds; }
        [Serializable] private class Source { public Raster ortho_meta; }
        [Serializable] private class SiteMeta { public string site_name; public double width_m, height_m; public Source source; }
        [Serializable] private class Building { public double u, v, su, sv, height_m; public string id; }
        [Serializable] private class BuildingList { public Building[] buildings; }
        [Serializable] private class Tree { public double u, v, height_m, radius_m; }
        [Serializable] private class TreeList { public Tree[] trees; }

        public void Show()
        {
            if (IsVisible) return;
            LoadError = null;
            try
            {
                _view = FindObjectOfType<DigitalTwin3DView>();
                if (_view == null) throw new InvalidOperationException("3B görüntü alanı bulunamadı.");
                _meta = Read<SiteMeta>("site_meta.json");
                if (_meta == null || Width <= 0 || Height <= 0) throw new InvalidDataException("Saha ölçüleri geçersiz.");
                var raster = _meta.source?.ortho_meta;
                if (raster?.bounds == null || raster.bounds.Length != 4) throw new InvalidDataException("Ortofoto koordinat bilgisi eksik.");
                _root = new GameObject("SurveySite").transform;
                _root.SetParent(transform, false); _root.position = new Vector3(10000,0,0);
                _trees = Child("TreeModels"); _buildings = Child("BuildingDrafts");
                BuildGround(); BuildContext(); BuildObjects();
                // Rectangular detections include roads in this dataset. Drafts are opt-in.
                _buildings.gameObject.SetActive(false); _trees.gameObject.SetActive(false);
                _uav = Vehicle("UAV", new Color(0.22f,0.8f,1f), true);
                _rover = Vehicle("Rover", new Color(1f,0.64f,0.25f), false);
                _uav.gameObject.SetActive(false); _rover.gameObject.SetActive(false);
                _trail = Line("UAV trail", Material(new Color(0.22f,0.8f,1f)),0.6f,new Vector3[0]);
                foreach (Transform item in _root.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = SiteLayer;
                _bridge = FindObjectOfType<DigitalTwinJsonPoseBridge>(); _roverAdapter = FindObjectOfType<DigitalTwinRoverAdapter>();
                _mapControl = FindObjectOfType<MapCameraController>();
                if (_mapControl != null) { _mapWasEnabled = _mapControl.enabled; _mapControl.enabled = false; }
                _view.SetSiteView(true); _camera = _view.RenderCamera; _previousMask = _camera.cullingMask;
                _camera.cullingMask = 1 << SiteLayer; _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0.035f,0.052f,0.075f); _camera.fieldOfView = 42f;
                _camera.nearClipPlane = 0.3f; _camera.farClipPlane = 5000; _camera.allowHDR = false;
                _orbit = _camera.GetComponent<SimurghOrbitController>();
                if (_orbit == null) _orbit = _camera.gameObject.AddComponent<SimurghOrbitController>();
                _orbit.enabled = true; _orbit.Init(_root.position,Mathf.Max(Width,Height)*1.48f,0,57);
                UavStatus = RoverStatus = "Konum bekleniyor";
            }
            catch (Exception ex) { Hide(); LoadError = ex.Message; Debug.LogError("[SimurghSite] " + LoadError); }
        }

        public void Hide()
        {
            if (_orbit != null) _orbit.enabled = false;
            if (_camera != null) { _camera.cullingMask = _previousMask; _camera.enabled = false; }
            if (_view != null) _view.SetSiteView(false);
            if (_mapControl != null) _mapControl.enabled = _mapWasEnabled;
            _mapControl = null;
            if (_root != null) { _root.gameObject.SetActive(false); Destroy(_root.gameObject); }
            _root = null;
            foreach (var asset in _owned) if (asset != null) Destroy(asset);
            _owned.Clear(); _trailPoints.Clear();
        }
        private void OnDestroy() { Hide(); }
        public void SetTreesVisible(bool visible) { if (_trees != null) _trees.gameObject.SetActive(visible); }
        public void SetBuildingsVisible(bool visible) { if (_buildings != null) _buildings.gameObject.SetActive(visible); }
        public void FocusUav() { if (UavVisible && _orbit != null) _orbit.Focus(_uav.position,100f); }

        public bool TryGeoToLocal(Vector2d geo, out Vector3 local)
        {
            local = Vector3.zero;
            var raster = _meta?.source?.ortho_meta;
            if (raster?.bounds == null || raster.bounds.Length != 4) return false;
            if (!SiteGeoReference.TryProject(geo.x,geo.y,raster.crs,out double east,out double north)) return false;
            local = new Vector3((float)(east-(raster.bounds[0]+raster.bounds[2])*0.5),0,(float)(north-(raster.bounds[1]+raster.bounds[3])*0.5));
            return Mathf.Abs(local.x) <= Width*0.5f && Mathf.Abs(local.z) <= Height*0.5f;
        }

        private void LateUpdate()
        {
            if (!IsVisible) return;
            bool fresh = _bridge != null && _bridge.HasRecentUavPose;
            Vector3 p = Vector3.zero;
            bool inside = fresh && TryGeoToLocal(_bridge.LastUavGeo,out p);
            UavStatus = !fresh ? "Konum bekleniyor" : inside ? "Saha içinde" : "Saha dışında";
            _uav.gameObject.SetActive(inside);
            if (inside)
            {
                // Ground-projected pin: the pose contract does not declare an altitude datum.
                _uav.localPosition = p+Vector3.up*3f;
                _uav.localRotation = Quaternion.Euler(0,_bridge.LastUavYaw,0);
                if (_trailPoints.Count == 0 || Vector3.Distance(_trailPoints[_trailPoints.Count-1],p+Vector3.up*0.8f)>0.8f)
                {
                    if (_trailPoints.Count>=1200) _trailPoints.RemoveAt(0);
                    _trailPoints.Add(p+Vector3.up*0.8f); _trail.positionCount=_trailPoints.Count; _trail.SetPositions(_trailPoints.ToArray());
                }
            }
            // Do not connect separate visits across stale or out-of-site segments.
            else if (_trailPoints.Count>0) { _trailPoints.Clear(); _trail.positionCount=0; }
            fresh = _roverAdapter != null && _roverAdapter.HasRecentPose;
            inside = fresh && TryGeoToLocal(_roverAdapter.LastGeo,out p);
            RoverStatus = !fresh ? "Konum bekleniyor" : inside ? "Saha içinde" : "Saha dışında";
            _rover.gameObject.SetActive(inside);
            if (inside) _rover.localPosition=p+Vector3.up*2f;
        }

        private T Own<T>(T asset) where T:UnityEngine.Object { _owned.Add(asset); return asset; }
        private Transform Child(string name) { var t=new GameObject(name).transform; t.SetParent(_root,false); return t; }
        private T Read<T>(string name) => JsonUtility.FromJson<T>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,folder,name)));
        private Material Material(Color color,bool shaded=false)
        {
            Shader shader=Shader.Find(shaded ? "Simurgh/SurveyModel" : "Unlit/Color");
            if(shader==null) throw new InvalidOperationException("Saha gölgelendiricisi bulunamadı.");
            return Own(new Material(shader){color=color,enableInstancing=true});
        }
        private void BuildGround()
        {
            var tex=Own(new Texture2D(2,2,TextureFormat.RGB24,true));
            if(!tex.LoadImage(File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath,folder,"orthomosaic.jpg")))) throw new InvalidDataException("Ortofoto okunamadı.");
            tex.wrapMode=TextureWrapMode.Clamp; tex.anisoLevel=8;
            var shader=Shader.Find("Simurgh/SurveyOrtho");
            if(shader==null) throw new InvalidOperationException("Ortofoto gölgelendiricisi bulunamadı.");
            var material=Own(new Material(shader){mainTexture=tex});
            // Explicit UVs: image top = north (+Z), image right = east (+X).
            var mesh=Own(new Mesh {name="Georeferenced orthophoto"});
            mesh.vertices=new[]{new Vector3(-Width/2,0,-Height/2),new Vector3(-Width/2,0,Height/2),new Vector3(Width/2,0,Height/2),new Vector3(Width/2,0,-Height/2)};
            mesh.uv=new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)};
            mesh.triangles=new[]{0,1,2,0,2,3}; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var ground=Child("Orthophoto"); ground.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            ground.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;
        }
        private void BuildContext()
        {
            Box("Survey base",_root,new Vector3(0,-2.5f,0),new Vector3(Width+1,4,Height+1),Material(new Color(0.075f,0.105f,0.13f),true));
            Line("Survey boundary",Material(new Color(0.24f,0.43f,0.48f)),0.35f,new[]{new Vector3(-Width/2,0.1f,-Height/2),new Vector3(-Width/2,0.1f,Height/2),new Vector3(Width/2,0.1f,Height/2),new Vector3(Width/2,0.1f,-Height/2),new Vector3(-Width/2,0.1f,-Height/2)});
            var mat=Material(new Color(0.055f,0.078f,0.1f));
            for(int n=-600;n<=600;n+=50)
            {
                Line("Grid east",mat,0.2f,new[]{new Vector3(n,-5,-600),new Vector3(n,-5,600)});
                Line("Grid north",mat,0.2f,new[]{new Vector3(-600,-5,n),new Vector3(600,-5,n)});
            }
        }
        private void BuildObjects()
        {
            var mat=Material(new Color(0.38f,0.47f,0.5f),true);
            var buildings=Read<BuildingList>("buildings.json");
            if(buildings?.buildings!=null) foreach(var b in buildings.buildings)
            {
                float h=Mathf.Clamp((float)b.height_m,1,100);
                Box(b.id??"Building",_buildings,Local(b.u,b.v)+Vector3.up*h/2,new Vector3((float)b.su*Width,h,(float)b.sv*Height),mat);
            }
            var treeMat=Material(new Color(0.18f,0.34f,0.27f),true);
            var trunkMat=Material(new Color(0.22f,0.2f,0.15f),true);
            var trees=Read<TreeList>("trees.json");
            if(trees?.trees==null)return;
            var prototype=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var crownMesh=prototype.GetComponent<MeshFilter>().sharedMesh; Destroy(prototype);
            for(int i=0;i<Mathf.Min(600,trees.trees.Length);i++)
            {
                var t=trees.trees[i]; float h=Mathf.Clamp((float)t.height_m,2,35),r=Mathf.Clamp((float)t.radius_m,0.5f,10);
                var p=Local(t.u,t.v);
                Box("Trunk",_trees,p+Vector3.up*h*0.24f,new Vector3(r*0.12f,h*0.48f,r*0.12f),trunkMat);
                var crown=new GameObject("Canopy").transform; crown.SetParent(_trees,false);
                crown.localPosition=p+Vector3.up*h*0.68f; crown.localScale=new Vector3(r*1.6f,h*0.64f,r*1.6f);
                crown.gameObject.AddComponent<MeshFilter>().sharedMesh=crownMesh; crown.gameObject.AddComponent<MeshRenderer>().sharedMaterial=treeMat;
            }
        }
        private Vector3 Local(double u,double v)=>new Vector3((float)(u-0.5)*Width,0,(float)(0.5-v)*Height);
        private Transform Vehicle(string name,Color color,bool drone)
        {
            var parent=Child(name);var mat=Material(color,true);
            Box("Body",parent,Vector3.zero,new Vector3(2,1,3),mat);
            if(drone) { Box("Wing",parent,Vector3.zero,new Vector3(7,0.5f,0.5f),mat); Box("Axis",parent,Vector3.zero,new Vector3(0.5f,0.5f,7),mat); }
            else Box("Chassis",parent,Vector3.down*0.5f,new Vector3(3,0.6f,4),mat);
            return parent;
        }
        private Transform Box(string name,Transform parent,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);
            go.transform.localPosition=position;go.transform.localScale=scale;Destroy(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=material;return go.transform;
        }
        private LineRenderer Line(string name,Material material,float width,Vector3[] points)
        {
            var line=Child(name).gameObject.AddComponent<LineRenderer>();line.useWorldSpace=false;line.sharedMaterial=material;
            line.widthMultiplier=width;line.positionCount=points.Length;line.SetPositions(points);
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;return line;
        }
    }
}
