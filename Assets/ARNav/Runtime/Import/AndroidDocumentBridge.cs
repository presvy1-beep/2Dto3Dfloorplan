using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace ARNav.Runtime.Import
{
    [Serializable]
    internal sealed class DocumentSaveResponse
    {
        public string status;
        public string uri;
        public long bytes;
        public string error;
    }

    /// <summary>Android clipboard and Storage Access Framework bridge for current-session data.</summary>
    public sealed class AndroidDocumentBridge : MonoBehaviour
    {
        private Action<bool, string, long> saveCompleted;
        private string stagingPath;

        public static bool TryCopyTextAndVerify(string text, out string readBack, out string error)
        {
            readBack = null; error = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var picker = new AndroidJavaClass("com.arnav.filepicker.ArnavImagePicker"))
                    readBack = picker.CallStatic<string>("copyTextAndReadBack", text ?? string.Empty);
                if (string.Equals(text ?? string.Empty, readBack ?? string.Empty, StringComparison.Ordinal)) return true;
                error = "Clipboard verification failed. Export the JSON file instead.";
                return false;
            }
            catch (Exception exception)
            {
                // Android clipboard writes are Binder transactions. Large maps can
                // exceed the system parcel limit; treat that as a recoverable copy
                // failure and keep the rest of the current session unaffected.
                var message = exception.Message ?? string.Empty;
                error = message.IndexOf("TransactionTooLarge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        exception.GetType().Name.IndexOf("TransactionTooLarge", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "JSON is too large for the Android clipboard. The map remains available for 3D build and export."
                    : message;
                return false;
            }
#else
            try
            {
                GUIUtility.systemCopyBuffer = text ?? string.Empty;
                readBack = GUIUtility.systemCopyBuffer;
                if (string.Equals(text ?? string.Empty, readBack ?? string.Empty, StringComparison.Ordinal)) return true;
                error = "Clipboard verification failed. Export the JSON file instead.";
                return false;
            }
            catch (Exception exception) { error = exception.Message; return false; }
#endif
        }

        public bool BeginGlbSave(byte[] glb, string suggestedName, Action<bool, string, long> completed, out string error)
        { return BeginSave(glb, suggestedName, "glb", false, completed, out error); }

        public bool BeginJsonSave(string json, string suggestedName, Action<bool, string, long> completed, out string error)
        {
            if (string.IsNullOrWhiteSpace(json)) { error = "JSON data is empty."; return false; }
            return BeginSave(new UTF8Encoding(false).GetBytes(json), suggestedName, "json", true, completed, out error);
        }

        private bool BeginSave(byte[] contents, string suggestedName, string extension, bool json, Action<bool, string, long> completed, out string error)
        {
            error = null;
            if (contents == null || contents.Length < (json ? 2 : 20)) { error = "Export data is empty or incomplete."; return false; }
            if (saveCompleted != null) { error = "A document export is already in progress."; return false; }
            try
            {
                stagingPath = Path.Combine(Application.temporaryCachePath, "arnav-export-" + Guid.NewGuid().ToString("N") + "." + extension);
                File.WriteAllBytes(stagingPath, contents);
                saveCompleted = completed;
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var picker = new AndroidJavaClass("com.arnav.filepicker.ArnavImagePicker"))
                    picker.CallStatic(json ? "openJsonSavePicker" : "openGlbSavePicker", gameObject.name, "OnGlbSaveResult", stagingPath, suggestedName);
                return true;
#elif UNITY_EDITOR
                var path = UnityEditor.EditorUtility.SaveFilePanel(json ? "Export map JSON" : "Export 3D model", string.Empty, Path.GetFileNameWithoutExtension(suggestedName), extension);
                if (string.IsNullOrEmpty(path)) { Complete(false, "Export cancelled.", 0); return true; }
                File.WriteAllBytes(path, contents); Complete(true, path, new FileInfo(path).Length); return true;
#else
                Complete(false, "The document save picker is configured for Android and Editor only.", 0); return false;
#endif
            }
            catch (Exception exception) { Complete(false, exception.Message, 0); error = exception.Message; return false; }
        }

        public void OnGlbSaveResult(string responseJson)
        {
            DocumentSaveResponse response = null;
            try { response = JsonUtility.FromJson<DocumentSaveResponse>(responseJson); }
            catch (Exception exception) { Debug.LogWarning("[ARNav][Export] Invalid Android save response: " + exception.Message); }
            if (response == null || response.status != "success")
            {
                Complete(false, response == null ? "Android save picker returned an invalid response." : response.status == "cancelled" ? "Export cancelled." : response.error, 0);
                return;
            }
            Complete(true, response.uri, response.bytes);
        }

        private void Complete(bool success, string destination, long bytes)
        {
            var callback = saveCompleted; saveCompleted = null;
            if (!string.IsNullOrEmpty(stagingPath)) { try { if (File.Exists(stagingPath)) File.Delete(stagingPath); } catch (IOException exception) { Debug.LogWarning("[ARNav][Export] Temporary-file cleanup failed: " + exception.Message); } }
            stagingPath = null;
            callback?.Invoke(success, destination, bytes);
        }

        public static string Sha256(byte[] bytes)
        { using (var sha = SHA256.Create()) { var hash = sha.ComputeHash(bytes); var builder = new StringBuilder(hash.Length * 2); foreach (var value in hash) builder.Append(value.ToString("x2")); return builder.ToString(); } }

        private void OnDestroy() { if (saveCompleted != null) Complete(false, "Export was interrupted.", 0); }
    }
}
