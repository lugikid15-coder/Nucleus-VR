using UnityEngine;

namespace NucleusVR.Maps
{
    internal static class FacilityGeometry
    {
        public static GameObject Block(Transform parent, string label, Vector3 position, Vector3 size,
            Material finish, int layer, bool collidable = true)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = label;
            item.layer = layer;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = size;
            item.GetComponent<MeshRenderer>().sharedMaterial = finish;
            if (!collidable)
            {
                var collider = item.GetComponent<Collider>();
                collider.enabled = false;
                Object.Destroy(collider);
            }
            return item;
        }

        public static TextMesh Label(Transform parent, string value, Vector3 position, float size, Color color)
        {
            var item = new GameObject(value);
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            var label = item.AddComponent<TextMesh>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (label.font != null)
                item.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
            label.fontSize = 72;
            label.characterSize = size / 7.2f;
            label.alignment = TextAlignment.Center;
            label.anchor = TextAnchor.MiddleCenter;
            label.text = value;
            label.color = color;
            return label;
        }
    }
}
