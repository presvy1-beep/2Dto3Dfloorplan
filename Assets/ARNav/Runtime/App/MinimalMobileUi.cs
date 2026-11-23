using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ARNav.Runtime.Navigation;
using ARNav.Runtime.Location;
using ARNav.Runtime.Import;
using ARNav.Runtime.Extraction;

namespace ARNav.Runtime.App
{
    /// <summary>
    /// Small production-facing mobile surface. The existing editor controls remain in
    /// the scenes for tooling, but are hidden from the normal runtime presentation.
    /// </summary>
    public sealed class MinimalMobileUi : MonoBehaviour
    {
        private static bool importJsonOnSetup;
        private Canvas canvas;
        private RectTransform safeRoot;
        private Rect lastSafeArea;
        private GameObject controls;
        private MapSetupController setup;
        private NavigationController navigation;
        private Button topViewButton;
        private Button export3DButton;
        private Button setupImportButton;
        private Text setupImportLabel;
        private Text statusReadout;
        private System.Action<string> statusAction;
        private FloorPlanAnalysisController analysis;
        private System.Action<bool> analysisStateAction;
        private UnityEngine.Events.UnityAction topViewAction;
        private UnityEngine.Events.UnityAction export3DAction;
        private System.Action<bool> exportAvailabilityAction;

