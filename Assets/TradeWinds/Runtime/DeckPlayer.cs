using UnityEngine;
using UnityEngine.InputSystem;

namespace TradeWinds
{
    public sealed class DeckPlayer : MonoBehaviour
    {
        public static readonly Vector3 HelmPosition = new Vector3(0, 2.15f, -5.3f);
        public ShipActor OfflineActor { get; private set; }
        public bool AtHelm { get { return Session != null && Session.Active ? networkAtHelm : OfflineActor != null && OfflineActor.AtHelm; } }
        public bool Climbing { get { return Session != null && Session.Active ? networkClimbing : OfflineActor != null && OfflineActor.Climbing; } }
        public bool Aboard { get { return Session != null && Session.Active ? networkAboard : OfflineActor != null && OfflineActor.Platform != null; } }
        public bool Paused { get; private set; }
        public bool ExternalView { get; private set; }
        public bool Overview { get; private set; }
        public bool NearHelm { get { return Aboard && Vector3.Distance(WorldPosition, ship.transform.TransformPoint(HelmPosition)) < 2.4f; } }
        public float Sensitivity { get; set; } = 0.12f;
        public float CameraRock { get; set; } = 0.45f;
        public CoopSession Session { get; set; }
        public Vector3 WorldPosition { get { return Session != null && Session.Active ? transform.position : OfflineActor.transform.position; } }
        public Vector3 DeckPosition { get { return ship.transform.InverseTransformPoint(WorldPosition); } }
        public float LookYaw { get { return yaw; } }
        public Camera View { get { return view; } }
        public bool IsGrounded { get { return Session != null && Session.Active ? networkGrounded : OfflineActor != null && OfflineActor.Grounded; } }
        private bool networkGrounded;
        private ShipController ship;
        private Camera view;
        private float yaw, pitch = 8, previousHeading;
        private bool networkAtHelm, networkClimbing, networkAboard;
        private CrewInput pending;

        public void Initialize(ShipController controller, Camera camera)
        {
            ship = controller; view = camera;
            OfflineActor = new GameObject("Physical local sailor").AddComponent<ShipActor>();
            OfflineActor.Initialize(ship, 0, new Vector3(1.5f, 2.2f, -4.6f));
            previousHeading = (float)ship.State.Heading;
            LockCursor(true);
        }

