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

    [Serializable]
    internal sealed class DocumentOpenResponse
    {
        public string status;
        public string path;
        public string error;
    }

    /// <summary>Android Storage Access Framework bridge for project files.</summary>
    public sealed class AndroidDocumentBridge : MonoBehaviour
    {
        private Action<bool, string, long> saveCompleted;
        private Action<bool, string, string> openCompleted;
        private string stagingPath;

        public bool BeginGlbSave(byte[] glb, string suggestedName, Action<bool, string, long> completed, out string error)
        { return BeginSave(glb, suggestedName, "glb", false, completed, out error); }

        public bool BeginJsonSave(string json, string suggestedName, Action<bool, string, long> completed, out string error)
        {
            if (string.IsNullOrWhiteSpace(json)) { error = "JSON data is empty."; return false; }
            return BeginSave(new UTF8Encoding(false).GetBytes(json), suggestedName, "json", true, completed, out error);
        }

        public bool BeginJsonOpen(Action<bool, string, string> completed, out string error)
        {
            error = null;
            if (openCompleted != null) { error = "A JSON import is already in progress."; return false; }
            openCompleted = completed;
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var picker = new AndroidJavaClass("com.arnav.filepicker.ArnavImagePicker"))
                    picker.CallStatic("openJsonPicker", gameObject.name, "OnJsonOpenResult");
#elif UNITY_EDITOR
                var path = UnityEditor.EditorUtility.OpenFilePanel("Import map JSON", string.Empty, "json");
                if (string.IsNullOrEmpty(path)) CompleteOpen(false, null, "JSON import cancelled.");
                else CompleteOpen(true, File.ReadAllText(path), null);
#else
                CompleteOpen(false, null, "JSON import is available in Android and the Unity Editor.");
#endif
                return true;
            }
            catch (Exception exception) { CompleteOpen(false, null, exception.Message); error = exception.Message; return false; }
        }

        public void OnJsonOpenResult(string responseJson)
        {
            DocumentOpenResponse response;
            try { response = JsonUtility.FromJson<DocumentOpenResponse>(responseJson); }
            catch (Exception exception) { CompleteOpen(false, null, exception.Message); return; }
            if (response == null || response.status != "success")
            { CompleteOpen(false, null, response == null ? "Invalid JSON picker response." : response.status == "cancelled" ? "JSON import cancelled." : response.error); return; }
            try
            {
                if (string.IsNullOrWhiteSpace(response.path) || !File.Exists(response.path)) throw new FileNotFoundException("Selected JSON was not copied into the app.");
                var info = new FileInfo(response.path);
                if (info.Length > 16 * 1024 * 1024) throw new InvalidDataException("JSON file exceeds the 16 MiB import limit.");
                CompleteOpen(true, File.ReadAllText(response.path), null);
            }
            catch (Exception exception) { CompleteOpen(false, null, exception.Message); }
            finally { if (!string.IsNullOrEmpty(response.path)) try { File.Delete(response.path); } catch (IOException) { } }
        }

        private void CompleteOpen(bool success, string contents, string error)
        { var callback = openCompleted; openCompleted = null; callback?.Invoke(success, contents, error); }

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

        private void OnDestroy() { if (saveCompleted != null) Complete(false, "Export was interrupted.", 0); if (openCompleted != null) CompleteOpen(false, null, "Import was interrupted."); }
    }
}
