using System;
using UnityEngine;

namespace NucleusVR.Player
{
    /// <summary>Authors the original containment subject's skeletal rig and low-poly skin.</summary>
    public static class GorillaAvatarFactory
    {
        private enum Pigment { Charcoal, Ash, Soot, Slate, Ochre, Copper, Ivory, Gum }

        private static readonly Color32[] Colors =
        {
            new(44, 56, 58, 255),
            new(76, 91, 89, 255),
            new(19, 25, 26, 255),
            new(108, 119, 107, 255),
            new(223, 142, 49, 255),
            new(170, 86, 49, 255),
            new(236, 217, 170, 255),
            new(89, 35, 34, 255)
        };

        public readonly struct BuildResult
        {
            public SkinnedMeshRenderer Body { get; }
            public SkinnedMeshRenderer Arms { get; }
            public Material Material { get; }
            public Texture2D Palette { get; }

            internal BuildResult(SkinnedMeshRenderer body, SkinnedMeshRenderer arms, Material material,
                Texture2D palette)
            {
                Body = body;
                Arms = arms;
                Material = material;
                Palette = palette;
            }
        }

        public static BuildResult Build(GorillaAvatar avatar, Transform trackedHead, Transform leftHand,
            Transform rightHand, bool firstPerson, Shader litShader = null)
        {
            if (avatar == null)
                throw new ArgumentNullException(nameof(avatar));
            if (avatar.HasModel)
                throw new InvalidOperationException("This avatar already has a model.");

            if (litShader == null)
                litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
                throw new InvalidOperationException("Assign the Universal Render Pipeline Lit shader.");

            var palette = new Texture2D(Colors.Length, 1, TextureFormat.RGBA32, false, false)
            {
                name = "Containment Subject Palette",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            palette.SetPixels32(Colors);
            palette.Apply(false);

            var material = new Material(litShader) { name = "Containment Subject Skin" };
            material.SetTexture("_BaseMap", palette);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.18f);

            Transform model = Child(avatar.transform, "Containment Subject Rig", Vector3.zero);
            Transform torso = Child(model, "Torso", Vector3.zero);
            Transform head = Child(torso, "Head", new Vector3(0f, 0.72f, 0f));
            Transform jaw = Child(head, "Jaw", new Vector3(0f, -0.057f, 0.037f));
            SkinnedMeshRenderer body = Renderer(model, "Head and Torso");
            SkinnedMeshRenderer arms = Renderer(model, "Arms and Hands");

            var bodySkin = new AvatarSkinBuilder(body.transform, Colors.Length);
            BuildTorso(bodySkin, torso);
            BuildFace(bodySkin, head, jaw);
            bodySkin.Bake(body, model, material, "Subject Head and Torso");

            var armSkin = new AvatarSkinBuilder(arms.transform, Colors.Length);
            GorillaAvatar.Arm leftArm = BuildArm(armSkin, torso, "Left", -1f);
            GorillaAvatar.Arm rightArm = BuildArm(armSkin, torso, "Right", 1f);
            armSkin.Bake(arms, model, material, "Subject Arms and Hands");

            avatar.Initialize(model, head, jaw, leftArm, rightArm, body);
            avatar.SetTrackingTargets(trackedHead, leftHand, rightHand);
            avatar.SetFirstPerson(firstPerson);
            avatar.RefreshPose(0f);

            if (Application.isPlaying)
                avatar.OwnAssets(body.sharedMesh, arms.sharedMesh, material, palette);

            return new BuildResult(body, arms, material, palette);
        }

