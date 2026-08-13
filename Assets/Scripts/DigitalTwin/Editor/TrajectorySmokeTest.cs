#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GroundStation.DigitalTwin;

/// <summary>
/// Tools > Simurgh > Yorunge Duman Testi (Play + Rapor)
///
/// Sahneyi acar, veri kumesi oynaticisini kurar, Play moduna gecer, izlerin GERCEKTEN
/// cizildigini olcer (LineRenderer vertex sayilari, izlerin kapladigi alan, sapma
/// istatistikleri) ve rapor + ekran goruntusu yazar. Komut satirindan da calisir:
///
///   Unity.exe -projectPath &lt;proje&gt; -executeMethod TrajectorySmokeTest.Run -quit
///
/// Cikti klasoru: SIMURGH_SMOKE_DIR ortam degiskeni, yoksa proje kokunde SmokeTest/.
/// Komut satirinda -simurghSmokeQuit verilirse test bitince editor kapanir.
/// </summary>
public static class TrajectorySmokeTest
{
    private const string ArmedKey = "SimurghSmokeArmed";
    private const string StartedKey = "SimurghSmokePlayStart";
    private const float PlaySeconds = 25f;   // veri kumesinden bu kadar sn oynatilir
    private const float PlayTimeout = 180f;  // Play moduna girilemezse birak

    private static readonly List<string> Errors = new List<string>();

    [MenuItem("Tools/Simurgh/Yorunge Duman Testi (Play + Rapor)")]
    public static void Run()
    {
        try
        {
            Directory.CreateDirectory(OutputDir);
            EditorSceneManager.OpenScene("Assets/deneme.unity", OpenSceneMode.Single);

            var comparison = UnityEngine.Object.FindObjectOfType<DigitalTwinTrajectoryComparison>();
            if (comparison == null)
            {
                comparison = new GameObject("DigitalTwinTrajectoryComparison")
                    .AddComponent<DigitalTwinTrajectoryComparison>();
                Log("Yorunge katmani sahnede yoktu, eklendi.");
            }

            var player = UnityEngine.Object.FindObjectOfType<DigitalTwinTrajectoryDatasetPlayer>();
            if (player == null)
            {
                player = new GameObject("DigitalTwinTrajectoryDatasetPlayer")
                    .AddComponent<DigitalTwinTrajectoryDatasetPlayer>();
                Log("Veri kumesi oynaticisi eklendi.");
            }

            // Testi belirlenimci kil: hizli oynat, dongu kapali.
            var so = new SerializedObject(player);
            so.FindProperty("playOnStart").boolValue = true;
            so.FindProperty("loop").boolValue = false;
            so.FindProperty("speed").floatValue = 8f;
            so.FindProperty("maxMessagesPerSecond").floatValue = 60f;
            so.ApplyModifiedPropertiesWithoutUndo();

            SessionState.SetBool(ArmedKey, true);
            SessionState.SetFloat(StartedKey, -1f);
            Log("Play moduna geciliyor...");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e)
        {
            WriteReport("KURULUM HATASI: " + e);
            QuitIfRequested(1);
        }
    }

    // Play moduna gecerken domain reload olur ve statik abonelikler silinir; bu kanca
    // reload sonrasi calisip yoklayiciyi yeniden baglar.
    [InitializeOnLoadMethod]
    private static void Rearm()
    {
        if (!SessionState.GetBool(ArmedKey, false)) return;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            Errors.Add(type + ": " + condition);
    }

    private static void Poll()
    {
        if (!SessionState.GetBool(ArmedKey, false))
        {
            EditorApplication.update -= Poll;
            return;
        }

        float t = (float)EditorApplication.timeSinceStartup;
        float started = SessionState.GetFloat(StartedKey, -1f);

        if (!EditorApplication.isPlaying)
        {
            if (started > 0f) return; // cikis surerken
            if (SessionState.GetFloat("SimurghSmokeArm", -1f) < 0f)
                SessionState.SetFloat("SimurghSmokeArm", t);
            if (t - SessionState.GetFloat("SimurghSmokeArm", t) > PlayTimeout)
                Finish("Play moduna girilemedi (zaman asimi).", 1);
            return;
        }

        if (started < 0f)
        {
            SessionState.SetFloat(StartedKey, t);
            return;
        }
        if (t - started < PlaySeconds) return;

        Finish(null, 0);
    }

