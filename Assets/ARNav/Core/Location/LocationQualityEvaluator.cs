using System;

namespace ARNav.Core.Location
{
    public static class LocationQualityEvaluator
    {
        public static LocationQuality Evaluate(double horizontalAccuracyMetres, LocationQualitySettings settings)
        {
            if (settings == null || double.IsNaN(horizontalAccuracyMetres) || double.IsInfinity(horizontalAccuracyMetres) || horizontalAccuracyMetres < 0) return LocationQuality.Unknown;
            if (horizontalAccuracyMetres <= settings.excellentAccuracyMetres) return LocationQuality.Excellent;
            if (horizontalAccuracyMetres <= settings.goodAccuracyMetres) return LocationQuality.Good;
            if (horizontalAccuracyMetres <= settings.fairAccuracyMetres) return LocationQuality.Fair;
            if (horizontalAccuracyMetres <= settings.poorAccuracyMetres) return LocationQuality.Poor;
            return LocationQuality.Unusable;
        }

        public static bool IsStale(LocationReading reading, double nowUnixSeconds, LocationQualitySettings settings, out double ageSeconds)
        { ageSeconds = reading == null ? double.PositiveInfinity : Math.Max(0, nowUnixSeconds - (reading.timestampUnixSeconds > 0 ? reading.timestampUnixSeconds : reading.receivedUnixSeconds)); return reading == null || settings == null || ageSeconds > settings.maxLocationAgeSeconds; }
    }
}
