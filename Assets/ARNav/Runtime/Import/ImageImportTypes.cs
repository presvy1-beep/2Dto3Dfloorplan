using System;
using UnityEngine;

namespace ARNav.Runtime.Import
{
    [Serializable]
    public sealed class ImageImportResult
    {
        public bool succeeded;
        public bool cancelled;
        public bool temporary;
        public string sourcePath;
        public string sourceUri;
        public string displayName;
        public string mimeType;
        public string error;
        public static ImageImportResult Cancelled() { return new ImageImportResult { cancelled = true }; }
        public static ImageImportResult Failure(string message) { return new ImageImportResult { error = message }; }
        public static ImageImportResult Success(string path, string displayName = null, string mimeType = null, bool temporary = false) { return new ImageImportResult { succeeded = true, sourcePath = path, displayName = displayName, mimeType = mimeType, temporary = temporary }; }
    }

    public interface IImageImportService
    {
        void SelectImage(Action<ImageImportResult> callback);
    }

    [Serializable]
    public sealed class AndroidImagePickerResponse
    {
        public string status;
        public string path;
        public string displayName;
        public string mimeType;
        public string sourceUri;
        public string error;
    }

    public static class AndroidImagePickerResultParser
    {
        public static ImageImportResult Parse(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson)) return ImageImportResult.Failure("Android picker returned an empty response.");
            var response = JsonUtility.FromJson<AndroidImagePickerResponse>(responseJson);
            if (response == null || response.status == "error") return ImageImportResult.Failure(response == null ? "Android picker returned an invalid response." : response.error);
            if (response.status == "cancelled") return ImageImportResult.Cancelled();
            if (response.status == "success" && !string.IsNullOrEmpty(response.path)) { var result = ImageImportResult.Success(response.path, response.displayName, response.mimeType, true); result.sourceUri = response.sourceUri; return result; }
            return ImageImportResult.Failure("Android picker did not return a readable image.");
        }
    }
}
