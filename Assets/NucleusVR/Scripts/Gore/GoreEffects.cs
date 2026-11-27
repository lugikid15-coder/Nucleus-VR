using NucleusVR.Anatomy;
using UnityEngine;
using UnityEngine.Rendering;

namespace NucleusVR.Gore
{
    /// <summary>One particle system and bounded reusable surface marks, drips and audio voices per scene.</summary>
    internal sealed class GoreEffects : MonoBehaviour
    {
        private const int MaximumMarks = 24;
        private const int MaximumDrips = 8;
        private const int MaximumParticles = 144;
        private const int MaximumChips = 6;

        private struct SurfaceMark
        {
            public Transform Visual;
            public float Start;
            public float End;
            public float Size;
            public float Delay;
        }

        private struct Wound
        {
            public Transform Anchor;
            public Vector3 LocalPoint;
            public float Next;
            public float End;
        }

        private struct BoneChip
        {
            public Transform Visual;
            public Vector3 Velocity;
            public float End;
        }

        private readonly SurfaceMark[] marks = new SurfaceMark[MaximumMarks];
        private readonly Wound[] wounds = new Wound[MaximumDrips];
        private readonly BoneChip[] chips = new BoneChip[MaximumChips];
        private readonly System.Random random = new(3417);
        private ParticleSystem drops;
        private Material fluid, bone;
        private Mesh markMesh, chipMesh;
        private AudioSource[] impactVoices;
        private AudioClip[] impactClips;
        private int nextVoice, nextMark, nextWound, nextChip;
        private LayerMask receivingSurfaces;
        private GoreQuality quality = GoreQuality.Medium;

        public GoreQuality Quality
        {
            get => quality;
            set
            {
                if (quality == value)
                    return;
                quality = value;
                Clear();
            }
        }

        public void Clear()
        {
            if (drops != null)
                drops.Clear();
            for (int i = 0; i < marks.Length; i++)
            {
                if (marks[i].Visual != null)
                    marks[i].Visual.gameObject.SetActive(false);
                marks[i].End = 0f;
            }
            System.Array.Clear(wounds, 0, wounds.Length);
            for (int i = 0; i < chips.Length; i++)
            {
                if (chips[i].Visual != null)
                    chips[i].Visual.gameObject.SetActive(false);
                chips[i].End = 0f;
            }
        }

        public void Build(Material surface, LayerMask surfaceMask)
        {
            receivingSurfaces = surfaceMask;
            fluid = new Material(surface) { name = "Anatomy fluid", color = new Color32(113, 22, 30, 255) };
            bone = new Material(surface) { name = "Bone chip", color = new Color32(222, 209, 176, 255) };
            markMesh = CreateStainMesh();
            chipMesh = CreateChipMesh();
            for (int i = 0; i < marks.Length; i++)
            {
                var mark = new GameObject("Stain");
                mark.transform.SetParent(transform, false);
                mark.AddComponent<MeshFilter>().sharedMesh = markMesh;
                var renderer = mark.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = fluid;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                mark.SetActive(false);
                marks[i].Visual = mark.transform;
            }
            for (int i = 0; i < chips.Length; i++)
            {
                var chip = new GameObject("Pooled bone fragment");
                chip.transform.SetParent(transform, false);
                chip.AddComponent<MeshFilter>().sharedMesh = chipMesh;
                var renderer = chip.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = bone;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                chip.SetActive(false);
                chips[i].Visual = chip.transform;
            }

            var particleObject = new GameObject("Blood droplets");
            particleObject.SetActive(false);
            particleObject.transform.SetParent(transform, false);
            drops = particleObject.AddComponent<ParticleSystem>();
            var main = drops.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = MaximumParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.75f;
            var emission = drops.emission;
            emission.enabled = false;
            var shape = drops.shape;
            shape.enabled = false;
            var particleRenderer = particleObject.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = fluid;
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            particleObject.SetActive(true);
            drops.Play();

            impactClips = new[] {
                MakeSound("Anatomy thud", 0.095f, 105f, 0.32f),
                MakeSound("Anatomy crack", 0.085f, 440f, 0.8f),
                MakeSound("Anatomy tear", 0.135f, 180f, 0.55f)
            };
            impactVoices = new AudioSource[4];
            for (int i = 0; i < impactVoices.Length; i++)
            {
                var source = new GameObject("Anatomy impact voice").AddComponent<AudioSource>();
                source.transform.SetParent(transform, false);
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.dopplerLevel = 0f;
                source.minDistance = 0.4f;
                source.maxDistance = 8f;
                impactVoices[i] = source;
            }
        }

