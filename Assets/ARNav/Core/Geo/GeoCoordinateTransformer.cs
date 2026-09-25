using System;
using ARNav.Core.Data;
using ARNav.Core.Geometry;

namespace ARNav.Core.Geo
{
    /// <summary>Applies the persisted rigid local X/Z to geographic East/North transform.</summary>
    public sealed class GeoCoordinateTransformer
    {
        private readonly GeoCalibrationData calibration;
        public GeoCoordinateTransformer(GeoCalibrationData value) { calibration = value; }
        public GeoCoordinate LocalToGeo(LocalPoint point)
        { var en = GeoCalibrationService.Transform(new EastNorthPoint(point.x, point.z), calibration.mapHeadingDegrees); en = new EastNorthPoint(en.east + calibration.translationEastMetres, en.north + calibration.translationNorthMetres); return GeoProjectionService.ToGeoCoordinate(en, calibration.geographicOrigin); }
        public LocalPoint GeoToLocal(GeoCoordinate coordinate)
        { var en = GeoProjectionService.ToEastNorth(coordinate, calibration.geographicOrigin); en = new EastNorthPoint(en.east - calibration.translationEastMetres, en.north - calibration.translationNorthMetres); var inverse = GeoCalibrationService.Transform(en, -calibration.mapHeadingDegrees); return new LocalPoint((float)inverse.east, (float)inverse.north); }
    }
}
