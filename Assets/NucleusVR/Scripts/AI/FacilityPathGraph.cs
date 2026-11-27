using UnityEngine;

namespace NucleusVR.AI
{
    internal sealed class FacilityPathGraph
    {
        private readonly Vector3[] points;
        private readonly int[,] next;

        public FacilityPathGraph(Vector3[] nodes, (int From, int To)[] links)
        {
            points = (Vector3[])nodes.Clone();
            int count = points.Length;
            next = new int[count, count];
            var costs = new float[count, count];
            for (int from = 0; from < count; from++)
            for (int to = 0; to < count; to++)
            {
                next[from, to] = from == to ? from : -1;
                costs[from, to] = from == to ? 0f : float.PositiveInfinity;
            }

            foreach ((int from, int to) in links)
            {
                if ((uint)from >= (uint)count || (uint)to >= (uint)count || from == to)
                    continue;
                float distance = Vector3.Distance(points[from], points[to]);
                if (distance <= 0.001f || !float.IsFinite(distance))
                    continue;
                costs[from, to] = costs[to, from] = distance;
                next[from, to] = to;
                next[to, from] = from;
            }

            for (int via = 0; via < count; via++)
            for (int from = 0; from < count; from++)
            for (int to = 0; to < count; to++)
            {
                float cost = costs[from, via] + costs[via, to];
                if (cost >= costs[from, to])
                    continue;
                costs[from, to] = cost;
                next[from, to] = next[from, via];
            }
        }

        public int Count => points.Length;
        public Vector3 Point(int index) => points[index];
        public int Next(int from, int to) => next[from, to];

        public int Nearest(Vector3 point)
        {
            int closest = -1;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 delta = point - points[i];
                float candidate = delta.x * delta.x + delta.z * delta.z;
                if (candidate >= distance)
                    continue;
                distance = candidate;
                closest = i;
            }
            return closest;
        }
    }
}
