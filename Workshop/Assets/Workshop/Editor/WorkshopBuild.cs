using System.IO;
using UnityEditor;
using UnityEditor.Build;
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
        private static int CommitCount()
        {
            try
            {
                var p = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo("git", "rev-list --count HEAD")
                    {
                        RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
                    },
                };
                p.Start();
                var n = int.Parse(p.StandardOutput.ReadToEnd().Trim());
                p.WaitForExit();
                return n;
            }
            catch { return 1; }
        }

        public static void Apk()
        {
            PlayerSettings.productName = "Opus Workshop";
            PlayerSettings.companyName = "Opus Systems";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "dev.opustower.workshop");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            // The Mac's now-playing and artwork endpoints are plain HTTP on
            // the LAN; without this every UnityWebRequest to them is refused.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            EditorUserBuildSettings.buildAppBundle = false;

            // Version: the commit count, so every build installs over the last.
            PlayerSettings.Android.bundleVersionCode = CommitCount();
            PlayerSettings.bundleVersion = "1." + PlayerSettings.Android.bundleVersionCode;

            // The icon.
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Workshop/Icon.png");
            if (icon)
            {
                var kinds = PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android);
                foreach (var k in kinds)
                {
                    var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, k);
                    foreach (var i in icons) for (var l = 0; l < i.maxLayerCount; l++) i.SetTexture(icon, l);
                    PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, k, icons);
                }
            }

            // Signing: a release keystore when the environment names one
            // (see README — it lives in ~/.config/opus-systems, never here),
            // else Unity's debug keystore as before.
            var ks = System.Environment.GetEnvironmentVariable("OPUS_KEYSTORE");
            if (!string.IsNullOrEmpty(ks) && File.Exists(ks))
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = ks;
                PlayerSettings.Android.keystorePass = System.Environment.GetEnvironmentVariable("OPUS_KEYSTORE_PASS") ?? "";
                PlayerSettings.Android.keyaliasName = System.Environment.GetEnvironmentVariable("OPUS_KEY_ALIAS") ?? "workshop";
                PlayerSettings.Android.keyaliasPass = System.Environment.GetEnvironmentVariable("OPUS_KEY_PASS") ?? "";
                Debug.Log("WorkshopBuild: signing with the release keystore");
            }
            else
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.Log("WorkshopBuild: OPUS_KEYSTORE not set — debug signing");
            }

            Directory.CreateDirectory("Builds");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Workshop/Scenes/Workshop.unity" },
                locationPathName = "Builds/Workshop.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            // Never leave the passwords in ProjectSettings.
            PlayerSettings.Android.keystorePass = "";
            PlayerSettings.Android.keyaliasPass = "";
            var s = report.summary;
            Debug.Log($"WorkshopBuild: {s.result}, {s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors, {s.totalTime}");
            if (s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
