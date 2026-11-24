# Historical OpenCV integration (superseded)

This audit records an earlier implementation. Commit `ce881d7` replaced it with the production C# `DirectBlackMaskExtractor` and `StructuralBlackMaskFilter`. The current Android build contains no OpenCV AAR, native OpenCV library, or Java OpenCV processor. The Android document picker remains in use. The details below describe the retired implementation only.

The former Android floorplan analyzer called `OpenCvFloorplanProcessor.process` on a worker thread. It used the official `org.opencv:opencv:4.14.0` Android AAR from Maven Central. The former `Assets/Plugins/Android/opencv-4.14.0-arm64.aar` was a repack of that AAR containing its Java classes, manifest, metadata, and `libopencv_java4.so` for **ARM64** only. Its `libc++_shared.so` copy was omitted because Unity already packages that library; including both made Gradle's `mergeReleaseNativeLibs` fail. The original AAR SHA-256 was `6d11b40f6a54113dafe8540b1237b637193cb21e83deb11bc53d6757d35d494d`. It had no network dependency at runtime. OpenCV 4.14.0 is Apache License 2.0; its license was included while the AAR was present.

The Java processor applied Gaussian blur, Otsu guided black thresholding, morphology, connected components, contour size checks, a distance transform for thickness, and exterior flood fill with an estimated footprint fill for open plans. It returned wall and floor masks in original image coordinates. The masks became the source annotations and were converted to saved local geometry by the existing converter. 3D generation and JSON load used that saved geometry.

The retired classifier assumed structural wall ink was black. It could reject pale walls, including the balcony outlines in the supplied landscape JPEG. It did not reliably distinguish all furniture from short walls.

Its estimated scale defaulted to 0.02 metres per image pixel. Those dimensions were approximate, not measurements.
