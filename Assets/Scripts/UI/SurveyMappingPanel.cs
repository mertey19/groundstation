using UnityEngine;
using UnityEngine.UI;
using System.Globalization;
using System;
using System.Collections.Generic;
using GroundStation.Routes;

namespace GroundStation.UI
{
    public class SurveyMappingPanel : MonoBehaviour
    {
        [Header("Core Refs")]
        [SerializeField] private SurveyMissionPlanner planner;
        [SerializeField] private SurveyAreaSelector areaSelector;

        [Header("Inputs")]
        [SerializeField] private InputField frontOverlapInput;
        [SerializeField] private InputField sideOverlapInput;
        [SerializeField] private InputField cameraHFovInput;
        [SerializeField] private InputField cameraVFovInput;
        [SerializeField] private InputField cameraTiltInput;

        [Header("Buttons")]
        [SerializeField] private Button selectAreaButton;
        [SerializeField] private Button selectPolygonButton;
        [SerializeField] private Button finishPolygonButton;
        [SerializeField] private Button clearAreaButton;
        [SerializeField] private Button applyButton;
        [SerializeField] private Button generateButton;

        [Header("QGC-style (opsiyonel)")]
        [SerializeField] private InputField transectAngleInput;
        [SerializeField] private InputField turnaroundInput;
        [SerializeField] private InputField groundResInput;
        [SerializeField] private InputField referenceWidthInput;
        [SerializeField] private Toggle useGroundResolutionToggle;
        [SerializeField] private Text surveyStatsText;
        [SerializeField] private Button rotateEntryButton;
        [SerializeField] private Button presetGenericButton;
        [SerializeField] private Button presetCorridorButton;
        [SerializeField] private Button presetFastButton;

        [Header("Visibility / Toggle")]
        [Tooltip("Play baslayinca Survey paneli kapali baslar; ayri butonla acilir.")]
        [SerializeField] private bool startHidden = true;
        [Tooltip("Toggle butonu atanmamissa runtime'da Canvas altinda olusturulur.")]
        [SerializeField] private bool createRuntimeToggleButton = true;
        [SerializeField] private Button togglePanelButton;
        [SerializeField] private string openPanelButtonText = "Survey Ac";
        [SerializeField] private string closePanelButtonText = "Survey Kapat";
        [SerializeField] private Vector2 toggleButtonSize = new Vector2(180f, 44f);
        [SerializeField] private Vector2 toggleButtonAnchoredPosition = new Vector2(-120f, -110f);
        [SerializeField] private bool autoScaleToggleButtonForSmallScreens = true;
        [SerializeField] private Vector2 designResolution = new Vector2(1920f, 1080f);
        [SerializeField] private float minToggleScale = 0.78f;

        [Header("Simple UI Mode")]
        [Tooltip("Acilista panel daha sade gorunsun; gelismis ayarlar butonla acilsin.")]
        [SerializeField] private bool simplifyUi = true;
        [SerializeField] private bool startWithAdvancedHidden = true;
        [SerializeField] private bool createAdvancedToggleButton = true;
        [SerializeField] private Button toggleAdvancedButton;
        [SerializeField] private string showAdvancedButtonText = "Gelismis";
        [SerializeField] private string hideAdvancedButtonText = "Basit";

        private bool _advancedVisible = true;

        [SerializeField] private float defaultFrontOverlap = 75f;
        [SerializeField] private float defaultSideOverlap = 65f;
        [SerializeField] private float defaultCameraHFov = 78f;
        [SerializeField] private float defaultCameraVFov = 52f;
        [SerializeField] private float defaultCameraTilt = 0f;
        [SerializeField] private float defaultTransectAngle = 0f;
        [SerializeField] private float defaultTurnaround = 10f;
        [SerializeField] private float defaultGroundResCm = 3f;
        [SerializeField] private int defaultReferenceWidthPx = 4000;

        [Header("Auto Design")]
        [SerializeField] private bool autoBindByName = true;
        [SerializeField] private bool autoDesignUI = true;
        [SerializeField] private int textSize = 22;
        [SerializeField] private int buttonTextSize = 22;
        [SerializeField] private Color panelColor = new Color(0.09f, 0.11f, 0.14f, 0.82f);
        [SerializeField] private Color buttonColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        [SerializeField] private Color buttonTextColor = new Color(0.12f, 0.12f, 0.12f, 1f);
        [SerializeField] private float panelWidth = 360f;
        [SerializeField] private float inputHeight = 46f;
        [SerializeField] private float buttonHeight = 48f;
        [SerializeField] private Font fallbackUIFont;

