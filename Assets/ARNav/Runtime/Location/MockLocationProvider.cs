using System;
using ARNav.Core.Location;
using UnityEngine;

namespace ARNav.Runtime.Location
{
    public sealed class MockLocationProvider : MonoBehaviour, ILocationProvider
    {
        public LocationProviderStatus Status { get; private set; } = LocationProviderStatus.Stopped;
        public LocationReading LastReading { get; private set; }
        public event Action<LocationReading> ReadingReceived;
        public event Action<LocationProviderStatus, string> StatusChanged;
        public void StartProvider() { Status = LocationProviderStatus.Running; StatusChanged?.Invoke(Status, "Mock location running."); }
        public void StopProvider() { Status = LocationProviderStatus.Stopped; StatusChanged?.Invoke(Status, "Mock location stopped."); }
        public void Inject(LocationReading reading) { LastReading = reading; if (Status != LocationProviderStatus.Running) StartProvider(); if (reading != null) ReadingReceived?.Invoke(reading); }
    }
}