        public void Report(Transform attachment, Vector3 point, Vector3 direction, AnatomyStage stage, float strength,
            bool freshFracture = false)
        {
            strength = Mathf.Clamp01(strength);
            PlaySound(point, stage, strength);
            if (quality == GoreQuality.Off || stage == AnatomyStage.Intact || drops == null)
                return;

            Vector3 outward = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.up;
            int budget = quality == GoreQuality.High ? 22 : quality == GoreQuality.Medium ? 12 : 5;
            int count = Mathf.Max(2, Mathf.RoundToInt(budget * Mathf.Lerp(0.4f, 1f, strength)));
            for (int i = 0; i < count; i++)
                Emit(point, (outward + RandomVector() * 0.58f) * Mathf.Lerp(0.8f, 3.1f, Next()), false);

            if (quality >= GoreQuality.Medium)
                ProjectStain(point, (outward + Vector3.down * 0.8f).normalized, 0.11f + strength * 0.18f, 0.08f);
            if (freshFracture && quality >= GoreQuality.Medium)
                ScatterChips(point, outward);

            if (quality >= GoreQuality.Medium && stage >= AnatomyStage.Exposed && attachment != null)
            {
                int dripLimit = quality == GoreQuality.High ? MaximumDrips : 4;
                wounds[nextWound++ % dripLimit] = new Wound
                {
                    Anchor = attachment,
                    LocalPoint = attachment.InverseTransformPoint(point),
                    Next = Time.time + 0.12f,
                    End = Time.time + (stage == AnatomyStage.Severed ? 3.8f : 2.1f),
                };
            }
        }

        public void SurfaceContact(Vector3 point, Vector3 normal)
        {
            if (quality < GoreQuality.Medium || markMesh == null)
                return;
            PlaceStain(point, normal, Mathf.Lerp(0.065f, 0.145f, Next()), 0f);
            if (quality == GoreQuality.High)
                Emit(point, normal + RandomVector() * 0.7f, false);
        }

        private void Update()
        {
            if (quality == GoreQuality.Off)
                return;

            float now = Time.time;
            for (int i = 0; i < wounds.Length; i++)
            {
                ref Wound wound = ref wounds[i];
                if (wound.Anchor == null || now >= wound.End || now < wound.Next)
                    continue;
                wound.Next = now + Mathf.Lerp(0.2f, 0.34f, Next());
                Vector3 point = wound.Anchor.TransformPoint(wound.LocalPoint);
                Emit(point, Vector3.down * 0.35f, true);
                ProjectStain(point, Vector3.down, 0.08f + Next() * 0.10f, 0.22f);
            }
            for (int i = 0; i < marks.Length; i++)
            {
                ref SurfaceMark mark = ref marks[i];
                if (mark.End == 0f)
                    continue;
                if (now >= mark.End)
                {
                    mark.Visual.gameObject.SetActive(false);
                    mark.End = 0f;
                    continue;
                }
                if (now < mark.Start + mark.Delay)
                    continue;
                if (!mark.Visual.gameObject.activeSelf)
                    mark.Visual.gameObject.SetActive(true);
                float phase = Mathf.Clamp01((now - mark.Start - mark.Delay) / 1.5f);
                mark.Visual.localScale = Vector3.one * (mark.Size * Mathf.Lerp(0.12f, 1f, phase));
            }
            for (int i = 0; i < chips.Length; i++)
                MoveChip(ref chips[i], now, Mathf.Min(Time.deltaTime, 0.035f));
        }

        private void ProjectStain(Vector3 source, Vector3 direction, float radius, float delay)
        {
            if (!Physics.Raycast(source, direction, out RaycastHit hit, 4.2f, receivingSurfaces, QueryTriggerInteraction.Ignore))
                return;
            PlaceStain(hit.point, hit.normal, radius, delay);
        }

        private void PlaceStain(Vector3 point, Vector3 normal, float radius, float delay)
        {
            int limit = quality == GoreQuality.High ? MaximumMarks : 12;
            ref SurfaceMark mark = ref marks[nextMark++ % limit];
            float angle = Next() * 360f;
            mark.Visual.SetPositionAndRotation(point + normal * 0.002f,
                Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, angle, 0f));
            mark.Visual.gameObject.SetActive(false);
            mark.Start = Time.time;
            mark.End = Time.time + 16f;
            mark.Size = radius;
            mark.Delay = delay;
        }

        private void ScatterChips(Vector3 point, Vector3 outward)
        {
            int count = quality == GoreQuality.High ? 3 : 1;
            for (int i = 0; i < count; i++)
            {
                ref BoneChip chip = ref chips[nextChip++ % chips.Length];
                chip.Visual.SetPositionAndRotation(point + outward * 0.03f, Quaternion.Euler(RandomVector() * 180f));
                chip.Visual.localScale = Vector3.one * Mathf.Lerp(0.038f, 0.064f, Next());
                chip.Visual.gameObject.SetActive(true);
                chip.Velocity = outward * 1.25f + RandomVector() * 0.53f + Vector3.up * 1.0f;
                chip.End = Time.time + 4.5f;
            }
        }

