using System;
using ARNav.Core.Data;
using ARNav.Core.Geometry;
using ARNav.Core.Location;
using ARNav.Core.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace ARNav.Runtime.Navigation
{
    public static class NavigationRouteService
    {
        public static bool TryCalculate(LocalPoint start, FloorData floor, string roomId, NavigationSettings settings, out NavigationRoute route, out string error)
        {
            route = new NavigationRoute { destinationRoomId = roomId }; error = null; if (floor == null) { error = "No floor is selected."; return false; }
            var room = RoomNavigationPointService.FindRoom(floor, roomId); if (room == null) { error = "Destination room was not found."; return false; }
            if (!RoomNavigationPointService.TryFindInteriorPoint(room, out var candidate, out error)) return false;
            return TryCalculate(start, floor, roomId, candidate, settings, out route, out error);
        }

        public static bool TryCalculate(LocalPoint start, FloorData floor, string roomId, LocalPoint destination, NavigationSettings settings, out NavigationRoute route, out string error)
        {
            route = new NavigationRoute { destinationRoomId = roomId }; error = null; if (floor == null) { error = "No floor is selected."; return false; }
            var room = RoomNavigationPointService.FindRoom(floor, roomId); if (room == null) { error = "Destination room was not found."; return false; }
            var sampleRadius = settings == null ? 1.5f : settings.navMeshSampleRadiusMetres; if (!NavMesh.SamplePosition(ToVector(start, floor.elevationMetres), out var startHit, settings == null ? 2f : settings.startSampleRadiusMetres, NavMesh.AllAreas)) { error = "Current position could not be matched to the walkable map."; return false; }
            if (!RegionLocator.Contains(destination, room.polygon)) { error = "Destination anchor is outside its room."; return false; }
            if (!NavMesh.SamplePosition(ToVector(destination, floor.elevationMetres), out var destinationHit, sampleRadius, NavMesh.AllAreas) || !RegionLocator.Contains(new LocalPoint(destinationHit.position.x, destinationHit.position.z), room.polygon)) { error = "Destination room has no navigable point."; return false; }
            if (!NavMeshPathToLocal(startHit.position, floor.elevationMetres, out var sampledStart)) { error = "Invalid sampled start position."; return false; }
            var path = new NavMeshPath(); if (!NavMesh.CalculatePath(startHit.position, destinationHit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete || path.corners == null || path.corners.Length < 2) { error = path.status == NavMeshPathStatus.PathPartial ? "Destination is unreachable (partial path)." : "Destination is unreachable."; return false; }
            route.valid = true; route.status = "Complete"; route.startLocal = sampledStart; route.destinationLocal = new LocalPoint(destinationHit.position.x, destinationHit.position.z); route.destinationRoomName = room.displayName; foreach (var corner in path.corners) route.rawCorners.Add(new LocalPoint(corner.x, corner.z)); route.waypoints.AddRange(RouteWaypointSampler.Sample(route.rawCorners, settings == null ? 1.5f : settings.waypointSpacingMetres)); route.totalDistanceMetres = RouteWaypointSampler.TotalDistance(route.rawCorners); return true;
        }

        /// <summary>Calculates between already-resolved NavMesh endpoints (in generated world X/Z).</summary>
        public static bool TryCalculateBetween(LocalPoint start, FloorData floor, string roomId, LocalPoint destination, NavigationSettings settings, out NavigationRoute route, out string error)
        {
            route = new NavigationRoute { destinationRoomId = roomId };
            error = null;
            if (floor == null) { error = "No floor is selected."; return false; }
            var room = RoomNavigationPointService.FindRoom(floor, roomId);
            if (room == null) { error = "Destination room was not found."; return false; }
            var elevation = floor.elevationMetres;
            var startPosition = ResolveNear(start, elevation, .35f);
            if (!startPosition.HasValue) { error = "Selected start waypoint has no nearby NavMesh position."; return false; }
            var endPosition = ResolveNear(destination, elevation, .35f);
            if (!endPosition.HasValue) { error = "Selected destination waypoint has no nearby NavMesh position."; return false; }
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(startPosition.Value, endPosition.Value, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete || path.corners == null || path.corners.Length < 2)
            { error = path.status == NavMeshPathStatus.PathPartial ? "Destination is unreachable (partial path)." : "Destination is unreachable."; return false; }
            route.valid = true; route.status = "Complete";
            route.startLocal = new LocalPoint(startPosition.Value.x, startPosition.Value.z);
            route.destinationLocal = new LocalPoint(endPosition.Value.x, endPosition.Value.z);
            route.destinationRoomName = room.displayName;
            foreach (var corner in path.corners) route.rawCorners.Add(new LocalPoint(corner.x, corner.z));
            route.waypoints.AddRange(RouteWaypointSampler.Sample(route.rawCorners, settings == null ? 1.5f : settings.waypointSpacingMetres));
            route.totalDistanceMetres = RouteWaypointSampler.TotalDistance(route.rawCorners);
            return true;
        }

        /// <summary>Bounded progressive lookup: never accepts a sample more than maxDistance from the visible room point.</summary>
        public static bool TryResolveNavMeshPosition(LocalPoint displayPoint, float elevation, float maxDistance, out Vector3 position, out float distance)
        {
            position = default(Vector3); distance = float.PositiveInfinity;
            var radii = new[] { .2f, .4f, .75f, 1.25f, 1.75f, 2.25f };
            foreach (var radius in radii)
            {
                if (radius > maxDistance + .0001f) break;
                if (!NavMesh.SamplePosition(ToVector(displayPoint, elevation), out var hit, radius, NavMesh.AllAreas)) continue;
                var delta = Vector2.Distance(new Vector2(displayPoint.x, displayPoint.z), new Vector2(hit.position.x, hit.position.z));
                if (delta <= maxDistance && delta < distance) { position = hit.position; distance = delta; }
                if (delta < .05f) return true;
            }
            return !float.IsInfinity(distance);
        }

        private static Vector3? ResolveNear(LocalPoint point, float elevation, float radius)
        { return NavMesh.SamplePosition(ToVector(point, elevation), out var hit, radius, NavMesh.AllAreas) ? hit.position : (Vector3?)null; }
        private static Vector3 ToVector(LocalPoint point, float elevation) { return new Vector3(point.x, elevation, point.z); }
        private static bool NavMeshPathToLocal(Vector3 point, float elevation, out LocalPoint local) { local = new LocalPoint(point.x, point.z); return IsFinite(point.x) && IsFinite(point.z) && IsFinite(elevation); }
        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
