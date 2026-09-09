using UnityEngine;

namespace TradeWinds
{
    public enum MovementState { WALKING, SWIMMING, CLIMBING }
    [RequireComponent(typeof(CharacterController), typeof(PlayerInteraction))]
    public sealed class ShipActor : MonoBehaviour
    {
        public ShipController Platform { get; private set; }
        public ShipController HomeShip { get; private set; }
        public CharacterController Controller { get; private set; }
        public PlayerInteraction Interaction { get; private set; }
        public LadderInteraction Ladder { get; private set; }
        public bool Climbing { get { return Ladder != null; } }
        public bool AtHelm { get { return HomeShip != null && HomeShip.Helmsman == this; } }
        public bool NearHelm { get { return Platform == HomeShip && Vector3.Distance(transform.position, HomeShip.transform.TransformPoint(DeckPlayer.HelmPosition)) < 2.4f; } }
        public bool Grounded { get { return Controller != null && Controller.isGrounded; } }
        public Vector3 Velocity { get; private set; }
        public ulong OwnerId { get; private set; }
        public MovementState MovementState { get; private set; }
        public bool Swimming { get { return MovementState == MovementState.SWIMMING; } }
        private float verticalSpeed;
        private float jumpQueuedUntil = -1;

        public void Initialize(ShipController ship, ulong ownerId, Vector3 deckPosition)
        {
            HomeShip = ship; OwnerId = ownerId;
            Controller = GetComponent<CharacterController>();
            Controller.height = 1.8f; Controller.radius = 0.28f;
            Controller.center = Vector3.up * 0.9f; Controller.skinWidth = 0.025f;
            Controller.stepOffset = 0.35f; Controller.slopeLimit = 55;
            gameObject.layer = LayerMask.NameToLayer("Player");
            Interaction = GetComponent<PlayerInteraction>(); Interaction.Initialize(this);
            Respawn(deckPosition);
        }

        public void Attach(ShipController ship)
        {
            if (Platform == ship) return;
            Platform = ship;
            transform.SetParent(ship.transform, true);
        }

        public void Detach()
        {
            if (HomeShip != null) HomeShip.ReleaseHelm(this);
            Platform = null;
            transform.SetParent(null, true); // Preserve world pose at the boundary.
        }

        public void Step(CrewInput input, float dt)
        {
            if (transform.position.y < -50) { Rescue(); return; }
            if (!Climbing) SetSwimming(transform.position.y < -0.15f && !Grounded && Platform == null);
            Interaction.SetAim(input.yaw, input.pitch);
            if (input.jump) jumpQueuedUntil = Time.time + 0.12f;
            if (Climbing)
            {
                Ladder.StepClimb(this, input.forward, dt, input.jump);
                return;
            }
            if (input.throwItem) Interaction.Release(true);
            if (input.helm || input.cargo)
            {
                if (AtHelm) HomeShip.ReleaseHelm(this);
                else Interaction.TryInteract();
            }
            if (Climbing) return;
            if (input.jump && AtHelm) HomeShip.ReleaseHelm(this);
            if (AtHelm)
            {
                HomeShip.Steering = input.horizontal; HomeShip.SailChange = input.forward;
                if (input.anchor) HomeShip.ToggleAnchor();
                Velocity = Vector3.zero;
                return;
            }
            Vector3 direction = Quaternion.Euler(0, input.yaw, 0) * Vector3.ClampMagnitude(new Vector3(input.horizontal, 0, input.forward), 1);
            if (Swimming)
            {
                float lift = input.swimUp ? 2 : input.sprint ? -2 : Mathf.Clamp((-0.9f - transform.position.y) * 1.5f, -0.6f, 0.7f);
                Vector3 start = transform.position;
                Controller.Move((direction * 1.8f * Interaction.SpeedMultiplier + Vector3.up * lift) * dt);
                Velocity = (transform.position - start) / dt;
                transform.rotation = Quaternion.Euler(0, input.yaw, 0);
                return;
            }
            float speed = (input.sprint ? 4.2f : 2.7f) * Interaction.SpeedMultiplier;
            if (Grounded && verticalSpeed < 0) verticalSpeed = -2;
            if (Grounded && Time.time < jumpQueuedUntil) { verticalSpeed = 6; jumpQueuedUntil = -1; }
            verticalSpeed -= 16 * dt;
            Vector3 before = transform.position;
            Controller.Move((direction * speed + Vector3.up * verticalSpeed) * dt);
            Velocity = (transform.position - before) / dt;
            transform.rotation = Quaternion.Euler(0, input.yaw, 0);
        }

        public void SetSwimming(bool swimming)
        {
            if (Climbing || Swimming == swimming) return;
            MovementState = swimming ? MovementState.SWIMMING : MovementState.WALKING;
            verticalSpeed = 0; jumpQueuedUntil = -1;
            if (swimming) Detach();
        }

        public void Rescue()
        {
            Interaction.Release(false);
            HomeShip.ReleaseHelm(this);
            Respawn(new Vector3(1.5f, 2.2f, -4.6f));
        }

        public void BeginClimb(LadderInteraction ladder)
        {
            HomeShip.ReleaseHelm(this);
            MovementState = MovementState.CLIMBING;
            Ladder = ladder; Controller.enabled = false; verticalSpeed = 0; Velocity = Vector3.zero;
            if (ladder.Ship != null) Attach(ladder.Ship);
            transform.SetParent(ladder.transform, true);
        }

        public void EndClimb(Vector3 worldExit, ShipController platform)
        {
            Ladder = null;
            MovementState = MovementState.WALKING;
            if (platform != null) { Platform = null; Attach(platform); } else Detach();
            transform.position = worldExit;
            verticalSpeed = 0; Controller.enabled = true;
        }

        public void Respawn(Vector3 deckPosition)
        {
            if (HomeShip == null) return;
            HomeShip.ReleaseHelm(this);
            MovementState = MovementState.WALKING; jumpQueuedUntil = -1;
            Controller.enabled = false; Ladder = null; Platform = null; Attach(HomeShip);
            transform.localPosition = deckPosition; transform.localRotation = Quaternion.identity;
            verticalSpeed = 0; Velocity = Vector3.zero; Controller.enabled = true;
        }

        private void OnDisable()
        {
            if (Interaction != null) Interaction.Release(false);
            if (HomeShip != null) HomeShip.ReleaseHelm(this);
        }
    }
}
