using System.Globalization;
using ARNav.Core.Location;
using UnityEngine;
using UnityEngine.UI;

namespace ARNav.Runtime.Location
{
    public sealed class LocationHud : MonoBehaviour
    {
        [SerializeField] private Text text;
        public void SetStatus(string status) { if (text != null) text.text = "GPS  " + status; }
        public void SetReading(LocationReading reading, UserRegionResult result)
        {
            if (text == null) return; if (reading == null) { text.text = "GPS  No Fix"; return; }
            var c = CultureInfo.InvariantCulture; var line = "GPS  " + reading.latitude.ToString("F6", c) + "  " + reading.longitude.ToString("F6", c) + "\nALT " + reading.altitudeMetres.ToString("0.0", c) + "m | H ±" + reading.horizontalAccuracyMetres.ToString("0.0", c) + "m | V ±" + reading.verticalAccuracyMetres.ToString("0.0", c) + "m";
            if (result != null) line += "\n" + result.quality + " | " + result.buildingStatus + " | " + (result.roomStatus == RoomPositionStatus.InsideRoom ? result.roomDisplayName : result.roomStatus.ToString()) + (result.hasValidLocation ? " | Local " + result.localPosition.x.ToString("0.0", c) + "," + result.localPosition.z.ToString("0.0", c) : "");
            text.text = line;
        }
    }
}
