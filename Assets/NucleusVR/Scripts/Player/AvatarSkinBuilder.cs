using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NucleusVR.Player
{
    /// <summary>Bakes the avatar's hand-shaped forms into a single skinned draw per visibility group.</summary>
    internal sealed class AvatarSkinBuilder
    {
        private readonly Transform surface;
        private readonly int paletteSize;
        private readonly List<Transform> bones = new();
        private readonly List<Matrix4x4> bindPoses = new();
        private readonly Dictionary<Transform, int> boneIndices = new();
        private readonly List<Vector3> vertices = new();
        private readonly List<Vector3> normals = new();
        private readonly List<Vector2> coordinates = new();
        private readonly List<int> triangles = new();
        private readonly List<BoneWeight> weights = new();

        public AvatarSkinBuilder(Transform surface, int paletteSize)
        {
            this.surface = surface;
            this.paletteSize = paletteSize;
        }

        public void AddLobe(Transform bone, Vector3 center, Vector3 radii,
            int color, int sides = 10, int rings = 6)
        {
            AddLobe(bone, center, radii, Quaternion.identity, color, sides, rings);
        }

        public void AddLobe(Transform bone, Vector3 center, Vector3 radii, Quaternion rotation,
            int color, int sides = 10, int rings = 6)
        {
            int boneIndex = AddBone(bone);
            var uv = new Vector2((color + 0.5f) / paletteSize, 0.5f);
            Matrix4x4 toSurface = surface.worldToLocalMatrix * bone.localToWorldMatrix;
            Matrix4x4 toNormal = toSurface.inverse.transpose;
            int top = vertices.Count;
            EmitVertex(Vector3.up);
            for (int ring = 1; ring < rings; ring++)
            {
                float latitude = Mathf.PI * ring / rings;
                float radius = Mathf.Sin(latitude);
                float y = Mathf.Cos(latitude);

                for (int side = 0; side < sides; side++)
                {
                    float angle = 2f * Mathf.PI * side / sides;
                    EmitVertex(new Vector3(radius * Mathf.Cos(angle), y, radius * Mathf.Sin(angle)));
                }
            }

            int bottom = vertices.Count;
            EmitVertex(Vector3.down);
            int firstRing = top + 1;
            int lastRing = bottom - sides;
            for (int side = 0; side < sides; side++)
            {
                int next = (side + 1) % sides;
                AddTriangle(top, firstRing + next, firstRing + side);
                for (int ringStart = firstRing; ringStart < lastRing; ringStart += sides)
                {
                    AddTriangle(ringStart + side, ringStart + next, ringStart + sides + next);
                    AddTriangle(ringStart + side, ringStart + sides + next, ringStart + sides + side);
                }

                AddTriangle(lastRing + side, lastRing + next, bottom);
            }

            void EmitVertex(Vector3 direction)
            {
                Vector3 point = center + rotation * Vector3.Scale(direction, radii);
                Vector3 normal = rotation * new Vector3(direction.x / radii.x,
                    direction.y / radii.y, direction.z / radii.z);
                vertices.Add(toSurface.MultiplyPoint3x4(point));
                normals.Add(toNormal.MultiplyVector(normal).normalized);
                coordinates.Add(uv);
                weights.Add(new BoneWeight { boneIndex0 = boneIndex, weight0 = 1f });
            }
        }

        public void Bake(SkinnedMeshRenderer renderer, Transform rootBone, Material material, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, coordinates);
            mesh.SetTriangles(triangles, 0, true);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = bindPoses.ToArray();

            renderer.bones = bones.ToArray();
            renderer.rootBone = rootBone;
            renderer.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.localBounds = new Bounds(new Vector3(0f, 0.42f, 0f), new Vector3(4f, 3f, 4f));
            renderer.quality = SkinQuality.Bone1;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        private int AddBone(Transform bone)
        {
            if (boneIndices.TryGetValue(bone, out int index))
                return index;

            index = bones.Count;
            bones.Add(bone);
            bindPoses.Add(bone.worldToLocalMatrix * surface.localToWorldMatrix);
            boneIndices.Add(bone, index);
            return index;
        }

        private void AddTriangle(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }
    }
}
