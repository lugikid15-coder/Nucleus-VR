using System.Collections;
using NucleusVR.Player;
using NucleusVR.VR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NucleusVR.Tests
{
    public sealed class GorillaAvatarPoseTests
    {
        private GameObject root;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LocalAndReplicatedGripsCurlTheFingersAndOpposeTheThumb()
        {
            root = new GameObject("Avatar Pose Test");
            Transform head = Child("Head Target");
            Transform left = Child("Left Target");
            Transform right = Child("Right Target");
            var tracking = root.AddComponent<TrackedRig>();
            tracking.Configure(head, left, right);
            tracking.SetSyntheticTracking(
                new Pose(new Vector3(0f, 1.64f, 0f), Quaternion.identity),
                new Pose(new Vector3(-0.35f, 1.11f, 0.44f), Quaternion.identity),
                new Pose(new Vector3(0.35f, 1.11f, 0.44f), Quaternion.identity),
                leftGrip: 1f);

            var avatar = root.AddComponent<GorillaAvatar>();
            GorillaAvatarFactory.Build(avatar, head, left, right, firstPerson: true);
            var input = root.AddComponent<LocalGorillaAvatar>();
            input.Configure(avatar, tracking);
            yield return new WaitForSeconds(0.2f);

            Transform leftFinger = FindBone("Left Finger 2 Base");
            Transform rightFinger = FindBone("Right Finger 2 Base");
            Transform leftThumb = FindBone("Left Thumb Base");
            Transform rightThumb = FindBone("Right Thumb Base");
            Transform leftThumbJoint = FindBone("Left Thumb Joint");
            Transform rightThumbJoint = FindBone("Right Thumb Joint");
            Assert.That(Quaternion.Angle(leftFinger.localRotation, rightFinger.localRotation), Is.GreaterThan(45f));
            Assert.That(Vector3.Distance(FindBone("Left Wrist").position, left.position), Is.LessThan(0.001f));
            AssertThumbGrip(leftThumb, leftThumbJoint, 1f, closed: true);
            AssertThumbGrip(rightThumb, rightThumbJoint, -1f, closed: false);

            input.enabled = false;
            avatar.SetFirstPerson(false);
            avatar.SetExpression(0f, 1f, 1f);
            yield return new WaitForSeconds(0.2f);

            Transform jaw = FindBone("Jaw");
            Assert.That(Quaternion.Angle(Quaternion.identity, jaw.localRotation), Is.GreaterThan(20f));
            Assert.That(Quaternion.Angle(Quaternion.identity, rightFinger.localRotation), Is.GreaterThan(45f));
            Assert.That(Quaternion.Angle(Quaternion.identity, leftFinger.localRotation), Is.LessThan(15f));
            AssertThumbGrip(leftThumb, leftThumbJoint, 1f, closed: false);
            AssertThumbGrip(rightThumb, rightThumbJoint, -1f, closed: true);
        }

        private static void AssertThumbGrip(Transform thumb, Transform joint, float inward, bool closed)
        {
            float splay = inward * (thumb.localRotation * Vector3.forward).x;
            float flex = Quaternion.Angle(Quaternion.identity, joint.localRotation);
            if (closed)
            {
                Assert.That(splay, Is.LessThan(-0.1f));
                Assert.That(flex, Is.GreaterThan(35f));
            }
            else
            {
                Assert.That(splay, Is.GreaterThan(0.6f));
                Assert.That(flex, Is.LessThan(15f));
            }
        }

        private Transform Child(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            return child.transform;
        }

        private Transform FindBone(string name)
        {
            foreach (Transform bone in root.GetComponentsInChildren<Transform>())
                if (bone.name == name)
                    return bone;

            Assert.Fail($"Missing avatar bone {name}.");
            return null;
        }
    }
}
