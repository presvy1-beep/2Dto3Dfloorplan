using System;
using ARNav.Core.Data;
using ARNav.Core.Serialization;
using ARNav.Runtime.App;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ARNav.Runtime.Generation
{
    public sealed class BuildingPreviewController : MonoBehaviour
    {
        [SerializeField] private ProceduralBuildingGenerator generator;
        [SerializeField] private Camera previewCamera;
        [SerializeField] private Text statusText;
        [SerializeField] private Button rebuildButton, clearButton, fitButton, backButton;
        private MapProjectData project;
        private readonly MapSession session = new MapSession();
        public MapSetupState SetupState { get { return session.SetupState; } }
        private Vector3 orbitTarget;
        private float orbitDistance = 20f;
        private float yaw = 35f, pitch = 45f;
        private Vector3 lastPointer;
        private bool dragging;

        private void Awake()
        {
            // AppBootstrap owns portrait locking and display settling. This
            // controller only consumes the settled state.
        }

        private void Start()
        {
            if (rebuildButton != null) rebuildButton.onClick.AddListener(Rebuild);
            if (clearButton != null) clearButton.onClick.AddListener(Clear);
            if (fitButton != null) fitButton.onClick.AddListener(FitBuilding);
            if (backButton != null) backButton.onClick.AddListener(BackToSetup);
            try
            {
                var host = MapSessionHost.Instance;
                if (host == null || host.Session == null || host.Session.ActiveProject == null || string.IsNullOrWhiteSpace(host.ActiveProjectId)) { SetStatus("No active map project. Return to setup and open a project first."); return; }
                project = host.Session.ActiveProject; Rebuild();
            }
            catch (Exception exception) { SetStatus("Could not load map: " + exception.Message); }
        }

        public void Rebuild()
        { if (generator == null || project == null) { SetStatus("3D generator or map is not configured."); return; } if (generator.Generate(project, out var error)) { session.Open(project, MapSetupState.MapGenerated); SetStatus("3D building generated."); FitBuilding(); } else SetStatus(error); }
        public void Clear() { generator?.ClearGeneratedBuilding(); SetStatus("Generated building cleared."); }
        public void FitBuilding()
        {
            if (generator == null || previewCamera == null || generator.GeneratedRoot == null) return;
            var bounds = generator.GeneratedBounds; orbitTarget = bounds.center; orbitDistance = Mathf.Max(5f, bounds.extents.magnitude * 2.2f); UpdateCamera();
        }
        public void BackToSetup() { SceneManager.LoadScene("MapSetup"); }
        private void Update()
        {
            if (previewCamera == null || generator == null || generator.GeneratedRoot == null) return;
            if (Input.mouseScrollDelta.y != 0) { orbitDistance = Mathf.Max(1f, orbitDistance * (1f - Input.mouseScrollDelta.y * 0.1f)); UpdateCamera(); }
            if (Input.GetMouseButtonDown(0)) { dragging = true; lastPointer = Input.mousePosition; }
            if (Input.GetMouseButtonUp(0)) dragging = false;
            if (dragging) { var delta = Input.mousePosition - lastPointer; lastPointer = Input.mousePosition; yaw += delta.x * 0.3f; pitch = Mathf.Clamp(pitch - delta.y * 0.3f, 10f, 85f); UpdateCamera(); }
            if (Input.touchCount == 2) { var first = Input.GetTouch(0); var second = Input.GetTouch(1); var previous = (first.position - first.deltaPosition - (second.position - second.deltaPosition)).magnitude; var current = (first.position - second.position).magnitude; orbitDistance = Mathf.Max(1f, orbitDistance - (current - previous) * 0.02f); UpdateCamera(); }
        }
        private void UpdateCamera() { var rotation = Quaternion.Euler(pitch, yaw, 0); previewCamera.transform.position = orbitTarget + rotation * Vector3.back * orbitDistance; previewCamera.transform.LookAt(new Vector3(orbitTarget.x, generator.GeneratedBounds.min.y, orbitTarget.z)); }
        private void SetStatus(string message) { if (statusText != null) statusText.text = message; Debug.Log("ARNav: " + message); }
        private void OnDestroy() { if (rebuildButton != null) rebuildButton.onClick.RemoveListener(Rebuild); if (clearButton != null) clearButton.onClick.RemoveListener(Clear); if (fitButton != null) fitButton.onClick.RemoveListener(FitBuilding); if (backButton != null) backButton.onClick.RemoveListener(BackToSetup); }
    }
}
