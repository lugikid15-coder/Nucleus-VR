using System.Collections.Generic;
using UnityEngine;

namespace NucleusVR.Anatomy
{
    internal sealed class AnatomyVisualMesh
    {
        private readonly List<Vector3> vertices = new();
        private readonly List<int> triangles = new();

        public void Lobe(Vector3 center, Vector3 radii, Quaternion rotation, int sides = 12, int rings = 8,
            float windowDegrees = 0f, float windowHeight = 0f, float windowCenter = 0f, int trim = 0,
            bool fromBottom = false, float ripple = 0f)
        {
            int start = vertices.Count;
            vertices.Add(center + rotation * (Vector3.up * radii.y));
            for (int ring = 1; ring < rings; ring++)
            {
                float latitude = Mathf.PI * ring / rings;
                for (int side = 0; side < sides; side++)
                {
                    float angle = Mathf.PI * 2f * side / sides;
                    float radial = Mathf.Sin(latitude)
                        * (1f + ripple * Mathf.Cos(angle * 6f + latitude * 1.3f));
                    Vector3 unit = new(Mathf.Cos(angle) * radial, Mathf.Cos(latitude), Mathf.Sin(angle) * radial);
                    vertices.Add(center + rotation * Vector3.Scale(unit, radii));
                }
            }

            int end = vertices.Count;
            vertices.Add(center + rotation * (Vector3.down * radii.y));
            float windowCosine = Mathf.Cos(windowDegrees * Mathf.Deg2Rad);
            for (int side = 0; side < sides; side++)
            {
                int next = (side + 1) % sides;
                float forward = Mathf.Sin(Mathf.PI * 2f * (side + 0.5f) / sides);
                if (trim == 0 || fromBottom)
                    Triangle(start, start + 1 + next, start + 1 + side);

                for (int ring = 1; ring < rings - 1; ring++)
                {
                    if (trim > 0 && (fromBottom ? ring >= rings - trim : ring < trim))
                        continue;
                    float y = Mathf.Cos(Mathf.PI * (ring + 0.5f) / rings);
                    // Vary the edge across the prepared wound instead of cutting a rectangular panel.
                    float edge = windowCosine + Mathf.Sin(ring * 2.1f + side * 0.81f) * 0.074f;
                    float edgeHeight = windowHeight + Mathf.Cos(side * 1.36f) * 0.067f;
                    if (windowDegrees > 0f && forward > edge && Mathf.Abs(y - windowCenter) <= edgeHeight)
                        continue;

                    int row = start + 1 + (ring - 1) * sides;
                    Triangle(row + side, row + next, row + sides + next);
                    Triangle(row + side, row + sides + next, row + sides + side);
                }

                if (trim == 0 || !fromBottom)
                    Triangle(end - sides + side, end - sides + next, end);
            }
        }

        public void Tube(Vector3[] points, float radius, float taper = 1f, int sides = 6)
        {
            int start = vertices.Count;
            for (int index = 0; index < points.Length; index++)
            {
                Vector3 before = points[Mathf.Max(0, index - 1)];
                Vector3 after = points[Mathf.Min(points.Length - 1, index + 1)];
                Vector3 tangent = (after - before).normalized;

                // Parallel-transport the ring frame to avoid twists along the authored bends.
                Vector3 reference = index == 0
                    ? (Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) < 0.9f ? Vector3.up : Vector3.forward)
                    : vertices[start + (index - 1) * sides] - before;
                Vector3 right = Vector3.ProjectOnPlane(reference, tangent).normalized;
                if (right.sqrMagnitude < 0.5f)
                    right = Vector3.Cross(tangent, Mathf.Abs(tangent.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
                Vector3 up = Vector3.Cross(tangent, right);
                float width = radius * Mathf.Lerp(1f, taper, index / (points.Length - 1f));
                for (int side = 0; side < sides; side++)
                {
                    float angle = Mathf.PI * 2f * side / sides;
                    vertices.Add(points[index] + width * (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)));

                    if (index == 0)
                        continue;
                    int next = (side + 1) % sides;
                    int previous = start + (index - 1) * sides;
                    int current = previous + sides;
                    Triangle(previous + side, previous + next, current + next);
                    Triangle(previous + side, current + next, current + side);
                }
            }

            // Cap tube ends without including collision or loose physics in the geometry.
            int firstCap = vertices.Count;
            vertices.Add(points[0]);
            int lastCap = vertices.Count;
            vertices.Add(points[points.Length - 1]);
            int lastRing = start + (points.Length - 1) * sides;
            for (int side = 0; side < sides; side++)
            {
                int next = (side + 1) % sides;
                Triangle(firstCap, start + next, start + side);
                Triangle(lastCap, lastRing + side, lastRing + next);
            }
        }

        public void Disc(Vector3 center, float width, float depth, bool down, bool uneven = false)
        {
            int start = vertices.Count;
            const int sides = 16;
            vertices.Add(center);
            for (int side = 0; side < sides; side++)
            {
                float angle = Mathf.PI * 2f * side / sides;
                float height = uneven ? Mathf.Sin(side * 5f) * 0.009f : 0f;
                vertices.Add(center + new Vector3(Mathf.Cos(angle) * width, height, Mathf.Sin(angle) * depth));
            }

            for (int side = 0; side < sides; side++)
            {
                int current = start + 1 + side;
                int next = start + 1 + (side + 1) % sides;
                if (down)
                    Triangle(start, current, next);
                else
                    Triangle(start, next, current);
            }
        }

        public Mesh Finish(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Triangle(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }
    }
}
