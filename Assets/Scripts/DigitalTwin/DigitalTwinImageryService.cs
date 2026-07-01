using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// JSON mesajindaki <see cref="TwinImageryBlock"/> ile StreamingAssets veya Resources'tan
    /// goruntu yukler; Digital Twin viewport uzerinde RawImage olarak gosterir.
    /// Google Earth Studio: PNG/JPEG kare dizisi veya tek stitch dosyasi bu yolla baglanir.
    /// </summary>
    public class DigitalTwinImageryService : MonoBehaviour
    {
        [SerializeField] private DigitalTwinRemoteState remoteState;
        [SerializeField] private RectTransform twinViewport;
        [SerializeField] private string autoTwinViewportName = "TwinViewPort";

        private RawImage _raw;
        private Texture2D _loadedTexture;
        private bool _textureIsOwned;
        private VideoPlayer _videoPlayer;
        private RenderTexture _videoRenderTexture;

        // Canli kamera akisi paneli icin son goruntu texture'ina erisim.
        public Texture CurrentTexture => _videoRenderTexture != null ? (Texture)_videoRenderTexture : _loadedTexture;
        public bool HasActiveImagery => _raw != null && _raw.enabled && CurrentTexture != null;

        private void Awake()
        {
            if (remoteState == null)
                remoteState = FindObjectOfType<DigitalTwinRemoteState>();
        }

        /// <summary>
        /// Mesajdan gelen goruntu blogunu uygular; basari durumunda true doner.
        /// </summary>
        public bool TryApplyImagery(TwinImageryBlock block, string sourceId)
        {
            if (block == null)
                return false;

            EnsureRawImageTarget();
            if (_raw == null)
            {
                SetStatus(block, sourceId, "Twin viewport bulunamadi (TwinViewPort)");
                return false;
            }

            if (IsVideoImagery(block))
                return TryApplyVideoImagery(block, sourceId);

            StopVideoIfPlaying();

            string resolved = ResolveFilePath(block);
            if (string.IsNullOrEmpty(resolved) && string.IsNullOrEmpty(block.resourceTexturePath))
            {
                SetStatus(block, sourceId, "imagery: yol yok (streamingAssetsFile / folder+pattern / resourceTexturePath)");
                return false;
            }

            Texture2D tex = null;
            bool owned = false;

            if (!string.IsNullOrEmpty(block.resourceTexturePath))
            {
                tex = Resources.Load<Texture2D>(block.resourceTexturePath);
                if (tex == null)
                {
                    SetStatus(block, sourceId, "Resources yuklenemedi: " + block.resourceTexturePath);
                    return false;
                }
                owned = false;
            }
            else
            {
                if (!TryLoadTextureFromFile(resolved, out tex))
                {
                    SetStatus(block, sourceId, "Dosya okunamadi: " + resolved);
                    return false;
                }
                owned = true;
            }

            ReleaseOwnedTexture();
            _loadedTexture = tex;
            _textureIsOwned = owned;

            _raw.texture = _loadedTexture;
            float a = Mathf.Clamp01(block.overlayAlpha <= 0f ? 0.88f : block.overlayAlpha);
            var col = _raw.color;
            col.a = a;
            _raw.color = col;
            _raw.enabled = true;
            _raw.raycastTarget = false;

            string shortPath = Path.GetFileName(resolved);
            if (string.IsNullOrEmpty(shortPath))
                shortPath = block.resourceTexturePath ?? "";
            SetStatus(block, sourceId, string.Format(CultureInfo.InvariantCulture, "OK | {0}", shortPath));
            return true;
        }

        private static bool IsVideoImagery(TwinImageryBlock block)
        {
            if (!string.IsNullOrEmpty(block.streamingAssetsVideoFile))
                return true;
            if (string.IsNullOrEmpty(block.mode))
                return false;
            return block.mode.IndexOf("video", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryApplyVideoImagery(TwinImageryBlock block, string sourceId)
        {
            StopVideoIfPlaying();
            ReleaseOwnedTexture();

            string rel = block.streamingAssetsVideoFile != null
                ? block.streamingAssetsVideoFile.Trim().TrimStart('/', '\\')
                : "";
            if (string.IsNullOrEmpty(rel))
            {
                SetStatus(block, sourceId, "video: streamingAssetsVideoFile bos");
                return false;
            }

            string fullPath = Path.Combine(Application.streamingAssetsPath, rel);
            if (fullPath.IndexOf("://", StringComparison.Ordinal) >= 0 ||
                fullPath.StartsWith("jar:", StringComparison.Ordinal))
            {
                SetStatus(block, sourceId, "video: URL yolu desteklenmiyor");
                return false;
            }

            if (!File.Exists(fullPath))
            {
                SetStatus(block, sourceId, "video dosyasi yok: " + fullPath);
                return false;
            }

            int vw = Mathf.Clamp(Screen.width, 640, 1920);
            int vh = Mathf.Clamp(Screen.height, 360, 1080);
            _videoRenderTexture = new RenderTexture(vw, vh, 0, RenderTextureFormat.ARGB32)
            {
                name = "TwinGESVideoRT"
            };
            _videoRenderTexture.Create();

            _raw.texture = _videoRenderTexture;
            float a = Mathf.Clamp01(block.overlayAlpha <= 0f ? 0.95f : block.overlayAlpha);
            var c = _raw.color;
            c.a = a;
            _raw.color = c;
            _raw.enabled = true;
            _raw.raycastTarget = false;

            _videoPlayer = _raw.gameObject.GetComponent<VideoPlayer>();
            if (_videoPlayer == null)
                _videoPlayer = _raw.gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            bool loop = string.IsNullOrEmpty(block.videoLoopMode) ||
                        block.videoLoopMode.Equals("loop", StringComparison.OrdinalIgnoreCase);
            _videoPlayer.isLooping = loop;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _videoRenderTexture;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _videoPlayer.skipOnDrop = true;

            try
            {
                _videoPlayer.url = new Uri(fullPath).AbsoluteUri;
            }
            catch
            {
                _videoPlayer.url = fullPath;
            }

            _videoPlayer.Play();
            SetStatus(block, sourceId, "OK | video " + Path.GetFileName(fullPath));
            return true;
        }

        private void StopVideoIfPlaying()
        {
            if (_videoPlayer != null)
            {
                try
                {
                    _videoPlayer.Stop();
                }
                catch { /* ignore */ }
                _videoPlayer.targetTexture = null;
                Destroy(_videoPlayer);
                _videoPlayer = null;
            }

            if (_videoRenderTexture != null)
            {
                if (_raw != null && _raw.texture == _videoRenderTexture)
                    _raw.texture = null;
                _videoRenderTexture.Release();
                Destroy(_videoRenderTexture);
                _videoRenderTexture = null;
            }
        }

        private void SetStatus(TwinImageryBlock block, string sourceId, string detail)
        {
            if (remoteState == null)
                return;
            remoteState.ApplyImageryStatus(block, sourceId, detail);
        }

        private static string ResolveFilePath(TwinImageryBlock block)
        {
            if (!string.IsNullOrEmpty(block.streamingAssetsFile))
                return Path.Combine(Application.streamingAssetsPath, block.streamingAssetsFile.TrimStart('/', '\\'));

            if (!string.IsNullOrEmpty(block.streamingAssetsFolder) && !string.IsNullOrEmpty(block.fileNamePattern))
            {
                string name = string.Format(CultureInfo.InvariantCulture, block.fileNamePattern, block.frameNumber);
                return Path.Combine(Application.streamingAssetsPath, block.streamingAssetsFolder.TrimStart('/', '\\'), name);
            }

            return null;
        }

        private static bool TryLoadTextureFromFile(string fullPath, out Texture2D texture)
        {
            texture = null;
            if (string.IsNullOrEmpty(fullPath))
                return false;

            try
            {
                if (fullPath.IndexOf("://", System.StringComparison.Ordinal) >= 0 ||
                    fullPath.StartsWith("jar:", System.StringComparison.Ordinal))
                {
                    Debug.LogWarning("[DigitalTwinImagery] Bu platformda URL tabanli StreamingAssets icin async yukleme gerekir; simdilik atlandi: " + fullPath);
                    return false;
                }

                if (!File.Exists(fullPath))
                    return false;
                byte[] bytes = File.ReadAllBytes(fullPath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes))
                {
                    Destroy(tex);
                    return false;
                }
                texture = tex;
                return true;
            }
            catch
            {
                if (texture != null)
                    Destroy(texture);
                texture = null;
                return false;
            }
        }

        private void EnsureRawImageTarget()
        {
            if (_raw != null)
                return;

            if (twinViewport == null)
            {
                foreach (var rt in FindObjectsOfType<RectTransform>(true))
                {
                    if (rt == null) continue;
                    string n = rt.name.ToLowerInvariant();
                    if (n.Contains("twinview"))
                    {
                        twinViewport = rt;
                        break;
                    }
                }
            }

            if (twinViewport == null)
            {
                var panel = GameObject.Find("DigitalTwinPanel");
                if (panel != null)
                {
                    var t = panel.transform.Find(autoTwinViewportName);
                    if (t != null)
                        twinViewport = t as RectTransform ?? t.GetComponent<RectTransform>();
                }
            }

            if (twinViewport == null)
                return;

            Transform existing = twinViewport.Find("TwinGESImagery");
            if (existing != null)
            {
                _raw = existing.GetComponent<RawImage>();
                if (_raw != null)
                    return;
            }

            var go = new GameObject("TwinGESImagery", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(DigitalTwinGesImageryView));
            go.transform.SetParent(twinViewport, false);
            var rtNew = go.GetComponent<RectTransform>();
            rtNew.anchorMin = Vector2.zero;
            rtNew.anchorMax = Vector2.one;
            rtNew.pivot = new Vector2(0.5f, 0.5f);
            rtNew.offsetMin = Vector2.zero;
            rtNew.offsetMax = Vector2.zero;
            rtNew.SetAsFirstSibling();

            _raw = go.GetComponent<RawImage>();
            _raw.raycastTarget = false;
            var ic = _raw.color;
            ic.a = 0f;
            _raw.color = ic;
            _raw.enabled = false;
        }

        private void OnDestroy()
        {
            StopVideoIfPlaying();
            ReleaseOwnedTexture();
        }

        private void ReleaseOwnedTexture()
        {
            if (_textureIsOwned && _loadedTexture != null)
            {
                Destroy(_loadedTexture);
                _loadedTexture = null;
            }
            _textureIsOwned = false;
        }
    }
}
