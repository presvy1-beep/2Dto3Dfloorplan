using System;
using System.Collections.Generic;
using ARNav.Core.Geometry;

namespace ARNav.Core.Navigation
{
    public enum NavigationNodeType { Waypoint, Door, RoomEntry, Corridor }

    [Serializable]
    public class NavigationData
    {
        public List<NavigationNodeData> nodes = new List<NavigationNodeData>();
        public List<NavigationEdgeData> edges = new List<NavigationEdgeData>();
    }

    [Serializable]
    public class NavigationNodeData
    {
        public string id;
        public LocalPoint localPosition;
        public float arrivalRadiusMetres = 0.5f;
        public string roomId;
        public string doorId;
        public string regionId;
        public NavigationNodeType type;
    }

    [Serializable]
    public class NavigationEdgeData
    {
        public string fromNodeId;
        public string toNodeId;
        public float distanceMetres;
        public float cost;
        public bool bidirectional = true;
    }
}