        private static void BuildTorso(AvatarSkinBuilder skin, Transform torso)
        {
            Lobe(skin, torso, 0f, 0.235f, -0.036f, 0.27f, 0.28f, 0.19f, Pigment.Charcoal, 12, 8);
            Lobe(skin, torso, 0f, 0.38f, -0.054f, 0.33f, 0.20f, 0.20f, Pigment.Charcoal, 12, 8);
            Lobe(skin, torso, 0f, 0.125f, 0.055f, 0.18f, 0.19f, 0.135f, Pigment.Soot);
            Lobe(skin, torso, 0f, 0.55f, -0.015f, 0.126f, 0.16f, 0.117f, Pigment.Charcoal);
            Lobe(skin, torso, 0f, 0.52f, -0.016f, 0.137f, 0.046f, 0.123f, Pigment.Soot);

            for (int sign = -1; sign <= 1; sign += 2)
            {
                skin.AddLobe(torso, new Vector3(sign * 0.117f, 0.375f, 0.074f),
                    new Vector3(0.142f, 0.136f, 0.118f), Quaternion.Euler(0f, 0f, sign * 10f),
                    (int)Pigment.Ash);
                Lobe(skin, torso, sign * 0.093f, 0.438f, -0.19f, 0.049f, 0.115f, 0.061f, Pigment.Slate);
            }

            // The throat-mounted telemetry tag marks the research subject.
            Lobe(skin, torso, 0f, 0.51f, 0.111f, 0.064f, 0.07f, 0.031f, Pigment.Copper, 8, 4);
            Lobe(skin, torso, 0f, 0.525f, 0.141f, 0.023f, 0.016f, 0.008f, Pigment.Ochre, 8, 4);
        }

        private static void BuildFace(AvatarSkinBuilder skin, Transform head, Transform jaw)
        {
            Lobe(skin, head, 0f, 0.013f, -0.043f, 0.169f, 0.177f, 0.149f, Pigment.Charcoal, 14, 8);
            Lobe(skin, head, 0f, 0.021f, 0.061f, 0.132f, 0.109f, 0.085f, Pigment.Soot, 12, 7);
            Lobe(skin, head, 0f, -0.056f, 0.104f, 0.106f, 0.081f, 0.080f, Pigment.Gum, 12, 6);
            Lobe(skin, head, 0f, -0.040f, 0.130f, 0.111f, 0.045f, 0.066f, Pigment.Ash, 12, 6);
            Lobe(skin, head, 0f, -0.015f, 0.170f, 0.067f, 0.033f, 0.037f, Pigment.Charcoal);
            Lobe(skin, jaw, 0f, -0.029f, 0.065f, 0.112f, 0.053f, 0.096f, Pigment.Ash, 12, 6);
            Lobe(skin, jaw, 0f, -0.007f, 0.116f, 0.081f, 0.013f, 0.046f, Pigment.Gum);
            Lobe(skin, jaw, 0f, -0.004f, 0.145f, 0.046f, 0.009f, 0.013f, Pigment.Copper, 8, 4);

            for (int sign = -1; sign <= 1; sign += 2)
            {
                Lobe(skin, head, sign * 0.153f, 0.009f, -0.033f, 0.049f, 0.063f, 0.038f,
                    Pigment.Charcoal, 8, 5);
                Lobe(skin, head, sign * 0.170f, 0.009f, -0.008f, 0.025f, 0.042f, 0.021f,
                    Pigment.Soot, 8, 5);
                skin.AddLobe(head, new Vector3(sign * 0.068f, 0.070f, 0.102f),
                    new Vector3(0.066f, 0.040f, 0.057f), Quaternion.Euler(0f, 0f, sign * 18f),
                    (int)Pigment.Charcoal, 10, 5);
                Lobe(skin, head, sign * 0.060f, 0.041f, 0.123f, 0.038f, 0.026f, 0.019f,
                    Pigment.Soot, 8, 5);
                Lobe(skin, head, sign * 0.058f, 0.034f, 0.136f, 0.021f, 0.017f, 0.012f,
                    Pigment.Ochre, 8, 5);
                Lobe(skin, head, sign * 0.057f, 0.034f, 0.145f, 0.007f, 0.013f, 0.004f,
                    Pigment.Soot, 8, 4);
                Lobe(skin, head, sign * 0.026f, -0.022f, 0.199f, 0.018f, 0.010f, 0.005f,
                    Pigment.Soot, 8, 4);
                Lobe(skin, head, sign * 0.066f, -0.078f, 0.156f, 0.013f, 0.025f, 0.013f,
                    Pigment.Ivory, 6, 4);
                Lobe(skin, jaw, sign * 0.051f, 0.004f, 0.129f, 0.011f, 0.019f, 0.010f,
                    Pigment.Ivory, 6, 4);
            }
        }

