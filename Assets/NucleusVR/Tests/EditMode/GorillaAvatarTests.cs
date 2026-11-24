using System.Linq;
using NucleusVR.Player;
using NUnit.Framework;
using UnityEngine;

namespace NucleusVR.Tests
{
    public sealed class GorillaAvatarTests
    {
        private GameObject root;
        private GorillaAvatarFactory.BuildResult model;
        private Transform head;
        private Transform left;
        private Transform right;
        private GorillaAvatar avatar;

        [SetUp]
        public void BuildModel()
        {
            root = new GameObject("Avatar Test Root");
            avatar = root.AddComponent<GorillaAvatar>();
            head = Target("Head Target", new Vector3(0f, 1.61f, 0.04f), 30f);
            left = Target("Left Contact", new Vector3(-0.7f, 1.21f, 0.49f), -17f);
            right = Target("Right Contact", new Vector3(0.89f, 1.34f, 1.02f), 50f);
            model = GorillaAvatarFactory.Build(avatar, head, left, right, firstPerson: true);
        }

        [TearDown]
        public void ReleaseModel()
        {
            Mesh bodyMesh = model.Body != null ? model.Body.sharedMesh : null;
            Mesh armMesh = model.Arms != null ? model.Arms.sharedMesh : null;
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(bodyMesh);
            Object.DestroyImmediate(armMesh);
            Object.DestroyImmediate(model.Material);
            Object.DestroyImmediate(model.Palette);
        }

        [Test]
        public void FirstPersonHidesFaceAndTorsoWhileKeepingTheArmsVisible()
        {
            Assert.That(model.Body.enabled, Is.False);
            Assert.That(model.Arms.enabled, Is.True);

            avatar.SetFirstPerson(false);
            Assert.That(model.Body.enabled, Is.True);
            Assert.That(model.Arms.enabled, Is.True);
            Assert.That(model.Arms.sharedMaterial, Is.SameAs(model.Body.sharedMaterial));
        }

        [Test]
        public void HeadAndWristsReachTheTrackedTargets()
        {
            MatchTarget(Bone("Head"), head);
            MatchTarget(Bone("Left Wrist"), left);
            MatchTarget(Bone("Right Wrist"), right);
            Assert.That(Bone("Right Forearm").localScale.z, Is.GreaterThan(1f));
        }

        [Test]
        public void MeshHasTwoFingersAndAMirroredThumbPerHandWithOutwardTriangles()
        {
            foreach (string side in new[] { "Left", "Right" })
            {
                Transform wrist = Bone($"{side} Wrist");
                Assert.That(wrist.childCount, Is.EqualTo(3));
                for (int index = 1; index <= 2; index++)
                {
                    Transform finger = Bone($"{side} Finger {index} Base");
                    Assert.That(finger.parent, Is.SameAs(wrist));
                    Assert.That(Bone($"{side} Finger {index} Joint").parent, Is.SameAs(finger));
                }

                Transform thumb = Bone($"{side} Thumb Base");
                Transform thumbTip = Bone($"{side} Thumb Joint");
                Assert.That(thumb.parent, Is.SameAs(wrist));
                Assert.That(thumbTip.parent, Is.SameAs(thumb));
                float inward = side == "Left" ? 1f : -1f;
                Assert.That(inward * thumb.localPosition.x, Is.GreaterThan(0.065f));
                Assert.That(inward * (thumb.localRotation * Vector3.forward).x, Is.GreaterThan(0.7f));
                Assert.That(model.Arms.bones, Does.Contain(thumb));
                Assert.That(model.Arms.bones, Does.Contain(thumbTip));
            }
            Assert.That(Bone("Jaw").parent, Is.SameAs(Bone("Head")));
            ValidateMesh(model.Arms);
            ValidateMesh(model.Body);
        }

        private static void ValidateMesh(SkinnedMeshRenderer renderer)
        {
            Mesh mesh = renderer.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.triangles;
            Assert.That(mesh.bindposes.Length, Is.EqualTo(renderer.bones.Length));
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            Assert.That(mesh.boneWeights.Length, Is.EqualTo(mesh.vertexCount));

            for (int index = 0; index < triangles.Length; index += 3)
            {
                int a = triangles[index];
                int b = triangles[index + 1];
                int c = triangles[index + 2];
                Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                Assert.That(Vector3.Dot(face, normals[a] + normals[b] + normals[c]), Is.Positive,
                    $"Triangle {index / 3} faces inward or has zero area.");
            }
        }

        private Transform Target(string name, Vector3 position, float yaw)
        {
            var target = new GameObject(name);
            target.transform.SetParent(root.transform, false);
            target.transform.SetPositionAndRotation(position, Quaternion.Euler(8f, yaw, 2f));
            return target.transform;
        }

        private Transform Bone(string name)
        {
            return root.GetComponentsInChildren<Transform>().Single(bone => bone.name == name);
        }

        private static void MatchTarget(Transform bone, Transform target)
        {
            Assert.That(Vector3.Distance(bone.position, target.position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(bone.rotation, target.rotation), Is.LessThan(0.05f));
        }
    }
}
