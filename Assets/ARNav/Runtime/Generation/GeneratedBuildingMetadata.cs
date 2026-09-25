using UnityEngine;

namespace ARNav.Runtime.Generation
{
    public sealed class GeneratedRoomMetadata : MonoBehaviour { public string floorId; public string roomId; public string displayName; }
    public sealed class GeneratedWallMetadata : MonoBehaviour { public string floorId; public string wallId; }
    public sealed class GeneratedDoorMetadata : MonoBehaviour { public string floorId; public string wallId; public string doorId; public float widthMetres; }
}
