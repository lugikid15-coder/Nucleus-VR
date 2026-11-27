using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NucleusVR.Editor
{
    // Copied into each private build snapshot by BuildQuestApks.py.
    public static class QuestApkBuild
    {
        public static void Run()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android) ||
                EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new BuildFailedException("Launch the matching Unity Editor with Android Build Support and -buildTarget Android.");

            string path = Environment.GetEnvironmentVariable("NUCLEUS_QUEST_APK_PATH");
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) ||
                !string.Equals(Path.GetExtension(path), ".apk", StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("Set NUCLEUS_QUEST_APK_PATH to an absolute .apk output path.");

            if (File.Exists(path))
                throw new BuildFailedException("Use a clean staging path for the APK build.");

            NucleusProjectSetup.Configure();
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            SetSigning();

            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0 || scenes[0] != NucleusProjectSetup.BootPath)
                throw new BuildFailedException("The enabled build scene list must start with NUCLEUS VR's Boot scene.");

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                // Keep credentials confined to the live build, including when Gradle fails.
                PlayerSettings.Android.keystorePass = "";
                PlayerSettings.Android.keyaliasPass = "";
            }
            if (report.summary.result != BuildResult.Succeeded || !File.Exists(path))
                throw new BuildFailedException($"Quest APK build failed ({report.summary.totalErrors} errors). Check the local Editor log.");

            Debug.Log($"NUCLEUS VR {PlayerSettings.bundleVersion} Android build complete: {report.summary.totalSize} bytes.");
        }

        private static void SetSigning()
        {
            string path = Environment.GetEnvironmentVariable("NUCLEUS_ANDROID_KEYSTORE");
            string password = Environment.GetEnvironmentVariable("NUCLEUS_ANDROID_KEYSTORE_PASSWORD");
            string alias = Environment.GetEnvironmentVariable("NUCLEUS_ANDROID_KEY_ALIAS");
            string aliasPassword = Environment.GetEnvironmentVariable("NUCLEUS_ANDROID_KEY_PASSWORD");
            string[] values = { path, password, alias, aliasPassword };
            if (values.All(string.IsNullOrEmpty))
            {
                // Unity uses its stable local development key for Quest sideload builds.
                PlayerSettings.Android.useCustomKeystore = false;
                return;
            }

            if (values.Any(string.IsNullOrEmpty) || !Path.IsPathRooted(path) || !File.Exists(path))
                throw new BuildFailedException("Supply all four Android signing settings and an existing absolute keystore path.");

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = path;
            PlayerSettings.Android.keystorePass = password;
            PlayerSettings.Android.keyaliasName = alias;
            PlayerSettings.Android.keyaliasPass = aliasPassword;
        }
    }
}
