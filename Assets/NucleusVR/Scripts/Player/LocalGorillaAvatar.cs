using NucleusVR.VR;
using UnityEngine;

namespace NucleusVR.Player
{
    [DisallowMultipleComponent]
    public sealed class LocalGorillaAvatar : MonoBehaviour
    {
        [SerializeField] private GorillaAvatar avatar;
        [SerializeField] private TrackedRig trackedRig;

        public void Configure(GorillaAvatar target, TrackedRig source)
        {
            avatar = target;
            trackedRig = source;
        }

        private void LateUpdate()
        {
            if (avatar == null || trackedRig == null)
                return;

            avatar.SetTrackingAvailability(trackedRig.LeftHandTracked, trackedRig.RightHandTracked);
            avatar.SetExpression(trackedRig.LeftGrip, trackedRig.RightGrip, 0f);
        }
    }
}
