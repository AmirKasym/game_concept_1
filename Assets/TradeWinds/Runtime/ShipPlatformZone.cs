using UnityEngine;

namespace TradeWinds
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ShipPlatformZone : MonoBehaviour
    {
        [SerializeField] private ShipController ship;
        private void Awake() { Configure(ship != null ? ship : GetComponentInParent<ShipController>()); }
        public void Configure(ShipController owner)
        {
            ship = owner;
            GetComponent<BoxCollider>().isTrigger = true;
            gameObject.layer = 2; // Zones must not occlude the interaction ray.
        }
        private void OnTriggerEnter(Collider other) { Enter(other); }
        private void OnTriggerStay(Collider other) { Enter(other); }
        private void Enter(Collider other)
        {
            var actor = other.GetComponent<ShipActor>();
            if (ship != null && actor != null && !actor.Climbing && actor.Platform == null) actor.Attach(ship);
        }
        private void OnTriggerExit(Collider other)
        {
            var actor = other.GetComponent<ShipActor>();
            if (actor != null && !actor.Climbing && actor.Platform == ship) actor.Detach();
        }
    }
}
