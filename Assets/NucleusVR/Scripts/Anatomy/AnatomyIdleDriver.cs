using UnityEngine;

namespace NucleusVR.Anatomy
{
    /// <summary>Gives the containment specimen an intact tracked pose that injuries can impair.</summary>
    internal sealed class AnatomyIdleDriver : MonoBehaviour
    {
        private readonly Pose[] rest = new Pose[(int)AnatomyRegion.Count];
        private readonly Vector3[] recoil = new Vector3[(int)AnatomyRegion.Count];
        private AnatomyRig rig;
        private float idleMotion;

        public void Configure(AnatomyRig target, float idle)
        {
            rig = target;
            idleMotion = idle;
            for (int i = 0; i < rest.Length; i++)
            {
                rig.TryGetBody((AnatomyRegion)i, out Rigidbody body);
                rest[i] = new Pose(body.transform.localPosition, body.transform.localRotation);
            }
            rig.ConfigureTrackedDriver(this);
        }

        public void React(AnatomyRegion region, Vector3 worldDirection, float strength)
        {
            if (!rig.ShouldDrive(region))
                return;
            int index = (int)region;
            Vector3 bump = transform.InverseTransformDirection(worldDirection.normalized) * (strength * 0.047f);
            recoil[index] = Vector3.ClampMagnitude(recoil[index] + bump, 0.06f);
        }

        private void OnDisable() => System.Array.Clear(recoil, 0, recoil.Length);

        private void FixedUpdate()
        {
            if (rig == null)
                return;

            float phase = Time.fixedTime * 1.6f;
            float breath = Mathf.Sin(phase) * (0.022f * idleMotion);
            float lean = Mathf.Sin(phase * 0.48f) * (0.021f * idleMotion);
            for (int i = 0; i < rest.Length; i++)
            {
                var region = (AnatomyRegion)i;
                if (!rig.ShouldDrive(region) || (idleMotion == 0f && recoil[i] == Vector3.zero))
                    continue;

                Pose initial = rest[i];
                float wrist = region is AnatomyRegion.LeftForearm or AnatomyRegion.RightForearm ? 1f : 0f;
                float accent = Mathf.Sin(phase + i) * wrist * idleMotion;
                Vector3 position = initial.position + recoil[i] +
                    new Vector3(lean + accent * 0.019f, breath, accent * 0.033f);
                Quaternion rotation = initial.rotation * Quaternion.Euler(accent * 8f, 0f, accent * 4f);
                recoil[i] = Vector3.MoveTowards(recoil[i], Vector3.zero, Time.fixedDeltaTime * 0.39f);
                if (idleMotion == 0f && recoil[i] == Vector3.zero)
                    position = initial.position;
                rig.TryDriveRegionPose(region, transform.TransformPoint(position), transform.rotation * rotation);
            }
        }
    }
}