        private void Awake()
        {
            NormalizeReadableDefaults();

            if (planner == null) planner = FindObjectOfType<SurveyMissionPlanner>();
            if (areaSelector == null) areaSelector = FindObjectOfType<SurveyAreaSelector>();

            if (autoBindByName)
                AutoBindByName();

            HookButton(selectAreaButton, OnSelectAreaClicked);
            HookButton(selectPolygonButton, OnSelectPolygonClicked);
            HookButton(finishPolygonButton, OnFinishPolygonClicked);
            HookButton(clearAreaButton, OnClearAreaClicked);
            HookButton(applyButton, ApplySettings);
            HookButton(generateButton, ApplyAndGenerate);

            if (frontOverlapInput != null && string.IsNullOrWhiteSpace(frontOverlapInput.text))
                frontOverlapInput.text = defaultFrontOverlap.ToString("F0", CultureInfo.InvariantCulture);
            if (sideOverlapInput != null && string.IsNullOrWhiteSpace(sideOverlapInput.text))
                sideOverlapInput.text = defaultSideOverlap.ToString("F0", CultureInfo.InvariantCulture);
            if (cameraHFovInput != null && string.IsNullOrWhiteSpace(cameraHFovInput.text))
                cameraHFovInput.text = defaultCameraHFov.ToString("F0", CultureInfo.InvariantCulture);
            if (cameraVFovInput != null && string.IsNullOrWhiteSpace(cameraVFovInput.text))
                cameraVFovInput.text = defaultCameraVFov.ToString("F0", CultureInfo.InvariantCulture);
            if (cameraTiltInput != null && string.IsNullOrWhiteSpace(cameraTiltInput.text))
                cameraTiltInput.text = defaultCameraTilt.ToString("F0", CultureInfo.InvariantCulture);

            if (autoDesignUI)
                ApplyMinimalModernUI();
            else
                EnsureQgcExtraUiRuntime();

            RebindButtonsDeterministic();
            EnsureAdvancedToggleUi();
            EnsurePanelToggleUi();
            if (startHidden)
                SetPanelVisible(false);
        }

        private void OnEnable()
        {
            RebindButtonsDeterministic();
            UpdateToggleButtonVisual();
            UpdateAdvancedToggleButtonVisual();
        }

        public void TogglePanelVisibility()
        {
            SetPanelVisible(!gameObject.activeSelf);
        }

        public void SetPanelVisible(bool visible)
        {
            gameObject.SetActive(visible);
            UpdateToggleButtonVisual();
        }

        public void ToggleAdvancedVisibility()
        {
            SetAdvancedVisible(!_advancedVisible);
        }

        public void SetAdvancedVisible(bool visible)
        {
            _advancedVisible = visible;
            foreach (var go in GetAdvancedUiObjects())
            {
                if (go == null) continue;
                if (toggleAdvancedButton != null && go == toggleAdvancedButton.gameObject) continue;
                go.SetActive(visible);
            }
            UpdateAdvancedToggleButtonVisual();
        }

        private void NormalizeReadableDefaults()
        {
            textSize = Mathf.Max(18, textSize);
            buttonTextSize = Mathf.Max(19, buttonTextSize);
            panelWidth = Mathf.Max(320f, panelWidth);
            inputHeight = Mathf.Max(44f, inputHeight);
            buttonHeight = Mathf.Max(46f, buttonHeight);
        }

        public void ApplySettings()
        {
            if (planner == null) planner = FindObjectOfType<SurveyMissionPlanner>();
            if (planner == null) return;

            if (areaSelector == null) areaSelector = FindObjectOfType<SurveyAreaSelector>();
            if (areaSelector != null) areaSelector.SuppressNextLeftClick(0.15f);

            float front = ParsePercent(frontOverlapInput, defaultFrontOverlap);
            float side = ParsePercent(sideOverlapInput, defaultSideOverlap);
            planner.SetOverlap(front, side);

            float hFov = ParseFloat(cameraHFovInput, defaultCameraHFov, 5f, 170f);
            float vFov = ParseFloat(cameraVFovInput, defaultCameraVFov, 5f, 170f);
            float tilt = ParseFloat(cameraTiltInput, defaultCameraTilt, 0f, 80f);
            planner.SetCameraAngles(hFov, vFov, tilt);

            float transect = ParseFloat(transectAngleInput, defaultTransectAngle, -180f, 180f);
            float turnaround = ParseFloat(turnaroundInput, defaultTurnaround, 0f, 500f);
            planner.SetTransects(transect, turnaround);

            float gsdCm = ParseFloat(groundResInput, defaultGroundResCm, 0.05f, 500f);
            bool useGsd = useGroundResolutionToggle != null && useGroundResolutionToggle.isOn;
            planner.SetAltitudeFootprintMode(!useGsd, gsdCm);
            int refW = Mathf.RoundToInt(ParseFloat(referenceWidthInput, defaultReferenceWidthPx, 320f, 32000f));
            planner.SetReferenceImageWidth(refW);
        }

        public void ApplyAndGenerate()
        {
            if (areaSelector == null) areaSelector = FindObjectOfType<SurveyAreaSelector>();
            if (areaSelector != null) areaSelector.SuppressNextLeftClick(0.15f);
            ApplySettings();
            if (planner != null) planner.GenerateSurveyRoute();
            RefreshSurveyStats();
        }

        public void OnSelectAreaClicked()
        {
            if (areaSelector == null) areaSelector = FindObjectOfType<SurveyAreaSelector>();
            if (areaSelector != null) areaSelector.SuppressNextLeftClick(0.25f);
            if (areaSelector != null) areaSelector.BeginAreaSelection();
        }

        public void OnClearAreaClicked()
        {
            if (planner == null) planner = FindObjectOfType<SurveyMissionPlanner>();
            if (planner != null) planner.ClearCustomSurveyArea();
            if (areaSelector == null) areaSelector = FindObjectOfType<SurveyAreaSelector>();
            if (areaSelector != null) areaSelector.SuppressNextLeftClick(0.25f);
            if (areaSelector != null) areaSelector.CancelAreaSelection();
        }

