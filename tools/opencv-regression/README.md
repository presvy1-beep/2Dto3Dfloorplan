# OpenCV synthetic and real-plan regression

This checks the production Java classifier against five generated layouts and the two local JPEGs without putting private images in the repository. It requires the official OpenCV 4.14.0 Windows package (`opencv-4140.jar` and `opencv_java4140.dll`) and a JDK. `OpenCVLoader.java` is a desktop test adapter; it is not included in the Android app. Synthetic expected masks are drawn before processing and compared with a one-pixel tolerance. The run reports precision, recall, false-positive and missed pixels, and door-gap preservation.

From the repository root in PowerShell, with `$opencvJava` set to the package's `opencv/build/java` directory and `$jdkBin` to a JDK `bin` directory:

```powershell
& "$jdkBin/javac.exe" -cp "$opencvJava/opencv-4140.jar" -d "$env:TEMP/opencv-regression-classes" tools/opencv-regression/org/opencv/android/OpenCVLoader.java Assets/Plugins/Android/src/main/java/com/arnav/filepicker/OpenCvFloorplanProcessor.java tools/opencv-regression/OpenCvFloorplanRegression.java
& "$jdkBin/java.exe" "-Djava.library.path=$opencvJava/x64" -cp "$env:TEMP/opencv-regression-classes;$opencvJava/opencv-4140.jar" OpenCvFloorplanRegression "$env:USERPROFILE/Downloads/f1.jpeg" "$env:USERPROFILE/Downloads/fp2.jpeg" "Logs/opencv-reproducibility.csv"
```

It writes real-plan and synthetic wall masks under `Logs/`, 20-run hash records to the CSV, and native buffers for Unity-side handoff checks under `Logs/opencv-native-runs/`. On the current samples the synthetic results are rectangle P=.939/R=.895, two rooms .952/.897, doorway .924/.895 (gap kept), L shape .973/.900, furniture/text .939/.896. Removing the prior 3x3 wall opening increased real-plan wall pixels from 10,553 to 10,706 on the landscape plan and from 12,527 to 14,692 on the portrait plan; this is preservation, not proof every added pixel is structural. Unity EditMode covers existing geometry and export behavior, but the Java harness does not itself hash Unity mesh output.
