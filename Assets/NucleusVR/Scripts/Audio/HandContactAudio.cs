using NucleusVR.Movement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NucleusVR.Audio
{
    [DisallowMultipleComponent]
    public sealed class HandContactAudio : MonoBehaviour
    {
        private const int VoiceCount = 4;
        private const int SampleRate = 24000;
        private const float PolishedSlip = 0.55f;

        [SerializeField] private HandLocomotion movement;
        [SerializeField, Range(0f, 1f)] private float gain = 0.72f;

        private HandLocomotion subscribedMovement;
        private GameObject voiceRoot;
        private AudioSource[] voices;
        private int nextVoice;
        private AudioClip gripTap;
        private AudioClip polishedTap;

        public void Configure(HandLocomotion source)
        {
            movement = source;
            if (isActiveAndEnabled)
                Subscribe();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            DestroyOwned(voiceRoot);
            DestroyOwned(gripTap);
            DestroyOwned(polishedTap);
        }

        private void Subscribe()
        {
            if (ReferenceEquals(movement, subscribedMovement))
                return;

            Unsubscribe();
            if (movement == null)
                return;

            if (voices == null)
                CreateVoices();

            subscribedMovement = movement;
            subscribedMovement.SurfaceTapped += PlayTap;
        }

        private void Unsubscribe()
        {
            if (!ReferenceEquals(subscribedMovement, null))
                subscribedMovement.SurfaceTapped -= PlayTap;
            subscribedMovement = null;

            if (voices != null)
                foreach (AudioSource voice in voices)
                    if (voice != null)
                        voice.Stop();
        }

        private void CreateVoices()
        {
            gripTap = SynthesizeTap(polished: false);
            polishedTap = SynthesizeTap(polished: true);

            // A voice stays at the impact point as the player moves away from it.
            voiceRoot = new GameObject("Hand Contact Voices");
            Scene ownerScene = gameObject.scene;
            if (ownerScene.IsValid() && ownerScene.isLoaded && voiceRoot.scene != ownerScene)
                SceneManager.MoveGameObjectToScene(voiceRoot, ownerScene);

            voices = new AudioSource[VoiceCount];
            for (int index = 0; index < VoiceCount; index++)
            {
                var point = new GameObject($"Contact Voice {index + 1}");
                point.transform.SetParent(voiceRoot.transform, false);
                var voice = point.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 1f;
                voice.dopplerLevel = 0f;
                voice.rolloffMode = AudioRolloffMode.Logarithmic;
                voice.minDistance = 0.45f;
                voice.maxDistance = 8f;
                voices[index] = voice;
            }
        }

        private void PlayTap(Collider surfaceCollider, Vector3 worldPoint, float strength)
        {
            if (surfaceCollider == null || !(strength > 0f) || gain <= 0f)
                return;

            AudioSource voice = GetIdleVoice();
            if (voice == null)
                return;

            HandSurface surface = surfaceCollider.GetComponentInParent<HandSurface>();
            AudioClip custom = surface != null ? surface.TapClip : null;
            bool polished = surface != null && surface.Slip >= PolishedSlip;
            AudioClip clip = custom != null ? custom : polished ? polishedTap : gripTap;
            if (clip == null)
                return;

            float amount = Mathf.Clamp01(strength);
            voice.transform.position = worldPoint;
            voice.pitch = custom == null ? Mathf.Lerp(0.94f, 1.06f, amount) : 1f;
            voice.volume = gain * Mathf.Lerp(0.28f, 0.82f, Mathf.Sqrt(amount));
            voice.PlayOneShot(clip);
        }

        private AudioSource GetIdleVoice()
        {
            for (int offset = 0; offset < VoiceCount; offset++)
            {
                int index = (nextVoice + offset) % VoiceCount;
                AudioSource voice = voices[index];
                if (voice == null || voice.isPlaying)
                    continue;

                nextVoice = (index + 1) % VoiceCount;
                return voice;
            }

            return null;
        }

        private static AudioClip SynthesizeTap(bool polished)
        {
            float duration = polished ? 0.096f : 0.071f;
            var samples = new float[Mathf.RoundToInt(duration * SampleRate)];
            float filteredNoise = 0f;
            float peak = 0f;
            uint noise = polished ? 3810923u : 3614219u;

            for (int index = 0; index < samples.Length; index++)
            {
                float t = (float)index / SampleRate;
                noise ^= noise << 13;
                noise ^= noise >> 17;
                noise ^= noise << 5;
                float white = (noise & 65535u) / 32767.5f - 1f;
                filteredNoise += (white - filteredNoise) * 0.23f;

                float attack = Mathf.Min(1f, t * 1800f);
                float release = Mathf.Clamp01((duration - t) * 108f);
                float amplitude = attack * release * release;
                float scrape = polished
                    ? (white - filteredNoise) * 0.29f * Mathf.Exp(-t * 320f)
                    : (filteredNoise + white * 0.36f) * 0.90f * Mathf.Exp(-t * 119f);
                float tone = polished
                    ? Oscillation(t, 826f, 55f, 0.45f) + Oscillation(t, 1837f, 93f, 0.25f)
                    : Oscillation(t, 241f, 91f, 0.44f) + Oscillation(t, 670f, 170f, 0.32f);
                samples[index] = (tone + scrape) * amplitude;
                peak = Mathf.Max(peak, Mathf.Abs(samples[index]));
            }

            float normalization = 0.73f / peak;
            for (int index = 0; index < samples.Length; index++)
                samples[index] *= normalization;

            var clip = AudioClip.Create(polished ? "Nucleus Polished Hand Tap" : "Nucleus Grip Hand Tap",
                samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static float Oscillation(float time, float frequency, float decay, float amplitude)
        {
            return amplitude * Mathf.Sin(2f * Mathf.PI * frequency * time) * Mathf.Exp(-time * decay);
        }

        private static void DestroyOwned(Object instance)
        {
            if (instance == null)
                return;

            if (Application.isPlaying)
                Destroy(instance);
            else
                DestroyImmediate(instance);
        }
    }
}
