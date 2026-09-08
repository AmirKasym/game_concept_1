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
        public CoopSession Session { get; set; }
        public Vector3 DeckPosition { get { return localPosition; } }
        public float LookYaw { get { return yaw; } }
        private readonly DeckMotor motor = new DeckMotor();
        private ShipController ship;
        private Camera view;
        private Vector3 localPosition = new Vector3(1.5f, 2.15f, -4.6f);
        private float yaw;
        private float pitch = 8;

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
            if (Paused)
            {
                if (Session != null) Session.SetInput(new CrewInput());
                return;
            }
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 look = mouse.delta.ReadValue() * Sensitivity;
                yaw += look.x;
                pitch = Mathf.Clamp(pitch - look.y, -65, 70);
            }
            if (keyboard.tabKey.wasPressedThisFrame) ExternalView = !ExternalView;
            float horizontal = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
            float vertical = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
            bool jump = keyboard.spaceKey.wasPressedThisFrame;
            if (Session != null && Session.Active)
            {
                Session.SetInput(new CrewInput { horizontal = horizontal, forward = vertical, yaw = yaw,
                    sprint = keyboard.leftShiftKey.isPressed, jump = jump, helm = keyboard.eKey.wasPressedThisFrame,
                    anchor = keyboard.bKey.wasPressedThisFrame, cargo = keyboard.fKey.wasPressedThisFrame });
                return;
            }
            if (keyboard.eKey.wasPressedThisFrame && (AtHelm || NearHelm) && motor.Grounded)
            {
                AtHelm = !AtHelm;
                if (AtHelm) { motor.Reset(0, -5.3f); localPosition = HelmPosition; yaw = 0; pitch = 8; }
            }
            if (keyboard.rKey.wasPressedThisFrame) ResetPlayerAndShip();
            if (jump) AtHelm = false;
            ship.Steering = AtHelm ? horizontal : 0;
            ship.SailChange = AtHelm ? vertical : 0;
            if (AtHelm && keyboard.bKey.wasPressedThisFrame) ship.ToggleAnchor();
            if (!AtHelm)
            {
                motor.Step(Mathf.Min(Time.deltaTime, 0.1f), horizontal, vertical, yaw, keyboard.leftShiftKey.isPressed, jump);
                localPosition = new Vector3(motor.X, motor.Y, motor.Z);
            }
            if (Session != null && keyboard.fKey.wasPressedThisFrame) Session.InteractOffline();
        }

        public void ApplyNetworkPose(Vector3 position, bool atHelm) { localPosition = position; AtHelm = atHelm; }

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
            ship.Paused = Session != null && Session.Active ? !Session.IsHost : paused;
            ship.Steering = ship.SailChange = 0;
            LockCursor(!paused);
        }

        public void ResetPlayerAndShip()
        {
            ship.ResetVoyage();
            motor.Reset();
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
