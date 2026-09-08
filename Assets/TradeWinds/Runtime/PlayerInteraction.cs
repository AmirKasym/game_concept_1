using UnityEngine;

namespace TradeWinds
{
    public sealed class PlayerInteraction : MonoBehaviour
    {
        [SerializeField] private float range = 2.5f;
        [SerializeField] private float throwImpulse = 65;
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private ShipActor actor;
        public Transform Eye { get; private set; }
        public Transform HoldPoint { get; private set; }
        public PickableItem HeldItem { get; private set; }
        public ShipActor Actor { get { return actor; } }
        public float SpeedMultiplier { get { return HeldItem == null ? 1 : Mathf.Clamp(1 - HeldItem.Weight * 0.02f, 0.25f, 1); } }

        public void Initialize(ShipActor owner)
        {
            actor = owner;
            Eye = new GameObject("Interaction eye").transform; Eye.SetParent(transform, false); Eye.localPosition = Vector3.up * 1.65f;
            HoldPoint = new GameObject("HoldPoint").transform; HoldPoint.SetParent(Eye, false); HoldPoint.localPosition = new Vector3(0, -0.3f, 1.4f);
        }

        public void SetAim(float yaw, float pitch) { Eye.rotation = Quaternion.Euler(pitch, yaw, 0); }

        public Component FindTarget()
        {
            Physics.SyncTransforms();
            int count = Physics.RaycastNonAlloc(Eye.position, Eye.forward, hits, range, ~(1 << 2), QueryTriggerInteraction.Collide);
            float nearest = range + 1; Component target = null;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.distance >= nearest) continue;
                var item = hit.collider.GetComponentInParent<PickableItem>();
                var ladder = hit.collider.GetComponentInParent<LadderInteraction>();
                if (hit.collider.isTrigger && ladder == null) continue;
                nearest = hit.distance; target = item != null ? (Component)item : ladder;
            }
            return target;
        }

        public bool TryInteract()
        {
            if (HeldItem != null) { Release(true); return true; }
            Component target = FindTarget();
            if (target is PickableItem item)
            {
                if (item.TryPickUp(this)) HeldItem = item;
                return true;
            }
            if (target is LadderInteraction ladder) { ladder.TryBegin(actor); return true; }
            return false;
        }

        public void Release(bool throwForward)
        {
            if (HeldItem == null) return;
            var item = HeldItem; HeldItem = null;
            Vector3 inherited = actor.Velocity;
            if (actor.Platform != null) inherited += actor.Platform.GetPointVelocity(item.transform.position);
            item.Release(inherited, throwForward ? (Eye.forward + Vector3.up * 0.15f).normalized * throwImpulse : Vector3.zero);
        }
    }
}
