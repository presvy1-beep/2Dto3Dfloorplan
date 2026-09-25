using System;
using ARNav.Core.Geometry;

namespace ARNav.Core.Location
{
    public enum LocationProviderStatus { Stopped, Initializing, Running, Failed, PermissionDenied, Disabled }
    public enum LocationQuality { Unknown, Excellent, Good, Fair, Poor, Unusable }
    public enum BuildingPositionStatus { Unknown, Inside, Outside, Uncertain }
    public enum RoomPositionStatus { Unknown, InsideRoom, MultiplePossibleRooms, NotInRoom, Uncertain }

    [Serializable]
    public sealed class LocationReading
    {
        public double latitude;
        public double longitude;
        public double altitudeMetres;
        public double horizontalAccuracyMetres;
        public double verticalAccuracyMetres;
        public double timestampUnixSeconds;
        public double receivedUnixSeconds;
        public bool isValid;
        public bool IsFiniteAndValid() { return isValid && Finite(latitude) && Finite(longitude) && Finite(altitudeMetres) && Finite(horizontalAccuracyMetres) && Finite(verticalAccuracyMetres) && horizontalAccuracyMetres >= 0 && verticalAccuracyMetres >= 0; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    public sealed class LocationQualitySettings
    {
        public double excellentAccuracyMetres = 3;
        public double goodAccuracyMetres = 7;
        public double fairAccuracyMetres = 15;
        public double poorAccuracyMetres = 30;
        public double maxLocationAgeSeconds = 15;
    }

    public sealed class UserRegionResult
    {
        public bool hasValidLocation;
        public LocalPoint localPosition;
        public double horizontalAccuracyMetres;
        public double ageSeconds;
        public LocationQuality quality;
        public BuildingPositionStatus buildingStatus;
        public RoomPositionStatus roomStatus;
        public string roomId;
        public string roomDisplayName;
        public readonly System.Collections.Generic.List<string> possibleRoomIds = new System.Collections.Generic.List<string>();
        public double distanceToBuildingBoundaryMetres = double.NaN;
        public double distanceToRoomBoundaryMetres = double.NaN;
        public string diagnostic;
    }
}
