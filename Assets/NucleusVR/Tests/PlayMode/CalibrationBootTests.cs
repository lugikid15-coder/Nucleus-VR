using System.Collections;
using System.Linq;
using NucleusVR.Core;
using NucleusVR.Maps;
using NucleusVR.Movement;
using NucleusVR.Multiplayer;
using NucleusVR.Player;
using NucleusVR.VR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace NucleusVR.Tests
{
    public sealed class CalibrationBootTests
    {
        private const string ScenePath = "Assets/NucleusVR/Scenes/Boot/Boot.unity";
        private Scene scene;

        [UnityTest]
        public IEnumerator BootBuildsAConnectedOfflineCalibrationBay()
        {
            var load = SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return new WaitForFixedUpdate();

            scene = SceneManager.GetSceneByPath(ScenePath);
            Assert.That(scene.isLoaded, Is.True);
            var boot = scene.GetRootGameObjects().Single(root => root.GetComponent<CalibrationBoot>() != null);
            var course = boot.GetComponentInChildren<CalibrationCourse>();
            var rig = boot.GetComponentInChildren<TrackedRig>();
            var motor = boot.GetComponentInChildren<HandLocomotion>();
            var avatar = boot.GetComponentInChildren<GorillaAvatar>();
            var session = Object.FindAnyObjectByType<DirectSession>();

            Assert.That(course.Stations.Count, Is.EqualTo(CalibrationCourse.CheckpointCount + 1));
            Assert.That(course.Stations[0].sharedMaterial.name, Is.EqualTo("Station armed"));
            Assert.That(course.Stations[1].sharedMaterial.name, Is.EqualTo("Station idle"));
            Assert.That(rig.Head.GetComponent<Camera>(), Is.Not.Null);
            Assert.That(rig.GetComponent<CapsuleCollider>().direction, Is.EqualTo(1));
            Assert.That(motor.LeftContact.GetComponent<Rigidbody>().isKinematic, Is.True);
            Assert.That(motor.RightContact.GetComponent<SphereCollider>().isTrigger, Is.False);
            Assert.That(avatar.HasModel, Is.True);
            Assert.That(session, Is.Not.Null);
            Assert.That(session.IsSessionActive, Is.False);

            int courseLayer = LayerMask.NameToLayer("NucleusCourse");
            Assert.That(courseLayer, Is.GreaterThanOrEqualTo(0));
            bool onFloor = Physics.Raycast(CalibrationCourse.Spawn + Vector3.up * 0.2f, Vector3.down,
                out RaycastHit hit, 0.5f, 1 << courseLayer);
            Assert.That(onFloor, Is.True);
            Assert.That(hit.collider.gameObject.name, Is.EqualTo("Foundation"));
        }

        [UnityTearDown]
        public IEnumerator UnloadBoot()
        {
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);

            // The network manager is a separate root and is destroyed by Bootstrap cleanup.
            yield return null;
        }
    }
}
