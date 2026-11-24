package com.arnav.filepicker;

import android.app.Activity;
import android.content.ContentResolver;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.provider.OpenableColumns;
import com.unity3d.player.UnityPlayer;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;

/** Small Storage Access Framework bridge. It copies the selected content URI to app cache. */
public final class ArnavImagePicker {
    public static final int REQUEST_CODE = 47101;
    public static final int EXPORT_REQUEST_CODE = 47102;
    public static final int JSON_OPEN_REQUEST_CODE = 47103;
    // Keep native SAF staging aligned with ImageImportLimits.Default (32 MiB).
    private static final long MAX_IMAGE_BYTES = 32L * 1024L * 1024L;
    private static final String PREFS = "ARNavImagePicker";
    private static final String PENDING_RESPONSE = "pendingResponse";
    private static String unityObjectName;
    private static String unityMethodName;
    private static String exportObjectName;
    private static String exportMethodName;
    private static String exportSourcePath;
    private static String jsonObjectName;
    private static String jsonMethodName;

    private ArnavImagePicker() {}

    /** One-shot process-death recovery for the result returned by ACTION_OPEN_DOCUMENT. */
    public static String consumePendingResponse(Activity activity) {
        android.content.SharedPreferences prefs = activity.getSharedPreferences(PREFS, Activity.MODE_PRIVATE);
        String response = prefs.getString(PENDING_RESPONSE, "");
        prefs.edit().remove(PENDING_RESPONSE).apply();
        return response == null ? "" : response;
    }

    public static void openPicker(String objectName, String methodName) {
        unityObjectName = objectName;
        unityMethodName = methodName;
        Activity activity = UnityPlayer.currentActivity;
        cleanupOldCacheFiles(activity);
        android.util.Log.d("ARNavImagePicker", "Opening ACTION_OPEN_DOCUMENT picker");
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("image/*");
        intent.putExtra(Intent.EXTRA_MIME_TYPES, new String[] { "image/png", "image/jpeg", "image/jpg" });
        activity.startActivityForResult(intent, REQUEST_CODE);
    }

    public static void openGlbSavePicker(String objectName, String methodName, String sourcePath, String suggestedName) {
        openDocumentSavePicker(objectName, methodName, sourcePath, suggestedName, "model/gltf-binary");
    }

    public static void openJsonSavePicker(String objectName, String methodName, String sourcePath, String suggestedName) {
        openDocumentSavePicker(objectName, methodName, sourcePath, suggestedName, "application/json");
    }

    public static void openJsonPicker(String objectName, String methodName) {
        Activity activity = UnityPlayer.currentActivity;
        jsonObjectName = objectName; jsonMethodName = methodName;
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("application/json");
        activity.startActivityForResult(intent, JSON_OPEN_REQUEST_CODE);
    }

