using UnityEngine;

namespace TradeWinds
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class CargoHoldZone : MonoBehaviour
    {
        [SerializeField] private ShipController ship;
        private void Awake() { Configure(ship != null ? ship : GetComponentInParent<ShipController>()); }
        public void Configure(ShipController owner)
        { ship = owner; GetComponent<BoxCollider>().isTrigger = true; gameObject.layer = 2; }
        private void OnTriggerStay(Collider other)
        {
            var item = other.attachedRigidbody != null ? other.attachedRigidbody.GetComponent<PickableItem>() : null;
            if (ship != null && item != null && !item.isCarried) item.TrySecure(ship);
        }
    }
}
