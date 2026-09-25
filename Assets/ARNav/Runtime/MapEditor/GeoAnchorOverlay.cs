using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Geo;
using ARNav.Core.Geometry;
using UnityEngine;
using UnityEngine.UI;

namespace ARNav.Runtime.MapEditor
{
    public sealed class GeoAnchorOverlay : Graphic
    {
        [SerializeField] private Import.FloorPlanViewer viewer;
        private IList<GeoAnchorData> anchors;
        private bool hasPending;
        private PixelPoint pending;
        public void SetAnchors(IList<GeoAnchorData> value, bool pendingSelected, PixelPoint pendingPoint) { anchors = value; hasPending = pendingSelected; pending = pendingPoint; SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if (viewer == null || !viewer.IsLoaded) return;
            if (anchors != null) foreach (var anchor in anchors) if (anchor != null && anchor.hasSourcePixel) Draw(vh, anchor.sourcePixel, Color.cyan, 16);
            if (hasPending) Draw(vh, pending, Color.yellow, 18);
        }
        private void Draw(VertexHelper vh, PixelPoint point, Color color, float size)
        { if (!viewer.TryImagePixelToViewportLocal(point, out var p)) return; var h = size / 2; var i = vh.currentVertCount; vh.AddVert(p + new Vector2(-h, -h), color, Vector2.zero); vh.AddVert(p + new Vector2(-h, h), color, Vector2.zero); vh.AddVert(p + new Vector2(h, h), color, Vector2.zero); vh.AddVert(p + new Vector2(h, -h), color, Vector2.zero); vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i + 2, i + 3, i); }
    }
}
