using System;

namespace ARNav.Core.Geo
{
    /// <summary>WGS84 local tangent-plane approximation intended for building/campus-scale maps.</summary>
    public static class GeoProjectionService
    {
        public const double Wgs84SemiMajorAxis = 6378137.0;
        public const double Wgs84Flattening = 1.0 / 298.257223563;

        public static EastNorthPoint ToEastNorth(GeoCoordinate coordinate, GeoCoordinate reference)
        {
            var lat = DegreesToRadians(reference.latitude); var sin = Math.Sin(lat); var e2 = Wgs84Flattening * (2 - Wgs84Flattening);
            var n = Wgs84SemiMajorAxis / Math.Sqrt(1 - e2 * sin * sin); var m = Wgs84SemiMajorAxis * (1 - e2) / Math.Pow(1 - e2 * sin * sin, 1.5);
            return new EastNorthPoint(DegreesToRadians(coordinate.longitude - reference.longitude) * n * Math.Cos(lat), DegreesToRadians(coordinate.latitude - reference.latitude) * m);
        }

        public static GeoCoordinate ToGeoCoordinate(EastNorthPoint offset, GeoCoordinate reference)
        {
            var lat = DegreesToRadians(reference.latitude); var sin = Math.Sin(lat); var e2 = Wgs84Flattening * (2 - Wgs84Flattening);
            var n = Wgs84SemiMajorAxis / Math.Sqrt(1 - e2 * sin * sin); var m = Wgs84SemiMajorAxis * (1 - e2) / Math.Pow(1 - e2 * sin * sin, 1.5);
            return new GeoCoordinate(reference.latitude + RadiansToDegrees(offset.north / m), reference.longitude + RadiansToDegrees(offset.east / (n * Math.Cos(lat))), reference.altitude);
        }

        public static bool IsValid(GeoCoordinate coordinate)
        { return Finite(coordinate.latitude) && Finite(coordinate.longitude) && coordinate.latitude >= -90 && coordinate.latitude <= 90 && coordinate.longitude >= -180 && coordinate.longitude <= 180; }
        public static double DegreesToRadians(double degrees) { return degrees * Math.PI / 180.0; }
        public static double RadiansToDegrees(double radians) { return radians * 180.0 / Math.PI; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
