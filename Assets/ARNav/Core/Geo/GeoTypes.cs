using System;
using ARNav.Core.Geometry;

namespace ARNav.Core.Geo
{
    [Serializable]
    public struct GeoCoordinate
    {
        public double latitude;
        public double longitude;
        public double altitude;

        public GeoCoordinate(double latitude, double longitude, double altitude = 0)
        { this.latitude = latitude; this.longitude = longitude; this.altitude = altitude; }
    }

    [Serializable]
    public class GeoAnchorData
    {
        public string id;
        public LocalPoint localPosition;
        public PixelPoint sourcePixel;
        public bool hasSourcePixel;
        public GeoCoordinate coordinate;
        public bool hasAltitude;
        public string label;
    }

    [Serializable]
    public struct EastNorthPoint
    {
        public double east;
        public double north;
        public EastNorthPoint(double east, double north) { this.east = east; this.north = north; }
    }

    public enum GeoCalibrationQuality { Invalid, Excellent, Good, Fair, Poor }

    public enum GeoCalibrationMethod { None, OneAnchorHeading, TwoAnchors, MultiAnchorRigid }
}
