using System;
using System.Globalization;

namespace ARNav.Core.Location
{
    public static class LocationDisplayFormatter
    {
        public static string Format(LocationReading reading, double nowUnixSeconds, double maxAgeSeconds = 15)
        {
            if (reading == null || !reading.IsFiniteAndValid()) return "GPS unavailable";
            var age = Math.Max(0, nowUnixSeconds - (reading.timestampUnixSeconds > 0 ? reading.timestampUnixSeconds : reading.receivedUnixSeconds));
            if (age > maxAgeSeconds) return "GPS stale (" + age.ToString("0", CultureInfo.InvariantCulture) + "s old)";
            var culture = CultureInfo.InvariantCulture;
            var altitude = !double.IsNaN(reading.altitudeMetres) && !double.IsInfinity(reading.altitudeMetres) &&
                           reading.verticalAccuracyMetres > 0
                ? reading.altitudeMetres.ToString("0.0", culture) + " m (±" + reading.verticalAccuracyMetres.ToString("0.0", culture) + " m)"
                : "unavailable";
            return "GPS lat " + reading.latitude.ToString("F6", culture) + "\nGPS lon " + reading.longitude.ToString("F6", culture) +
                   "\nAltitude " + altitude + "\nAccuracy ±" + reading.horizontalAccuracyMetres.ToString("0.0", culture) + " m";
        }
    }
}