        private static GorillaAvatar.Arm BuildArm(AvatarSkinBuilder skin, Transform torso, string side,
            float sign)
        {
            Transform shoulder = Child(torso, $"{side} Shoulder", new Vector3(sign * 0.285f, 0.42f, -0.039f));
            Transform upper = Child(shoulder, $"{side} Upper Arm", Vector3.zero);
            Transform elbow = Child(shoulder, $"{side} Elbow", new Vector3(0f, 0f, 0.34f));
            Transform forearm = Child(elbow, $"{side} Forearm", Vector3.zero);
            Transform wrist = Child(elbow, $"{side} Wrist", new Vector3(0f, 0f, 0.32f));

            Lobe(skin, upper, 0f, 0f, 0.08f, 0.116f, 0.123f, 0.159f, Pigment.Charcoal);
            Lobe(skin, upper, 0f, 0f, 0.22f, 0.094f, 0.096f, 0.147f, Pigment.Ash);
            Lobe(skin, elbow, 0f, 0f, 0f, 0.071f, 0.078f, 0.081f, Pigment.Charcoal);
            Lobe(skin, forearm, 0f, 0f, 0.137f, 0.088f, 0.085f, 0.179f, Pigment.Charcoal);
            Lobe(skin, forearm, 0f, 0.034f, 0.148f, 0.060f, 0.054f, 0.127f, Pigment.Ash);
            Lobe(skin, wrist, 0f, 0f, -0.035f, 0.083f, 0.078f, 0.061f, Pigment.Soot, 10, 5);
            Lobe(skin, wrist, 0f, 0.009f, -0.017f, 0.085f, 0.076f, 0.031f, Pigment.Ochre, 10, 5);
            Lobe(skin, wrist, 0f, 0f, 0.044f, 0.080f, 0.043f, 0.088f, Pigment.Ash, 12, 6);
            Lobe(skin, wrist, 0f, -0.027f, 0.047f, 0.067f, 0.022f, 0.069f, Pigment.Slate);

            var knuckles = new Transform[3];
            var joints = new Transform[3];
            for (int index = 0; index < knuckles.Length; index++)
            {
                float length = index == 1 ? 0.068f : 0.057f;
                knuckles[index] = Child(wrist, $"{side} Finger {index + 1} Base",
                    new Vector3((index - 1) * 0.052f, -0.001f, 0.093f));
                joints[index] = Child(knuckles[index], $"{side} Finger {index + 1} Joint",
                    new Vector3(0f, 0f, length));

                Lobe(skin, knuckles[index], 0f, 0f, length * 0.38f, 0.026f, 0.029f, length * 0.70f,
                    Pigment.Charcoal, 8, 4);
                Lobe(skin, joints[index], 0f, 0f, 0.015f, 0.022f, 0.021f, 0.037f,
                    Pigment.Slate, 8, 4);
                Lobe(skin, joints[index], 0f, 0.013f, 0.033f, 0.012f, 0.011f, 0.013f,
                    Pigment.Soot, 6, 4);
            }

            return new GorillaAvatar.Arm
            {
                Shoulder = shoulder,
                UpperSkin = upper,
                Elbow = elbow,
                ForearmSkin = forearm,
                Wrist = wrist,
                Knuckles = knuckles,
                MiddleJoints = joints
            };
        }

        private static void Lobe(AvatarSkinBuilder skin, Transform bone, float x, float y,
            float z, float width, float height, float depth, Pigment color, int sides = 10, int rings = 6)
        {
            skin.AddLobe(bone, new Vector3(x, y, z), new Vector3(width, height, depth), (int)color,
                sides, rings);
        }

        private static Transform Child(Transform parent, string name, Vector3 offset)
        {
            var child = new GameObject(name);
            child.layer = parent.gameObject.layer;
            child.transform.SetParent(parent, false);
            child.transform.localPosition = offset;
            return child.transform;
        }

        private static SkinnedMeshRenderer Renderer(Transform parent, string name)
        {
            return Child(parent, name, Vector3.zero).gameObject.AddComponent<SkinnedMeshRenderer>();
        }
    }
}
