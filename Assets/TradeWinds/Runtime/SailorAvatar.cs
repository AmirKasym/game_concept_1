using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TradeWinds
{
    // Lightweight, original stylized sailor assembled from shared Unity meshes.
    public sealed class SailorAvatar : MonoBehaviour
    {
        private readonly List<Renderer> renderers = new List<Renderer>();
        private Transform leftArm, rightArm, leftLeg, rightLeg, torso;
        private Vector3 previousPosition, targetPosition;
        private float targetYaw, phase;
        private bool initialized;
        private bool carrying;

        public void Build(Material material, int variant)
        {
            Color skin = variant % 2 == 0 ? new Color(0.72f, 0.46f, 0.3f) : new Color(0.9f, 0.66f, 0.43f);
            Color navy = new Color(0.07f, 0.16f, 0.23f), cream = new Color(0.91f, 0.86f, 0.69f);
            Color coat = variant % 3 == 0 ? new Color(0.64f, 0.22f, 0.12f)
                : variant % 3 == 1 ? new Color(0.12f, 0.39f, 0.4f) : new Color(0.74f, 0.53f, 0.16f);
            torso = new GameObject("Jacket and striped shirt").transform;
            torso.SetParent(transform, false);
            Part("Jacket", torso, PrimitiveType.Capsule, new Vector3(0, 1.1f, 0), new Vector3(0.52f, 0.36f, 0.3f), coat, material);
            Part("Shirt", torso, PrimitiveType.Cube, new Vector3(0, 1.17f, 0.145f), new Vector3(0.22f, 0.4f, 0.025f), cream, material);
            for (int i = 0; i < 4; i++) Part("Shirt stripe", torso, PrimitiveType.Cube,
                new Vector3(0, 1.04f + i * 0.075f, 0.162f), new Vector3(0.225f, 0.025f, 0.012f), navy, material);
            Part("Belt", torso, PrimitiveType.Cube, new Vector3(0, 0.88f, 0), new Vector3(0.48f, 0.08f, 0.3f), navy, material);
            Part("Brass buckle", torso, PrimitiveType.Cube, new Vector3(0, 0.88f, 0.162f), new Vector3(0.095f, 0.09f, 0.028f), new Color(0.9f, 0.66f, 0.2f), material);
            Part("Neck", torso, PrimitiveType.Cylinder, new Vector3(0, 1.44f, 0), new Vector3(0.14f, 0.075f, 0.14f), skin, material);
            Part("Face", torso, PrimitiveType.Sphere, new Vector3(0, 1.61f, 0.02f), new Vector3(0.32f, 0.36f, 0.3f), skin, material);
            Part("Nose", torso, PrimitiveType.Sphere, new Vector3(0, 1.61f, 0.178f), new Vector3(0.065f, 0.075f, 0.06f), skin, material);
            foreach (int side in new[] { -1, 1 })
            {
                Part("Eye", torso, PrimitiveType.Sphere, new Vector3(side * 0.065f, 1.65f, 0.154f), Vector3.one * 0.035f, navy, material);
                Part("Ear", torso, PrimitiveType.Sphere, new Vector3(side * 0.165f, 1.61f, 0.02f), new Vector3(0.06f, 0.095f, 0.07f), skin, material);
            }
            Part("Sailor cap", torso, PrimitiveType.Cylinder, new Vector3(0, 1.79f, 0.015f), new Vector3(0.37f, 0.065f, 0.34f), cream, material);
            Part("Cap band", torso, PrimitiveType.Cylinder, new Vector3(0, 1.735f, 0.015f), new Vector3(0.34f, 0.024f, 0.32f), navy, material);
            Part("Cap visor", torso, PrimitiveType.Cube, new Vector3(0, 1.735f, 0.18f), new Vector3(0.28f, 0.035f, 0.2f), navy, material);
            leftArm = Limb("Left arm", new Vector3(-0.32f, 1.35f, 0), coat, skin, material, true);
            rightArm = Limb("Right arm", new Vector3(0.32f, 1.35f, 0), coat, skin, material, true);
            leftLeg = Limb("Left leg", new Vector3(-0.135f, 0.82f, 0), navy, navy, material, false);
            rightLeg = Limb("Right leg", new Vector3(0.135f, 0.82f, 0), navy, navy, material, false);
        }

        private Transform Limb(string title, Vector3 position, Color cloth, Color skin, Material material, bool arm)
        {
            var pivot = new GameObject(title).transform;
            pivot.SetParent(transform, false); pivot.localPosition = position;
            Part("Sleeve", pivot, PrimitiveType.Capsule, new Vector3(0, arm ? -0.22f : -0.3f, 0),
                arm ? new Vector3(0.17f, 0.25f, 0.18f) : new Vector3(0.22f, 0.33f, 0.23f), cloth, material);
            Part(arm ? "Hand" : "Boot", pivot, arm ? PrimitiveType.Sphere : PrimitiveType.Cube,
                new Vector3(0, arm ? -0.47f : -0.71f, arm ? 0 : 0.05f),
                arm ? new Vector3(0.15f, 0.19f, 0.15f) : new Vector3(0.24f, 0.21f, 0.35f), skin, material);
            return pivot;
        }

        private void Part(string title, Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Color color, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = title; part.transform.SetParent(parent, false);
            part.transform.localPosition = position; part.transform.localScale = scale;
            Destroy(part.GetComponent<Collider>());
            var renderer = part.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            var properties = new MaterialPropertyBlock(); properties.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(properties); renderers.Add(renderer);
        }

        public void SetPose(Vector3 position, float yaw, bool hideBody, bool holdsCargo = false)
        {
            carrying = holdsCargo;
            targetPosition = position; targetYaw = yaw;
            if (!initialized) { previousPosition = position; transform.localPosition = position; initialized = true; }
            foreach (var renderer in renderers)
                renderer.shadowCastingMode = hideBody ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, 1 - Mathf.Exp(-24 * Time.deltaTime));
            transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.Euler(0, targetYaw, 0), 1 - Mathf.Exp(-20 * Time.deltaTime));
            float speed = Vector3.Distance(transform.localPosition, previousPosition) / Mathf.Max(Time.deltaTime, 0.001f);
            previousPosition = transform.localPosition;
            phase += Mathf.Min(speed, 4.2f) * Time.deltaTime * 3.3f;
            float swing = Mathf.Sin(phase) * Mathf.Clamp01(speed) * 27;
            leftLeg.localRotation = Quaternion.Euler(swing, 0, 0); rightLeg.localRotation = Quaternion.Euler(-swing, 0, 0);
            leftArm.localRotation = Quaternion.Euler(carrying ? -65 : -swing, 0, 5);
            rightArm.localRotation = Quaternion.Euler(carrying ? -65 : swing, 0, -5);
        }
    }
}
