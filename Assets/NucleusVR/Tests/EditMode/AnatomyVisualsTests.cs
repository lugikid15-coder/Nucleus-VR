using System;
using System.Linq;
using NucleusVR.Anatomy;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NucleusVR.Tests
{
    public sealed class AnatomyVisualsTests
    {
        private GameObject root;
        private Material template;
        private AnatomyVisuals visuals;

        [SetUp]
        public void CreateSceneObjects()
        {
            root = new GameObject("Anatomy Visual Test");
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            template = new Material(shader);
            visuals = new AnatomyVisuals(template);
        }

        [TearDown]
        public void ReleaseSceneObjects()
        {
            Object.DestroyImmediate(root);
            visuals?.Dispose();
            Object.DestroyImmediate(template);
        }

        [Test]
        public void SeveranceLevelsRespectRendererBudgetsAndRestoreSkin()
        {
            AnatomyVisuals.RegionView region = visuals.BuildRegion(root.transform, new Vector3(0.23f, 0.43f, 0.24f),
                AnatomyRegion.LeftForearm);
            Assert.That(Drawn().Count(), Is.EqualTo(1));

            foreach (var quality in new[] { GoreQuality.Low, GoreQuality.Medium, GoreQuality.High })
            {
                region.ShowState(AnatomyStage.Severed, quality);
                int budget = quality == GoreQuality.Low ? 3 : quality == GoreQuality.Medium ? 6 : 9;
                MeshRenderer[] visible = Drawn().ToArray();
                Assert.That(visible, Has.Length.EqualTo(budget));
                Assert.That(visible.Any(renderer => renderer.name == "Cut Surface"), Is.True);
            }

            region.ShowState(AnatomyStage.Severed, GoreQuality.Off);
            Assert.That(Drawn().Single().GetComponent<MeshFilter>().sharedMesh.name, Is.EqualTo("Intact skin"));
            region.ShowState(AnatomyStage.Intact, GoreQuality.High);
            Assert.That(Drawn().Single().GetComponent<MeshFilter>().sharedMesh.name, Is.EqualTo("Intact skin"));
        }

        [Test]
        public void EveryOrganHasAnInteractionBoundsAndBothDamageMeshes()
        {
            foreach (OrganKind kind in Enum.GetValues(typeof(OrganKind)))
            {
                if (kind == OrganKind.Count)
                    continue;

                var supported = new GameObject(kind.ToString());
                supported.transform.SetParent(root.transform, false);
                AnatomyVisuals.OrganView organ = visuals.BuildOrgan(supported.transform, kind);
                Assert.That(organ.LocalBounds.size.sqrMagnitude, Is.GreaterThan(0.01f));

                MeshFilter shape = supported.GetComponentInChildren<MeshFilter>();
                organ.ShowState(false, GoreQuality.High);
                Mesh intact = shape.sharedMesh;
                organ.ShowState(true, GoreQuality.High);
                Assert.That(shape.sharedMesh, Is.Not.SameAs(intact));
                Assert.That(shape.sharedMesh.name, Does.StartWith("Damaged"));
                organ.ShowState(true, GoreQuality.Low);
                Assert.That(shape.GetComponent<MeshRenderer>().enabled, Is.True);
                Assert.That(shape.sharedMesh, Is.SameAs(intact));
                organ.ShowState(false, GoreQuality.High, false);
                Assert.That(shape.GetComponent<MeshRenderer>().enabled, Is.False);
            }
        }

        private System.Collections.Generic.IEnumerable<MeshRenderer> Drawn()
        {
            return root.GetComponentsInChildren<MeshRenderer>().Where(renderer => renderer.enabled);
        }
    }
}
