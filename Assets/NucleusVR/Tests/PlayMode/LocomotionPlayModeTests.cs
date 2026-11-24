using System.Collections;
using System.Collections.Generic;
using NucleusVR.Movement;
using NucleusVR.VR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NucleusVR.Tests
{
    public sealed class LocomotionPlayModeTests
    {
        private const int QueryLayer = 30;
        private static readonly Vector3 Origin = new(80f, 0f, 80f);
        private readonly List<GameObject> objects = new();

        private TrackedRig rig;
        private HandLocomotion movement;
        private Rigidbody body;
        private CapsuleCollider capsule;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject item in objects)
                Object.Destroy(item);
            objects.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator BodySettlesAndCrouches()
        {
            MakeBox("Floor", new Vector3(0, -0.125f, 0), new Vector3(10, 0.25f, 10));
            CreateRig(0.36f);
            Sample(1.5f);
            yield return PhysicsSteps(40);

            Assert.That(body.position.y, Is.EqualTo(0f).Within(0.035f));
            Assert.That(body.linearVelocity.y, Is.EqualTo(0f).Within(0.1f));
            Assert.That(capsule.height, Is.EqualTo(1.63f).Within(0.02f));
            Sample(0.88f);
            yield return PhysicsSteps(3);

            Assert.That(capsule.height, Is.EqualTo(1.01f).Within(0.02f));
            Assert.That(capsule.center.y, Is.EqualTo(capsule.height * 0.5f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator RoomScaleSweepCannotCrossThinWall()
        {
            MakeBox("Floor", new Vector3(0, -0.125f, 0), new Vector3(10, 0.25f, 10));
            MakeBox("Wall", new Vector3(0.6f, 1f, 0f), new Vector3(0.08f, 2f, 4f));
            CreateRig(0.025f);
            Sample(1.35f, 0.13f, 45f);
            yield return PhysicsSteps(4);

            Assert.That(capsule.center.x, Is.EqualTo(0.13f).Within(0.01f));
            Assert.That(Quaternion.Angle(body.rotation, Quaternion.identity), Is.LessThan(0.1f));
            Sample(1.35f, 1.2f, 45f);
            yield return PhysicsSteps(4);

            Assert.That(capsule.center.x, Is.InRange(0.23f, 0.36f));
            Assert.That(rig.Head.localPosition.x, Is.EqualTo(capsule.center.x).Within(0.02f));
            Assert.That(movement.HeadObstructionMetres, Is.GreaterThan(0.7f));
            Assert.That(Quaternion.Angle(body.rotation, Quaternion.identity), Is.LessThan(0.1f));
        }

        [UnityTest]
        public IEnumerator StandingUnderOverhangStopsAtTheCeiling()
        {
            MakeBox("Floor", new Vector3(0, -0.125f, 0), new Vector3(10, 0.25f, 10));
            MakeBox("Overhang", new Vector3(0, 1.42f, 0), new Vector3(2, 0.25f, 2));
            CreateRig(0.025f);
            Sample(0.91f);
            yield return PhysicsSteps(6);
            Sample(1.67f);
            yield return PhysicsSteps(6);

            Assert.That(capsule.height, Is.InRange(1.13f, 1.31f));
            Assert.That(rig.Head.localPosition.y, Is.LessThan(1.19f));
            Assert.That(movement.HeadObstructionMetres, Is.GreaterThan(0.45f));
            Assert.That(body.linearVelocity.y, Is.EqualTo(0f).Within(0.1f));
        }

        [UnityTest]
        public IEnumerator AcquiringTrackingBehindASurfaceDoesNotLaunchThePlayer()
        {
            MakeBox("Floor", new Vector3(0, -0.125f, 0), new Vector3(10, 0.25f, 10));
            CreateRig(0.02f);
            int taps = 0;
            movement.SurfaceTapped += (_, _, _) => taps++;
            Sample(1.07f, left: new Vector3(-0.23f, -0.19f, 0.30f));
            yield return PhysicsSteps(12);

            Assert.That(movement.LeftHandSurface, Is.Null);
            Assert.That(body.position.y, Is.LessThan(0.04f));
            Assert.That(body.linearVelocity.y, Is.EqualTo(0f).Within(0.1f));
            Assert.That(taps, Is.Zero);
        }

        [UnityTest]
        public IEnumerator AnchoredHandPushesOffFloorAndReleasesWithBoundedSpeed()
        {
            Collider floor = MakeBox("Floor", new Vector3(0, -0.125f, 0), new Vector3(10, 0.25f, 10));
            CreateRig(0.02f);
            Sample(1.07f, left: new Vector3(-0.23f, 0.30f, 0.30f));
            yield return PhysicsSteps(6);

            float start = body.position.y;
            Sample(1.07f, left: new Vector3(-0.23f, -0.18f, 0.30f));
            yield return PhysicsSteps(2);

            Assert.That(movement.LeftHandSurface, Is.SameAs(floor));
            Assert.That(body.position.y, Is.GreaterThan(start + 0.025f));
            Assert.That(body.linearVelocity.magnitude, Is.LessThan(5.25f));

            Sample(1.07f, left: new Vector3(-0.23f, 0.56f, 0.30f));
            yield return PhysicsSteps(2);

            Assert.That(movement.LeftHandSurface, Is.Null);
            Assert.That(body.linearVelocity.y, Is.InRange(0.02f, 5.25f));
        }

        [UnityTest]
        public IEnumerator AnchoringOnWallLetsPlayerPullUpward()
        {
            MakeBox("Floor", new Vector3(0, -0.125f, 0), new Vector3(10, 0.25f, 10));
            Collider wall = MakeBox("Wall", new Vector3(0, 1.5f, 0.8f), new Vector3(4, 3, 0.08f));
            CreateRig(0.02f);
            Sample(1.26f, left: new Vector3(-0.25f, 0.89f, 0.34f));
            yield return PhysicsSteps(6);
            Sample(1.26f, left: new Vector3(-0.25f, 0.89f, 0.98f));
            yield return PhysicsSteps(6);

            Assert.That(movement.LeftHandSurface, Is.SameAs(wall));
            float height = body.position.y;
            Sample(1.26f, left: new Vector3(-0.25f, 0.61f, 0.98f));
            yield return PhysicsSteps(4);

            Assert.That(body.position.y, Is.GreaterThan(height + 0.05f));
            Assert.That(body.linearVelocity.magnitude, Is.LessThan(5.25f));
        }

        [UnityTest]
        public IEnumerator SlickWallLetsHandSlideWhileStillResistingNormalPush()
        {
            MakeBox("Floor", new Vector3(0, -0.125f, 0), new Vector3(10, 0.25f, 10));
            Collider wall = MakeBox("Wall", new Vector3(0, 1.5f, 0.8f), new Vector3(4, 3, 0.08f));
            CreateRig(0.025f);
            float rootTravel = 0f;
            float handTravel = 0f;
            yield return MeasureDrag();
            float grippyRoot = rootTravel;
            float grippyHand = handTravel;

            wall.gameObject.AddComponent<HandSurface>().Configure(1f);
            movement.ResetTo(Origin + Vector3.up * 0.025f, Quaternion.identity);
            yield return MeasureDrag();

            Assert.That(grippyRoot, Is.GreaterThan(0.12f));
            Assert.That(grippyHand, Is.LessThan(0.02f));
            Assert.That(rootTravel, Is.LessThan(0.065f));
            Assert.That(handTravel, Is.GreaterThan(0.11f));

            IEnumerator MeasureDrag()
            {
                Sample(1.26f, left: new Vector3(-0.25f, 0.89f, 0.34f));
                yield return PhysicsSteps(6);
                Sample(1.26f, left: new Vector3(-0.25f, 0.89f, 0.98f));
                yield return PhysicsSteps(9);
                Assert.That(movement.LeftHandSurface, Is.SameAs(wall));
                Assert.That(body.position.z, Is.LessThan(Origin.z - 0.17f));

                float bodyX = body.position.x;
                float handX = movement.LeftContact.GetComponent<Rigidbody>().position.x;
                Sample(1.26f, left: new Vector3(-0.08f, 0.89f, 0.98f));
                yield return PhysicsSteps(8);
                rootTravel = Mathf.Abs(body.position.x - bodyX);
                handTravel = Mathf.Abs(movement.LeftContact.GetComponent<Rigidbody>().position.x - handX);
            }
        }

        [UnityTest]
        public IEnumerator FullySlickWallLeavesGravityOnAndReleasesAtAnEdge()
        {
            Collider wall = MakeBox("Slick panel", new Vector3(-0.25f, 1.7f, 0.8f),
                new Vector3(0.34f, 3.3f, 0.08f));
            wall.gameObject.AddComponent<HandSurface>().Configure(1f);
            CreateRig(1.25f);
            Sample(1.26f, left: new Vector3(-0.25f, 0.89f, 0.34f));
            yield return PhysicsSteps(3);
            Sample(1.26f, left: new Vector3(-0.25f, 0.89f, 0.98f));
            yield return PhysicsSteps(5);

            Assert.That(movement.LeftHandSurface, Is.SameAs(wall));
            Assert.That(body.useGravity, Is.True);
            Assert.That(body.linearVelocity.y, Is.LessThan(-0.5f));

            for (int index = 1; index <= 5; index++)
            {
                Sample(1.26f, left: new Vector3(-0.25f + 0.08f * index, 0.89f, 0.98f));
                yield return PhysicsSteps(1);
            }

            Assert.That(movement.LeftHandSurface, Is.Null);
            Assert.That(body.linearVelocity.y, Is.LessThan(-0.5f));
            Assert.That(Mathf.Abs(body.linearVelocity.x), Is.LessThan(2.5f));
        }

        [UnityTest]
        public IEnumerator ContactTapReportsNewImpactOnly()
        {
            MakeBox("Floor", new Vector3(0, -0.125f, 0), new Vector3(10, 0.25f, 10));
            Collider wall = MakeBox("Wall", new Vector3(0, 1.5f, 0.8f), new Vector3(4, 3, 0.08f));
            CreateRig(0.025f);
            var tapped = new List<(Collider collider, Vector3 point, float strength)>();
            movement.SurfaceTapped += (collider, point, strength) => tapped.Add((collider, point, strength));
            Sample(1.26f, left: new Vector3(-0.25f, 0.89f, 0.34f));
            yield return PhysicsSteps(5);
            Assert.That(tapped, Is.Empty);

            Sample(1.26f, left: new Vector3(-0.25f, 0.89f, 0.98f));
            yield return PhysicsSteps(18);
            Assert.That(tapped, Has.Count.EqualTo(1));
            Assert.That(tapped[0].collider, Is.SameAs(wall));
            Assert.That(tapped[0].point.z, Is.EqualTo(Origin.z + 0.76f).Within(0.02f));
            Assert.That(tapped[0].strength, Is.InRange(0.01f, 1f));

            Sample(1.26f, left: new Vector3(-0.25f, 0.89f, 0.30f));
            yield return PhysicsSteps(5);
            Sample(1.26f, left: new Vector3(-0.25f, 0.89f, 1.05f));
            yield return PhysicsSteps(8);
            Assert.That(tapped, Has.Count.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator SnapTurnPivotsAtHeadAndRequiresInputRelease()
        {
            MakeBox("Floor", new Vector3(0, -0.125f, 0), new Vector3(10, 0.25f, 10));
            CreateRig(0.025f);
            Sample(1.35f, 0.2f);
            yield return PhysicsSteps(4);
            Vector3 headBefore = rig.Head.position;
            Sample(1.35f, 0.2f, turn: 1f);
            yield return PhysicsSteps(6);

            Assert.That(Quaternion.Angle(Quaternion.identity, body.rotation), Is.EqualTo(30f).Within(0.1f));
            Vector3 headDelta = Vector3.ProjectOnPlane(rig.Head.position - headBefore, Vector3.up);
            Assert.That(headDelta.magnitude, Is.LessThan(0.015f));
            Sample(1.35f, 0.2f);
            yield return PhysicsSteps(2);
            Sample(1.35f, 0.2f, turn: 1f);
            yield return PhysicsSteps(2);

            Assert.That(Quaternion.Angle(Quaternion.identity, body.rotation), Is.EqualTo(60f).Within(0.1f));
        }

        [UnityTest]
        public IEnumerator PhysicalHandsMoveDynamicPropsWithoutUsingThemAsAnchors()
        {
            MakeBox("Floor", new Vector3(0, -0.125f, 0), new Vector3(10, 0.25f, 10));
            Collider prop = MakeBox("Free prop", new Vector3(-0.23f, 0.92f, 0.37f), Vector3.one * 0.15f);
            var propBody = prop.gameObject.AddComponent<Rigidbody>();
            propBody.useGravity = false;
            propBody.mass = 0.5f;
            propBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            CreateRig(0.025f);
            Sample(1.34f, left: new Vector3(-0.23f, 0.92f, 0.10f));
            yield return PhysicsSteps(6);

            float previousZ = propBody.position.z;
            Sample(1.34f, left: new Vector3(-0.23f, 0.92f, 0.70f));
            yield return PhysicsSteps(6);

            Assert.That(propBody.position.z, Is.GreaterThan(previousZ + 0.04f));
            Assert.That(movement.LeftHandSurface, Is.Null);
        }

        [UnityTest]
        public IEnumerator TrackingLossDisablesBothPhysicalHands()
        {
            CreateRig(0.1f);
            Sample(1.35f);
            yield return PhysicsSteps(3);
            Assert.That(movement.LeftContact.GetComponent<SphereCollider>().enabled, Is.True);
            Assert.That(movement.RightContact.GetComponent<SphereCollider>().enabled, Is.True);

            rig.ClearSyntheticTracking();
            yield return PhysicsSteps(2);

            Assert.That(movement.LeftContact.GetComponent<SphereCollider>().enabled, Is.False);
            Assert.That(movement.RightContact.GetComponent<SphereCollider>().enabled, Is.False);
        }

        private void CreateRig(float height)
        {
            GameObject root = Create("Player physics root");
            root.transform.position = Origin + Vector3.up * height;
            body = root.AddComponent<Rigidbody>();
            capsule = root.AddComponent<CapsuleCollider>();
            capsule.radius = 0.24f;
            capsule.height = 1.54f;
            capsule.center = Vector3.up * 0.77f;
            rig = root.AddComponent<TrackedRig>();
            rig.Configure(Child(root, "Head"), Child(root, "Left device"), Child(root, "Right device"));
            movement = root.AddComponent<HandLocomotion>();
            movement.Configure(rig, capsule, MakePhysicalHand("Left contact"), MakePhysicalHand("Right contact"),
                1 << QueryLayer);
            Physics.SyncTransforms();
        }

        private Transform MakePhysicalHand(string name)
        {
            GameObject hand = Create(name);
            hand.transform.position = Origin;
            hand.AddComponent<SphereCollider>().radius = 0.085f;
            Rigidbody handBody = hand.AddComponent<Rigidbody>();
            handBody.isKinematic = true;
            handBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            return hand.transform;
        }

        private Transform Child(GameObject root, string name)
        {
            Transform result = Create(name).transform;
            result.SetParent(root.transform, false);
            return result;
        }

        private Collider MakeBox(string name, Vector3 position, Vector3 size)
        {
            GameObject box = Create(name);
            box.layer = QueryLayer;
            box.transform.position = Origin + position;
            var collider = box.AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }

        private GameObject Create(string name)
        {
            var item = new GameObject(name);
            objects.Add(item);
            return item;
        }

        private void Sample(float height, float x = 0f, float pitch = 0f, float turn = 0f, Vector3? left = null)
        {
            rig.SetSyntheticTracking(new Pose(new Vector3(x, height, 0), Quaternion.Euler(pitch, 0, 0)),
                new Pose(left ?? new Vector3(x - 0.23f, height - 0.4f, 0.2f), Quaternion.identity),
                new Pose(new Vector3(x + 0.23f, height - 0.4f, 0.2f), Quaternion.identity),
                new Vector2(turn, 0f));
        }

        private static IEnumerator PhysicsSteps(int steps)
        {
            var delay = new WaitForFixedUpdate();
            for (int index = 0; index < steps; index++)
                yield return delay;
            yield return null;
        }
    }
}
