using System;
using System.Collections;
using ARNav.Core.Location;
using UnityEngine;

namespace ARNav.Runtime.Location
{
    public sealed class UnityLocationProvider : MonoBehaviour, ILocationProvider
    {
        [SerializeField] private float desiredAccuracyMetres = 3f;
        [SerializeField] private float updateDistanceMetres = 0.5f;
        [SerializeField] private float initializationTimeoutSeconds = 20f;
        [SerializeField] private float updateIntervalSeconds = 1f;
        private Coroutine routine;
        private double lastPublishedTimestamp = -1;
        private int publishedSampleCount;
        public LocationProviderStatus Status { get; private set; } = LocationProviderStatus.Stopped;
        public LocationReading LastReading { get; private set; }
        public event Action<LocationReading> ReadingReceived;
        public event Action<LocationProviderStatus, string> StatusChanged;
        public void StartProvider() { if (routine == null) routine = StartCoroutine(StartRoutine()); }
        public void StopProvider() { if (routine != null) StopCoroutine(routine); routine = null; lastPublishedTimestamp = -1; publishedSampleCount = 0; Input.location.Stop(); SetStatus(LocationProviderStatus.Stopped, "Location stopped."); }
        private IEnumerator StartRoutine()
        {
            SetStatus(LocationProviderStatus.Initializing, "Initializing GPS...");
#if UNITY_ANDROID && !UNITY_EDITOR
            var fine = UnityEngine.Android.Permission.FineLocation; var coarse = UnityEngine.Android.Permission.CoarseLocation;
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(fine) && !UnityEngine.Android.Permission.HasUserAuthorizedPermission(coarse)) UnityEngine.Android.Permission.RequestUserPermissions(new[] { fine, coarse });
            var permissionEnd = Time.realtimeSinceStartup + initializationTimeoutSeconds;
            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(fine) && !UnityEngine.Android.Permission.HasUserAuthorizedPermission(coarse) && Time.realtimeSinceStartup < permissionEnd) yield return null;
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(fine) && !UnityEngine.Android.Permission.HasUserAuthorizedPermission(coarse)) { SetStatus(LocationProviderStatus.PermissionDenied, "Location permission was denied."); routine = null; yield break; }
#endif
            if (!Input.location.isEnabledByUser) { SetStatus(LocationProviderStatus.Disabled, "Location services are disabled."); routine = null; yield break; }
            Input.location.Start(desiredAccuracyMetres, updateDistanceMetres); var end = Time.realtimeSinceStartup + initializationTimeoutSeconds;
            while (Input.location.status == LocationServiceStatus.Initializing && Time.realtimeSinceStartup < end) yield return null;
            if (Input.location.status != LocationServiceStatus.Running) { SetStatus(LocationProviderStatus.Failed, "GPS initialization failed or timed out."); routine = null; yield break; }
            SetStatus(LocationProviderStatus.Running, "GPS running."); while (true) { Publish(); yield return new WaitForSecondsRealtime(updateIntervalSeconds); }
        }
        private void Publish()
        {
            var info = Input.location.lastData;
            if (info.timestamp <= 0 || Math.Abs(info.timestamp - lastPublishedTimestamp) < 0.0001) return;
            lastPublishedTimestamp = info.timestamp; publishedSampleCount++;
            LastReading = new LocationReading { latitude = info.latitude, longitude = info.longitude, altitudeMetres = info.verticalAccuracy > 0 ? info.altitude : double.NaN, horizontalAccuracyMetres = info.horizontalAccuracy, verticalAccuracyMetres = info.verticalAccuracy, timestampUnixSeconds = info.timestamp, receivedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0, isValid = true };
            if (!LastReading.IsFiniteAndValid()) return;
            Debug.Log("[ARNav][GPS] sample=" + publishedSampleCount + " providerTimestamp=" + info.timestamp.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " hAcc=" + info.horizontalAccuracy.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " vAcc=" + info.verticalAccuracy.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            ReadingReceived?.Invoke(LastReading);
        }
        private void SetStatus(LocationProviderStatus value, string message) { Status = value; StatusChanged?.Invoke(value, message); }
        private void OnApplicationPause(bool paused) { if (!paused && Status == LocationProviderStatus.Running) { StopProvider(); StartProvider(); } }
        private void OnDestroy() { StopProvider(); }
    }
}
