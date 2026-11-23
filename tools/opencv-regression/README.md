# OpenCV real-plan regression

This checks the production Java classifier on the user's two local JPEGs without putting those images in the repository. It requires the official OpenCV 4.14.0 Windows package (`opencv-4140.jar` and `opencv_java4140.dll`) and a JDK. `OpenCVLoader.java` is a desktop test adapter; it is not included in the Android app.

From the repository root in PowerShell, with `$opencvJava` set to the package's `opencv/build/java` directory and `$jdkBin` to a JDK `bin` directory:

```powershell
& "$jdkBin/javac.exe" -cp "$opencvJava/opencv-4140.jar" -d "$env:TEMP/opencv-regression-classes" tools/opencv-regression/org/opencv/android/OpenCVLoader.java Assets/Plugins/Android/src/main/java/com/arnav/filepicker/OpenCvFloorplanProcessor.java tools/opencv-regression/OpenCvFloorplanRegression.java
& "$jdkBin/java.exe" "-Djava.library.path=$opencvJava/x64" -cp "$env:TEMP/opencv-regression-classes;$opencvJava/opencv-4140.jar" OpenCvFloorplanRegression "$env:USERPROFILE/Downloads/f1.jpeg" "$env:USERPROFILE/Downloads/fp2.jpeg"
```

This checks wall/floor mask landmarks and title-block exclusion. It does not validate Unity mesh rendering or Android input and export UI.
