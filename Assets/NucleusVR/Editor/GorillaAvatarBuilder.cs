using System;
using NucleusVR.Player;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NucleusVR.Editor
{
    public static class GorillaAvatarBuilder
    {
        private const string Models = "Assets/NucleusVR/Models/Avatar";
        private const string Materials = "Assets/NucleusVR/Materials/Avatar";

        public static void Build(GorillaAvatar avatar, Transform trackedHead, Transform leftHand,
            Transform rightHand, bool firstPerson, Shader litShader = null)
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Use GorillaAvatarFactory to build an avatar in Play mode.");

            EnsureFolder(Models);
            EnsureFolder(Materials);
            GorillaAvatarFactory.BuildResult model =
                GorillaAvatarFactory.Build(avatar, trackedHead, leftHand, rightHand, firstPerson, litShader);

            Texture2D palette = Persist(model.Palette, $"{Materials}/ContainmentSubjectPalette.asset");
            model.Material.SetTexture("_BaseMap", palette);
            Material material = Persist(model.Material, $"{Materials}/ContainmentSubject.mat");
            model.Body.sharedMaterial = material;
            model.Arms.sharedMaterial = material;
            model.Body.sharedMesh = Persist(model.Body.sharedMesh, $"{Models}/ContainmentSubjectBody.asset");
            model.Arms.sharedMesh = Persist(model.Arms.sharedMesh, $"{Models}/ContainmentSubjectArms.asset");
        }

        private static T Persist<T>(T generated, string path) where T : Object
        {
            Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }

            if (asset is not T existing)
                throw new InvalidOperationException($"The avatar asset path {path} is already in use.");

            Object.DestroyImmediate(generated);
            return existing;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            int separator = path.LastIndexOf('/');
            string parent = path[..separator];
            EnsureFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, path[(separator + 1)..])))
                throw new InvalidOperationException($"Could not create avatar asset folder {path}.");
        }
    }
}
