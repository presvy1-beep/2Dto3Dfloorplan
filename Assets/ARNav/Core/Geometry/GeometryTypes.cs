using System;

namespace ARNav.Core.Geometry
{
    [Serializable]
    /// <summary>Source-image pixels: origin upper-left, X right, Y downward.</summary>
    public struct PixelPoint
    {
        public float x;
        public float y;

        public PixelPoint(float x, float y) { this.x = x; this.y = y; }
    }

    [Serializable]
    /// <summary>Local map metres: source-image lower-left is (0,0), X is right, Z is upward.</summary>
    public struct LocalPoint
    {
        public float x;
        public float z;

        public LocalPoint(float x, float z) { this.x = x; this.z = z; }
    }

    [Serializable]
    public class LocalPolygon
    {
        public System.Collections.Generic.List<LocalPoint> vertices = new System.Collections.Generic.List<LocalPoint>();
    }
}
