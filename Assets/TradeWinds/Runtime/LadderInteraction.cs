using UnityEngine;

namespace TradeWinds
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class LadderInteraction : MonoBehaviour
    {
        [SerializeField] private ShipController ship;
        [SerializeField] private float bottomY = 0.5f, topY = 2.2f, climbSpeed = 1.8f;
        [SerializeField] private Vector3 topExit = new Vector3(0, 2.2f, 1.8f);
        [SerializeField] private Vector3 bottomExit = new Vector3(0, 0.55f, -1.5f);
        public ShipController Ship { get { return ship; } }
        public bool IsRescueRope { get; private set; }
        public void ConfigureRescue(ShipController owner)
        {
            Configure(owner); IsRescueRope = true; bottomY = -1.8f; topY = 3.5f;
            topExit = new Vector3(-1.5f, 3.5f, 0); bottomExit = new Vector3(0.7f, -1, 0);
        }
        private void Awake() { Configure(ship != null ? ship : GetComponentInParent<ShipController>()); }
        public void Configure(ShipController owner) { ship = owner; GetComponent<BoxCollider>().isTrigger = true; }

        public bool TryBegin(ShipActor actor)
        {
            if (actor.Climbing || actor.Interaction.HeldItem != null) return false;
            Vector3 local = transform.InverseTransformPoint(actor.transform.position);
            if (Vector2.Distance(new Vector2(local.x, local.z), Vector2.zero) > 2.5f) return false;
            if (local.y < bottomY - 1 || local.y > topY + 1) return false;
            actor.BeginClimb(this);
            actor.transform.localPosition = new Vector3(0, Mathf.Clamp(local.y, bottomY, topY), 0);
            return true;
        }

        public void StepClimb(ShipActor actor, float vertical, float dt, bool cancel)
        {
            float y = Mathf.Clamp(actor.transform.localPosition.y + Mathf.Clamp(vertical, -1, 1) * climbSpeed * dt, bottomY, topY);
            actor.transform.localPosition = new Vector3(0, y, 0);
            if ((vertical > 0 && y >= topY) || (cancel && y > (topY + bottomY) * 0.5f))
                actor.EndClimb(transform.TransformPoint(topExit), ship);
            else if ((vertical < 0 && y <= bottomY) || cancel)
                actor.EndClimb(transform.TransformPoint(bottomExit), null);
        }
    }
}
