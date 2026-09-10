using System.Collections.Generic;
using GroundStation.UI;
using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.DigitalTwin
{
    /// <summary>One survey viewport; legacy prototype graphics never cover the camera.</summary>
    public class DigitalTwinSiteWorkspace : MonoBehaviour
    {
        private readonly Dictionary<Graphic,bool> _legacyGraphics = new Dictionary<Graphic,bool>();
        private readonly Dictionary<CanvasGroup,Vector3> _mapGroups = new Dictionary<CanvasGroup,Vector3>();
        private DigitalTwinUIController _controller;
        private SimurghSiteImporter _site;
        private LiveMapReceiver _liveReceiver;
        private LiveMapView _liveView;
        private bool _liveMode = true;
        private string _exportInfo = "";
        public bool LiveMode => _liveMode;
        private DigitalTwinRemoteState _state;
        private DigitalTwinPresenter _presenter;
        private bool _presenterWasEnabled, _open;
        private GUIStyle _heading, _caption, _button, _body, _muted;
        private RectTransform _viewport;
        private Vector2 _anchorMin, _anchorMax, _offsetMin, _offsetMax;

        public void SetOpen(bool open, DigitalTwinUIController controller)
        {
            _controller = controller;
            if (_open == open) return;
            _open = open;
            if (!open) { if (_liveView != null) _liveView.Hide(); Restore(); return; }
            _site = FindObjectOfType<SimurghSiteImporter>();
            _state = FindObjectOfType<DigitalTwinRemoteState>();
            _presenter = GetComponent<DigitalTwinPresenter>();
            if (_presenter != null) { _presenterWasEnabled = _presenter.enabled; _presenter.enabled = false; }
            var view = GetComponent<DigitalTwin3DView>();
            var raw = view != null ? view.ViewportImage : null;
            foreach(var graphic in GetComponentsInChildren<Graphic>(true))
            {
                if(graphic == raw) continue;
                _legacyGraphics[graphic] = graphic.enabled;
                graphic.enabled = false;
            }
            if (raw != null)
            {
                _viewport = raw.transform.parent as RectTransform;
                // Existing RawImage can be on the viewport itself.
                if(raw.name == "TwinViewPort" || raw.name == "TwinViewport") _viewport = raw.rectTransform;
                if(_viewport != null)
                {
                    _anchorMin=_viewport.anchorMin;_anchorMax=_viewport.anchorMax;
                    _offsetMin=_viewport.offsetMin;_offsetMax=_viewport.offsetMax;
                    _viewport.anchorMin=Vector2.zero;_viewport.anchorMax=Vector2.one;
                    _viewport.offsetMin=_viewport.offsetMax=Vector2.zero;
                }
                raw.enabled=true;raw.raycastTarget=false;
            }
            foreach(var rect in FindObjectsOfType<RectTransform>(true))
            {
                if (rect.IsChildOf(transform) || rect == transform) continue;
                // Suppress sibling UI roots too: an invisible route Start button must not
                // receive the same click as the IMGUI camera toolbar above it.
                if(rect.parent != transform.parent && rect.name != "TelemetryPanel" && rect.name != "MapStylePanel" && rect.name != "SurveyPanelToggleButton" && rect.name != "SurveyMappingPanel"
                    && rect.GetComponent<DroneSpeedAltitudePanel>() == null && rect.GetComponent<AltitudePanelAppearance>() == null) continue;
                var group=rect.GetComponent<CanvasGroup>();if(group==null)group=rect.gameObject.AddComponent<CanvasGroup>();
                _mapGroups[group]=new Vector3(group.alpha,group.interactable?1:0,group.blocksRaycasts?1:0);
                group.alpha=0;group.interactable=false;group.blocksRaycasts=false;
            }
            transform.SetAsLastSibling();
            SetLiveMode(_liveMode);
        }

        public void SetLiveMode(bool live)
        {
            _liveMode = live;
            if (!_open) return;
            _liveReceiver = FindObjectOfType<LiveMapReceiver>();
            if (_liveView == null) _liveView = GetComponent<LiveMapView>() ?? gameObject.AddComponent<LiveMapView>();
            if (live)
            {
                if (_site != null && _site.IsVisible) _site.Hide();
                _liveView.Show(_liveReceiver);
            }
            else
            {
                _liveView.Hide();
                if (_site == null) _site = FindObjectOfType<SimurghSiteImporter>() ?? new GameObject("SimurghSite").AddComponent<SimurghSiteImporter>();
                _site.Show();
            }
        }

        private void Restore()
        {
            foreach(var pair in _legacyGraphics) if(pair.Key != null)pair.Key.enabled=pair.Value;
            _legacyGraphics.Clear();
            foreach(var pair in _mapGroups) if(pair.Key != null)
            { pair.Key.alpha=pair.Value.x;pair.Key.interactable=pair.Value.y>0;pair.Key.blocksRaycasts=pair.Value.z>0; }
            _mapGroups.Clear();
            if(_presenter!=null)_presenter.enabled=_presenterWasEnabled;
            if(_viewport!=null) { _viewport.anchorMin=_anchorMin;_viewport.anchorMax=_anchorMax;_viewport.offsetMin=_offsetMin;_viewport.offsetMax=_offsetMax; }
        }

        private void Styles()
        {
            if(_heading!=null)return;
            _heading=new GUIStyle(TwinHudTheme.Title){fontSize=24};
            _caption=new GUIStyle(TwinHudTheme.Small){fontSize=12};
            _body=new GUIStyle(TwinHudTheme.Label){fontSize=14,clipping=TextClipping.Clip,richText=false};
            _muted=new GUIStyle(_body){normal={textColor=TwinHudTheme.TextSecondary}};
            _button=new GUIStyle(TwinHudTheme.Button){fontSize=13};
            _caption.richText = _button.richText = false;
        }

        private void OnGUI()
        {
            if(!_open)return;
            TwinHudTheme.BeginScaledHud();Styles();
            float w=TwinHudTheme.ScreenW,h=TwinHudTheme.ScreenH;
            TwinHudTheme.Fill(new Rect(0,h-43,w,43),new Color(0.035f,0.052f,0.075f,0.94f),0);
            TwinHudTheme.Panel(new Rect(16,16,w-32,78));
            TwinHudTheme.Fill(new Rect(34,35,4,37),new Color(0.23f,0.82f,0.72f),2);
            GUI.Label(new Rect(50,28,290,32),"DİJİTAL İKİZ",_heading);
            GUI.Label(new Rect(51,61,380,19),_liveMode?"Uçuş sırasında ölçülen çevre":(_site!=null?_site.SiteName:"Referans saha"),_caption);
            Color oldColor = GUI.backgroundColor;
            GUI.backgroundColor = _liveMode ? new Color(0.3f,0.9f,0.8f) : oldColor;
            if (GUI.Button(new Rect(w-570,34,170,40),"Canlı 3B tarama",_button)) SetLiveMode(true);
            GUI.backgroundColor = !_liveMode ? new Color(0.3f,0.9f,0.8f) : oldColor;
            if (GUI.Button(new Rect(w-392,34,170,40),"Referans ortofoto",_button)) SetLiveMode(false);
            GUI.backgroundColor = oldColor;
            if(GUI.Button(new Rect(w-207,34,171,40),"Haritaya dön  ›",_button))_controller.SetViewOpen(false);

            if (_liveMode) { DrawLive(w,h); TwinHudTheme.EndScaledHud(); return; }

            var card=new Rect(16,110,344,266);TwinHudTheme.Panel(card);
            GUI.Label(new Rect(32,124,290,22),"SAHA VE VERİ",TwinHudTheme.Title);
            bool fresh=_state!=null&&_state.HasFreshMessage;
            string source=_state!=null?_state.LastSourceId:"";
            bool replay=source.Contains("dataset")||source.Contains("demo")||source.Contains("sample")||source.Contains("test");
            Color badge=fresh? (replay?TwinHudTheme.Warn:TwinHudTheme.Good):TwinHudTheme.TextSecondary;
            TwinHudTheme.Fill(new Rect(32,157,7,7),badge,4);
            GUI.Label(new Rect(48,148,294,24),fresh?(replay?"Kayıt / test verisi alınıyor":"Telemetri alınıyor"):"Telemetri bekleniyor",_body);
            GUI.Label(new Rect(32,183,64,24),"İHA",_muted);
            GUI.Label(new Rect(116,183,220,24),_site!=null?_site.UavStatus:"—",_body);
            GUI.Label(new Rect(32,210,64,24),"Rover",_muted);
            GUI.Label(new Rect(116,210,220,24),_site!=null?_site.RoverStatus:"—",_body);
            GUI.Label(new Rect(32,239,312,20),_state!=null&&_state.HasFreshTelemetry ? _state.LastAltitudeText+"  ·  "+_state.LastSpeedText : "Yükseklik: —   ·   Hız: —",_caption);
            GUI.Label(new Rect(32,262,296,20),_site!=null?$"{_site.Width:F0} × {_site.Height:F0} m  ·  {_site.Width*_site.Height/10000f:F2} ha":"—",_caption);
            TwinHudTheme.Fill(new Rect(32,292,312,1),new Color(0.3f,0.4f,0.5f,0.3f),0);
            GUI.Label(new Rect(32,300,298,20),"Yaklaşık model katmanları",_caption);
            if(_site!=null)
            {
                if(GUI.Button(new Rect(32,328,150,30),_site.TreesVisible?"Ağaçlar  • açık":"Ağaçlar",_button))_site.SetTreesVisible(!_site.TreesVisible);
                if(GUI.Button(new Rect(190,328,154,30),_site.BuildingsVisible?"Binalar  • açık":"Bina taslakları",_button))_site.SetBuildingsVisible(!_site.BuildingsVisible);
            }

            var bar=new Rect(w/2-260,h-101,520,53);TwinHudTheme.Panel(bar);
            bool enabled=GUI.enabled;GUI.enabled=_site!=null&&_site.Orbit!=null;
            if(GUI.Button(new Rect(bar.x+10,bar.y+10,104,33),"Sahaya dön",_button))_site.Orbit.ResetView();
            if(GUI.Button(new Rect(bar.x+121,bar.y+10,104,33),"Üstten bak",_button))_site.Orbit.TopView();
            GUI.enabled=enabled&&_site!=null&&_site.UavVisible;
            if(GUI.Button(new Rect(bar.x+232,bar.y+10,132,33),"İHA'ya odaklan",_button))_site.FocusUav();
            GUI.enabled=enabled&&_site!=null&&_site.Orbit!=null;
            if(GUI.Button(new Rect(bar.x+372,bar.y+10,64,33),"−",_button))_site.Orbit.Zoom(1.15f);
            if(GUI.Button(new Rect(bar.x+444,bar.y+10,64,33),"+",_button))_site.Orbit.Zoom(0.85f);
            GUI.enabled=enabled;
            GUI.Label(new Rect(w/2-265,h-36,560,22),"Sol sürükle: döndür     Sağ sürükle: kaydır     Tekerlek: yakınlaştır",_caption);
            GUI.Label(new Rect(24,h-37,450,22),"Yükseklik modeli yok · Araçlar zemine izdüşürülür",_caption);
            if(_site!=null&&_site.Orbit!=null)GUI.Label(new Rect(w-184,h-37,160,22),$"Kuzey  {Mathf.Repeat(_site.Orbit.yaw,360):F0}°",_caption);
            if(_site!=null&&!string.IsNullOrEmpty(_site.LoadError))
            {TwinHudTheme.Panel(new Rect(w/2-240,h/2-45,480,90));GUI.Label(new Rect(w/2-223,h/2-27,446,62),_site.LoadError,_body);}
            TwinHudTheme.EndScaledHud();
        }

        private void DrawLive(float w, float h)
        {
            var map = _liveReceiver != null ? _liveReceiver.Current : null;
            bool fresh = _liveReceiver != null && _liveReceiver.IsFresh;
            bool test = map != null && map.Header.dataKind == "test";
            var card = new Rect(16,110,326,278); TwinHudTheme.Panel(card);
            GUI.Label(new Rect(32,125,294,24),"ÇEVRE HARİTASI",TwinHudTheme.Title);
            Color color = map == null ? TwinHudTheme.TextSecondary : fresh && !test ? TwinHudTheme.Good : TwinHudTheme.Warn;
            TwinHudTheme.Fill(new Rect(32,161,7,7),color,4);
            string state = map == null ? "Sensör haritası bekleniyor" : test ? "TEST VERİSİ · gerçek uçuş değil" : fresh ? "Canlı harita güncelleniyor" : "Veri kesildi · son harita korunuyor";
            GUI.Label(new Rect(48,152,282,24),state,_body);
            GUI.Label(new Rect(32,190,294,22),map == null ? "Nokta sayısı   —" : $"{map.Positions.Length:N0} ölçüm noktası  ·  sürüm {map.Header.sequence}",_body);
            GUI.Label(new Rect(32,220,294,21),map == null ? "Son ölçüm   —" : $"Son ölçüm   {System.Math.Max(0,(LiveMapProtocol.NowMs-map.Header.capturedAtMs)/1000.0):F1} sn önce",_caption);
            GUI.Label(new Rect(32,244,294,21),map == null ? "Kaynak   —" : "Kaynak   " + map.Header.sourceId,_caption);
            GUI.Label(new Rect(32,268,294,21),map == null ? "Örnekleme   —" : $"Örnekleme   {map.Header.voxelSizeM:F2} m · doğruluk ölçümü değildir",_caption);
            TwinHudTheme.Fill(new Rect(32,303,294,1),new Color(0.3f,0.4f,0.5f,0.3f),0);
            GUI.Label(new Rect(32,315,294,21),"Yerel SLAM koordinatları · metre",_caption);
            GUI.Label(new Rect(32,340,294,30),"GPS / uydu haritası hizalaması yapılmadı",_caption);
            if (map == null)
            {
                var empty = new Rect(w/2-175,h/2-57,470,116); TwinHudTheme.Panel(empty);
                GUI.Label(new Rect(empty.x+22,empty.y+18,430,28),"İlk 3B ölçüm bekleniyor",_heading);
                GUI.Label(new Rect(empty.x+22,empty.y+55,430,43),"Jetson haritası geldiğinde çevre burada oluşacak.\nHenüz ölçülmeyen alanlar boş bırakılır.",_body);
            }
            string error = _liveView != null && !string.IsNullOrEmpty(_liveView.Error) ? _liveView.Error : _liveReceiver != null ? _liveReceiver.LastError : "Harita alıcısı bulunamadı.";
            if (!string.IsNullOrEmpty(error))
            {
                TwinHudTheme.Panel(new Rect(w/2-250,110,500,60));
                GUI.Label(new Rect(w/2-234,120,468,42),error,new GUIStyle(_caption){wordWrap=true});
            }
            var bar = new Rect(w/2-310,h-101,620,53); TwinHudTheme.Panel(bar);
            bool enabled = GUI.enabled; GUI.enabled = _liveView != null && _liveView.Orbit != null;
            if (GUI.Button(new Rect(bar.x+10,bar.y+10,120,33),"Haritaya odaklan",_button)) _liveView.Fit();
            if (GUI.Button(new Rect(bar.x+137,bar.y+10,94,33),"Üstten bak",_button)) _liveView.Orbit.TopView();
            GUI.enabled = enabled && map != null;
            if (GUI.Button(new Rect(bar.x+238,bar.y+10,114,33),"PLY kaydet",_button))
            {
                try { _exportInfo = "Kaydedildi: " + System.IO.Path.GetFileName(_liveReceiver.ExportPly()); }
                catch (System.Exception ex) { _exportInfo = ex.Message; }
            }
            GUI.enabled = enabled && _liveReceiver != null;
            if (GUI.Button(new Rect(bar.x+359,bar.y+10,140,33),"Oturumu sıfırla",_button)) { _liveReceiver.ResetSession(); _exportInfo = ""; }
            GUI.enabled = enabled && _liveView != null && _liveView.Orbit != null;
            if (GUI.Button(new Rect(bar.x+506,bar.y+10,47,33),"−",_button)) _liveView.Orbit.Zoom(1.15f);
            if (GUI.Button(new Rect(bar.x+560,bar.y+10,47,33),"+",_button)) _liveView.Orbit.Zoom(0.85f);
            GUI.enabled = enabled;
            if (!string.IsNullOrEmpty(_exportInfo)) GUI.Label(new Rect(w/2-300,h-128,610,22),_exportInfo,_caption);
            GUI.Label(new Rect(24,h-36,430,22),"Nokta bulutu · boş alanlar henüz ölçülmedi",_caption);
            GUI.Label(new Rect(w/2-170,h-36,460,22),"Sol: döndür   Sağ: kaydır   Tekerlek: yakınlaştır",_caption);
            GUI.Label(new Rect(w-203,h-36,180,22),_liveReceiver != null && _liveReceiver.Listening ? "Harita alıcısı hazır" : "Harita alıcısı kapalı",_caption);
        }
    }
}