    private static void openDocumentSavePicker(String objectName, String methodName, String sourcePath, String suggestedName, String mimeType) {
        Activity activity = UnityPlayer.currentActivity;
        exportObjectName = objectName;
        exportMethodName = methodName;
        exportSourcePath = sourcePath;
        Intent intent = new Intent(Intent.ACTION_CREATE_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType(mimeType);
        intent.putExtra(Intent.EXTRA_TITLE, suggestedName);
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
        try { activity.startActivityForResult(intent, EXPORT_REQUEST_CODE); }
        catch (android.content.ActivityNotFoundException exception) {
            intent.setType("application/octet-stream");
            activity.startActivityForResult(intent, EXPORT_REQUEST_CODE);
        }
    }

    public static void handleActivityResult(final Activity activity, int resultCode, Intent data) {
        handleActivityResult(activity, REQUEST_CODE, resultCode, data);
    }

    public static void handleActivityResult(final Activity activity, int requestCode, int resultCode, Intent data) {
        if (requestCode == JSON_OPEN_REQUEST_CODE) {
            if (resultCode != Activity.RESULT_OK || data == null || data.getData() == null) { sendJsonOpen(activity, "{\"status\":\"cancelled\"}"); return; }
            final Uri uri = data.getData();
            new Thread(new Runnable() { @Override public void run() { copyJsonToCache(activity, uri); } }, "ARNavJsonImport").start();
            return;
        }
        if (requestCode == EXPORT_REQUEST_CODE) {
            if (resultCode != Activity.RESULT_OK || data == null || data.getData() == null) {
                sendExport(activity, "{\"status\":\"cancelled\"}");
                return;
            }
            final Uri uri = data.getData();
            new Thread(new Runnable() { @Override public void run() { writeExport(activity, uri); } }, "ARNavGlbExport").start();
            return;
        }
        if (requestCode != REQUEST_CODE) return;
        if (resultCode != Activity.RESULT_OK || data == null || data.getData() == null) {
            android.util.Log.d("ARNavImagePicker", "Picker cancelled");
            send(activity, "{\"status\":\"cancelled\"}");
            return;
        }
        final Uri uri = data.getData();
        new Thread(new Runnable() {
            @Override public void run() { copyToCache(activity, uri); }
        }, "ARNavImagePicker").start();
    }

    private static void writeExport(Activity activity, Uri uri) {
        long bytesCopied = 0;
        try {
            if (exportSourcePath == null) throw new IllegalStateException("Export staging file is unavailable.");
            long expectedBytes = new File(exportSourcePath).length();
            InputStream input = new java.io.FileInputStream(exportSourcePath);
            java.io.OutputStream output = activity.getContentResolver().openOutputStream(uri, "w");
            if (output == null) { input.close(); throw new IllegalStateException("Could not open destination document."); }
            byte[] buffer = new byte[32768]; int read;
            try { while ((read = input.read(buffer)) != -1) { output.write(buffer, 0, read); bytesCopied += read; } output.flush(); }
            finally { try { input.close(); } finally { output.close(); } }
            if (bytesCopied != expectedBytes) throw new IOException("Export byte count mismatch: wrote " + bytesCopied + " of " + expectedBytes + ".");
            sendExport(activity, "{\"status\":\"success\",\"uri\":" + quote(uri.toString()) + ",\"bytes\":" + bytesCopied + "}");
        } catch (Exception exception) {
            android.util.Log.e("ARNavImagePicker", "Could not write GLB document", exception);
            sendExport(activity, "{\"status\":\"error\",\"error\":" + quote(exception.getMessage()) + "}");
        }
    }

    private static void sendExport(final Activity activity, final String json) {
        final String objectName = exportObjectName; final String methodName = exportMethodName;
        exportObjectName = null; exportMethodName = null; exportSourcePath = null;
        activity.runOnUiThread(new Runnable() {
            @Override public void run() { if (objectName != null && methodName != null) UnityPlayer.UnitySendMessage(objectName, methodName, json); }
        });
    }

    private static void copyJsonToCache(Activity activity, Uri uri) {
        File output = new File(activity.getCacheDir(), "arnav-json-" + java.util.UUID.randomUUID() + ".json");
        try (InputStream input = activity.getContentResolver().openInputStream(uri);
             FileOutputStream stream = new FileOutputStream(output)) {
            if (input == null) throw new IOException("Selected JSON could not be opened.");
            byte[] buffer = new byte[8192]; int read; long total = 0;
            while ((read = input.read(buffer)) != -1) {
                total += read;
                if (total > 16L * 1024L * 1024L) throw new IOException("JSON exceeds the 16 MiB import limit.");
                stream.write(buffer, 0, read);
            }
            stream.flush();
            sendJsonOpen(activity, "{\"status\":\"success\",\"path\":" + quote(output.getAbsolutePath()) + "}");
        } catch (Exception exception) {
            output.delete();
            sendJsonOpen(activity, errorJson(exception.getMessage()));
        }
    }

    private static void sendJsonOpen(final Activity activity, final String json) {
        final String objectName = jsonObjectName; final String methodName = jsonMethodName;
        jsonObjectName = null; jsonMethodName = null;
        activity.runOnUiThread(new Runnable() {
            @Override public void run() { if (objectName != null && methodName != null) UnityPlayer.UnitySendMessage(objectName, methodName, json); }
        });
    }

    private static void copyToCache(final Activity activity, Uri uri) {
        ContentResolver resolver = activity.getContentResolver();
        String mime = resolver.getType(uri);
        if (mime != null && !mime.equals("image/png") && !mime.equals("image/jpeg") && !mime.equals("image/jpg")) {
            send(activity, errorJson("Selected document is not a PNG or JPEG image."));
            return;
        }
        String displayName = displayName(resolver, uri);
        String extension = "image/png".equals(mime) || lower(displayName).endsWith(".png") ? ".png" : ".jpg";
        File output = new File(activity.getCacheDir(), "arnav-picker-" + System.currentTimeMillis() + "-" + java.util.UUID.randomUUID() + extension);
        try {
            InputStream input = resolver.openInputStream(uri);
            if (input == null) { send(activity, errorJson("Could not read the selected document.")); return; }
            FileOutputStream outputStream = null;
            long totalBytes = 0;
            try {
                outputStream = new FileOutputStream(output);
                byte[] buffer = new byte[8192]; int read;
                while ((read = input.read(buffer)) != -1) {
                    totalBytes += read;
                    if (totalBytes > MAX_IMAGE_BYTES) throw new IOException("Image exceeds the supported 32 MB limit.");
                    outputStream.write(buffer, 0, read);
                }
                outputStream.flush();
            } finally {
                try { input.close(); }
                finally { if (outputStream != null) outputStream.close(); }
            }
            send(activity, "{\"status\":\"success\",\"path\":" + quote(output.getAbsolutePath()) + ",\"sourceUri\":" + quote(uri.toString()) + ",\"displayName\":" + quote(displayName) + ",\"mimeType\":" + quote(mime == null ? "" : mime) + "}");
            android.util.Log.d("ARNavImagePicker", "Copied selected document to app cache");
        } catch (Exception exception) {
            android.util.Log.e("ARNavImagePicker", "Could not copy selected document", exception);
            if (output.exists()) output.delete();
            String message = exception.getMessage();
            send(activity, errorJson(message != null && message.contains("supported 32 MB limit") ? message : "Could not read the selected image. Check that the file is available and try again."));
        }
    }

    private static String displayName(ContentResolver resolver, Uri uri) {
        Cursor cursor = null;
        try {
            cursor = resolver.query(uri, new String[] { OpenableColumns.DISPLAY_NAME }, null, null, null);
            if (cursor != null && cursor.moveToFirst()) { String value = cursor.getString(0); if (value != null && value.length() > 0) return value; }
        } catch (Exception ignored) {} finally { if (cursor != null) cursor.close(); }
        return "floorplan.jpg";
    }

    private static void send(final Activity activity, final String json) {
        // Store before notifying Unity. If Android recreates/kills the Unity activity
        // while the external picker is foregrounded, the new runtime consumes this.
        activity.getSharedPreferences(PREFS, Activity.MODE_PRIVATE).edit().putString(PENDING_RESPONSE, json).commit();
        activity.runOnUiThread(new Runnable() { @Override public void run() { if (unityObjectName != null && unityMethodName != null) UnityPlayer.UnitySendMessage(unityObjectName, unityMethodName, json); unityObjectName = null; unityMethodName = null; } });
    }
    private static String errorJson(String message) { return "{\"status\":\"error\",\"error\":" + quote(message) + "}"; }
    private static void cleanupOldCacheFiles(Activity activity) {
        File cache = activity.getCacheDir(); File[] files = cache.listFiles();
        if (files == null) return;
        for (File file : files) if (file.getName().startsWith("arnav-picker-")) file.delete();
    }
    private static String quote(String value) { if (value == null) return "\"\""; return "\"" + value.replace("\\", "\\\\").replace("\"", "\\\"").replace("\n", "\\n").replace("\r", "\\r") + "\""; }
    private static String lower(String value) { return value == null ? "" : value.toLowerCase(java.util.Locale.US); }
}
