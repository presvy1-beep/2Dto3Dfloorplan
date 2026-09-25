using UnityEngine;

namespace ARNav.Core.Navigation
{
    /// <summary>Perspective-camera distance needed to fit a bounding sphere in the current viewport.</summary>
    public static class CameraFramingUtility
    {
        public static float CalculateFitDistance(Bounds bounds, float verticalFieldOfViewDegrees, float aspect, float margin = 1.12f)
        {
            var halfVertical = Mathf.Clamp(verticalFieldOfViewDegrees, 1f, 179f) * Mathf.Deg2Rad * 0.5f;
            aspect = Mathf.Max(0.01f, aspect);
            var halfHorizontal = Mathf.Atan(Mathf.Tan(halfVertical) * aspect);
            var limitingHalfFov = Mathf.Min(halfVertical, halfHorizontal);
            var radius = Mathf.Max(0.01f, bounds.extents.magnitude);
            return radius / Mathf.Sin(limitingHalfFov) * Mathf.Max(1f, margin);
        }
    }
}
