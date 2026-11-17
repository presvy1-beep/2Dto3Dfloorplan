using System;

namespace ARNav.Runtime.Extraction
{
    /// <summary>Managed equivalents used by worker-thread extraction. Rounding matches Mathf.RoundToInt (ties to even).</summary>
    internal static class ExtractionMath
    {
        public static int Abs(int v) => Math.Abs(v);
        public static float Abs(float v) => Math.Abs(v);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Min(float a, int b) => Math.Min(a, b);
        public static float Min(int a, float b) => Math.Min(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Max(float a, int b) => Math.Max(a, b);
        public static float Max(int a, float b) => Math.Max(a, b);
        public static float Max(float a, float b, float c) => Math.Max(a, Math.Max(b, c));
        public static int Max(int a, int b, int c) => Math.Max(a, Math.Max(b, c));
        public static int Min(int a, int b, int c) => Math.Min(a, Math.Min(b, c));
        public static int Clamp(int v, int min, int max) => Math.Min(max, Math.Max(min, v));
        public static float Clamp(float v, float min, float max) => Math.Min(max, Math.Max(min, v));
        public static float Clamp(float v, int min, int max) => Math.Min(max, Math.Max(min, v));
        public static int RoundToInt(float v) => (int)Math.Round(v, MidpointRounding.ToEven);
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        private static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Distance(float ax, float ay, float bx, float by) { var x = bx - ax; var y = by - ay; return Sqrt(x * x + y * y); }
    }

    internal struct ExtractionPoint
    {
        public int x, y;
        public ExtractionPoint(int x, int y) { this.x = x; this.y = y; }
    }
}
