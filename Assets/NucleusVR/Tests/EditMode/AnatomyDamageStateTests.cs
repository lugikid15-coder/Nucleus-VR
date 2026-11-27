using System;
using NucleusVR.Anatomy;
using NUnit.Framework;

namespace NucleusVR.Tests
{
    public sealed class AnatomyDamageStateTests
    {
        [Test]
        public void CumulativeDamageAdvancesStagesAndRevealsLayers()
        {
            var state = new AnatomyDamageState();
            const AnatomyRegion target = AnatomyRegion.RightForearm;

            Assert.That(state.GetRegion(target).VisibleLayers, Is.EqualTo(AnatomyLayers.Skin));
            Assert.That(state.ApplyDamage(target, 19f), Is.True);
            Assert.That(state.GetRegion(target).Stage, Is.EqualTo(AnatomyStage.Intact));

            Assert.That(state.ApplyDamage(target, 1f), Is.True);
            AnatomyRegionState wound = state.GetRegion(target);
            Assert.That(wound.Stage, Is.EqualTo(AnatomyStage.Wounded));
            Assert.That(wound.VisibleLayers, Is.EqualTo(AnatomyLayers.Skin | AnatomyLayers.Muscles));

            Assert.That(state.ApplyDamage(target, 25f), Is.True);
            AnatomyRegionState exposed = state.GetRegion(target);
            Assert.That(exposed.Stage, Is.EqualTo(AnatomyStage.Exposed));
            Assert.That(exposed.VisibleLayers & (AnatomyLayers.Veins | AnatomyLayers.Nerves | AnatomyLayers.InternalTissue),
                Is.EqualTo(AnatomyLayers.Veins | AnatomyLayers.Nerves | AnatomyLayers.InternalTissue));
            Assert.That(exposed.VisibleLayers & AnatomyLayers.Organs, Is.EqualTo(AnatomyLayers.None));

            Assert.That(state.ApplyDamage(target, 25f), Is.True);
            AnatomyRegionState fractured = state.GetRegion(target);
            Assert.That(fractured.Stage, Is.EqualTo(AnatomyStage.Fractured));
            Assert.That(fractured.VisibleLayers & AnatomyLayers.Bones, Is.EqualTo(AnatomyLayers.Bones));
            Assert.That(fractured.Impairment, Is.GreaterThan(exposed.Impairment));
            Assert.That(state.ApplyDamage(target, 30f), Is.True);
            Assert.That(state.GetRegion(target).Stage, Is.EqualTo(AnatomyStage.Severed));
            Assert.That(state.GetRegion(AnatomyRegion.RightUpperArm).Stage, Is.EqualTo(AnatomyStage.Intact));
        }

        [Test]
        public void RejectsInvalidDamageWithoutUpdatingEitherRegionOrOrgan()
        {
            var state = new AnatomyDamageState();

            Assert.That(state.ApplyDamage(AnatomyRegion.Count, 100f), Is.False);
            Assert.That(state.ApplyDamage(AnatomyRegion.Head, float.NaN), Is.False);
            Assert.That(state.ApplyDamage(AnatomyRegion.Head, float.PositiveInfinity), Is.False);
            Assert.That(state.ApplyFracture(AnatomyRegion.Head, -5f), Is.False);
            Assert.That(state.ApplyJointDamage(AnatomyRegion.Head, 0f), Is.False);
            Assert.That(state.ApplyDamage(AnatomyRegion.Torso, 100f, OrganKind.Brain), Is.False);
            Assert.That(state.ApplyDamage(AnatomyRegion.Torso, 100f, OrganKind.Count), Is.False);
            Assert.That(state.Revision, Is.Zero);
            Assert.That(state.GetRegion(AnatomyRegion.Head).Damage, Is.Zero);
            Assert.That(state.GetRegion(AnatomyRegion.Torso).Damage, Is.Zero);
            Assert.That(state.GetOrgan(OrganKind.Brain).Damage, Is.Zero);
        }

