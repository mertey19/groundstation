using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// Gorev sonu ozet/rapor ekrani — PDF: "gorev bitiminde elde edilen ... verilerin
    /// yer istasyonu arayuzune eksiksiz sunulmasi" + hakem sunumu.
    /// MissionEngine olay gunlugunu dinler; faz "complete" olunca OTOMATIK acilir.
    /// Icerik: gorev suresi, faz sureleri, hedef basarisi (X/Y), replan / relay /
    /// operator mudahale / saha ihlali sayilari, link kalitesi (min/ort), GPS-SLAM
    /// sapma istatistikleri (ort/maks/RMSE). "Raporu Kaydet" TXT olarak disa aktarir.
    /// </summary>
    public class DigitalTwinMissionSummaryPanel : MonoBehaviour
    {
        [SerializeField] private DigitalTwinMissionEngine missionEngine;
        [SerializeField] private DigitalTwinTrajectoryComparison trajectory;
        [SerializeField] private bool autoOpenOnComplete = true;

        private bool _visible;
        private float _missionStartAt = -1f, _missionEndAt = -1f;
        private int _replanCount, _interventionCount, _breachCount, _relayCount;
        private readonly List<KeyValuePair<string, float>> _phaseStarts = new List<KeyValuePair<string, float>>();
        private Vector2 _hudPos = new Vector2(-99999f, 0f);
        private bool _drag;
        private float _nextResolveAt;
        private string _savedPath = "";
        private float _savedPathUntil;

        private void Update()
        {
            if (Time.unscaledTime >= _nextResolveAt)
            {
                _nextResolveAt = Time.unscaledTime + 2f;
                if (trajectory == null) trajectory = FindObjectOfType<DigitalTwinTrajectoryComparison>();
                if (missionEngine == null)
                {
                    missionEngine = FindObjectOfType<DigitalTwinMissionEngine>();
                    if (missionEngine != null)
                    {
                        missionEngine.OnMissionEvent -= HandleEvent;
                        missionEngine.OnMissionEvent += HandleEvent;
                    }
                }
            }
        }

        private void OnDisable()
        {
            if (missionEngine != null)
                missionEngine.OnMissionEvent -= HandleEvent;
        }

        private void HandleEvent(DigitalTwinMissionEngine.MissionEvent evt)
        {
            switch (evt.eventType)
            {
                case "phase_change":
                    if (_missionStartAt < 0f) _missionStartAt = evt.timeSeconds;
                    _phaseStarts.Add(new KeyValuePair<string, float>(evt.details ?? "", evt.timeSeconds));
                    if (evt.details != null && evt.details.Contains(TwinOperationPhases.Complete))
                    {
                        _missionEndAt = evt.timeSeconds;
                        if (autoOpenOnComplete) _visible = true;
                    }
                    break;
                case "rover_replan": _replanCount++; break;
                case "operator_intervention": _interventionCount++; break;
                case "geofence_breach": _breachCount++; break;
                case "relay_mode_change": _relayCount++; break;
            }
        }

        [ContextMenu("Ozeti Ac/Kapat")]
        public void ToggleVisible() { _visible = !_visible; }

        private void OnGUI()
        {
            if (!_visible || missionEngine == null) return;
            TwinHudTheme.BeginScaledHud();

            float w = 356f, h = 268f;
            Vector2 def = new Vector2((TwinHudTheme.ScreenW - w) * 0.5f, (TwinHudTheme.ScreenH - h) * 0.5f - 40f);
            Rect r = TwinHudTheme.Drag(ref _hudPos, ref _drag, def, w, h, "twinhud_summary_v2");
            TwinHudTheme.Panel(r);

            float x = r.x + 16f, y = r.y + 12f;
            GUI.Label(new Rect(x, y, w - 32f, 18f), "GÖREV ÖZETİ  ·  Tamamlandı", TwinHudTheme.Title);
            y += 23f;
            TwinHudTheme.Separator(x, y, w - 32f);
            y += 9f;

            float dur = (_missionEndAt > 0f && _missionStartAt >= 0f) ? _missionEndAt - _missionStartAt : 0f;
            Row(x, ref y, w, "Görev süresi", FormatDuration(dur));
            Row(x, ref y, w, "Hedefler", missionEngine.TargetsReachedCount + " / " + missionEngine.TargetsTotalCount + " tamamlandı",
                missionEngine.TargetsTotalCount > 0 && missionEngine.TargetsReachedCount >= missionEngine.TargetsTotalCount ? TwinHudTheme.Good : TwinHudTheme.Warn);
            Row(x, ref y, w, "Dinamik replan", _replanCount + " kez");
            Row(x, ref y, w, "Relay geçişi", _relayCount + " kez");
            Row(x, ref y, w, "Operatör müdahalesi", _interventionCount + " kez");
            Row(x, ref y, w, "Saha ihlali", _breachCount + " kez", _breachCount > 0 ? TwinHudTheme.Bad : TwinHudTheme.Good);

            var hist = missionEngine.MeshHistory;
            if (hist != null && hist.Count > 0)
            {
                float min = float.MaxValue, sum = 0f;
                for (int i = 0; i < hist.Count; i++) { float q = hist[i].linkQualityPercent; if (q < min) min = q; sum += q; }
                Row(x, ref y, w, "Link kalitesi", string.Format(CultureInfo.InvariantCulture, "min %{0:F0} · ort %{1:F0}", min, sum / hist.Count));
            }
            if (trajectory != null && trajectory.SampleCount > 0)
                Row(x, ref y, w, "GPS–SLAM sapma", string.Format(CultureInfo.InvariantCulture, "ort {0:F2} · maks {1:F2} · RMSE {2:F2} m",
                    trajectory.DeviationAvg, trajectory.DeviationMax, trajectory.DeviationRmse));

            y = r.y + h - 34f;
            if (GUI.Button(new Rect(x, y, 110f, 22f), "Raporu Kaydet", TwinHudTheme.Button))
            {
                _savedPath = SaveReport();
                _savedPathUntil = Time.unscaledTime + 6f;
            }
            if (GUI.Button(new Rect(x + 118f, y, 78f, 22f), "Kapat", TwinHudTheme.Button))
                _visible = false;
            if (!string.IsNullOrEmpty(_savedPath) && Time.unscaledTime < _savedPathUntil)
                GUI.Label(new Rect(x + 204f, y + 3f, w - 232f, 16f), System.IO.Path.GetFileName(_savedPath), TwinHudTheme.Small);

            TwinHudTheme.EndScaledHud();
        }

        private void Row(float x, ref float y, float w, string label, string value, Color? valueColor = null)
        {
            GUI.Label(new Rect(x, y, 150f, 16f), label, TwinHudTheme.Small);
            var st = new GUIStyle(TwinHudTheme.Label) { fontSize = 12 };
            st.normal.textColor = valueColor ?? TwinHudTheme.TextPrimary;
            GUI.Label(new Rect(x + 152f, y, w - 184f, 16f), value, st);
            y += 20f;
        }

        private string SaveReport()
        {
            try
            {
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "digital-twin-logs");
                System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir, "gorev_ozeti_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("SIMURGH X.1 — GOREV OZETI  (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")");
                sb.AppendLine(new string('-', 52));
                float dur = (_missionEndAt > 0f && _missionStartAt >= 0f) ? _missionEndAt - _missionStartAt : 0f;
                sb.AppendLine("Gorev suresi        : " + FormatDuration(dur));
                sb.AppendLine("Hedefler            : " + missionEngine.TargetsReachedCount + "/" + missionEngine.TargetsTotalCount);
                sb.AppendLine("Dinamik replan      : " + _replanCount);
                sb.AppendLine("Relay gecisi        : " + _relayCount);
                sb.AppendLine("Operator mudahalesi : " + _interventionCount);
                sb.AppendLine("Saha ihlali         : " + _breachCount);
                if (trajectory != null && trajectory.SampleCount > 0)
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "GPS-SLAM sapma      : ort {0:F2} m, maks {1:F2} m, RMSE {2:F2} m ({3} ornek)",
                        trajectory.DeviationAvg, trajectory.DeviationMax, trajectory.DeviationRmse, trajectory.SampleCount));
                sb.AppendLine();
                sb.AppendLine("OLAY GUNLUGU:");
                var log = missionEngine.EventLog;
                for (int i = 0; i < log.Count; i++)
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  [{0,8:F1}s] {1}: {2}", log[i].timeSeconds, log[i].eventType, log[i].details));
                System.IO.File.WriteAllText(file, sb.ToString());
                Debug.Log("[GorevOzeti] Rapor kaydedildi: " + file);
                return file;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GorevOzeti] Rapor kaydedilemedi: " + e.Message);
                return null;
            }
        }

        private static string FormatDuration(float seconds)
        {
            if (seconds <= 0f) return "—";
            int m = Mathf.FloorToInt(seconds / 60f);
            int s = Mathf.FloorToInt(seconds % 60f);
            return string.Format("{0:D2}:{1:D2}", m, s);
        }
    }
}
