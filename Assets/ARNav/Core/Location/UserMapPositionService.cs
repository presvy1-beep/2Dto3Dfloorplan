using System;
using ARNav.Core.Data;
using ARNav.Core.Geo;
using ARNav.Core.Geometry;

namespace ARNav.Core.Location
{
    public sealed class UserMapPositionService
    {
        private readonly LocationQualitySettings settings;
        public UserMapPositionService(LocationQualitySettings value = null) { settings = value ?? new LocationQualitySettings(); }
        public UserRegionResult Evaluate(MapProjectData project, LocationReading reading, double nowUnixSeconds)
        {
            var result = new UserRegionResult { quality = LocationQuality.Unknown, buildingStatus = BuildingPositionStatus.Unknown, roomStatus = RoomPositionStatus.Unknown };
            if (reading == null || !reading.IsFiniteAndValid()) { result.diagnostic = "No valid location fix."; return result; }
            if (LocationQualityEvaluator.IsStale(reading, nowUnixSeconds, settings, out result.ageSeconds)) { result.horizontalAccuracyMetres = reading.horizontalAccuracyMetres; result.quality = LocationQualityEvaluator.Evaluate(reading.horizontalAccuracyMetres, settings); result.diagnostic = "Stale GPS fix."; return result; }
            result.horizontalAccuracyMetres = reading.horizontalAccuracyMetres; result.quality = LocationQualityEvaluator.Evaluate(reading.horizontalAccuracyMetres, settings);
            if (project == null || project.geographicCalibration == null || !project.geographicCalibration.isValid || project.floors == null || project.floors.Count == 0) { result.diagnostic = "Map position unavailable — geographic calibration required."; return result; }
            var local = new GeoCoordinateTransformer(project.geographicCalibration).GeoToLocal(new GeoCoordinate(reading.latitude, reading.longitude, reading.altitudeMetres)); result = RegionLocator.Locate(project.floors[0], local, reading.horizontalAccuracyMetres, result.quality); result.ageSeconds = Math.Max(0, nowUnixSeconds - (reading.timestampUnixSeconds > 0 ? reading.timestampUnixSeconds : reading.receivedUnixSeconds)); result.diagnostic = result.quality == LocationQuality.Unusable ? "GPS accuracy is unusable." : "Map position updated."; return result;
        }
    }
}
