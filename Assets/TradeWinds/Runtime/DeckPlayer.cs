using UnityEngine;
using UnityEngine.InputSystem;

namespace TradeWinds
{
    public sealed class DeckPlayer : MonoBehaviour
    {
        public static readonly Vector3 HelmPosition = new Vector3(0, 2.15f, -5.3f);
        public bool AtHelm { get; private set; }
        public bool Paused { get; private set; }
        public bool ExternalView { get; private set; }
        public bool NearHelm { get { return Vector3.Distance(localPosition, HelmPosition) < 2.4f; } }
        public float Sensitivity { get; set; } = 0.12f;
        public float CameraRock { get; set; } = 0.65f;
        private ShipController ship;
        private Camera view;
        private Vector3 localPosition = new Vector3(1.5f, 2.15f, -4.6f);
        private float yaw;
        private float pitch = 8;
        // Deterministic local deck bounds avoid rigidbody/parent jitter on a moving ship.
        private readonly Rect[] obstacles = {
            new Rect(-0.65f, -0.65f, 1.3f, 1.3f),
            new Rect(-2.8f, 1.7f, 2.15f, 2.6f),
            new Rect(0.75f, 1.7f, 2.05f, 2.6f),
            new Rect(-0.7f, -4.4f, 1.4f, 1.3f)
        };

        public void Initialize(ShipController controller, Camera camera)
        {
            ship = controller;
            view = camera;
            LockCursor(true);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || ship == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame) SetPaused(!Paused);
            if (Paused) return;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 look = mouse.delta.ReadValue() * Sensitivity;
                yaw += look.x;
                pitch = Mathf.Clamp(pitch - look.y, -65, 70);
            }
            if (keyboard.eKey.wasPressedThisFrame && (AtHelm || NearHelm))
            {
                AtHelm = !AtHelm;
                if (AtHelm) { localPosition = HelmPosition; yaw = 0; pitch = 8; }
            }
            if (keyboard.tabKey.wasPressedThisFrame) ExternalView = !ExternalView;
            if (keyboard.rKey.wasPressedThisFrame) ResetPlayerAndShip();
            float horizontal = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
            float vertical = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
            ship.Steering = AtHelm ? horizontal : 0;
            ship.SailChange = AtHelm ? vertical : 0;
            if (AtHelm && keyboard.spaceKey.wasPressedThisFrame) ship.ToggleAnchor();
            if (!AtHelm)
            {
                Vector3 direction = Quaternion.Euler(0, yaw, 0) * new Vector3(horizontal, 0, vertical).normalized;
                Vector3 delta = direction * (keyboard.leftShiftKey.isPressed ? 4.2f : 2.7f) * Time.deltaTime;
                MoveLocal(new Vector3(delta.x, 0, 0));
                MoveLocal(new Vector3(0, 0, delta.z));
            }
        }

        private void MoveLocal(Vector3 delta)
        {
            var next = localPosition + delta;
            next.x = Mathf.Clamp(next.x, -2.5f, 2.5f);
            next.z = Mathf.Clamp(next.z, -6.5f, 6.3f);
            foreach (Rect obstacle in obstacles)
                if (obstacle.Contains(new Vector2(next.x, next.z))) return;
            localPosition = next;
        }

        private void LateUpdate()
        {
            if (ship == null) return;
            ship.UpdatePresentation();
            transform.SetPositionAndRotation(ship.transform.TransformPoint(localPosition),
                ship.transform.rotation * Quaternion.Euler(0, yaw, 0));
            if (ExternalView)
            {
                view.transform.position = ship.transform.position + Quaternion.Euler(0, (float)ship.State.Heading + yaw, 0)
                    * new Vector3(17, 12, -24);
                view.transform.LookAt(ship.transform.position + Vector3.up * 3);
            }
            else
            {
                view.transform.position = ship.transform.TransformPoint(localPosition + Vector3.up * 1.65f);
                Quaternion level = Quaternion.Euler(0, (float)ship.State.Heading, 0);
                view.transform.rotation = Quaternion.Slerp(level, ship.transform.rotation, CameraRock)
                    * Quaternion.Euler(pitch, yaw, 0);
            }
            Shader.SetGlobalFloat("_VoyageTime", (float)ship.State.Clock);
            Shader.SetGlobalFloat("_SeaStrength", ship.SeaStrength);
        }

        public void SetPaused(bool paused)
        {
            Paused = paused;
            ship.Paused = paused;
            ship.Steering = ship.SailChange = 0;
            LockCursor(!paused);
        }

        public void ResetPlayerAndShip()
        {
            ship.ResetVoyage();
            localPosition = new Vector3(1.5f, 2.15f, -4.6f);
            AtHelm = false;
            yaw = 0;
            pitch = 8;
        }

        private void OnApplicationFocus(bool focus) { if (!focus && ship != null) SetPaused(true); }
        private void OnDisable() { LockCursor(false); if (ship != null) ship.Steering = ship.SailChange = 0; }
        private static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