        private void Start()
        {
            setup = GetComponent<MapSetupController>();
            navigation = GetComponent<NavigationController>();
            canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                var namedCanvas = GameObject.Find(navigation != null ? "NavigationCanvas" : "Canvas");
                if (namedCanvas != null) canvas = namedCanvas.GetComponent<Canvas>();
            }
            if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null) return;
            var safeObject = new GameObject("SafeArea", typeof(RectTransform));
            safeRoot = safeObject.GetComponent<RectTransform>();
            safeRoot.SetParent(canvas.transform, false);
            ApplySafeArea();
            if (setup != null) { BuildSetupControls(); if (importJsonOnSetup) { importJsonOnSetup = false; StartCoroutine(OpenPendingJson()); } }
            else if (navigation != null) BuildNavigationControls();
        }

        private void Update()
        {
            if (safeRoot != null && Screen.safeArea != lastSafeArea) ApplySafeArea();
        }

        private void ApplySafeArea()
        {
            lastSafeArea = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            safeRoot.anchorMin = new Vector2(lastSafeArea.xMin / Screen.width, lastSafeArea.yMin / Screen.height);
            safeRoot.anchorMax = new Vector2(lastSafeArea.xMax / Screen.width, lastSafeArea.yMax / Screen.height);
            safeRoot.offsetMin = safeRoot.offsetMax = Vector2.zero;
        }

        private void BuildSetupControls()
        {
            Hide("TopBar", "PrimaryActions", "AnnotationEditorPanel", "ScaleCalibrationPanel", "GeoCalibrationPanel", "LocationHudText", "LocationMockPanel", "Enable GPS", "Status");
            CreateTitle("Floorplan Converter");
            controls = CreatePanel("FloorplanActions", new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.18f));
            var import = CreateButton("Import Floorplan", controls.transform, new Vector2(0.01f, 0.12f), new Vector2(0.35f, 0.88f));
            setupImportButton = import; setupImportLabel = import.GetComponentInChildren<Text>();
            var build = CreateButton("View 3D", controls.transform, new Vector2(0.36f, 0.12f), new Vector2(0.58f, 0.88f));
            var importJson = CreateButton("Import JSON", controls.transform, new Vector2(0.59f, 0.12f), new Vector2(0.79f, 0.88f));
            var exportJson = CreateButton("Export JSON", controls.transform, new Vector2(0.80f, 0.12f), new Vector2(0.99f, 0.88f));
            import.onClick.AddListener(setup.ImportFloorPlan);
            analysis = GetComponent<FloorPlanAnalysisController>();
            if (analysis != null) { analysisStateAction = analyzing => { if (setupImportLabel != null) setupImportLabel.text = analyzing ? "Cancel" : "Import Floorplan"; }; analysis.AnalysisStateChanged += analysisStateAction; }
            build.onClick.AddListener(setup.Open3DPreview);
            importJson.onClick.AddListener(setup.ImportMapJson);
            exportJson.onClick.AddListener(setup.ExportCurrentJson);
            statusReadout = CreateStatusReadout(); statusAction = message => { if (statusReadout != null) statusReadout.text = message; }; setup.StatusChanged += statusAction;
            var location = FindObjectOfType<LocationTrackingController>();
            if (location != null) { location.gameObject.SetActive(true); location.SetMinimalReadout(CreateGpsReadout()); }
        }

        private void BuildNavigationControls()
        {
            Hide("NavigationTopBar", "NavigationControls", "NavigationLocationHudText", "NavigationStatus");
            controls = CreatePanel("ModelToolbar", new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.11f));
            var import = CreateButton("Import", controls.transform, new Vector2(0f, 0.08f), new Vector2(0.12f, 0.92f));
            var importJson = CreateButton("JSON In", controls.transform, new Vector2(0.125f, 0.08f), new Vector2(0.245f, 0.92f));
            var exportJson = CreateButton("JSON Out", controls.transform, new Vector2(0.25f, 0.08f), new Vector2(0.38f, 0.92f));
            var gps = CreateButton("GPS", controls.transform, new Vector2(0.89f, 0.08f), new Vector2(1f, 0.92f));
            import.onClick.AddListener(() => SceneManager.LoadScene("MapSetup"));
            importJson.onClick.AddListener(() => { importJsonOnSetup = true; SceneManager.LoadScene("MapSetup"); });
            exportJson.onClick.AddListener(navigation.ExportCurrentJson);
            gps.onClick.AddListener(navigation.EnableLocation);
            navigation.SetGpsReadout(CreateGpsReadout());
            topViewButton = CreateButton("Top", controls.transform, new Vector2(0.385f, 0.08f), new Vector2(0.48f, 0.92f));
            topViewAction = ApplyTopView;
            topViewButton.onClick.AddListener(topViewAction);
            export3DButton = CreateButton("GLB", controls.transform, new Vector2(0.755f, 0.08f), new Vector2(0.885f, 0.92f));
            export3DAction = navigation.ExportCurrent3D;
            export3DButton.onClick.AddListener(export3DAction);
            CreateButton("Fit", controls.transform, new Vector2(0.485f, 0.08f), new Vector2(0.58f, 0.92f)).onClick.AddListener(navigation.FitBuilding);
            CreateButton("Reset", controls.transform, new Vector2(0.585f, 0.08f), new Vector2(0.75f, 0.92f)).onClick.AddListener(navigation.ResetCamera);
            statusReadout = CreateStatusReadout(); statusAction = message => { if (statusReadout != null) statusReadout.text = message; }; navigation.StatusChanged += statusAction;
            export3DButton.interactable = navigation.CanExportCurrentModel;
            exportAvailabilityAction = available => { if (export3DButton != null) export3DButton.interactable = available; };
            navigation.ExportAvailabilityChanged += exportAvailabilityAction;
        }

        private void ApplyTopView()
        {
            navigation?.FitBuildingTopDown();
        }

        private System.Collections.IEnumerator OpenPendingJson()
        {
            yield return null;
            setup?.ImportMapJson();
        }

        private void CreateTitle(string value)
        {
            var go = new GameObject("AppTitle", typeof(RectTransform), typeof(Text)); go.transform.SetParent(safeRoot, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(0.04f, 0.90f); rect.anchorMax = new Vector2(0.96f, 0.97f); rect.offsetMin = rect.offsetMax = Vector2.zero;
            var label = go.GetComponent<Text>(); label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = 23; label.color = Color.white; label.alignment = TextAnchor.MiddleLeft; label.text = value; label.raycastTarget = false;
        }

        private void OnDestroy()
        {
            if (topViewButton != null && topViewAction != null) topViewButton.onClick.RemoveListener(topViewAction);
            if (export3DButton != null && export3DAction != null) export3DButton.onClick.RemoveListener(export3DAction);
            if (navigation != null && exportAvailabilityAction != null) navigation.ExportAvailabilityChanged -= exportAvailabilityAction;
            if (analysis != null && analysisStateAction != null) analysis.AnalysisStateChanged -= analysisStateAction;
            if (setup != null && statusAction != null) setup.StatusChanged -= statusAction;
            if (navigation != null && statusAction != null) navigation.StatusChanged -= statusAction;
        }

        private Text CreateStatusReadout()
        {
            var go = new GameObject("WorkflowStatus", typeof(RectTransform), typeof(Text)); go.transform.SetParent(safeRoot, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(0.04f, 0.80f); rect.anchorMax = new Vector2(0.96f, 0.89f); rect.offsetMin = rect.offsetMax = Vector2.zero;
            var label = go.GetComponent<Text>(); label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = 16; label.color = Color.white; label.alignment = TextAnchor.MiddleLeft; label.raycastTarget = false; label.text = "";
            var outline = go.AddComponent<Outline>(); outline.effectColor = Color.black; outline.effectDistance = new Vector2(2f, -2f);
            return label;
        }

        private void Hide(params string[] names)
        {
            foreach (var name in names)
            {
                var found = GameObject.Find(name);
                if (found != null) found.SetActive(false);
            }
        }

        private GameObject CreatePanel(string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(safeRoot, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.07f, 0.92f);
            return go;
        }

        private Text CreateGpsReadout()
        {
            var go = new GameObject("GPSReadout", typeof(RectTransform), typeof(Text)); go.transform.SetParent(safeRoot, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(0.64f, 0.67f); rect.anchorMax = new Vector2(0.98f, 0.82f); rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = 13; text.color = Color.white; text.alignment = TextAnchor.UpperRight; text.raycastTarget = false; text.text = "GPS off"; var outline = go.AddComponent<Outline>(); outline.effectColor = Color.black; outline.effectDistance = new Vector2(2f, -2f); return text;
        }

        private static Button CreateButton(string label, Transform parent, Vector2 min, Vector2 max)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.12f, 0.16f, 0.20f, 1f);
            var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text)); textGo.transform.SetParent(go.transform, false);
            var text = textGo.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.text = label; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white; text.fontSize = 15;
            var textRect = text.rectTransform; textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one; textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            return go.GetComponent<Button>();
        }

    }
}
