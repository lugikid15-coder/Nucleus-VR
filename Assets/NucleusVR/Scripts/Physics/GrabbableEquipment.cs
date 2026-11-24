using UnityEngine;

namespace NucleusVR.Props
{
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody), typeof(Collider))]
    internal sealed class GrabbableEquipment : MonoBehaviour
    {
        public Rigidbody Body { get; private set; }
        public Collider Shape { get; private set; }
        public bool IsHeld { get; private set; }

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Shape = GetComponent<Collider>();
        }

        public bool TryClaim()
        {
            if (!isActiveAndEnabled || IsHeld)
                return false;

            IsHeld = true;
            return true;
        }

        public void Release() => IsHeld = false;
    }
}
