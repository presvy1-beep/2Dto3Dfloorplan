using System.Collections.Generic;
using ARNav.Core.Geometry;

namespace ARNav.Core.Navigation
{
    public sealed class NavigationRoute
    {
        public bool valid;
        public string destinationRoomId;
        public string destinationRoomName;
        public LocalPoint startLocal;
        public LocalPoint destinationLocal;
        public readonly List<LocalPoint> rawCorners = new List<LocalPoint>();
        public readonly List<LocalPoint> waypoints = new List<LocalPoint>();
        public double totalDistanceMetres;
        public string status;
        public string backend;
        public string mapSessionId;
        public string sourceSha256;
        public int exploredNodes;
        public int rawGridPathLength;
    }
}