    private static void Finish(string failure, int exitCode)
    {
        SessionState.SetBool(ArmedKey, false);
        EditorApplication.update -= Poll;

        var sb = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;
        sb.AppendLine("SIMURGH YORUNGE DUMAN TESTI");
        sb.AppendLine("tarih: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("unity: " + Application.unityVersion);
        sb.AppendLine();

        if (failure != null)
        {
            sb.AppendLine("SONUC: BASARISIZ — " + failure);
            WriteReport(sb.ToString());
            QuitIfRequested(exitCode);
            return;
        }

        bool ok = true;
        var comparisons = UnityEngine.Object.FindObjectsOfType<DigitalTwinTrajectoryComparison>();
        var players = UnityEngine.Object.FindObjectsOfType<DigitalTwinTrajectoryDatasetPlayer>();
        sb.AppendLine($"sahnedeki ornekler: iz katmani={comparisons.Length}, oynatici={players.Length}");
        var comparison = comparisons.Length > 0 ? comparisons[0] : null;
        var player = players.Length > 0 ? players[0] : null;
        if (comparison == null)
        {
            sb.AppendLine("HATA: DigitalTwinTrajectoryComparison bulunamadi.");
            ok = false;
        }
        else
        {
            ok &= ReportLine(sb, comparison.transform, "GPS_Trajectory", "GERCEK KONUM IZI");
            ok &= ReportLine(sb, comparison.transform, "SLAM_Trajectory", "SLAM IZI");
            sb.AppendLine();
            sb.AppendLine(string.Format(ci,
                "Sapma  anlik {0:F3} m · ort {1:F3} m · maks {2:F3} m · RMSE {3:F3} m · ornek {4}",
                comparison.DeviationNow, comparison.DeviationAvg, comparison.DeviationMax,
                comparison.DeviationRmse, comparison.SampleCount));
            if (comparison.SampleCount <= 0)
            {
                sb.AppendLine("HATA: hic GT+SLAM ornegi eslesmedi.");
                ok = false;
            }
        }

        if (player != null)
        {
            sb.AppendLine(string.Format(ci, "Oynatici: hizalama olcegi {0:F4}, hizalama sonrasi ort sapma {1:F3} m, oynatiyor={2}",
                player.LastAlignmentScale, player.LastMeanDeviationM, player.IsPlaying));
        }
        else
        {
            sb.AppendLine("HATA: veri kumesi oynaticisi bulunamadi.");
            ok = false;
        }

        var map = UnityEngine.Object.FindObjectOfType<Mapbox.Unity.Map.AbstractMap>();
        if (map != null)
        {
            int tiles = map.Root != null ? map.Root.childCount : -1;
            sb.AppendLine(string.Format(ci, "Harita: token gecerli={0} · zoom={1:F2} · merkez={2:F5},{3:F5} · yuklenen tile objesi={4}",
                map.IsAccessTokenValid, map.Zoom, map.CenterLatitudeLongitude.x, map.CenterLatitudeLongitude.y, tiles));
        }
        else
        {
            sb.AppendLine("Harita: AbstractMap bulunamadi.");
        }

        string shot = CaptureCamera(comparison != null ? TrailBounds(comparison.transform) : (Bounds?)null);
        sb.AppendLine();
        sb.AppendLine("ekran goruntusu: " + (shot ?? "alinamadi"));
        sb.AppendLine();
        sb.AppendLine("konsol hatalari: " + Errors.Count);
        for (int i = 0; i < Errors.Count && i < 20; i++)
            sb.AppendLine("  - " + Errors[i].Replace("\n", " ").Substring(0, Math.Min(300, Errors[i].Length)));

        sb.AppendLine();
        sb.AppendLine("SONUC: " + (ok ? "BASARILI — iki iz de ciziliyor." : "BASARISIZ"));
        WriteReport(sb.ToString());
        QuitIfRequested(ok ? 0 : 1);
    }

    private static bool ReportLine(StringBuilder sb, Transform parent, string childName, string label)
    {
        var child = parent.Find(childName);
        var lr = child != null ? child.GetComponent<LineRenderer>() : null;
        if (lr == null)
        {
            sb.AppendLine(label + ": LineRenderer YOK (" + childName + ")");
            return false;
        }
        int n = lr.positionCount;
        var ci = CultureInfo.InvariantCulture;
        if (n < 2)
        {
            sb.AppendLine(string.Format(ci, "{0}: vertex={1} — CIZILMEDI", label, n));
            return false;
        }

        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        float length = 0f;
        Vector3 prev = lr.GetPosition(0);
        for (int i = 0; i < n; i++)
        {
            var p = lr.GetPosition(i);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
            if (i > 0) length += Vector3.Distance(prev, p);
            prev = p;
        }
        var size = max - min;
        sb.AppendLine(string.Format(ci,
            "{0}: vertex={1} · renk={2} · kalinlik={10:F2} birim · uzunluk={3:F1} birim · alan={4:F1}x{5:F1} birim · dikey sacilma={11:F2} birim · ilk=({6:F1},{7:F1}) son=({8:F1},{9:F1})",
            label, n, ColorName(lr.startColor), length, size.x, size.z,
            lr.GetPosition(0).x, lr.GetPosition(0).z, lr.GetPosition(n - 1).x, lr.GetPosition(n - 1).z,
            lr.widthMultiplier, size.y));
        return true;
    }

    private static string ColorName(Color c) =>
        string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}",
            (int)(c.r * 255), (int)(c.g * 255), (int)(c.b * 255));

    /// <summary>Iki izin birlikte kapladigi hacim (dunya uzayi).</summary>
    private static Bounds? TrailBounds(Transform parent)
    {
        Bounds? b = null;
        foreach (var name in new[] { "GPS_Trajectory", "SLAM_Trajectory" })
        {
            var child = parent.Find(name);
            var lr = child != null ? child.GetComponent<LineRenderer>() : null;
            if (lr == null || lr.positionCount == 0) continue;
            for (int i = 0; i < lr.positionCount; i++)
            {
                var p = lr.GetPosition(i);
                if (b == null) b = new Bounds(p, Vector3.zero);
                else { var bb = b.Value; bb.Encapsulate(p); b = bb; }
            }
        }
        return b;
    }

    /// <summary>
    /// Izleri tepeden kadraja alan gecici bir kamerayla PNG yazar. ScreenCapture'a gore
    /// avantaji: pencere duzeninden ve Game view'in onde olup olmamasindan bagimsizdir;
    /// ayrica sahnenin kendi kamerasi bozulmaz.
    /// </summary>
    private static string CaptureCamera(Bounds? trail)
    {
        GameObject temp = null;
        try
        {
            var cam = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();
            if (trail.HasValue)
            {
                // Izleri ceviren, tepeden bakan gecici kamera (kanit karesi).
                temp = new GameObject("SmokeShotCamera");
                var shotCam = temp.AddComponent<Camera>();
                if (cam != null)
                {
                    shotCam.clearFlags = cam.clearFlags;
                    shotCam.backgroundColor = cam.backgroundColor;
                    shotCam.cullingMask = cam.cullingMask;
                }
                var b = trail.Value;
                float extent = Mathf.Max(b.size.x, b.size.z, 1f);
                shotCam.fieldOfView = 60f;
                shotCam.nearClipPlane = 0.1f;
                shotCam.farClipPlane = Mathf.Max(5000f, extent * 10f);
                shotCam.transform.position = b.center + Vector3.up * (extent * 1.1f + 5f);
                shotCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                cam = shotCam;
            }
            if (cam == null) return null;

            const int w = 1600, h = 900;
            var rt = new RenderTexture(w, h, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            cam.targetTexture = prev;
            RenderTexture.active = null;

            string path = Path.Combine(OutputDir, "trajectory_view.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            return path;
        }
        catch (Exception e)
        {
            return "HATA: " + e.Message;
        }
        finally
        {
            if (temp != null) UnityEngine.Object.DestroyImmediate(temp);
        }
    }

    private static string OutputDir
    {
        get
        {
            string env = Environment.GetEnvironmentVariable("SIMURGH_SMOKE_DIR");
            if (!string.IsNullOrEmpty(env)) return env;
            return Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "SmokeTest");
        }
    }

    private static void WriteReport(string text)
    {
        try
        {
            Directory.CreateDirectory(OutputDir);
            string path = Path.Combine(OutputDir, "trajectory_smoketest.txt");
            File.WriteAllText(path, text);
            Debug.Log("[DumanTesti] Rapor: " + path + "\n" + text);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DumanTesti] Rapor yazilamadi: " + e.Message);
        }
    }

    private static void Log(string message) => Debug.Log("[DumanTesti] " + message);

    private static void QuitIfRequested(int code)
    {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (string.Equals(arg, "-simurghSmokeQuit", StringComparison.Ordinal))
            {
                EditorApplication.Exit(code);
                return;
            }
        }
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
#endif
