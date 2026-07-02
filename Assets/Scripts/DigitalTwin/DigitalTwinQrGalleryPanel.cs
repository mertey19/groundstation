using System.Collections.Generic;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// Karekod fotograflari galerisi — PDF gorev senaryosu: "Rover her hedefte
    /// ortamin/karekodlarin fotografini ceker ve mesh uzerinden yer istasyonuna iletir;
    /// demo sonunda sonuclar hakemlere sunulur." Ag uzerinden (imagery.imageBase64)
    /// gelen her fotograf burada hedef id'siyle arsivlenir; kucuk resme tiklaninca
    /// buyuk onizleme acilir. Suruklenebilir, cozunurluk olcekli.
    /// </summary>
    public class DigitalTwinQrGalleryPanel : MonoBehaviour
    {
        [SerializeField] private int maxPhotos = 12;
        [SerializeField] private bool showWhenEmpty = false;

        private class Photo
        {
            public string id;
            public Texture2D tex;
            public float time;
        }

        private readonly List<Photo> _photos = new List<Photo>();
        private Vector2 _hudPos = new Vector2(-99999f, 0f);
        private bool _drag;
        private int _selected = -1;
        private GUIStyle _thumbLabel, _centerStyle;

        /// <summary>Ag uzerinden gelen fotografi arsive ekler (kendi kopyasini cozer).</summary>
        public void AddPhoto(string targetId, byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0) return;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(imageBytes)) { Destroy(tex); return; }

            // Ayni hedef icin yeni fotograf: eskisini guncelle (id basina tek kart).
            for (int i = 0; i < _photos.Count; i++)
            {
                if (_photos[i].id == targetId)
                {
                    if (_photos[i].tex != null) Destroy(_photos[i].tex);
                    _photos[i].tex = tex;
                    _photos[i].time = Time.unscaledTime;
                    return;
                }
            }

            _photos.Add(new Photo { id = targetId ?? "-", tex = tex, time = Time.unscaledTime });
            while (_photos.Count > Mathf.Max(1, maxPhotos))
            {
                if (_photos[0].tex != null) Destroy(_photos[0].tex);
                _photos.RemoveAt(0);
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _photos.Count; i++)
                if (_photos[i].tex != null) Destroy(_photos[i].tex);
            _photos.Clear();
        }

        private void OnGUI()
        {
            if (_photos.Count == 0 && !showWhenEmpty) return;
            TwinHudTheme.BeginScaledHud();

            const float thumbW = 84f, thumbH = 64f, pad = 6f;
            int cols = 3;
            int rows = Mathf.Max(1, Mathf.CeilToInt(_photos.Count / (float)cols));
            float w = 16f * 2f + cols * thumbW + (cols - 1) * pad;
            float h = 58f + rows * (thumbH + 18f + pad);

            // Sol sutun #3 (mesh panelinin altinda).
            Vector2 def = new Vector2(16f, 638f);
            Rect r = TwinHudTheme.Drag(ref _hudPos, ref _drag, def, w, h, "twinhud_gallery_v2");
            TwinHudTheme.Panel(r);

            float x = r.x + 16f, y = r.y + 12f;
            GUI.Label(new Rect(x, y, w - 32f, 18f), "KAREKOD FOTOĞRAFLARI", TwinHudTheme.Title);
            y += 23f;
            TwinHudTheme.Separator(x, y, w - 32f);
            y += 8f;

            if (_thumbLabel == null) _thumbLabel = new GUIStyle(TwinHudTheme.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };

            for (int i = 0; i < _photos.Count; i++)
            {
                int cx = i % cols, cy = i / cols;
                var cell = new Rect(x + cx * (thumbW + pad), y + cy * (thumbH + 18f + pad), thumbW, thumbH);
                TwinHudTheme.Fill(cell, new Color(0f, 0f, 0f, 0.5f), 4f);
                if (_photos[i].tex != null)
                    GUI.DrawTexture(cell, _photos[i].tex, ScaleMode.ScaleAndCrop, false);
                if (GUI.Button(cell, GUIContent.none, GUIStyle.none))
                    _selected = (_selected == i) ? -1 : i;
                GUI.Label(new Rect(cell.x, cell.yMax + 1f, thumbW, 14f), _photos[i].id, _thumbLabel);
            }

            // Buyuk onizleme (tiklanan fotograf).
            if (_selected >= 0 && _selected < _photos.Count && _photos[_selected].tex != null)
            {
                float bw = Mathf.Min(420f, TwinHudTheme.ScreenW * 0.5f);
                float bh = bw * 0.75f + 36f;
                var big = new Rect((TwinHudTheme.ScreenW - bw) * 0.5f, (TwinHudTheme.ScreenH - bh) * 0.5f, bw, bh);
                TwinHudTheme.Panel(big);
                GUI.Label(new Rect(big.x + 14f, big.y + 8f, bw - 90f, 18f), "Hedef: " + _photos[_selected].id, TwinHudTheme.Title);
                if (GUI.Button(new Rect(big.xMax - 72f, big.y + 8f, 58f, 18f), "Kapat", TwinHudTheme.Button))
                    _selected = -1;
                var imgRect = new Rect(big.x + 12f, big.y + 32f, bw - 24f, bh - 44f);
                TwinHudTheme.Fill(imgRect, new Color(0f, 0f, 0f, 0.6f), 4f);
                GUI.DrawTexture(imgRect, _photos[_selected].tex, ScaleMode.ScaleToFit, false);
            }

            TwinHudTheme.EndScaledHud();
        }
    }
}
