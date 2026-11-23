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
        private Canvas canvas;
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
            if (setup != null) BuildSetupControls();
            else if (navigation != null) BuildNavigationControls();
        }

        private void BuildSetupControls()
        {
            Hide("TopBar", "PrimaryActions", "AnnotationEditorPanel", "ScaleCalibrationPanel", "GeoCalibrationPanel", "LocationHudText", "LocationMockPanel", "Enable GPS");
            controls = CreatePanel("ARNavControls", new Vector2(0.02f, 0.01f), new Vector2(0.98f, 0.11f));
            var import = CreateButton("Import", controls.transform, new Vector2(0.01f, 0.08f), new Vector2(0.23f, 0.92f));
            setupImportButton = import; setupImportLabel = import.GetComponentInChildren<Text>();
            var copy = CreateButton("Copy JSON", controls.transform, new Vector2(0.26f, 0.08f), new Vector2(0.49f, 0.92f));
            var build = CreateButton("BUILD 3D", controls.transform, new Vector2(0.52f, 0.08f), new Vector2(0.76f, 0.92f));
            var gps = CreateButton("GPS", controls.transform, new Vector2(0.79f, 0.08f), new Vector2(0.99f, 0.92f));
            import.onClick.AddListener(setup.ImportFloorPlan);
            analysis = GetComponent<FloorPlanAnalysisController>();
            if (analysis != null) { analysisStateAction = analyzing => { if (setupImportLabel != null) setupImportLabel.text = analyzing ? "Cancel analysis" : "Import"; }; analysis.AnalysisStateChanged += analysisStateAction; }
            copy.onClick.AddListener(setup.CopyCurrentJson);
            build.onClick.AddListener(setup.Open3DPreview);
            CreateButton("Review / Edit", canvas.transform, new Vector2(0.02f, 0.92f), new Vector2(0.21f, 0.98f)).onClick.AddListener(setup.ReviewGeometry);
            CreateButton("Calibrate Scale", canvas.transform, new Vector2(0.22f, 0.92f), new Vector2(0.44f, 0.98f)).onClick.AddListener(setup.BeginScaleCalibration);
            CreateButton("Export JSON", canvas.transform, new Vector2(0.45f, 0.92f), new Vector2(0.65f, 0.98f)).onClick.AddListener(setup.ExportCurrentJson);
            CreateButton("Import JSON", canvas.transform, new Vector2(0.66f, 0.92f), new Vector2(0.84f, 0.98f)).onClick.AddListener(setup.ImportMapJson);
            statusReadout = CreateStatusReadout(); statusAction = message => { if (statusReadout != null) statusReadout.text = message; }; setup.StatusChanged += statusAction;
            var location = FindObjectOfType<LocationTrackingController>();
            if (location != null) { location.gameObject.SetActive(true); gps.onClick.AddListener(location.EnableLocation); location.SetMinimalReadout(CreateGpsReadout()); }
        }

        private void BuildNavigationControls()
        {
            Hide("NavigationTopBar", "NavigationControls", "NavigationLocationHudText");
            controls = CreatePanel("ARNavNavigationControls", new Vector2(0.02f, 0.01f), new Vector2(0.98f, 0.11f));
            var import = CreateButton("Import", controls.transform, new Vector2(0.01f, 0.08f), new Vector2(0.23f, 0.92f));
            var copy = CreateButton("Copy JSON", controls.transform, new Vector2(0.26f, 0.08f), new Vector2(0.49f, 0.92f));
            var exportJson = CreateButton("Export JSON", controls.transform, new Vector2(0.52f, 0.08f), new Vector2(0.76f, 0.92f));
            var gps = CreateButton("GPS", controls.transform, new Vector2(0.79f, 0.08f), new Vector2(0.99f, 0.92f));
            import.onClick.AddListener(() => SceneManager.LoadScene("MapSetup"));
            copy.onClick.AddListener(navigation.CopyCurrentJson);
            exportJson.onClick.AddListener(navigation.ExportCurrentJson);
            gps.onClick.AddListener(navigation.EnableLocation);
            navigation.SetGpsReadout(CreateGpsReadout());
            topViewButton = CreateButton("TOP VIEW", canvas.transform, new Vector2(0.02f, 0.92f), new Vector2(0.19f, 0.98f));
            topViewAction = ApplyTopView;
            topViewButton.onClick.AddListener(topViewAction);
            export3DButton = CreateButton("EXPORT 3D", canvas.transform, new Vector2(0.20f, 0.92f), new Vector2(0.39f, 0.98f));
            export3DAction = navigation.ExportCurrent3D;
            export3DButton.onClick.AddListener(export3DAction);
            CreateButton("FIT", canvas.transform, new Vector2(0.40f, 0.92f), new Vector2(0.54f, 0.98f)).onClick.AddListener(navigation.FitBuilding);
            CreateButton("RESET", canvas.transform, new Vector2(0.55f, 0.92f), new Vector2(0.74f, 0.98f)).onClick.AddListener(navigation.ResetCamera);
            statusReadout = CreateStatusReadout(); statusAction = message => { if (statusReadout != null) statusReadout.text = message; }; navigation.StatusChanged += statusAction;
            export3DButton.interactable = navigation.CanExportCurrentModel;
            exportAvailabilityAction = available => { if (export3DButton != null) export3DButton.interactable = available; };
            navigation.ExportAvailabilityChanged += exportAvailabilityAction;
        }

        private void ApplyTopView()
        {
            navigation?.FitBuildingTopDown();
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
            var go = new GameObject("WorkflowStatus", typeof(RectTransform), typeof(Text)); go.transform.SetParent(canvas.transform, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(0.02f, 0.83f); rect.anchorMax = new Vector2(0.98f, 0.91f); rect.offsetMin = rect.offsetMax = Vector2.zero;
            var label = go.GetComponent<Text>(); label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = 16; label.color = Color.white; label.alignment = TextAnchor.MiddleLeft; label.raycastTarget = false; label.text = "Import an image or saved JSON map.";
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
            go.transform.SetParent(canvas.transform, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.07f, 0.92f);
            return go;
        }

        private Text CreateGpsReadout()
        {
            var go = new GameObject("GPSReadout", typeof(RectTransform), typeof(Text)); go.transform.SetParent(canvas.transform, false);
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
