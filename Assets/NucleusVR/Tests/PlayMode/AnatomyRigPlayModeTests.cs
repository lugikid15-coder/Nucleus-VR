using System.Collections;
using System.Collections.Generic;
using NucleusVR.Anatomy;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NucleusVR.Tests
{
    public sealed class AnatomyRigPlayModeTests
    {
        private static readonly Vector3 Origin = new(94f, 0f, 94f);
        private readonly List<GameObject> roots = new();

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            foreach (GameObject root in roots)
                Object.Destroy(root);
            roots.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator SeveringAnUpperArmReleasesOnlyItsBranchJoint()
        {
            AnatomyRig rig = CreateRig();
            Rigidbody torso = Part(rig, "Torso", new Vector3(0, 1.55f, 0), 0.17f);
            Rigidbody upper = Part(rig, "Upper", new Vector3(-0.45f, 1.55f, 0), 0.13f);
            Rigidbody forearm = Part(rig, "Forearm", new Vector3(-0.76f, 1.55f, 0), 0.12f);
            Assert.That(rig.BindRegion(AnatomyRegion.Torso, torso), Is.True);
            Assert.That(rig.BindRegion(AnatomyRegion.LeftUpperArm, upper, torso), Is.True);
            Assert.That(rig.BindRegion(AnatomyRegion.LeftForearm, forearm, upper), Is.True);
            Vector3 rest = upper.position;

            Assert.That(rig.SetStage(AnatomyRegion.LeftUpperArm, AnatomyStage.Severed), Is.True);
            Assert.That(rig.SetStage(AnatomyRegion.LeftForearm, AnatomyStage.Severed), Is.True);
            Assert.That(torso.isKinematic, Is.True);
            Assert.That(upper.isKinematic, Is.False);
            Assert.That(forearm.isKinematic, Is.False);
            Assert.That(upper.GetComponent<ConfigurableJoint>().xMotion, Is.EqualTo(ConfigurableJointMotion.Free));
            Assert.That(forearm.GetComponent<ConfigurableJoint>().connectedBody, Is.SameAs(upper));
            Assert.That(forearm.GetComponent<ConfigurableJoint>().xMotion, Is.EqualTo(ConfigurableJointMotion.Locked));
            Assert.That(rig.ApplyImpact(AnatomyRegion.LeftUpperArm, upper.position, Vector3.left * 1.5f), Is.True);
            yield return PhysicsSteps(6);
            Assert.That(upper.position.x, Is.LessThan(rest.x - 0.04f));
            Assert.That(Vector3.Distance(upper.position, forearm.position), Is.LessThan(0.43f));

            rig.ResetRig();
            Assert.That(upper.isKinematic, Is.True);
            Assert.That(forearm.isKinematic, Is.True);
            Assert.That(Vector3.Distance(upper.position, rest), Is.LessThan(0.002f));
            Assert.That(upper.GetComponent<ConfigurableJoint>().xMotion, Is.EqualTo(ConfigurableJointMotion.Locked));
        }

        [UnityTest]
        public IEnumerator AttachedOrgansFollowBodyAndDetachOnlyUnderAuthority()
        {
            AnatomyRig rig = CreateRig();
            Rigidbody torso = Part(rig, "Torso", new Vector3(0, 1.55f, 0), 0.12f);
            Rigidbody heart = Part(rig, "Heart", new Vector3(0.22f, 1.55f, 0), 0.06f);
            Assert.That(rig.BindRegion(AnatomyRegion.Torso, torso), Is.True);
            Assert.That(rig.BindOrgan(OrganKind.Heart, heart, torso), Is.True);
            Assert.That(rig.BindOrgan(OrganKind.Count, heart, torso), Is.False);
            Assert.That(heart.GetComponent<Collider>().isTrigger, Is.True);

            torso.position += Vector3.right * 0.18f;
            yield return PhysicsSteps(3);
            Assert.That(heart.position.x - torso.position.x, Is.EqualTo(0.22f).Within(0.012f));

            Assert.That(rig.DetachOrgan(OrganKind.Heart, Vector3.right * 1.4f, heart.position), Is.True);
            Assert.That(heart.isKinematic, Is.False);
            Assert.That(heart.GetComponent<Collider>().isTrigger, Is.False);
            yield return PhysicsSteps(4);
            Assert.That(heart.linearVelocity.x, Is.GreaterThan(0.6f));

            rig.SetAuthority(false);
            Assert.That(heart.isKinematic, Is.True);
            Assert.That(heart.GetComponent<Collider>().isTrigger, Is.True);
            Assert.That(rig.SetStage(AnatomyRegion.Torso, AnatomyStage.Fractured), Is.False);
            Assert.That(rig.ActivateRagdoll(), Is.False);
            Assert.That(rig.DetachOrgan(OrganKind.Heart, Vector3.right, heart.position), Is.False);
            Assert.That(rig.SetReplicaStage(AnatomyRegion.Torso, AnatomyStage.Fractured), Is.True);
            Assert.That(torso.isKinematic, Is.True);
            Vector3 remotePoint = Origin + new Vector3(0.35f, 1.3f, 0.2f);
            Assert.That(rig.SetReplicaOrganPose(OrganKind.Heart, remotePoint, Quaternion.identity), Is.True);
            yield return PhysicsSteps(2);
            Assert.That(Vector3.Distance(heart.position, remotePoint), Is.LessThan(0.002f));
        }

        [UnityTest]
        public IEnumerator ImpairedKinematicBranchReducesTrackingAndReplicasRejectLocalDrive()
        {
            AnatomyRig rig = CreateRig();
            Rigidbody torso = Part(rig, "Torso", new Vector3(0, 1.5f, 0), 0.15f);
            Rigidbody upper = Part(rig, "Upper", new Vector3(-0.44f, 1.5f, 0), 0.13f);
            Rigidbody forearm = Part(rig, "Forearm", new Vector3(-0.76f, 1.5f, 0), 0.11f);
            Assert.That(rig.BindRegion(AnatomyRegion.Torso, torso), Is.True);
            Assert.That(rig.BindRegion(AnatomyRegion.LeftUpperArm, upper, torso), Is.True);
            Assert.That(rig.BindRegion(AnatomyRegion.LeftForearm, forearm, upper), Is.True);
            Vector3 rest = forearm.position;
            Vector3 tracked = rest + Vector3.forward * 0.4f;

            Assert.That(rig.SetImpairment(AnatomyRegion.LeftUpperArm, 0.4f), Is.True);
            Assert.That(rig.GetGripStrength(true), Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(rig.TryDriveRegionPose(AnatomyRegion.LeftForearm, tracked, Quaternion.identity), Is.True);
            Assert.That(forearm.position.z - rest.z, Is.EqualTo(0.24f).Within(0.002f));

            rig.SetAuthority(false);
            Assert.That(rig.SetImpairment(AnatomyRegion.LeftUpperArm, 0f), Is.False);
            Assert.That(rig.SetReplicaImpairment(AnatomyRegion.LeftUpperArm, 0.75f), Is.True);
            Assert.That(rig.GetImpairment(AnatomyRegion.LeftUpperArm), Is.EqualTo(0.75f));
            Assert.That(rig.ShouldDrive(AnatomyRegion.LeftForearm), Is.False);
            Assert.That(rig.TryDriveRegionPose(AnatomyRegion.LeftForearm, tracked, Quaternion.identity), Is.False);
            Assert.That(rig.SetReplicaPose(AnatomyRegion.LeftForearm, tracked, Quaternion.identity), Is.True);
            Assert.That(Vector3.Distance(forearm.position, tracked), Is.LessThan(0.002f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TwoHandsDragOneBodyAndLastReleaseThrowsIt()
        {
            AnatomyRig rig = CreateRig();
            Rigidbody torso = Part(rig, "Torso", new Vector3(0, 1.55f, 0), 0.13f);
            Assert.That(rig.BindRegion(AnatomyRegion.Torso, torso), Is.True);
            Assert.That(rig.ActivateRagdoll(), Is.True);
            torso.useGravity = false;
            Rigidbody left = Wrist("Left", torso.position + Vector3.left * 0.16f);
            Rigidbody right = Wrist("Right", torso.position + Vector3.right * 0.16f);
            AnatomyGrip grip = NewRoot("Grip").AddComponent<AnatomyGrip>();
            AnatomyGrip other = NewRoot("Other grip").AddComponent<AnatomyGrip>();
            grip.Configure(left, right, true);
            other.Configure(left, right, true);
            Physics.SyncTransforms();

            Assert.That(grip.TryGrab(true, torso, torso.position + Vector3.left * 0.13f), Is.True);
            Assert.That(grip.TryGrab(false, torso, torso.position + Vector3.right * 0.13f), Is.True);
            Assert.That(other.TryGrab(true, torso, torso.position), Is.False);
            Assert.That(torso.GetComponents<ConfigurableJoint>().Length, Is.EqualTo(2));
            Assert.That(grip.Release(true), Is.True);
            Assert.That(grip.GetHeldBody(false), Is.SameAs(torso));

            float start = torso.position.x;
            for (int step = 0; step < 8; step++)
            {
                right.position += Vector3.right * 0.025f;
                yield return PhysicsSteps(1);
            }
            Assert.That(torso.position.x, Is.GreaterThan(start + 0.04f));
            Assert.That(grip.Release(false), Is.True);
            Assert.That(grip.GetHeldBody(false), Is.Null);
            Assert.That(torso.linearVelocity.x, Is.InRange(0.15f, 7.5f));

            Assert.That(grip.TryGrab(false, torso, torso.position + Vector3.right * 0.13f), Is.True);
            rig.ResetRig();
            Assert.That(grip.GetHeldBody(false), Is.Null);
            Assert.That(torso.isKinematic, Is.True);
        }

        private AnatomyRig CreateRig()
        {
            AnatomyRig rig = NewRoot("Specimen").AddComponent<AnatomyRig>();
            rig.transform.position = Origin;
            rig.Configure(true);
            return rig;
        }

        private static Rigidbody Part(AnatomyRig rig, string name, Vector3 local, float radius)
        {
            var child = new GameObject(name);
            child.transform.SetParent(rig.transform, false);
            child.transform.localPosition = local;
            child.AddComponent<SphereCollider>().radius = radius;
            return child.AddComponent<Rigidbody>();
        }

        private Rigidbody Wrist(string name, Vector3 position)
        {
            GameObject hand = NewRoot(name);
            hand.transform.position = position;
            Rigidbody wrist = hand.AddComponent<Rigidbody>();
            wrist.isKinematic = true;
            return wrist;
        }

        private GameObject NewRoot(string name)
        {
            var root = new GameObject(name);
            roots.Add(root);
            return root;
        }

        private static IEnumerator PhysicsSteps(int steps)
        {
            var wait = new WaitForFixedUpdate();
            for (int index = 0; index < steps; index++)
                yield return wait;
            yield return null;
        }
    }
}
