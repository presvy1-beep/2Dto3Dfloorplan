using System;
using ARNav.Core.Geometry;

namespace ARNav.Core.Navigation
{
    public sealed class WaypointProgressTracker
    {
        public int CurrentIndex { get; private set; } = -1;
        public bool DestinationReached { get; private set; }
        public double DistanceToCurrentMetres { get; private set; } = double.NaN;
        public void Reset(NavigationRoute route) { CurrentIndex = route == null || route.waypoints.Count == 0 ? -1 : 0; DestinationReached = false; DistanceToCurrentMetres = double.NaN; }
        public bool Update(LocalPoint position, NavigationRoute route, double accuracyMetres, bool bypassAccuracy, float arrivalRadiusMetres)
        {
            if (route == null || !route.valid || CurrentIndex < 0 || DestinationReached) return false;
            if (!bypassAccuracy && (double.IsNaN(accuracyMetres) || double.IsInfinity(accuracyMetres) || accuracyMetres > arrivalRadiusMetres * 2.5)) return false;
            var waypoint = route.waypoints[CurrentIndex]; var dx = position.x - waypoint.x; var dz = position.z - waypoint.z; DistanceToCurrentMetres = Math.Sqrt(dx * dx + dz * dz); if (DistanceToCurrentMetres > arrivalRadiusMetres) return false;
            if (CurrentIndex >= route.waypoints.Count - 1) { DestinationReached = true; return true; }
            CurrentIndex++; var next = route.waypoints[CurrentIndex]; dx = position.x - next.x; dz = position.z - next.z; DistanceToCurrentMetres = Math.Sqrt(dx * dx + dz * dz); return true;
        }
    }
}
