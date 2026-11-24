using System;
using System.Linq;
using NucleusVR.Core;
using NucleusVR.Multiplayer;
using NucleusVR.Player;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace NucleusVR.Editor
{
    public static class NucleusProjectSetup
    {
        internal const string BootPath = "Assets/NucleusVR/Scenes/Boot/Boot.unity";
        internal const string QuestFeature = "com.unity.openxr.feature.metaquest";
        internal const string TouchProfile = "com.unity.openxr.feature.input.oculustouch";
        internal const string TouchPlusProfile = "com.unity.openxr.feature.input.metaquestplus";
        private const string SettingsPath = "Assets/NucleusVR/Settings";
        private const string NetworkPrefabPath = "Assets/NucleusVR/Prefabs/Players/Direct Player.prefab";
        private const string XrSettingsPath = SettingsPath + "/Nucleus XR Settings.asset";

        [MenuItem("Nucleus VR/Configure Project")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before configuring the project.");

            EnsureAssetFolder(SettingsPath);
            ConfigureRendering();
            AssignDirectPlayer();

            // Desktop iteration remains available without Android Build Support installed.
            if (BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                ConfigureQuest();
            else
                Debug.Log("Desktop configuration saved. Install Android Build Support and rerun Configure Project for Quest.");

            AssetDatabase.SaveAssets();
            Debug.Log("Nucleus VR configuration saved. Open Assets/NucleusVR/Scenes/Boot/Boot.unity to enter the calibration bay.");
        }

        private static void ConfigureRendering()
        {
            const string rendererPath = SettingsPath + "/Nucleus Forward Renderer.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }

            const string pipelinePath = SettingsPath + "/Nucleus URP.asset";
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.supportsHDR = false;
                pipeline.msaaSampleCount = 4;
                pipeline.shadowDistance = 18f;
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
        }

        private static void ConfigureQuest()
        {
            const BuildTargetGroup group = BuildTargetGroup.Android;
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget settings))
            {
                settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XrSettingsPath);
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(settings, XrSettingsPath);
                }
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            }

            if (!settings.HasManagerSettingsForBuildTarget(group))
                settings.CreateDefaultManagerSettingsForBuildTarget(group);
            XRGeneralSettings general = settings.SettingsForBuildTarget(group);
            if (!general.Manager.activeLoaders.Any(loader => loader is OpenXRLoader) &&
                !XRPackageMetadataStore.AssignLoader(general.Manager, typeof(OpenXRLoader).FullName, group))
                throw new InvalidOperationException("Could not assign OpenXR to the Android build target.");

            FeatureHelpers.RefreshFeatures(group);
            SetFeature(group, QuestFeature);
            SetFeature(group, TouchProfile);
            SetFeature(group, TouchPlusProfile);

            OpenXRSettings openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (openXr == null)
                throw new InvalidOperationException("OpenXR did not create Android feature settings.");

            openXr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            general.InitManagerOnStart = true;
            EditorUtility.SetDirty(openXr);
            EditorUtility.SetDirty(general.Manager);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(settings);
        }

        private static void SetFeature(BuildTargetGroup target, string id)
        {
            OpenXRFeature feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(target, id);
            if (feature == null)
                throw new InvalidOperationException("The pinned OpenXR package is missing feature " + id);

            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }

        private static void AssignDirectPlayer()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkPrefabPath);
            if (prefab == null)
                prefab = BuildDirectPlayer();

            Scene scene = SceneManager.GetSceneByPath(BootPath);
            bool closeWhenFinished = !scene.IsValid() || !scene.isLoaded;
            if (closeWhenFinished)
                scene = EditorSceneManager.OpenScene(BootPath, OpenSceneMode.Additive);
            try
            {
                CalibrationBoot boot = scene.GetRootGameObjects().Select(root => root.GetComponent<CalibrationBoot>())
                    .FirstOrDefault(candidate => candidate != null);
                if (boot == null)
                    throw new InvalidOperationException("The Boot scene has no CalibrationBoot root.");

                var component = new SerializedObject(boot);
                SerializedProperty field = component.FindProperty("networkPlayerPrefab");
                if (field.objectReferenceValue == prefab)
                    return;

                if (!closeWhenFinished && scene.isDirty)
                    throw new InvalidOperationException("Save your open Boot scene and rerun Configure Project.");

                field.objectReferenceValue = prefab;
                component.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException("Could not save the direct-player reference in Boot.");
            }
            finally
            {
                if (closeWhenFinished)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static GameObject BuildDirectPlayer()
        {
            EnsureAssetFolder("Assets/NucleusVR/Prefabs/Players");
            var root = new GameObject("Direct Player");
            try
            {
                root.AddComponent<NetworkObject>();
                var network = root.AddComponent<NetworkRig>();
                Transform origin = Child(root.transform, "Pose origin", Vector3.zero);
                Transform head = Child(origin, "Head target", new Vector3(0f, 1.63f, 0f));
                Transform left = Child(origin, "Left hand target", new Vector3(-0.4f, 0.95f, 0.4f));
                Transform right = Child(origin, "Right hand target", new Vector3(0.4f, 0.95f, 0.4f));
                Transform visuals = Child(origin, "Gorilla", Vector3.zero);
                var avatar = visuals.gameObject.AddComponent<GorillaAvatar>();
                GorillaAvatarBuilder.Build(avatar, head, left, right, firstPerson: false);
                network.Configure(origin, head, left, right, visuals.gameObject, avatar);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, NetworkPrefabPath);
                if (prefab == null)
                    throw new InvalidOperationException("Could not create the direct-player prefab.");
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Transform Child(Transform parent, string label, Vector3 position)
        {
            Transform child = new GameObject(label).transform;
            child.SetParent(parent, false);
            child.localPosition = position;
            return child;
        }

        private static void EnsureAssetFolder(string path)
        {
            string[] parts = path.Split('/');
            for (int index = 1; index < parts.Length; index++)
            {
                string parent = string.Join("/", parts, 0, index);
                string folder = parent + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.CreateFolder(parent, parts[index]);
            }
        }
    }

    internal sealed class QuestBuildValidation : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            const BuildTargetGroup group = BuildTargetGroup.Android;
            XRGeneralSettings general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
            if (general == null || general.Manager == null || !general.InitManagerOnStart ||
                !general.Manager.activeLoaders.Any(loader => loader is OpenXRLoader) ||
                !FeatureEnabled(group, NucleusProjectSetup.QuestFeature) ||
                !FeatureEnabled(group, NucleusProjectSetup.TouchProfile) ||
                !FeatureEnabled(group, NucleusProjectSetup.TouchPlusProfile) ||
                GraphicsSettings.defaultRenderPipeline is not UniversalRenderPipelineAsset)
                throw new BuildFailedException("Run Nucleus VR > Configure Project before building for Quest.");
        }

        private static bool FeatureEnabled(BuildTargetGroup target, string id)
        {
            OpenXRFeature candidate = FeatureHelpers.GetFeatureWithIdForBuildTarget(target, id);
            return candidate != null && candidate.enabled;
        }
    }
}
