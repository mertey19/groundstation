#if UNITY_EDITOR
using UnityEditor;

// Historical findings remain in INCELEME-2026-09-08.md and its original log.
// The former audit menu now runs assertions against the corrected behavior.
public static class GroundStationAuditChecks
{
    [MenuItem("Tools/Simurgh/Yer Istasyonu Inceleme Raporu")]
    public static void Run() => GroundStationRegressionChecks.Run();
}
#endif

