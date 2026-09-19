using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OpusSystems.Workshop.Editor
{
    /// <summary>
    /// Builds the Quest APK from the CLI:
    ///   Unity -batchmode -projectPath . -buildTarget Android -executeMethod OpusSystems.Workshop.Editor.WorkshopBuild.Apk -quit
    /// Output: Builds/Workshop.apk. Then `adb install -r Builds/Workshop.apk`.
    /// </summary>
    public static class WorkshopBuild
    {
        public static void Apk()
        {
            PlayerSettings.productName = "Opus Workshop";
            PlayerSettings.companyName = "Opus Systems";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "dev.opustower.workshop");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            EditorUserBuildSettings.buildAppBundle = false;

            Directory.CreateDirectory("Builds");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Workshop/Scenes/Workshop.unity" },
                locationPathName = "Builds/Workshop.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"WorkshopBuild: {s.result}, {s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors, {s.totalTime}");
            if (s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
