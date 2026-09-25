using UnityEngine;

namespace ARNav.Runtime.Generation
{
    public sealed class GeneratedRoomNavAnchor : MonoBehaviour
    {
        public string waypointId;
        public string floorId;
        public string roomId;
        public string roomNumber;
        public string displayName;
        public string sessionId;
        public string sourceSha256;
        public Vector3 displayPosition;
        public Vector3 navMeshPosition;
        public bool hasNavMeshPosition;
        public float navMeshSampleDistance;
        public bool hasGridPosition;
        public Vector3 gridPosition;
        public int gridComponentId = -1;
    }
}
