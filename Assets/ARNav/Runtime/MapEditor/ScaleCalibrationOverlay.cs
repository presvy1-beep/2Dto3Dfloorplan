using ARNav.Core.Geometry;
using UnityEngine;
using UnityEngine.UI;

namespace ARNav.Runtime.MapEditor
{
    /// <summary>Draws the two source-image points used by scale calibration.</summary>
    public sealed class ScaleCalibrationOverlay : Graphic
    {
        [SerializeField] private Import.FloorPlanViewer viewer;
        private bool hasFirst;
        private bool hasSecond;
        private PixelPoint first;
        private PixelPoint second;

        public void SetPoints(bool firstSelected, PixelPoint firstPoint, bool secondSelected, PixelPoint secondPoint)
        { hasFirst = firstSelected; hasSecond = secondSelected; first = firstPoint; second = secondPoint; SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (viewer == null || !viewer.IsLoaded) return;
            if (hasFirst) DrawMarker(vh, first, Color.yellow, 14f);
            if (hasSecond) DrawMarker(vh, second, Color.magenta, 14f);
        }

        private void DrawMarker(VertexHelper vh, PixelPoint point, Color color, float size)
        {
            if (!viewer.TryImagePixelToViewportLocal(point, out var local)) return;
            var half = size * 0.5f;
            var index = vh.currentVertCount;
            vh.AddVert(local + new Vector2(-half, -half), color, Vector2.zero);
            vh.AddVert(local + new Vector2(-half, half), color, Vector2.zero);
            vh.AddVert(local + new Vector2(half, half), color, Vector2.zero);
            vh.AddVert(local + new Vector2(half, -half), color, Vector2.zero);
            vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index + 2, index + 3, index);
        }
    }
}
