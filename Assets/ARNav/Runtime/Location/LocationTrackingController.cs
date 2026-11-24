using System;
using System.Collections;
using System.Globalization;
using ARNav.Core.Data;
using ARNav.Core.Location;
using UnityEngine;
using UnityEngine.UI;

namespace ARNav.Runtime.Location
{
    public sealed class LocationTrackingController : MonoBehaviour
    {
        [SerializeField] private UnityLocationProvider unityProvider;
        [SerializeField] private MockLocationProvider mockProvider;
        [SerializeField] private LocationHud hud;
        [SerializeField] private Button enableButton;
        [SerializeField] private InputField mockLatitude, mockLongitude, mockAltitude, mockHorizontalAccuracy, mockVerticalAccuracy;
        [SerializeField] private Button applyMockButton;
        private ILocationProvider provider;
        private MapProjectData project;
        private UserMapPositionService positionService;
        private Text minimalReadout;
        private LocationReading lastReading;
        private Coroutine freshnessRoutine;
        public void SetMinimalReadout(Text readout) { minimalReadout = readout; }
        public void Initialize(MapProjectData map)
        { if (provider != null) { provider.ReadingReceived -= OnReading; provider.StatusChanged -= OnStatus; } if (enableButton != null) enableButton.onClick.RemoveListener(EnableLocation); if (applyMockButton != null) applyMockButton.onClick.RemoveListener(ApplyMock); if (freshnessRoutine != null) StopCoroutine(freshnessRoutine); lastReading = null; project = map; positionService = new UserMapPositionService(); provider = Application.isEditor ? (ILocationProvider)mockProvider : unityProvider; if (provider != null) { provider.ReadingReceived += OnReading; provider.StatusChanged += OnStatus; } if (enableButton != null) enableButton.onClick.AddListener(EnableLocation); if (applyMockButton != null) applyMockButton.onClick.AddListener(ApplyMock); if (hud != null) hud.SetStatus("Off"); freshnessRoutine = StartCoroutine(CheckFreshness()); Debug.Log("ARNav: location controller initialized; provider=" + (provider == null ? "missing" : provider.GetType().Name) + ", button=" + (enableButton == null ? "missing" : "bound")); }
        public void EnableLocation() { Debug.Log("ARNav: location enable requested"); provider?.StartProvider(); }
        public void DisableLocation() { provider?.StopProvider(); }
        private void OnReading(LocationReading reading) { lastReading = reading; var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0; var result = positionService.Evaluate(project, reading, now); hud?.SetReading(reading, result); if (minimalReadout != null) minimalReadout.text = LocationDisplayFormatter.Format(reading, now); }
        private void OnStatus(LocationProviderStatus status, string message) { var display = status == LocationProviderStatus.Running ? "GPS waiting for fix" : "GPS " + (message ?? status.ToString()); hud?.SetStatus(display); if (minimalReadout != null) minimalReadout.text = display; }
        private IEnumerator CheckFreshness()
        { while (true) { yield return new WaitForSecondsRealtime(2f); if (lastReading == null) continue; var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0; if (LocationQualityEvaluator.IsStale(lastReading, now, new LocationQualitySettings(), out _)) { var text = LocationDisplayFormatter.Format(lastReading, now); hud?.SetStatus(text); if (minimalReadout != null) minimalReadout.text = text; } } }
        private void ApplyMock()
        { if (!Application.isEditor || mockProvider == null) return; if (!Try(mockLatitude, out var lat) || !Try(mockLongitude, out var lon) || !Try(mockAltitude, out var alt) || !Try(mockHorizontalAccuracy, out var h) || !Try(mockVerticalAccuracy, out var v)) { hud?.SetStatus("Invalid mock location"); return; } mockProvider.Inject(new LocationReading { latitude = lat, longitude = lon, altitudeMetres = alt, horizontalAccuracyMetres = h, verticalAccuracyMetres = v, timestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), receivedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), isValid = true }); }
        private static bool Try(InputField field, out double value) { value = 0; return field != null && double.TryParse(field.text, NumberStyles.Float, CultureInfo.InvariantCulture, out value); }
        private void OnDestroy() { if (freshnessRoutine != null) StopCoroutine(freshnessRoutine); if (provider != null) { provider.ReadingReceived -= OnReading; provider.StatusChanged -= OnStatus; } if (enableButton != null) enableButton.onClick.RemoveListener(EnableLocation); if (applyMockButton != null) applyMockButton.onClick.RemoveListener(ApplyMock); }
    }
}