        [Test]
        public void JointSeveranceCascadesOnlyAlongTheAffectedArm()
        {
            var state = new AnatomyDamageState();

            Assert.That(state.ApplyFracture(AnatomyRegion.LeftUpperArm, 100f), Is.True);
            AnatomyRegionState fracture = state.GetRegion(AnatomyRegion.LeftUpperArm);
            Assert.That(fracture.Stage, Is.EqualTo(AnatomyStage.Fractured));
            Assert.That(fracture.JointDamage, Is.Zero);
            Assert.That(fracture.Damage, Is.Zero);

            Assert.That(state.ApplyJointDamage(AnatomyRegion.LeftUpperArm, 100f), Is.True);
            Assert.That(state.Revision, Is.EqualTo(2u));
            Assert.That(state.GetRegion(AnatomyRegion.LeftUpperArm).Stage, Is.EqualTo(AnatomyStage.Severed));
            AnatomyRegionState forearm = state.GetRegion(AnatomyRegion.LeftForearm);
            Assert.That(forearm.Stage, Is.EqualTo(AnatomyStage.Severed));
            Assert.That(forearm.Impairment, Is.EqualTo(1f));
            Assert.That(forearm.Damage, Is.Zero);
            Assert.That(state.GetRegion(AnatomyRegion.RightForearm).Stage, Is.EqualTo(AnatomyStage.Intact));
            Assert.That(state.ApplyDamage(AnatomyRegion.LeftForearm, 30f), Is.False);
            Assert.That(state.Revision, Is.EqualTo(2u));
        }

        [Test]
        public void ExposedOrganCanDetachOnceAndCanBeDamagedAtASeveredRegion()
        {
            var state = new AnatomyDamageState();

            Assert.That(state.TryDetachOrgan(OrganKind.Brain), Is.False);
            Assert.That(state.ApplyDamage(AnatomyRegion.Head, 100f), Is.True);
            Assert.That(state.GetOrgan(OrganKind.Brain).IsExposed, Is.True);
            Assert.That(state.ApplyDamage(AnatomyRegion.Head, 48f, OrganKind.Brain), Is.True);
            Assert.That(state.GetOrgan(OrganKind.Brain).Stage, Is.EqualTo(AnatomyOrganStage.Critical));
            Assert.That(state.TryDetachOrgan(OrganKind.Brain), Is.True);
            Assert.That(state.GetOrgan(OrganKind.Brain).IsDetached, Is.True);

            Assert.That(state.TryDetachOrgan(OrganKind.Brain), Is.False);
            Assert.That(state.ApplyDamage(AnatomyRegion.Head, 10f, OrganKind.Brain), Is.False);
            Assert.That(state.GetOrgan(OrganKind.Heart).IsExposed, Is.False);
            Assert.That(state.Revision, Is.EqualTo(3u));
        }

        [Test]
        public void MonsterProfileScalesTheDamageChannelsAndProtectsTheCentralBody()
        {
            var state = new AnatomyDamageState(AnatomyProfile.CreateScaled(2f, 1.5f, 2f));

            Assert.That(state.ApplyDamage(AnatomyRegion.Torso, 45f, OrganKind.Heart), Is.True);
            Assert.That(state.GetRegion(AnatomyRegion.Torso).Damage, Is.EqualTo(0.225f).Within(0.0001f));
            Assert.That(state.GetOrgan(OrganKind.Heart).Damage, Is.EqualTo(0.1875f).Within(0.0001f));
            Assert.That(state.ApplyFracture(AnatomyRegion.LeftForearm, 210f), Is.True);
            Assert.That(state.GetRegion(AnatomyRegion.LeftForearm).Stage, Is.EqualTo(AnatomyStage.Fractured));
            Assert.That(state.ApplyJointDamage(AnatomyRegion.Torso, float.MaxValue), Is.True);
            Assert.That(state.GetRegion(AnatomyRegion.Torso).Stage, Is.EqualTo(AnatomyStage.Fractured));
        }

