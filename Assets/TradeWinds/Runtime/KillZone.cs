using UnityEngine;

namespace TradeWinds
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class KillZone : MonoBehaviour
    {
        private void Awake() { GetComponent<BoxCollider>().isTrigger = true; }
        private void OnTriggerEnter(Collider other)
        {
            var actor = other.GetComponent<ShipActor>();
            if (actor != null) actor.Rescue();
            var cargo = other.attachedRigidbody != null ? other.attachedRigidbody.GetComponent<PickableItem>() : null;
            if (cargo != null && !cargo.IsReplica) cargo.ResetItem();
        }
    }
}
