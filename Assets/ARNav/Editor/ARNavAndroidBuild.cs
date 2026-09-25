using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using ARNav.Core.Data;
using ARNav.Core.Serialization;
using ARNav.Runtime.Extraction;
using ARNav.Runtime.App;

namespace ARNav.Editor
{
    public static class ARNavAndroidBuild
    {
        public static void BuildForVerification()
        {
            try
            {
                var scenes = new[] { "Assets/ARNav/Scenes/Boot.unity", "Assets/ARNav/Scenes/MapSetup.unity", "Assets/ARNav/Scenes/Navigation.unity" };
                var options = new BuildPlayerOptions { scenes = scenes, locationPathName = "/tmp/arnav-dev.apk", target = BuildTarget.Android, options = BuildOptions.None };
                var report = BuildPipeline.BuildPlayer(options);
                var success = report.summary.result == BuildResult.Succeeded && report.summary.totalErrors == 0 && File.Exists(options.locationPathName);
                Debug.Log("ARNav Android build result: " + report.summary.result + ", errors=" + report.summary.totalErrors + ", warnings=" + report.summary.totalWarnings + ", apk=" + options.locationPathName);
                EditorApplication.Exit(success ? 0 : 1);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        /// <summary>Builds the single non-development APK intended for sideloading and sharing.</summary>
        public static void BuildShareableApk()
        {
            try
            {
                var scenes = new[] { "Assets/ARNav/Scenes/Boot.unity", "Assets/ARNav/Scenes/MapSetup.unity", "Assets/ARNav/Scenes/Navigation.unity" };
                var output = Path.GetFullPath(Path.Combine("b", "ARNav.apk"));
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                var options = new BuildPlayerOptions { scenes = scenes, locationPathName = output, target = BuildTarget.Android, options = BuildOptions.None };
                var report = BuildPipeline.BuildPlayer(options);
                var success = report.summary.result == BuildResult.Succeeded && report.summary.totalErrors == 0 && File.Exists(output) && new FileInfo(output).Length > 0;
                Debug.Log("ARNav shareable Android build result: " + report.summary.result + ", errors=" + report.summary.totalErrors +
                          ", warnings=" + report.summary.totalWarnings + ", developmentBuild=false, package=" + PlayerSettings.applicationIdentifier +
                          ", version=" + PlayerSettings.bundleVersion + ", apk=" + output);
                EditorApplication.Exit(success ? 0 : 1);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        /// <summary>Development-only APK that enables the on-device production-pipeline smoke runner.</summary>
        public static void BuildForDeviceSmoke()
        {
            var scenes = new[] { "Assets/ARNav/Scenes/Boot.unity", "Assets/ARNav/Scenes/MapSetup.unity", "Assets/ARNav/Scenes/Navigation.unity" };
            var options = new BuildPlayerOptions { scenes = scenes, locationPathName = "/tmp/arnav-dev.apk", target = BuildTarget.Android, options = BuildOptions.Development };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log("ARNav Android device-smoke build result: " + report.summary.result + ", errors=" + report.summary.totalErrors + ", warnings=" + report.summary.totalWarnings);
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        /// <summary>Separate development APK for the professor presentation; it uses the same real smoke/demo path.</summary>
        public static void BuildProfessorDemo()
        {
            var scenes = new[] { "Assets/ARNav/Scenes/Boot.unity", "Assets/ARNav/Scenes/MapSetup.unity", "Assets/ARNav/Scenes/Navigation.unity" };
            var options = new BuildPlayerOptions { scenes = scenes, locationPathName = "/tmp/arnav-prof-demo.apk", target = BuildTarget.Android, options = BuildOptions.Development | BuildOptions.ConnectWithProfiler };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log("ARNav professor demo build result: " + report.summary.result + ", errors=" + report.summary.totalErrors + ", warnings=" + report.summary.totalWarnings);
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

    }
}
