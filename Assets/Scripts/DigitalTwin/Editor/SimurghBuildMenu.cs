#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Tools > Simurgh > Windows Surumu Al (x64)
///
/// Unity kurulu olmayan bir bilgisayarda calisan bagimsiz surum uretir.
/// Komut satirindan:
///
///   Unity.exe -quit -batchmode -projectPath &lt;proje&gt; \
///             -executeMethod SimurghBuildMenu.BuildWindows64Cli -logFile build.log
///
/// Cikti klasoru: SIMURGH_BUILD_DIR ortam degiskeni, yoksa &lt;proje&gt;/Build/Windows.
/// Klasorun TAMAMI (exe + _Data) hedef bilgisayara kopyalanmalidir.
/// </summary>
public static class SimurghBuildMenu
{
    private const string MainScene = "Assets/deneme.unity";

    [MenuItem("Tools/Simurgh/Windows Surumu Al (x64)")]
    public static void BuildWindows64() => Build(false);

    [MenuItem("Tools/Simurgh/Windows Surumu Al (gelistirici, konsollu)")]
    public static void BuildWindows64Dev() => Build(true);

    /// <summary>Komut satiri girisi: hata durumunda 1 ile cikar.</summary>
    public static void BuildWindows64Cli()
    {
        bool dev = Environment.GetCommandLineArgs().Any(a => a == "-simurghDevBuild");
        bool ok = Build(dev);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static bool Build(bool development)
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            // Proje "Scenes In Build" listesi bostu; bu haliyle alinan surum bos bir
            // uygulama olurdu. Ana sahneyi listeye kalici olarak ekle.
            if (!File.Exists(MainScene))
            {
                Debug.LogError("[Surum] Ana sahne bulunamadi: " + MainScene);
                return false;
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScene, true) };
            scenes = new[] { MainScene };
            Debug.Log("[Surum] Build listesi bostu; " + MainScene + " eklendi.");
        }

        // Baska bir bilgisayarda ilk acilista makul davransin: pencereli, yeniden
        // boyutlanabilir ve arka planda calismaya devam eden bir uygulama.
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.defaultIsNativeResolution = false;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);

        EnsureSceneObjects(scenes[0]);

        string dir = OutputDir;
        string exe = Path.Combine(dir, PlayerSettings.productName + ".exe");
        Directory.CreateDirectory(dir);

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = exe,
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = development
                ? BuildOptions.Development | BuildOptions.AllowDebugging
                : BuildOptions.None
        };

        Debug.Log($"[Surum] Derleniyor -> {exe}  (sahne: {string.Join(", ", scenes)}, gelistirici={development})");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;

        var sb = new StringBuilder();
        sb.AppendLine("SIMURGH WINDOWS SURUMU");
        sb.AppendLine("tarih   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("unity   : " + Application.unityVersion);
        sb.AppendLine("sonuc   : " + summary.result);
        sb.AppendLine("sure    : " + summary.totalTime);
        sb.AppendLine("boyut   : " + (summary.totalSize / 1048576.0).ToString("F1") + " MB");
        sb.AppendLine("hata    : " + summary.totalErrors + ", uyari: " + summary.totalWarnings);
        sb.AppendLine("cikti   : " + exe);
        sb.AppendLine("sahneler: " + string.Join(", ", scenes));

        if (summary.result != BuildResult.Succeeded)
        {
            foreach (var step in report.steps)
                foreach (var msg in step.messages)
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        sb.AppendLine("  HATA: " + msg.content);
        }
        else
        {
            WriteReadme(dir);
        }

        string reportPath = Path.Combine(dir, "surum_raporu.txt");
        try { File.WriteAllText(reportPath, sb.ToString()); } catch { /* rapor yazilamadi, log yeterli */ }
        Debug.Log("[Surum] " + sb);
        return summary.result == BuildResult.Succeeded;
    }

    /// <summary>
    /// Bagimsiz surumun kendi kendine gosterim yapabilmesi icin sahnede yorunge katmani
    /// ve veri kumesi oynaticisi bulunmali. Oynatici KENDILIGINDEN BASLAMAZ (canli veriyle
    /// cakismasin); operator YORUNGE panelindeki "Demo veriyi oynat" dugmesini kullanir.
    /// </summary>
    private static void EnsureSceneObjects(string scenePath)
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        bool opened = false;
        if (!scene.isLoaded || scene.path != scenePath)
        {
            scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            opened = true;
        }

        bool changed = false;
        if (UnityEngine.Object.FindObjectOfType<GroundStation.DigitalTwin.DigitalTwinTrajectoryComparison>() == null)
        {
            new GameObject("DigitalTwinTrajectoryComparison")
                .AddComponent<GroundStation.DigitalTwin.DigitalTwinTrajectoryComparison>();
            changed = true;
            Debug.Log("[Surum] Sahneye yorunge katmani eklendi.");
        }

        var player = UnityEngine.Object.FindObjectOfType<GroundStation.DigitalTwin.DigitalTwinTrajectoryDatasetPlayer>();
        if (player == null)
        {
            player = new GameObject("DigitalTwinTrajectoryDatasetPlayer")
                .AddComponent<GroundStation.DigitalTwin.DigitalTwinTrajectoryDatasetPlayer>();
            changed = true;
            Debug.Log("[Surum] Sahneye veri kumesi oynaticisi eklendi (elle baslatilir).");
        }
        var so = new SerializedObject(player);
        var playOnStart = so.FindProperty("playOnStart");
        if (playOnStart != null && playOnStart.boolValue)
        {
            playOnStart.boolValue = false;   // canli veriyle cakismasin
            so.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }

        if (changed || opened)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log("[Surum] Sahne kaydedildi: " + scenePath);
        }
    }

    /// <summary>Hedef bilgisayarda uygulamayi acacak kisi icin kisa kullanim notu.</summary>
    private static void WriteReadme(string dir)
    {
        string text = @"SIMURGH YER ISTASYONU — Windows surumu
=======================================

Calistirma
----------
Bu klasorun TAMAMINI kopyalayin (exe tek basina calismaz; yanindaki
*_Data klasoru zorunludur), sonra " + PlayerSettings.productName + @".exe dosyasina cift tiklayin.

Unity kurulu olmasi gerekmez. Windows 10/11 64-bit yeterlidir.
Pencere modunda 1600x900 acilir; kenarindan yeniden boyutlandirilabilir,
Alt+Enter ile tam ekran yapilabilir. Kapatmak icin Alt+F4.

Veri baglantisi
---------------
Uygulama UDP 19090 portunu dinler (ACK: 19091). Ayni agdaki bir
gonderici (ROS koprusu, ORB-SLAM3 besleyici vb.) JSON poz/telemetri
mesajlarini bu porta gonderir. Windows Guvenlik Duvari ilk acilista
izin isterse ""Ozel aglar"" icin izin verin.

Yorunge gosterimi
-----------------
Gercek konum mavi, SLAM kestirimi turuncu cizgi olarak birikerek cizilir;
arac ilerledikce izler kendiliginden uzar, dugmeye basmak gerekmez.

Elinizde canli veri yokken sistemi gostermek icin: sol taraftaki YORUNGE
panelinde ""Demo veriyi oynat"" dugmesine basin. Uygulamanin icinde gelen
ornek veri kumesi (EuRoC MH01 gercek konum + SLAM kestirimi) oynatilir.
Canli veriyle cakismamasi icin demo kendiliginden BASLAMAZ.

Kayitlar
--------
CSV kayitlari ve gunluk dosyasi su klasore yazilir:
  %USERPROFILE%\AppData\LocalLow\" + PlayerSettings.companyName + @"\" + PlayerSettings.productName + @"\

Sorun giderme
-------------
- Uygulama acilmiyorsa: yukaridaki klasordeki Player.log dosyasina bakin.
- Harita gri geliyorsa internet baglantisini ve Mapbox erisimini kontrol edin.
";
        try { File.WriteAllText(Path.Combine(dir, "OKUBENI.txt"), text, new UTF8Encoding(true)); }
        catch (Exception e) { Debug.LogWarning("[Surum] OKUBENI yazilamadi: " + e.Message); }
    }

    private static string OutputDir
    {
        get
        {
            string env = Environment.GetEnvironmentVariable("SIMURGH_BUILD_DIR");
            if (!string.IsNullOrEmpty(env)) return env;
            return Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Build", "Windows");
        }
    }
}
#endif
