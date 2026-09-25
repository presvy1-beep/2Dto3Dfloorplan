using System;
using ARNav.Core.Geometry;

namespace ARNav.Core.Calibration
{
    [Serializable]
    public sealed class ScaleCalibrationResult
    {
        public bool IsValid;
        public string Error;
        public float PixelDistance;
        public float MetresPerPixel;
    }

    /// <summary>Calculates and validates image scale without depending on Unity.</summary>
    public static class ScaleCalibrationService
    {
        public const float MinimumPixelDistance = 0.001f;

        public static ScaleCalibrationResult Calculate(PixelPoint first, PixelPoint second, float knownDistanceMetres, int imageWidth, int imageHeight)
        {
            var result = new ScaleCalibrationResult();
            if (imageWidth <= 0 || imageHeight <= 0) return Fail(result, "Source image dimensions must be positive.");
            if (!Inside(first, imageWidth, imageHeight) || !Inside(second, imageWidth, imageHeight)) return Fail(result, "Calibration points must be inside the source image.");
            if (!Finite(knownDistanceMetres) || knownDistanceMetres <= 0) return Fail(result, "Known distance must be a finite value greater than zero.");
            var dx = second.x - first.x;
            var dy = second.y - first.y;
            var distance = (float)Math.Sqrt(dx * dx + dy * dy);
            if (!Finite(distance) || distance < MinimumPixelDistance) return Fail(result, "Calibration points are too close together.");
            var scale = knownDistanceMetres / distance;
            if (!Finite(scale) || scale <= 0) return Fail(result, "Calculated scale is invalid.");
            result.IsValid = true;
            result.PixelDistance = distance;
            result.MetresPerPixel = scale;
            return result;
        }

        public static bool IsInside(PixelPoint point, int imageWidth, int imageHeight) { return Inside(point, imageWidth, imageHeight); }
        private static bool Inside(PixelPoint point, int width, int height) { return Finite(point.x) && Finite(point.y) && point.x >= 0 && point.x <= width && point.y >= 0 && point.y <= height; }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static ScaleCalibrationResult Fail(ScaleCalibrationResult result, string error) { result.Error = error; return result; }
    }
}