        public void OnSelectPolygonClicked()
        {
            if (areaSelector == null) areaSelector = FindObjectOfType<SurveyAreaSelector>();
            if (areaSelector != null) areaSelector.SuppressNextLeftClick(0.25f);
            if (areaSelector != null) areaSelector.BeginPolygonSelection();
        }

        public void OnFinishPolygonClicked()
        {
            if (areaSelector == null) areaSelector = FindObjectOfType<SurveyAreaSelector>();
            if (areaSelector != null) areaSelector.SuppressNextLeftClick(0.25f);
            if (areaSelector != null) areaSelector.FinishPolygonSelection();
        }

        [ContextMenu("Auto Bind By Name")]
        public void AutoBindByName()
        {
            if (frontOverlapInput == null) frontOverlapInput = FindInputLike("frontoverlap");
            if (sideOverlapInput == null) sideOverlapInput = FindInputLike("sideoverlap");
            if (cameraHFovInput == null) cameraHFovInput = FindInputLike("camerahfov");
            if (cameraVFovInput == null) cameraVFovInput = FindInputLike("cameravfov");
            if (cameraTiltInput == null) cameraTiltInput = FindInputLike("cameratilt");

            if (selectAreaButton == null) selectAreaButton = FindButtonLike("selectarea");
            if (selectPolygonButton == null) selectPolygonButton = FindButtonLike("selectpolygon");
            if (finishPolygonButton == null) finishPolygonButton = FindButtonLike("finishpolygon");
            if (clearAreaButton == null) clearAreaButton = FindButtonLike("cleararea");
            if (applyButton == null) applyButton = FindButtonLike("apply");
            if (generateButton == null) generateButton = FindButtonLike("generate");

            if (transectAngleInput == null) transectAngleInput = FindInputLike("transect");
            if (turnaroundInput == null) turnaroundInput = FindInputLike("turnaround");
            if (groundResInput == null) groundResInput = FindInputLike("groundres") ?? FindInputLike("gsd");
            if (referenceWidthInput == null) referenceWidthInput = FindInputLike("referencewidth") ?? FindInputLike("imagewidth");
            if (useGroundResolutionToggle == null)
            {
                foreach (var tg in GetComponentsInChildren<Toggle>(true))
                {
                    if (tg.gameObject.name.ToLowerInvariant().Contains("gsd") ||
                        tg.gameObject.name.ToLowerInvariant().Contains("ground"))
                    {
                        useGroundResolutionToggle = tg;
                        break;
                    }
                }
            }
            if (surveyStatsText == null)
            {
                foreach (var tx in GetComponentsInChildren<Text>(true))
                {
                    if (tx.gameObject.name.ToLowerInvariant().Contains("surveystats")) { surveyStatsText = tx; break; }
                }
            }
            if (rotateEntryButton == null) rotateEntryButton = FindButtonLike("rotateentry");

            if (selectAreaButton == null) selectAreaButton = FindButtonByLabelContains("alan");
            if (selectPolygonButton == null) selectPolygonButton = FindButtonByLabelContains("poligon başlat") ?? FindButtonByLabelContains("poligon baslat");
            if (finishPolygonButton == null) finishPolygonButton = FindButtonByLabelContains("poligon bitir");
            if (clearAreaButton == null) clearAreaButton = FindButtonByLabelContains("temizle");
            if (applyButton == null) applyButton = FindButtonByLabelContains("uygula");
            if (generateButton == null) generateButton = FindButtonByLabelContains("survey üret") ?? FindButtonByLabelContains("survey uret");

            if (frontOverlapInput == null || sideOverlapInput == null || cameraHFovInput == null || cameraVFovInput == null || cameraTiltInput == null)
            {
                var inputs = GetComponentsInChildren<InputField>(true);
                if (inputs.Length >= 5)
                {
                    if (frontOverlapInput == null) frontOverlapInput = inputs[0];
                    if (sideOverlapInput == null) sideOverlapInput = inputs[1];
                    if (cameraHFovInput == null) cameraHFovInput = inputs[2];
                    if (cameraVFovInput == null) cameraVFovInput = inputs[3];
                    if (cameraTiltInput == null) cameraTiltInput = inputs[4];
                }
            }

            if (selectAreaButton == null || selectPolygonButton == null || finishPolygonButton == null ||
                clearAreaButton == null || applyButton == null || generateButton == null)
            {
                var buttons = GetComponentsInChildren<Button>(true);
                if (buttons.Length >= 6)
                {
                    if (selectAreaButton == null) selectAreaButton = buttons[0];
                    if (selectPolygonButton == null) selectPolygonButton = buttons[1];
                    if (finishPolygonButton == null) finishPolygonButton = buttons[2];
                    if (clearAreaButton == null) clearAreaButton = buttons[3];
                    if (applyButton == null) applyButton = buttons[4];
                    if (generateButton == null) generateButton = buttons[5];
                }
            }
        }

        [ContextMenu("Apply Minimal Modern UI")]
        public void ApplyMinimalModernUI()
        {
            var panelImage = GetComponent<Image>();
            if (panelImage == null) panelImage = gameObject.AddComponent<Image>();
            panelImage.color = new Color(0.06f, 0.08f, 0.12f, 0.95f);   // QGC koyu cam
            panelImage.sprite = RoundedUiSprite();
            panelImage.type = Image.Type.Sliced;

            var rt = transform as RectTransform;
            if (rt != null)
            {
                rt.anchorMin = new Vector2(1f, 0.5f);
                rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(-20f, -40f);
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, panelWidth);
            }

