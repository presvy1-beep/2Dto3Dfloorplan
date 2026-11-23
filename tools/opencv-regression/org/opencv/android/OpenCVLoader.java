package org.opencv.android;

/** Desktop test adapter; Android builds use the loader from the official AAR. */
public final class OpenCVLoader {
    private OpenCVLoader() {}
    public static boolean initLocal() {
        System.loadLibrary("opencv_java4140");
        return true;
    }
}
