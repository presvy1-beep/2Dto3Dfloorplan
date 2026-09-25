using System;
using System.Collections.Generic;
using ARNav.Core.Geo;
using ARNav.Core.Geometry;
using ARNav.Core.Navigation;

namespace ARNav.Core.Data
{
    [Serializable]
    public class MapProjectData
    {
        public int schemaVersion = 1;
        public string projectId;
        public string projectName;
        public string createdUtc;
        public string modifiedUtc;
        public SourceImageData sourceImage = new SourceImageData();
        public ScaleCalibrationData scaleCalibration = new ScaleCalibrationData();
        public GeoCalibrationData geographicCalibration = new GeoCalibrationData();
        public List<FloorData> floors = new List<FloorData>();

        public static MapProjectData Create(string id, string name)
        {
            var now = DateTime.UtcNow.ToString("o");
            return new MapProjectData { projectId = id, projectName = name, createdUtc = now, modifiedUtc = now };
        }
    }

    [Serializable]
    public class SourceImageData
    {
        public string fileName;
        public string sha256;
        public long sourceByteLength;
        public string sourceUri;
        public string pathReference;
        public int originalWidthPixels;
        public int originalHeightPixels;
        public string format;
        public string importedUtc;
    }

    [Serializable]
    public class ScaleCalibrationData
    {
        public PixelPoint firstPixel;
        public PixelPoint secondPixel;
        public float knownDistanceMetres;
        public float metresPerPixel;
        public bool isValid;
        public float pixelDistance;
        public string calibratedUtc;
        public string validationMessage;
    }

    [Serializable]
    public class GeoCalibrationData
    {
        public string method;
        public List<GeoAnchorData> anchors = new List<GeoAnchorData>();
        public float rotationDegrees;
        public double mapHeadingDegrees;
        public double translationEastMetres;
        public double translationNorthMetres;
        public LocalPoint localOrigin;
        public GeoCoordinate geographicOrigin;
        public float fittedScale;
        public float rmsErrorMetres;
        public double maxErrorMetres;
        public double impliedScaleRatio = 1;
        public double scaleMismatchPercent;
        public int anchorCount;
        public string referenceAnchorId;
        public string quality;
        public string validationMessage;
        public float confidence;
        public bool isValid;
    }

    [Serializable]
    public class FloorData
    {
        public string id;
        public string mapSessionId;
        public string sourceSha256;
        public string name;
        public int level;
        public float elevationMetres;
        public LocalPolygon buildingBoundary = new LocalPolygon();
        public List<RoomData> rooms = new List<RoomData>();
        public List<WallData> walls = new List<WallData>();
        // Lossless local-space raster wall rectangles derived from sourceAnnotations.wallRuns.
        public List<LocalRasterRun> architecturalWallRuns = new List<LocalRasterRun>();
        // Scale-derived wall centerlines; rebuilt only when geometry is regenerated,
        // never from transient extraction state during BUILD 3D.
        public List<LocalWallPathData> architecturalWallPaths = new List<LocalWallPathData>();
        public List<DoorData> doors = new List<DoorData>();
        public NavigationData navigation = new NavigationData();
        public FloorPlanAnnotationData sourceAnnotations = new FloorPlanAnnotationData();
    }

    [Serializable]
    public class RoomData
    {
        public string id;
        public string displayName;
        public string roomNumber;
        public LocalPolygon polygon = new LocalPolygon();
        public List<LocalPolygon> holes = new List<LocalPolygon>();
        public List<LocalRasterRun> rasterRuns = new List<LocalRasterRun>();
        public bool preferRasterGeometry;
        public float heightMetres = 3f;
        public List<string> doorIds = new List<string>();
        public string category;
        public string regionType = "Room";
        public bool walkable = true;
        public bool destinationSelectable = true;
        public float detectionConfidence = 1f;
    }

    [Serializable]
    public class LocalRasterRun { public float minX, minZ, maxX, maxZ; }

    [Serializable]
    public class LocalWallPathData
    {
        public int id;
        public float widthMetres;
        public List<LocalPoint> points = new List<LocalPoint>();
        public bool closed;
    }

    [Serializable]
    public class WallData
    {
        public string id;
        public LocalPoint start;
        public LocalPoint end;
        public float heightMetres = 3f;
        public float thicknessMetres = 0.15f;
        public List<string> doorIds = new List<string>();
    }

    [Serializable]
    public class DoorData
    {
        public string id;
        public string parentWallId;
        public float distanceAlongWallMetres;
        public float widthMetres = 0.9f;
        public List<string> connectedRegionIds = new List<string>();
    }
}
