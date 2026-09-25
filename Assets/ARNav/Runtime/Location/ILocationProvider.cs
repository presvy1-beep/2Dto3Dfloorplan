using System;
using ARNav.Core.Location;

namespace ARNav.Runtime.Location
{
    public interface ILocationProvider
    {
        LocationProviderStatus Status { get; }
        LocationReading LastReading { get; }
        event Action<LocationReading> ReadingReceived;
        event Action<LocationProviderStatus, string> StatusChanged;
        void StartProvider();
        void StopProvider();
    }
}