            var vlg = GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(14, 14, 14, 14);
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            EnsureSectionLabel("HeaderTitle", "Survey Mapping", 0, 24, FontStyle.Bold);
            EnsureSectionLabel("AreaTitle", "Alan Seçimi", 1, 19, FontStyle.Bold);

            StyleInput(frontOverlapInput, "Ön overlap %");
            StyleInput(sideOverlapInput, "Yan overlap %");
            StyleInput(cameraHFovInput, "HFOV (°)");
            StyleInput(cameraVFovInput, "VFOV (°)");
            StyleInput(cameraTiltInput, "Tilt (°)");

            EnsureSectionLabel("OverlapRowTitle", "Overlap (%)", 3, 17, FontStyle.Bold);
            MoveInputsToRow("RowOverlap", frontOverlapInput, sideOverlapInput, 4);
            EnsureSectionLabel("CameraTitle", "Kamera Parametreleri", 4, 19, FontStyle.Bold);
            EnsureSectionLabel("FovRowTitle", "HFOV / VFOV (°)", 5, 17, FontStyle.Bold);
            MoveInputsToRow("RowFov", cameraHFovInput, cameraVFovInput, 6);
            EnsureSectionLabel("TiltTitle", "Tilt (°)", 6, 17, FontStyle.Bold);
            MoveSingleInputToRow("RowTilt", cameraTiltInput, 8);

            StyleButton(selectAreaButton, "Alan (2 Nokta)");
            StyleButton(selectPolygonButton, "Poligon Başlat");
            StyleButton(finishPolygonButton, "Poligon Bitir");
            StyleButton(clearAreaButton, "Temizle");
            StyleButton(applyButton, "Uygula");
            StyleButton(generateButton, "Survey Üret");

            EnsureQgcExtraUiRuntime();

            RebindButtonsDeterministic();
        }

        private void EnsureQgcExtraUiRuntime()
        {
            if (frontOverlapInput != null)
            {
                if (transectAngleInput == null)
                    transectAngleInput = DuplicateInputFromTemplate("TransectAngleInput", "Açı (°)", frontOverlapInput, defaultTransectAngle.ToString("F1", CultureInfo.InvariantCulture));
                if (turnaroundInput == null)
                    turnaroundInput = DuplicateInputFromTemplate("TurnaroundInput", "Dönüş mesafesi (m)", frontOverlapInput, defaultTurnaround.ToString("F2", CultureInfo.InvariantCulture));
                if (groundResInput == null)
                    groundResInput = DuplicateInputFromTemplate("GroundResInput", "GSD (cm/px)", frontOverlapInput, defaultGroundResCm.ToString("F2", CultureInfo.InvariantCulture));
                if (referenceWidthInput == null)
                    referenceWidthInput = DuplicateInputFromTemplate("ReferenceWidthInput", "Görüntü genişliği (px)", frontOverlapInput, defaultReferenceWidthPx.ToString(CultureInfo.InvariantCulture));
            }

            if (useGroundResolutionToggle == null)
            {
                var t = transform.Find("UseGroundResolutionToggle");
                if (t == null)
                {
                    var go = new GameObject("UseGroundResolutionToggle", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(Toggle));
                    go.transform.SetParent(transform, false);
                    var le = go.GetComponent<LayoutElement>();
                    le.preferredHeight = 32f;
                    var toggle = go.GetComponent<Toggle>();
                    var bg = go.GetComponent<Image>();
                    bg.color = new Color(0.25f, 0.28f, 0.32f, 1f);
                    toggle.targetGraphic = bg;
                    toggle.isOn = false;

                    var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                    labelGo.transform.SetParent(go.transform, false);
                    var lrt = labelGo.GetComponent<RectTransform>();
                    lrt.anchorMin = new Vector2(0f, 0f);
                    lrt.anchorMax = new Vector2(1f, 1f);
                    lrt.offsetMin = new Vector2(28f, 2f);
                    lrt.offsetMax = new Vector2(-6f, -2f);
                    var lt = labelGo.GetComponent<Text>();
                    var f = ResolveUIFont();
                    if (f != null) lt.font = f;
                    lt.text = "Yer çözünürlüğü (GSD) ile yükseklik";
                    lt.fontSize = textSize - 3;
                    lt.color = new Color(0.9f, 0.94f, 1f, 1f);
                    lt.alignment = TextAnchor.MiddleLeft;
                    useGroundResolutionToggle = toggle;
                }
                else
                    useGroundResolutionToggle = t.GetComponent<Toggle>();
            }

            if (surveyStatsText == null)
            {
                var existing = transform.Find("SurveyStatsText");
                if (existing != null)
                    surveyStatsText = existing.GetComponent<Text>();
                else
                {
                    var go = new GameObject("SurveyStatsText", typeof(RectTransform), typeof(LayoutElement), typeof(Text));
                    go.transform.SetParent(transform, false);
                    var le = go.GetComponent<LayoutElement>();
                    le.preferredHeight = 130f;
                    surveyStatsText = go.GetComponent<Text>();
                    var f = ResolveUIFont();
                    if (f != null) surveyStatsText.font = f;
                    surveyStatsText.fontSize = Mathf.Max(15, textSize - 4);
                    surveyStatsText.color = new Color(0.82f, 0.9f, 1f, 1f);
                    surveyStatsText.alignment = TextAnchor.UpperLeft;
                    surveyStatsText.horizontalOverflow = HorizontalWrapMode.Wrap;
                    surveyStatsText.verticalOverflow = VerticalWrapMode.Overflow;
                    surveyStatsText.text = "İstatistik: Survey üretildikten sonra güncellenir.";
                }
            }

            if (rotateEntryButton == null && generateButton != null)
            {
                var copy = Instantiate(generateButton.gameObject, transform);
                copy.name = "RotateEntryButton";
                rotateEntryButton = copy.GetComponent<Button>();
                StyleButton(rotateEntryButton, "Giriş noktasını döndür");
            }

            if (presetGenericButton == null && generateButton != null)
            {
                var copy = Instantiate(generateButton.gameObject, transform);
                copy.name = "PresetGenericButton";
                presetGenericButton = copy.GetComponent<Button>();
                StyleButton(presetGenericButton, "Profil: Generic");
            }
            if (presetCorridorButton == null && generateButton != null)
            {
                var copy = Instantiate(generateButton.gameObject, transform);
                copy.name = "PresetCorridorButton";
                presetCorridorButton = copy.GetComponent<Button>();
                StyleButton(presetCorridorButton, "Profil: Corridor HD");
            }
            if (presetFastButton == null && generateButton != null)
            {
                var copy = Instantiate(generateButton.gameObject, transform);
                copy.name = "PresetFastButton";
                presetFastButton = copy.GetComponent<Button>();
                StyleButton(presetFastButton, "Profil: Fast");
            }

            if (transectAngleInput != null) StyleInput(transectAngleInput, "Açı (°)");
            if (turnaroundInput != null) StyleInput(turnaroundInput, "Dönüş mesafesi (m)");
            if (groundResInput != null) StyleInput(groundResInput, "GSD (cm/px)");
            if (referenceWidthInput != null) StyleInput(referenceWidthInput, "Görüntü genişliği (px)");

            HookButton(rotateEntryButton, OnRotateEntryClicked);
            HookButton(presetGenericButton, OnPresetGenericClicked);
            HookButton(presetCorridorButton, OnPresetCorridorClicked);
            HookButton(presetFastButton, OnPresetFastClicked);
        }

