using UnityEngine;

namespace NucleusVR.Movement
{
    /// <summary>Optional hand contact settings for a collider. Unmarked surfaces use full grip.</summary>
    [DisallowMultipleComponent]
    public sealed class HandSurface : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)]
        [Tooltip("How freely a hand slides along this surface while pressing against it.")]
        private float slip;

        [SerializeField, Tooltip("Optional spatial tap sound for a new hand contact.")]
        private AudioClip tapClip;

        public float Slip => Mathf.Clamp01(slip);
        public AudioClip TapClip => tapClip;

        public void Configure(float sliding, AudioClip contactClip = null)
        {
            slip = float.IsFinite(sliding) ? Mathf.Clamp01(sliding) : 0f;
            tapClip = contactClip;
        }
    }
}
