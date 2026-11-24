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
            // The native activity and Unity both follow Android's user orientation.
            // Wait for stable portrait dimensions before applying portrait safe areas.
            Debug.Log("[ARNav][Orientation] boot screen=" + Screen.width + "x" + Screen.height +
                      " Screen.orientation=" + Screen.orientation + " deviceOrientation=" + Input.deviceOrientation);
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
                              " safeArea=" + Screen.safeArea + " Screen.orientation=" + Screen.orientation +
                              " deviceOrientation=" + Input.deviceOrientation +
                              " aspect=" + (Screen.height > 0 ? ((float)Screen.width / Screen.height).ToString("0.####") : "0"));
                    yield break;
                }
                yield return null;
            }
            DisplayOrientationState.TryCommitStablePortrait();
            Debug.LogWarning("[ARNav][Display] Portrait settling timeout; using current size=" + Screen.width + "x" + Screen.height +
                             " safeArea=" + Screen.safeArea + " Screen.orientation=" + Screen.orientation +
                             " deviceOrientation=" + Input.deviceOrientation);
        }
        private void Start() { if (SceneManager.GetActiveScene().name != setupSceneName) SceneManager.LoadScene(setupSceneName); }
        private void Update() { DisplayOrientationState.RefreshIfChanged(); }
    }
}
