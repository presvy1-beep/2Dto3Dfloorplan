using UnityEngine;
using System;

namespace ARNav.Runtime.Import
{
    /// Selects floor-plan images through the Editor picker or the Android document-picker bridge.
    public sealed class UnityImageImportService : MonoBehaviour, IImageImportService
    {
        private bool busy;
        public event Action<ImageImportResult> ResultRecovered;

#if UNITY_ANDROID && !UNITY_EDITOR
        private void Start()
        {
            // Activity/process recreation while the Android document picker is open
            // loses the managed callback. Recover the one-shot native response.
            TryRecoverPendingResponse();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) TryRecoverPendingResponse();
        }

        private void TryRecoverPendingResponse()
        {
            if (pendingCallback != null) return;
            try
            {
                string response;
                using (var picker = new AndroidJavaClass("com.arnav.filepicker.ArnavImagePicker"))
                using (var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer").GetStatic<AndroidJavaObject>("currentActivity"))
                    response = picker.CallStatic<string>("consumePendingResponse", activity);
                if (!string.IsNullOrWhiteSpace(response)) OnAndroidPickerResult(response);
            }
            catch (Exception exception) { Debug.LogWarning("ARNav: picker result recovery failed: " + exception.Message); }
        }
#endif

        public void SelectImage(Action<ImageImportResult> callback)
        {
            if (callback == null) throw new ArgumentNullException("callback");
            if (busy) { callback(ImageImportResult.Failure("An image import is already in progress.")); return; }
#if UNITY_EDITOR
            try
            {
                var path = UnityEditor.EditorUtility.OpenFilePanel("Import Floor Plan", "", "png,jpg,jpeg");
                callback(string.IsNullOrEmpty(path) ? ImageImportResult.Cancelled() : ImageImportResult.Success(path, System.IO.Path.GetFileName(path)));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("ARNav: image selection failed: " + exception.Message);
                callback(ImageImportResult.Failure("The image picker could not open. Try selecting the image again."));
            }
#elif UNITY_ANDROID
            busy = true;
            pendingCallback = callback;
            try
            {
                Debug.Log("ARNav: opening Android document picker.");
                using (var picker = new AndroidJavaClass("com.arnav.filepicker.ArnavImagePicker")) picker.CallStatic("openPicker", gameObject.name, "OnAndroidPickerResult");
            }
            catch (Exception exception)
            {
                busy = false; pendingCallback = null; callback(ImageImportResult.Failure("Could not open the Android document picker.")); Debug.LogException(exception);
            }
#else
            callback(ImageImportResult.Failure("Image selection is only configured for the Unity Editor and Android in this phase."));
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private Action<ImageImportResult> pendingCallback;
        private string lastResponseJson;
        public void OnAndroidPickerResult(string responseJson)
        {
            if (string.IsNullOrEmpty(responseJson) || responseJson == lastResponseJson)
            {
                Debug.Log("ARNav: duplicate Android picker response ignored.");
                return;
            }
            lastResponseJson = responseJson;
            busy = false;
            var callback = pendingCallback; pendingCallback = null;
            var result = AndroidImagePickerResultParser.Parse(responseJson);
            Debug.Log("ARNav: Android picker completed with " + (result.succeeded ? "success" : result.cancelled ? "cancelled" : "failure") + ".");
            try
            {
                using (var picker = new AndroidJavaClass("com.arnav.filepicker.ArnavImagePicker"))
                using (var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer").GetStatic<AndroidJavaObject>("currentActivity"))
                    picker.CallStatic<string>("consumePendingResponse", activity);
            }
            catch (Exception exception) { Debug.LogWarning("ARNav: could not acknowledge recovered picker response: " + exception.Message); }
            if (callback != null) callback(result); else ResultRecovered?.Invoke(result);
        }
#endif
    }
}
