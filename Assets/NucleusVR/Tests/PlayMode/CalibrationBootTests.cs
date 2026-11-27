using System.Collections;
using System.Linq;
using System.Reflection;
using NucleusVR.Anatomy;
using NucleusVR.Audio;
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
            Assert.That(avatar.GetComponentsInChildren<SkinnedMeshRenderer>().Length, Is.EqualTo(2));
            Assert.That(course.GetComponentInChildren<HandSurface>().Slip, Is.EqualTo(1f));
            Assert.That(boot.GetComponent<HandContactAudio>(), Is.Not.Null);
            Assert.That(session, Is.Not.Null);
            Assert.That(session.IsSessionActive, Is.False);
            var online = session.GetComponent<OnlineSession>();
            Assert.That(online, Is.Not.Null);
            Assert.That(online.Busy, Is.False);
            Assert.That(online.InRoom, Is.False);
            Assert.That(online.RoomCode, Is.Null.Or.Empty);

            AnatomyRig[] specimens = boot.GetComponentsInChildren<AnatomyRig>();
            Assert.That(specimens.Length, Is.EqualTo(2));
            foreach (AnatomyRig specimen in specimens)
            {
                Assert.That(specimen.HasAuthority, Is.True);
                Assert.That(specimen.TryGetBody(AnatomyRegion.Torso, out Rigidbody torso), Is.True);
                Assert.That(torso.isKinematic, Is.True);
                Assert.That(specimen.TryGetOrganBody(OrganKind.Heart, out Rigidbody heart), Is.True);
                Assert.That(heart.isKinematic, Is.True);
                Assert.That(heart.GetComponent<Collider>().enabled, Is.False);
            }

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

        [UnityTest]
        public IEnumerator BootCanSwitchBetweenGameplayWorlds()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
            scene = SceneManager.GetSceneByPath(ScenePath);
            GameObject boot = scene.GetRootGameObjects().Single(root => root.GetComponent<CalibrationBoot>() != null);
            Transform modes = boot.transform.Find("Game modes");
            Assert.That(modes, Is.Not.Null);
            MonoBehaviour flow = modes.GetComponents<MonoBehaviour>().Single();
            MethodInfo use = flow.GetType().GetMethod("Use", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(use, Is.Not.Null);

            use.Invoke(flow, new object[] { 0UL, (ushort)1 });
            yield return null;
            Assert.That(modes.Find("Nucleus research facility").gameObject.activeSelf, Is.True);
            Assert.That(boot.transform.Find("Calibration bay").gameObject.activeSelf, Is.False);

            use.Invoke(flow, new object[] { 0UL, (ushort)3 });
            yield return null;
            Transform workshop = modes.Find("Physics sandbox");
            Assert.That(workshop.gameObject.activeSelf, Is.True);
            Assert.That(modes.Find("Nucleus research facility").gameObject.activeSelf, Is.False);
            Assert.That(workshop.GetComponentsInChildren<AnatomyRig>().Length, Is.EqualTo(1));

            use.Invoke(flow, new object[] { 0UL, (ushort)302 });
            yield return null;
            Assert.That(workshop.GetComponentsInChildren<AnatomyRig>().Length, Is.EqualTo(2));

            use.Invoke(flow, new object[] { 0UL, (ushort)2 });
            yield return null;
            Assert.That(modes.Find("Monster briefing").gameObject.activeSelf, Is.True);
            Assert.That(workshop.gameObject.activeSelf, Is.False);

            use.Invoke(flow, new object[] { 0UL, (ushort)4 });
            yield return null;
            Assert.That(boot.transform.Find("Calibration bay").gameObject.activeSelf, Is.True);
            Assert.That(modes.Find("Monster briefing").gameObject.activeSelf, Is.False);
        }
    }
}