        private void MoveChip(ref BoneChip chip, float now, float step)
        {
            if (chip.End == 0f)
                return;
            if (now >= chip.End)
            {
                chip.Visual.gameObject.SetActive(false);
                chip.End = 0f;
                return;
            }
            if (chip.Velocity == Vector3.zero)
                return;

            chip.Velocity += Vector3.down * (9.81f * step);
            Vector3 point = chip.Visual.position;
            Vector3 delta = chip.Velocity * step;
            if (Physics.Raycast(point, delta.normalized, out RaycastHit hit, delta.magnitude + 0.009f,
                    receivingSurfaces, QueryTriggerInteraction.Ignore))
            {
                chip.Visual.position = hit.point + hit.normal * 0.010f;
                chip.Velocity = Vector3.Reflect(chip.Velocity, hit.normal) * 0.34f;
                if (chip.Velocity.sqrMagnitude < 0.24f)
                    chip.Velocity = Vector3.zero;
            }
            else
                chip.Visual.position = point + delta;
            chip.Visual.Rotate(115f * step, 70f * step, 20f * step);
        }

        private void Emit(Vector3 place, Vector3 velocity, bool drip)
        {
            var emission = new ParticleSystem.EmitParams
            {
                position = place,
                velocity = velocity,
                startLifetime = drip ? 0.65f : Mathf.Lerp(0.15f, 0.49f, Next()),
                startSize = drip ? 0.026f : Mathf.Lerp(0.016f, 0.047f, Next()),
                applyShapeToPosition = false
            };
            drops.Emit(emission, 1);
        }

        private void PlaySound(Vector3 point, AnatomyStage stage, float strength)
        {
            if (impactVoices == null)
                return;
            int variant = quality == GoreQuality.Off ? 0 :
                stage == AnatomyStage.Severed ? 2 : stage == AnatomyStage.Fractured ? 1 : 0;
            AudioSource source = impactVoices[nextVoice++ % impactVoices.Length];
            source.Stop();
            source.transform.position = point;
            source.pitch = Mathf.Lerp(0.9f, 1.13f, Next());
            source.PlayOneShot(impactClips[variant], Mathf.Lerp(0.19f, 0.63f, strength));
        }

        private Vector3 RandomVector() => new(Next() * 2f - 1f, Next() * 2f - 1f, Next() * 2f - 1f);
        private float Next() => (float)random.NextDouble();

        private static Mesh CreateStainMesh()
        {
            const int sides = 14;
            var vertices = new Vector3[sides + 1];
            var triangles = new int[sides * 3];
            for (int i = 0; i < sides; i++)
            {
                float angle = 2f * Mathf.PI * i / sides;
                float radius = 0.73f + 0.27f * Mathf.Sin(i * 2.9f);
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % sides + 1;
                triangles[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = "Reusable surface stain", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            return mesh;
        }

        private static Mesh CreateChipMesh()
        {
            var mesh = new Mesh
            {
                name = "Bone fragment",
                vertices = new[] {
                    new Vector3(-0.55f, -0.2f, -0.4f), new Vector3(0.56f, -0.12f, -0.3f),
                    new Vector3(0.21f, -0.27f, 0.5f), new Vector3(0.08f, 0.65f, -0.15f)
                },
                triangles = new[] { 0, 1, 2, 0, 3, 1, 1, 3, 2, 2, 3, 0 }
            };
            mesh.RecalculateNormals();
            return mesh;
        }

        private static AudioClip MakeSound(string label, float duration, float frequency, float noiseMix)
        {
            const int rate = 24000;
            var samples = new float[Mathf.RoundToInt(duration * rate)];
            var source = new System.Random(samples.Length);
            float filtered = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (float)i / rate;
                float noise = (float)(source.NextDouble() * 2.0 - 1.0);
                filtered = Mathf.Lerp(filtered, noise, 0.49f);
                float envelope = Mathf.Clamp01(t * 1400f) * Mathf.Pow(1f - t / duration, 2.4f);
                float tone = Mathf.Sin(t * Mathf.PI * 2f * frequency * (1f - t * 2f));
                samples[i] = Mathf.Lerp(tone * 0.67f, filtered, noiseMix) * envelope;
            }
            var clip = AudioClip.Create(label, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDestroy()
        {
            if (impactClips != null)
                foreach (AudioClip clip in impactClips)
                    Destroy(clip);
            Destroy(fluid);
            Destroy(markMesh);
            Destroy(bone);
            Destroy(chipMesh);
        }
    }
}
