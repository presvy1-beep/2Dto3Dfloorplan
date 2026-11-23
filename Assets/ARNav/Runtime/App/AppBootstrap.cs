using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

namespace ARNav.Runtime.App
{
    public sealed class AppBootstrap : MonoBehaviour
    {
        [SerializeField] private string setupSceneName = "MapSetup";
        private void Awake()
        {
            // Lock orientation before the first user-facing scene is shown. Android's
            // manifest is also portrait-only; this prevents a sensor/landscape flash.
            // Android Player Settings and the Activity manifest select portrait
            // before Unity's splash. Do not request it again after the first frame.
            DisplayOrientationState.Reset();
            StartCoroutine(SettlePortraitDisplay());
            if (GetComponent<MapSessionHost>() == null) gameObject.AddComponent<MapSessionHost>();
        }
        private IEnumerator SettlePortraitDisplay()
        {
            var previousWidth = -1; var previousHeight = -1; var stableFrames = 0;
            for (var frame = 0; frame < 120; frame++)
            {
                var valid = Screen.width > 0 && Screen.height > 0 && Screen.height >= Screen.width;
                if (valid && Screen.width == previousWidth && Screen.height == previousHeight) stableFrames++; else stableFrames = 0;
                previousWidth = Screen.width; previousHeight = Screen.height;
                if (stableFrames >= 2 && DisplayOrientationState.TryCommitStablePortrait())
                {
                    Debug.Log("[ARNav][Display] Stable portrait ready frame=" + frame + " size=" + Screen.width + "x" + Screen.height +
                              " safeArea=" + Screen.safeArea + " aspect=" + (Screen.height > 0 ? ((float)Screen.width / Screen.height).ToString("0.####") : "0"));
                    yield break;
                }
                yield return null;
            }
            DisplayOrientationState.TryCommitStablePortrait();
            Debug.LogWarning("[ARNav][Display] Portrait settling timeout; using current size=" + Screen.width + "x" + Screen.height + " safeArea=" + Screen.safeArea);
        }
        private void Start() { if (SceneManager.GetActiveScene().name != setupSceneName) SceneManager.LoadScene(setupSceneName); }
        private void Update() { DisplayOrientationState.RefreshIfChanged(); }
    }
}