        [Test]
        public void ProfileOverridesCanRelinkAnOrganWithoutChangingDefaultDefinitions()
        {
            AnatomyProfile profile = AnatomyProfile.Default
                .WithRegion(AnatomyRegion.Head, new AnatomyRegionDefinition(200f, canSever: false))
                .WithOrgan(OrganKind.Brain, new AnatomyOrganDefinition(AnatomyRegion.Torso, 80f));
            var state = new AnatomyDamageState(profile);

            Assert.That(state.ApplyDamage(AnatomyRegion.Head, 100f, OrganKind.Brain), Is.False);
            Assert.That(state.ApplyDamage(AnatomyRegion.Head, 100f), Is.True);
            Assert.That(state.GetRegion(AnatomyRegion.Head).Damage, Is.EqualTo(0.5f));
            Assert.That(state.GetRegion(AnatomyRegion.Head).VisibleLayers & AnatomyLayers.Organs,
                Is.EqualTo(AnatomyLayers.None));
            Assert.That(state.ApplyDamage(AnatomyRegion.Torso, 45f, OrganKind.Brain), Is.True);
            Assert.That(state.GetOrgan(OrganKind.Brain).IsExposed, Is.True);
            Assert.That(AnatomyProfile.Default.GetOrgan(OrganKind.Brain).Region, Is.EqualTo(AnatomyRegion.Head));
        }

        [Test]
        public void ResetClearsDamageWhileCapturedSnapshotsRemainUnchanged()
        {
            var state = new AnatomyDamageState();
            Assert.That(state.ApplyDamage(AnatomyRegion.Torso, 100f, OrganKind.Heart), Is.True);
            Assert.That(state.TryDetachOrgan(OrganKind.Heart), Is.True);
            AnatomySnapshot injured = state.CaptureSnapshot();

            state.Reset();
            AnatomySnapshot recovered = state.CaptureSnapshot();

            Assert.That(injured.Revision, Is.EqualTo(2u));
            Assert.That(injured.GetRegion(AnatomyRegion.Torso).Stage, Is.EqualTo(AnatomyStage.Fractured));
            Assert.That(injured.GetOrgan(OrganKind.Heart).IsDetached, Is.True);
            Assert.That(recovered.Revision, Is.EqualTo(3u));
            Assert.That(recovered.GetRegion(AnatomyRegion.Torso).Stage, Is.EqualTo(AnatomyStage.Intact));
            Assert.That(recovered.GetOrgan(OrganKind.Heart).Stage, Is.EqualTo(AnatomyOrganStage.Intact));
            Assert.That(recovered.GetOrgan(OrganKind.Heart).IsExposed, Is.False);
        }

        [Test]
        public void SnapshotCopiesBuffersThatMayBeReusedByAReceiver()
        {
            var regions = new AnatomyRegionState[(int)AnatomyRegion.Count];
            var organs = new AnatomyOrganState[(int)OrganKind.Count];
            regions[(int)AnatomyRegion.Head] = new AnatomyRegionState(0.5f, 0f, 0f, 0.3f,
                AnatomyStage.Exposed, AnatomyLayers.Skin | AnatomyLayers.Organs);
            organs[(int)OrganKind.Brain] = new AnatomyOrganState(AnatomyRegion.Head, 0.8f,
                AnatomyOrganStage.Critical, true);
            var snapshot = new AnatomySnapshot(7u, regions, organs);

            Array.Clear(regions, 0, regions.Length);
            Array.Clear(organs, 0, organs.Length);

            Assert.That(snapshot.Revision, Is.EqualTo(7u));
            Assert.That(snapshot.GetRegion(AnatomyRegion.Head).Stage, Is.EqualTo(AnatomyStage.Exposed));
            Assert.That(snapshot.GetOrgan(OrganKind.Brain).Stage, Is.EqualTo(AnatomyOrganStage.Critical));
        }
    }
}
