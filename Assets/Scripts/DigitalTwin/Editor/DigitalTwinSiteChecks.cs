#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using GroundStation.DigitalTwin;
using Mapbox.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using Object = UnityEngine.Object;

public static class DigitalTwinSiteChecks
{
    private static IEnumerator _run;
    private static readonly StringBuilder Report = new StringBuilder();
    private static double _resumeAt;
    private static int _count;

    [MenuItem("Tools/Simurgh/Digital Twin Kontrolu (Play)")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying || _run != null) return;
        Report.Clear(); _count=0; _resumeAt=0;
        _run=Checks(); EditorApplication.update+=Tick;
    }
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup<_resumeAt) return;
        try
        {
            if(!EditorApplication.isPlaying) throw new InvalidOperationException("Play stopped.");
            if(!_run.MoveNext()) { Finish("PASS"); return; }
            _resumeAt=EditorApplication.timeSinceStartup+(_run.Current is float seconds?seconds:0.15f);
        }
        catch(Exception ex) { Report.AppendLine(ex.ToString()); Finish("FAIL"); Debug.LogException(ex); }
    }
    private static void Finish(string result)
    {
        EditorApplication.update-=Tick;_run=null;Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/digital-twin-checks.txt",$"{result}: {_count} checks\n"+Report);
        Debug.Log($"[DigitalTwinSiteChecks] {result}: {_count} checks.");
    }
    private static void Check(bool ok,string text)
    { if(!ok)throw new InvalidOperationException(text);_count++;Report.AppendLine("PASS: "+text); }

    private static IEnumerator Checks()
    {
        Check(SiteGeoReference.TryProject(41.3,-81.75,"EPSG:32617",out double east,out double north)
            && Math.Abs(east-437210.48241794045)<0.02 && Math.Abs(north-4572332.017959274)<0.02,"Northern UTM agrees with independent PROJ reference within 2 cm");
        Check(SiteGeoReference.TryProject(-33,151,"EPSG:32756",out east,out north)
            && Math.Abs(east-313152.77721466334)<0.02 && Math.Abs(north-6346936.4957081545)<0.02,"Southern UTM reference within 2 cm");
        Check(!SiteGeoReference.TryProject(double.NaN,0,"EPSG:32617",out east,out north),"Invalid coordinates rejected");
        Check(!SiteGeoReference.TryProject(37.78,-122.4,"EPSG:32617",out east,out north),"Remote UTM zone rejected");
        Check(!SiteGeoReference.TryProject(41.3,-81.75,"EPSG:4326",out east,out north),"Unsupported CRS rejected explicitly");
        var controller=Object.FindObjectOfType<DigitalTwinUIController>(true);
        Check(controller!=null,"Twin controller found");
        controller.SetViewOpen(false);yield return 0.25f;
        var map=Object.FindObjectOfType<MapCameraController>();bool mapEnabled=map!=null&&map.enabled;
        Material sky=RenderSettings.skybox;bool fog=RenderSettings.fog;Color ambient=RenderSettings.ambientSkyColor;
        for(int i=0;i<3;i++)
        {
            controller.SetViewOpen(true);
            Object.FindObjectOfType<DigitalTwinSiteWorkspace>().SetLiveMode(false);
            yield return 0.3f;
            var site=Object.FindObjectOfType<SimurghSiteImporter>();
            Check(site!=null&&site.IsVisible&&site.LoadError==null,"Open cycle "+(i+1));
            var view=Object.FindObjectOfType<DigitalTwin3DView>();
            Check(view!=null&&view.IsSiteView&&view.RenderCamera.enabled&&view.RenderCamera.targetTexture!=null,"A single active camera renders the site");
            Check(Object.FindObjectsOfType<SimurghOrbitController>().Length==1,"No duplicate orbit cameras");
            Check(view.RenderCamera.targetTexture.width==Mathf.Min(2560,Screen.width)&&view.RenderCamera.targetTexture.height==Mathf.Min(2560,Screen.height),"Render texture follows viewport resolution");
            int stray=0;foreach(var g in controller.GetComponentsInChildren<Graphic>())if(g.enabled&&g!=view.ViewportImage)stray++;
            Check(stray==0,"No legacy squares or text over the viewport");
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = new Vector2(Screen.width/2f-195f*TwinHudTheme.UiScale,74f*TwinHudTheme.UiScale) },hits);
            Check(!hits.Exists(hit=>hit.module is GraphicRaycaster),"Camera toolbar cannot click hidden map buttons");
            Check(!site.BuildingsVisible&&!site.TreesVisible,"Approximate models are opt-in");
            site.SetTreesVisible(true);Check(site.TreesVisible,"Tree layer switch");site.SetTreesVisible(false);
            site.SetBuildingsVisible(true);Check(site.BuildingsVisible,"Building draft switch");site.SetBuildingsVisible(false);
            site.Orbit.TopView();Check(site.Orbit.IsTopView,"Top view preset");site.Orbit.ResetView();
            if(i<2)
            {
                controller.SetViewOpen(false);yield return 0.3f;
                Check(!site.IsVisible&&(map==null||map.enabled==mapEnabled),"Map controls restored after close");
                Check(RenderSettings.skybox==sky&&RenderSettings.fog==fog&&RenderSettings.ambientSkyColor==ambient,"Global environment remains unchanged");
                Check(!view.RenderCamera.enabled,"Closed twin camera does not keep rendering");
            }
        }
        var activeSite=Object.FindObjectOfType<SimurghSiteImporter>();
        Check(activeSite.TryGeoToLocal(new Vector2d(41.30422962584914,-81.75230772357038),out Vector3 local)&&local.magnitude<0.02,"Raster center maps to site origin");
        Check(!activeSite.TryGeoToLocal(new Vector2d(41.5012,-81.6945),out local),"Inaccurate metadata display origin is not used as a position fix");
        var bridge=Object.FindObjectOfType<DigitalTwinJsonPoseBridge>();
        string token=Guid.NewGuid().ToString("N");
        string uav=Pose("test-site-uav-"+token,"uav",1,41.30422962584914,-81.75230772357038);
        Check(bridge.TryApplyDigitalTwinJson(uav),"UAV test pose accepted by real ingress bridge");
        Check(bridge.TryApplyDigitalTwinJson(Pose("test-site-rover-"+token,"rover",1,41.30432962584914,-81.75240772357038)),"Independent rover sequence accepted");
        yield return 0.3f;
        Check(activeSite.UavVisible&&activeSite.RoverVisible,"Both vehicle pins appear within the survey bounds");
        Check(!bridge.TryApplyDigitalTwinJson(uav),"Duplicate sequence rejected");
        Check(bridge.TryApplyDigitalTwinJson(Pose("test-site-uav-"+token,"uav",2,37.78418,-122.4016)),"Out-of-site pose accepted as telemetry");
        yield return 0.3f;
        Check(!activeSite.UavVisible&&activeSite.UavStatus=="Saha dışında","Out-of-site telemetry never fabricates an on-site vehicle");
        SetField(bridge,"_lastUavPoseAt",Time.unscaledTime-6f);
        var rover=Object.FindObjectOfType<DigitalTwinRoverAdapter>();SetField(rover,"_lastPoseAt",Time.unscaledTime-6f);
        yield return 0.3f;
        Check(!activeSite.UavVisible&&!activeSite.RoverVisible&&activeSite.UavStatus=="Konum bekleniyor","Stale vehicle poses disappear");
        Object.FindObjectOfType<DigitalTwinRemoteState>().Clear();
        Report.AppendLine("No vehicle commands sent. Test poses only; stop Play to discard the fixture state.");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/digital-twin-preview.png"));
    }
    private static string Pose(string source,string vehicle,long seq,double lat,double lon)
    {
        return "{\"schemaVersion\":\"1.0\",\"sourceId\":\""+source+"\",\"vehicleType\":\""+vehicle+"\",\"sequenceId\":"+seq+",\"timestampMs\":"+DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            +",\"authToken\":\"simurgh-2026\",\"pose\":{\"latitude\":"+lat.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"longitude\":"+lon.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"altitudeM\":30,\"yawDeg\":45}}";
    }
    private static void SetField(object obj,string name,object value)=>obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);
}
#endif