        private InputField DuplicateInputFromTemplate(string objectName, string placeholder, InputField template, string initialText)
        {
            if (transform.Find(objectName) != null)
                return transform.Find(objectName).GetComponent<InputField>();
            var copy = Instantiate(template.gameObject, transform);
            copy.name = objectName;
            var input = copy.GetComponent<InputField>();
            if (input != null)
            {
                input.text = initialText;
                StyleInput(input, placeholder);
            }
            return input;
        }

        private void OnRotateEntryClicked()
        {
            if (planner == null) planner = FindObjectOfType<SurveyMissionPlanner>();
            if (areaSelector == null) areaSelector = FindObjectOfType<SurveyAreaSelector>();
            if (areaSelector != null) areaSelector.SuppressNextLeftClick(0.25f);
            if (planner != null) planner.RotateSurveyEntryPoint();
            RefreshSurveyStats();
        }

        private void OnPresetGenericClicked()
        {
            ApplyPreset(SurveyQgcPreset.GenericMap, 75f, 65f, 3.0f, 10f);
        }

        private void OnPresetCorridorClicked()
        {
            ApplyPreset(SurveyQgcPreset.CorridorHighDetail, 80f, 75f, 2.0f, 15f);
        }

        private void OnPresetFastClicked()
        {
            ApplyPreset(SurveyQgcPreset.FastCoverage, 65f, 55f, 5.0f, 8f);
        }

        private void ApplyPreset(SurveyQgcPreset preset, float front, float side, float gsd, float turnaround)
        {
            if (planner == null) planner = FindObjectOfType<SurveyMissionPlanner>();
            if (planner == null) return;
            planner.ApplyQgcPreset(preset);

            if (frontOverlapInput != null) frontOverlapInput.text = front.ToString("F0", CultureInfo.InvariantCulture);
            if (sideOverlapInput != null) sideOverlapInput.text = side.ToString("F0", CultureInfo.InvariantCulture);
            if (groundResInput != null) groundResInput.text = gsd.ToString("F2", CultureInfo.InvariantCulture);
            if (turnaroundInput != null) turnaroundInput.text = turnaround.ToString("F1", CultureInfo.InvariantCulture);
            if (useGroundResolutionToggle != null) useGroundResolutionToggle.isOn = true;

            ApplySettings();
        }

        private void RefreshSurveyStats()
        {
            if (planner == null) planner = FindObjectOfType<SurveyMissionPlanner>();
            if (planner == null || surveyStatsText == null) return;
            var s = planner.LastPlanStats;
            if (s.waypointCount <= 0)
                return;
            surveyStatsText.text =
                $"Alan: {s.areaSquareMeters:F1} m²\n" +
                $"Foto / WP: {s.waypointCount}\n" +
                $"Tetik mesafesi: {s.triggerDistanceMeters:F2} m\n" +
                $"Tahmini foto aralığı: {s.estimatedPhotoIntervalSeconds:F2} s\n" +
                $"GSD: {s.groundResolutionCmPerPixel:F2} cm/px\n" +
                $"Yükseklik: {s.effectiveAltitudeMeters:F1} m\n" +
                $"Transect açısı: {s.transectAngleDeg:F1}°";
        }

