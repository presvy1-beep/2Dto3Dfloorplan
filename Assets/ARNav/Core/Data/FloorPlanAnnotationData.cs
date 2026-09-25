using System;
using System.Collections.Generic;
using ARNav.Core.Geometry;

namespace ARNav.Core.Data
{
    [Serializable]
    public class FloorPlanAnnotationData
    {
        public List<RoomAnnotationData> rooms = new List<RoomAnnotationData>();
        public List<WallAnnotationData> walls = new List<WallAnnotationData>();
        public List<DoorAnnotationData> doors = new List<DoorAnnotationData>();
        // Serialized architectural wall occupancy as merged image-space rectangles.
        // This mask, not detected labels or room semantics, is the 3D wall source.
        public List<PixelRasterRun> wallRuns = new List<PixelRasterRun>();
        // Ordered, derived centerline paths in source-image pixel coordinates.
        public List<WallPathData> wallPaths = new List<WallPathData>();
        public WallPathDiagnostics wallPathDiagnostics = new WallPathDiagnostics();
        public int wallThreshold;
        public int textComponentCount;
        public int textExclusionPixelCount;
        public int wallContinuityRepairCount;
        public long initialBlackPixelCount;
        public long architecturalWallPixelCount;
    }

    [Serializable]
    public class RoomAnnotationData
    {
        public string id;
        public string displayName;
        public string roomNumber;
        public float roomNumberConfidence;
        public List<PixelPoint> vertices = new List<PixelPoint>();
        public List<PixelPolygon> holes = new List<PixelPolygon>();
        // Lossless occupancy fallback, represented as image-space scanline rectangles.
        public List<PixelRasterRun> rasterRuns = new List<PixelRasterRun>();
        public bool preferRasterGeometry;
        public string category;
        // Human-readable semantic classification for extracted/reviewed regions.
        // Existing maps deserialize with the defaults below.
        public string regionType = "Room";
        public bool walkable = true;
        public bool destinationSelectable = true;
        public float detectionConfidence = 1f;
    }

    [Serializable]
    public class PixelPolygon
    {
        public List<PixelPoint> vertices = new List<PixelPoint>();
    }

    [Serializable]
    public class PixelRasterRun { public float minX, minY, maxX, maxY; }

    [Serializable]
    public class WallPathData
    {
        public int id;
        public float widthPixels;
        public List<PixelPoint> points = new List<PixelPoint>();
        public bool closed;
    }

    [Serializable]
    public class WallPathDiagnostics
    {
        public long rawMaskPixels;
        public int skeletonPixels;
        public int endpoints;
        public int junctions;
        public int initialPaths;
        public int finalPaths;
        public int pointsBeforeSimplification;
        public int pointsAfterSimplification;
        public int joinedGaps;
        public float estimatedWidthPixels;
    }

    [Serializable]
    public class WallAnnotationData
    {
        public string id;
        public PixelPoint start;
        public PixelPoint end;
        public List<string> linkedRoomIds = new List<string>();
    }

    [Serializable]
    public class DoorAnnotationData
    {
        public string id;
        public string wallId;
        public float startT;
        public float endT;
        public List<string> connectedRoomIds = new List<string>();
        public float confidence = 1f;
        public string confidenceClass = "High";
    }
}
