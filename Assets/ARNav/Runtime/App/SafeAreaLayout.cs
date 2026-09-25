using UnityEngine;

namespace ARNav.Runtime.App
{
    /// <summary>Constrains a full-screen UI container to the device safe area.</summary>
    public sealed class SafeAreaLayout : MonoBehaviour
    {
        private RectTransform rectTransform;
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;

        private void Awake() { rectTransform = GetComponent<RectTransform>(); if (DisplayOrientationState.IsStable) Apply(); }
        private void OnEnable() { if (rectTransform == null) rectTransform = GetComponent<RectTransform>(); if (DisplayOrientationState.IsStable) Apply(); }
        private void Update()
        {
            if (rectTransform == null || !DisplayOrientationState.IsStable) return;
            if (lastSafeArea != Screen.safeArea || lastScreenSize.x != Screen.width || lastScreenSize.y != Screen.height) Apply();
        }
        private void Apply()
        {
            var area = Screen.safeArea;
            var width = Mathf.Max(1, Screen.width); var height = Mathf.Max(1, Screen.height);
            rectTransform.anchorMin = new Vector2(area.xMin / width, area.yMin / height);
            rectTransform.anchorMax = new Vector2(area.xMax / width, area.yMax / height);
            rectTransform.offsetMin = Vector2.zero; rectTransform.offsetMax = Vector2.zero;
            lastSafeArea = area; lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        }
    }
}