        private static float ParsePercent(InputField input, float fallback)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.text)) return fallback;
            string txt = input.text.Trim().Replace(',', '.');
            if (float.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                return Mathf.Clamp(v, 0f, 95f);
            return fallback;
        }

        private static float ParseFloat(InputField input, float fallback, float min, float max)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.text)) return fallback;
            string txt = input.text.Trim().Replace(',', '.');
            if (float.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                return Mathf.Clamp(v, min, max);
            return fallback;
        }

        private InputField FindInputLike(string contains)
        {
            foreach (var input in GetComponentsInChildren<InputField>(true))
            {
                if (input.gameObject.name.ToLowerInvariant().Contains(contains))
                    return input;
            }
            return null;
        }

        private Button FindButtonLike(string contains)
        {
            foreach (var button in GetComponentsInChildren<Button>(true))
            {
                if (button.gameObject.name.ToLowerInvariant().Contains(contains))
                    return button;
            }
            return null;
        }

        private Button FindButtonByLabelContains(string labelPart)
        {
            if (string.IsNullOrWhiteSpace(labelPart)) return null;
            foreach (var button in GetComponentsInChildren<Button>(true))
            {
                var t = button.GetComponentInChildren<Text>(true);
                if (t == null || string.IsNullOrWhiteSpace(t.text)) continue;
                if (t.text.IndexOf(labelPart, StringComparison.OrdinalIgnoreCase) >= 0)
                    return button;
            }
            return null;
        }

        private static void HookButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null || action == null) return;
            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        private void RebindButtonsDeterministic()
        {
            if (selectAreaButton == null) selectAreaButton = FindButtonByLabelContains("alan");
            if (selectPolygonButton == null) selectPolygonButton = FindButtonByLabelContains("poligon başlat") ?? FindButtonByLabelContains("poligon baslat");
            if (finishPolygonButton == null) finishPolygonButton = FindButtonByLabelContains("poligon bitir");
            if (clearAreaButton == null) clearAreaButton = FindButtonByLabelContains("temizle");
            if (applyButton == null) applyButton = FindButtonByLabelContains("uygula");
            if (generateButton == null) generateButton = FindButtonByLabelContains("survey üret") ?? FindButtonByLabelContains("survey uret");

            if (selectAreaButton == null || selectPolygonButton == null || finishPolygonButton == null ||
                clearAreaButton == null || applyButton == null || generateButton == null)
            {
                var ordered = GetButtonsInSiblingOrder();
                if (ordered.Count >= 6)
                {
                    if (selectAreaButton == null) selectAreaButton = ordered[0];
                    if (selectPolygonButton == null) selectPolygonButton = ordered[1];
                    if (finishPolygonButton == null) finishPolygonButton = ordered[2];
                    if (clearAreaButton == null) clearAreaButton = ordered[3];
                    if (applyButton == null) applyButton = ordered[4];
                    if (generateButton == null) generateButton = ordered[5];
                }
            }

            if (selectAreaButton != null) selectAreaButton.gameObject.name = "SelectAreaButton";
            if (selectPolygonButton != null) selectPolygonButton.gameObject.name = "SelectPolygonButton";
            if (finishPolygonButton != null) finishPolygonButton.gameObject.name = "FinishPolygonButton";
            if (clearAreaButton != null) clearAreaButton.gameObject.name = "ClearAreaButton";
            if (applyButton != null) applyButton.gameObject.name = "ApplyButton";
            if (generateButton != null) generateButton.gameObject.name = "GenerateButton";

            HookButton(selectAreaButton, OnSelectAreaClicked);
            HookButton(selectPolygonButton, OnSelectPolygonClicked);
            HookButton(finishPolygonButton, OnFinishPolygonClicked);
            HookButton(clearAreaButton, OnClearAreaClicked);
            HookButton(applyButton, ApplySettings);
            HookButton(generateButton, ApplyAndGenerate);
        }

        private List<Button> GetButtonsInSiblingOrder()
        {
            var list = new List<Button>();
            foreach (Transform child in transform)
            {
                var b = child.GetComponent<Button>();
                if (b != null) list.Add(b);
            }
            return list;
        }

        private void StyleInput(InputField input, string placeholder)
        {
            if (input == null) return;
            var le = input.GetComponent<LayoutElement>();
            if (le == null) le = input.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = inputHeight;
            le.flexibleHeight = 0f;

            var inImg = input.GetComponent<Image>();
            if (inImg != null)
            {
                inImg.color = new Color(0.10f, 0.13f, 0.18f, 0.96f);
                inImg.sprite = RoundedUiSprite();
                inImg.type = Image.Type.Sliced;
            }

            input.contentType = InputField.ContentType.DecimalNumber;
            if (input.textComponent != null)
            {
                var uiFont = ResolveUIFont();
                if (uiFont != null) input.textComponent.font = uiFont;
                input.textComponent.fontSize = textSize;
                input.textComponent.fontStyle = FontStyle.Bold;
                input.textComponent.alignment = TextAnchor.MiddleCenter;
                input.textComponent.color = new Color(0.94f, 0.97f, 1f, 1f);
            }
            if (input.placeholder is Text ph)
            {
                var uiFont = ResolveUIFont();
                if (uiFont != null) ph.font = uiFont;
                ph.text = placeholder;
                ph.fontSize = textSize - 2;
                ph.alignment = TextAnchor.MiddleCenter;
                ph.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            }
        }

        private void StyleButton(Button button, string label)
        {
            if (button == null) return;
            var le = button.GetComponent<LayoutElement>();
            if (le == null) le = button.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = buttonHeight;
            le.flexibleHeight = 0f;

            var img = button.GetComponent<Image>();
            if (img != null)
            {
                img.color = new Color(0.13f, 0.17f, 0.24f, 0.96f);   // QGC koyu buton (UiButtonThemer ile uyumlu)
                img.sprite = RoundedUiSprite();
                img.type = Image.Type.Sliced;
            }

            var txt = button.GetComponentInChildren<Text>(true);
            if (txt != null)
            {
                var uiFont = ResolveUIFont();
                if (uiFont != null) txt.font = uiFont;
                txt.text = label;
                txt.fontSize = buttonTextSize;
                txt.fontStyle = FontStyle.Bold;
                txt.color = new Color(0.94f, 0.97f, 1f, 1f);
                txt.alignment = TextAnchor.MiddleCenter;
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        private void EnsureSectionLabel(string objectName, string title, int siblingIndex, int size, FontStyle style)
        {
            var t = transform.Find(objectName);
            Text txt = null;
            if (t == null)
            {
                var go = new GameObject(objectName, typeof(RectTransform), typeof(LayoutElement), typeof(Text));
                go.transform.SetParent(transform, false);
                go.transform.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, transform.childCount - 1));
                txt = go.GetComponent<Text>();
                var le = go.GetComponent<LayoutElement>();
                le.preferredHeight = 24f;
            }
            else
            {
                txt = t.GetComponent<Text>();
            }
            if (txt == null) return;
            var uiFontForLabel = ResolveUIFont();
            if (uiFontForLabel != null) txt.font = uiFontForLabel;
            txt.text = title;
            txt.fontSize = size;
            txt.fontStyle = style;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.color = new Color(0.92f, 0.96f, 1f, 1f);
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private void MoveInputsToRow(string rowName, InputField left, InputField right, int siblingIndex)
        {
            if (left == null || right == null) return;
            var row = transform.Find(rowName);
            if (row == null)
            {
                var go = new GameObject(rowName, typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
                go.transform.SetParent(transform, false);
                row = go.transform;
                var le = go.GetComponent<LayoutElement>();
                le.preferredHeight = Mathf.Max(inputHeight, 40f);
                var h = go.GetComponent<HorizontalLayoutGroup>();
                h.spacing = 6f;
                h.padding = new RectOffset(0, 0, 0, 0);
                h.childAlignment = TextAnchor.MiddleCenter;
                h.childControlHeight = true;
                h.childControlWidth = true;
                h.childForceExpandHeight = false;
                h.childForceExpandWidth = true;
            }
            row.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, transform.childCount - 1));

            left.transform.SetParent(row, false);
            right.transform.SetParent(row, false);
            var leL = left.GetComponent<LayoutElement>() ?? left.gameObject.AddComponent<LayoutElement>();
            var leR = right.GetComponent<LayoutElement>() ?? right.gameObject.AddComponent<LayoutElement>();
            leL.preferredWidth = panelWidth * 0.5f - 20f;
            leR.preferredWidth = panelWidth * 0.5f - 20f;
        }

        private void MoveSingleInputToRow(string rowName, InputField input, int siblingIndex)
        {
            if (input == null) return;
            var row = transform.Find(rowName);
            if (row == null)
            {
                var go = new GameObject(rowName, typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
                go.transform.SetParent(transform, false);
                row = go.transform;
                var le = go.GetComponent<LayoutElement>();
                le.preferredHeight = Mathf.Max(inputHeight, 40f);
                var h = go.GetComponent<HorizontalLayoutGroup>();
                h.spacing = 0f;
                h.padding = new RectOffset(0, 0, 0, 0);
                h.childAlignment = TextAnchor.MiddleCenter;
                h.childControlHeight = true;
                h.childControlWidth = true;
                h.childForceExpandHeight = false;
                h.childForceExpandWidth = true;
            }
            row.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, transform.childCount - 1));

            input.transform.SetParent(row, false);
            var inputLe = input.GetComponent<LayoutElement>() ?? input.gameObject.AddComponent<LayoutElement>();
            inputLe.preferredWidth = panelWidth - 24f;
        }

        private Font ResolveUIFont()
        {
            if (fallbackUIFont != null) return fallbackUIFont;

            try
            {
                fallbackUIFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch
            {
            }

            if (fallbackUIFont == null)
            {
                try
                {
                    fallbackUIFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                catch
                {
                }
            }

            if (fallbackUIFont == null)
            {
                var anyText = GetComponentInChildren<Text>(true);
                if (anyText != null) fallbackUIFont = anyText.font;
            }

            return fallbackUIFont;
        }

        // QGroundControl tarzi yuvarlatilmis 9-slice sprite (panel/buton/input arka plani icin).
        private static Sprite _roundedUiSprite;
        private static Sprite RoundedUiSprite()
        {
            if (_roundedUiSprite != null) return _roundedUiSprite;
            int s = 48, r = 10;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float cx = Mathf.Clamp(x, r, s - 1 - r);
                    float cy = Mathf.Clamp(y, r, s - 1 - r);
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d + 0.5f)));
                }
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            _roundedUiSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            return _roundedUiSprite;
        }

        private void EnsurePanelToggleUi()
        {
            if (togglePanelButton == null && createRuntimeToggleButton)
                togglePanelButton = CreateRuntimeToggleButton();

            if (togglePanelButton != null)
            {
                HookButton(togglePanelButton, TogglePanelVisibility);
                UpdateToggleButtonVisual();
            }
        }

        private void EnsureAdvancedToggleUi()
        {
            if (!simplifyUi) return;

            if (toggleAdvancedButton == null && createAdvancedToggleButton)
            {
                if (generateButton != null)
                {
                    var copy = Instantiate(generateButton.gameObject, transform);
                    copy.name = "ToggleAdvancedButton";
                    toggleAdvancedButton = copy.GetComponent<Button>();
                }
                else
                {
                    var go = new GameObject("ToggleAdvancedButton", typeof(RectTransform), typeof(Image), typeof(Button));
                    go.transform.SetParent(transform, false);
                    toggleAdvancedButton = go.GetComponent<Button>();
                    var img = go.GetComponent<Image>();
                    img.color = buttonColor;
                }
            }

            if (toggleAdvancedButton != null)
            {
                StyleButton(toggleAdvancedButton, showAdvancedButtonText);
                HookButton(toggleAdvancedButton, ToggleAdvancedVisibility);
            }

            SetAdvancedVisible(!startWithAdvancedHidden);
        }

        private Button CreateRuntimeToggleButton()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return null;

            var existing = canvas.transform.Find("SurveyPanelToggleButton");
            if (existing != null)
                return existing.GetComponent<Button>();

            var go = new GameObject("SurveyPanelToggleButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(canvas.transform, false);

            float scale = ResolveToggleScale();

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(
                Mathf.Max(120f, toggleButtonSize.x * scale),
                Mathf.Max(34f, toggleButtonSize.y * scale));
            rt.anchoredPosition = toggleButtonAnchoredPosition * scale;

            var img = go.GetComponent<Image>();
            img.color = new Color(0.92f, 0.92f, 0.92f, 0.98f);

            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = new Color(0.92f, 0.92f, 0.92f, 0.98f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.86f, 0.88f, 0.92f, 1f);
            colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.65f);
            btn.colors = colors;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var txt = textGo.GetComponent<Text>();
            txt.alignment = TextAnchor.MiddleCenter;
            txt.fontSize = Mathf.Max(14, Mathf.RoundToInt(20f * scale));
            txt.fontStyle = FontStyle.Bold;
            txt.color = new Color(0.12f, 0.12f, 0.12f, 1f);
            var uiFont = ResolveUIFont();
            if (uiFont != null) txt.font = uiFont;

            return btn;
        }

        private void UpdateToggleButtonVisual()
        {
            if (togglePanelButton == null) return;

            var txt = togglePanelButton.GetComponentInChildren<Text>(true);
            if (txt != null)
                txt.text = gameObject.activeSelf ? closePanelButtonText : openPanelButtonText;
        }

        private float ResolveToggleScale()
        {
            if (!autoScaleToggleButtonForSmallScreens) return 1f;
            if (designResolution.x <= 0f || designResolution.y <= 0f) return 1f;

            float sx = Screen.width / designResolution.x;
            float sy = Screen.height / designResolution.y;
            return Mathf.Clamp(Mathf.Min(sx, sy), minToggleScale, 1f);
        }

        private void UpdateAdvancedToggleButtonVisual()
        {
            if (toggleAdvancedButton == null) return;
            var txt = toggleAdvancedButton.GetComponentInChildren<Text>(true);
            if (txt != null)
                txt.text = _advancedVisible ? hideAdvancedButtonText : showAdvancedButtonText;
        }

        private List<GameObject> GetAdvancedUiObjects()
        {
            var result = new List<GameObject>(16);
            AddIfNotNull(result, transectAngleInput != null ? transectAngleInput.gameObject : null);
            AddIfNotNull(result, turnaroundInput != null ? turnaroundInput.gameObject : null);
            AddIfNotNull(result, groundResInput != null ? groundResInput.gameObject : null);
            AddIfNotNull(result, referenceWidthInput != null ? referenceWidthInput.gameObject : null);
            AddIfNotNull(result, useGroundResolutionToggle != null ? useGroundResolutionToggle.gameObject : null);
            AddIfNotNull(result, surveyStatsText != null ? surveyStatsText.gameObject : null);
            AddIfNotNull(result, rotateEntryButton != null ? rotateEntryButton.gameObject : null);
            AddIfNotNull(result, presetGenericButton != null ? presetGenericButton.gameObject : null);
            AddIfNotNull(result, presetCorridorButton != null ? presetCorridorButton.gameObject : null);
            AddIfNotNull(result, presetFastButton != null ? presetFastButton.gameObject : null);
            return result;
        }

        private static void AddIfNotNull(List<GameObject> list, GameObject go)
        {
            if (go != null && !list.Contains(go))
                list.Add(go);
        }
    }
}
