using System;

namespace ARNav.Core.Geometry
{
    [Serializable]
    public struct ViewerPoint
    {
        public float x;
        public float y;
        public ViewerPoint(float x, float y) { this.x = x; this.y = y; }
    }

    /// Coordinate math for an aspect-preserving image centered in a viewport.
    /// Viewer points use a centered viewport with +Y upward; pixels use upper-left +Y downward.
    public static class FloorPlanCoordinateMapper
    {
        public static float CalculateFitScale(float imageWidth, float imageHeight, float viewportWidth, float viewportHeight)
        { return imageWidth <= 0 || imageHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0 ? 0 : Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight); }

        public static ViewerPoint ImagePixelToViewportLocal(PixelPoint pixel, float imageWidth, float imageHeight, float scale, ViewerPoint pan)
        { return new ViewerPoint(pan.x + (pixel.x / imageWidth - 0.5f) * imageWidth * scale, pan.y + (0.5f - pixel.y / imageHeight) * imageHeight * scale); }

        public static bool TryViewportLocalToImagePixel(ViewerPoint local, float imageWidth, float imageHeight, float scale, ViewerPoint pan, out PixelPoint pixel)
        {
            pixel = new PixelPoint();
            if (imageWidth <= 0 || imageHeight <= 0 || scale <= 0) return false;
            var x = (local.x - pan.x) / (imageWidth * scale) + 0.5f;
            var y = 0.5f - (local.y - pan.y) / (imageHeight * scale);
            if (x < 0 || x > 1 || y < 0 || y > 1) return false;
            pixel = new PixelPoint(x * imageWidth, y * imageHeight);
            return true;
        }
    }
}