        private void Update()
        {
            var keyboard = Keyboard.current; var mouse = Mouse.current;
            if (keyboard == null || ship == null) return;
            float heading = (float)ship.State.Heading;
            if (Aboard) yaw += Mathf.DeltaAngle(previousHeading, heading);
            previousHeading = heading;
            if (keyboard.escapeKey.wasPressedThisFrame) SetPaused(!Paused);
            if (Paused) { pending = new CrewInput { yaw = yaw, pitch = pitch }; if (Session != null) Session.SetInput(pending); return; }
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 look = mouse.delta.ReadValue() * Sensitivity;
                yaw += look.x; pitch = Mathf.Clamp(pitch - look.y, -65, 70);
            }
            if (keyboard.tabKey.wasPressedThisFrame) { ExternalView = !ExternalView; Overview = false; }
            if (keyboard.vKey.wasPressedThisFrame) { Overview = !Overview; ExternalView = Overview; }
            var input = new CrewInput {
                horizontal = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0),
                forward = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0),
                yaw = yaw, pitch = pitch, sprint = keyboard.leftShiftKey.isPressed,
                swimUp = keyboard.spaceKey.isPressed,
                jump = keyboard.spaceKey.wasPressedThisFrame, helm = keyboard.eKey.wasPressedThisFrame,
                anchor = keyboard.bKey.wasPressedThisFrame, cargo = keyboard.fKey.wasPressedThisFrame,
                throwItem = mouse != null && mouse.leftButton.wasPressedThisFrame
            };
            if (Session != null && Session.Active) { Session.SetInput(input); return; }
            input.jump |= pending.jump; input.helm |= pending.helm; input.cargo |= pending.cargo;
            input.anchor |= pending.anchor; input.throwItem |= pending.throwItem; pending = input;
            if (keyboard.rKey.wasPressedThisFrame) ResetPlayerAndShip();
        }

        private void FixedUpdate()
        {
            if (OfflineActor == null || Paused || (Session != null && Session.Active)) return;
            OfflineActor.Step(pending, Time.fixedDeltaTime);
            pending.jump = pending.helm = pending.cargo = pending.anchor = pending.throwItem = false;
        }

        public void ApplyNetworkPose(CrewPose pose)
        {
            networkAtHelm = pose.atHelm; networkClimbing = pose.climbing; networkAboard = pose.aboard;
            networkGrounded = pose.grounded;
            Transform parent = pose.aboard ? ship.transform : null;
            if (transform.parent != parent) transform.SetParent(parent, true);
            if (pose.aboard) transform.localPosition = pose.position; else transform.position = pose.position;
        }

        public void SetNetworkMode(bool enabled)
        {
            if (OfflineActor != null) OfflineActor.gameObject.SetActive(!enabled);
            if (!enabled) { transform.SetParent(null, true); networkAtHelm = networkClimbing = false; }
        }

        public void LookAtPoint(Vector3 worldTarget)
        {
            Vector3 direction = worldTarget - (WorldPosition + Vector3.up * 1.65f);
            yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
        }

        private void LateUpdate()
        {
            if (ship == null || OfflineActor == null) return;
            Vector3 feet = WorldPosition;
            if (Overview)
            {
                view.transform.position = ship.transform.position + Quaternion.Euler(0, yaw, 0) * new Vector3(17, 12, -24);
                view.transform.LookAt(ship.transform.position + Vector3.up * 3);
            }
            else if (ExternalView)
            {
                Vector3 target = feet + Vector3.up * 1.2f;
                Vector3 offset = Quaternion.Euler(pitch, yaw, 0) * new Vector3(0.75f, 1.1f, -4.5f);
                float length = offset.magnitude;
                if (Physics.SphereCast(target, 0.18f, offset.normalized, out RaycastHit hit, length, ~(1 << 2), QueryTriggerInteraction.Ignore)) length = Mathf.Max(0.25f, hit.distance - 0.1f);
                view.transform.position = target + offset.normalized * length; view.transform.LookAt(target + Quaternion.Euler(0, yaw, 0) * Vector3.forward * 1.4f);
            }
            else
            {
                view.transform.position = feet + Vector3.up * 1.65f;
                float roll = Aboard ? Mathf.DeltaAngle(0, ship.transform.eulerAngles.z) * CameraRock : 0;
                view.transform.rotation = Quaternion.Euler(pitch, yaw, 0) * Quaternion.AngleAxis(roll, Vector3.forward);
            }
            Shader.SetGlobalFloat("_VoyageTime", (float)ship.State.Clock); Shader.SetGlobalFloat("_SeaStrength", ship.SeaStrength);
        }

        public void SetPaused(bool paused)
        {
            if (Paused == paused) return;
            Paused = paused; pending = new CrewInput { yaw = yaw, pitch = pitch };
            ship.Paused = Session != null && Session.Active ? !Session.IsHost : paused;
            if (Session == null || !Session.Active) ship.Steering = ship.SailChange = 0;
            LockCursor(!paused);
        }

        public void ResetPlayerAndShip()
        {
            ship.ResetVoyage(); yaw = 0; pitch = 8;
            OfflineActor.Respawn(new Vector3(1.5f, 2.2f, -4.6f));
            if (Session != null) Session.ResetCargo();
        }
        private void OnApplicationFocus(bool focus) { if (!focus && ship != null) SetPaused(true); }
        private void OnDisable() { LockCursor(false); }
        private void OnDestroy() { if (OfflineActor != null) Destroy(OfflineActor.gameObject); }
        private static void LockCursor(bool locked)
        { Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !locked; }
    }
}
