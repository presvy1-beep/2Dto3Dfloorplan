using System.Globalization;
using ARNav.Core.Location;
using UnityEngine;
using UnityEngine.UI;

namespace ARNav.Runtime.Location
{
    public sealed class LocationHud : MonoBehaviour
    {
        [SerializeField] private Text text;
        public void SetStatus(string status) { if (text != null) text.text = status != null && status.StartsWith("GPS", System.StringComparison.Ordinal) ? status : "GPS  " + status; }
        public void SetReading(LocationReading reading, UserRegionResult result)
        {
            if (text == null) return; if (reading == null) { text.text = "GPS  No Fix"; return; }
            var c = CultureInfo.InvariantCulture; var line = LocationDisplayFormatter.Format(reading, System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
            if (result != null) line += "\n" + result.quality + " | " + result.buildingStatus + " | " + (result.roomStatus == RoomPositionStatus.InsideRoom ? result.roomDisplayName : result.roomStatus.ToString()) + (result.hasValidLocation ? " | Local " + result.localPosition.x.ToString("0.0", c) + "," + result.localPosition.z.ToString("0.0", c) : "");
            text.text = line;
        }
    }
}
