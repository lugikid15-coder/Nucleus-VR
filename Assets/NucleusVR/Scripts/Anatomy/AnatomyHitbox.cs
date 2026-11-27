using NucleusVR.Core;
using UnityEngine;

namespace NucleusVR.Anatomy
{
    internal sealed class AnatomyHitbox : MonoBehaviour
    {
        public AnatomySpecimen Subject { get; private set; }
        public AnatomyRegion Region { get; private set; }
        public OrganKind? Organ { get; private set; }
        public Rigidbody Body { get; private set; }
        private float nextMark;

        public void Bind(AnatomySpecimen subject, AnatomyRegion region, Rigidbody body, OrganKind? organ = null)
        {
            Subject = subject;
            Region = region;
            Body = body;
            Organ = organ;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (Subject == null || collision.collider.gameObject.layer != CalibrationLayers.Course ||
                Time.time < nextMark || collision.relativeVelocity.sqrMagnitude < 0.4f ||
                collision.contactCount == 0)
                return;

            nextMark = Time.time + 0.3f;
            ContactPoint touch = collision.GetContact(0);
            Vector3 normal = Vector3.Dot(touch.normal, Body.worldCenterOfMass - touch.point) >= 0f
                ? touch.normal : -touch.normal;
            Subject.MarkSurface(Region, Organ, touch.point, normal);
        }
    }
}
