using UnityEngine;

namespace TradeWinds
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WaterZone : MonoBehaviour
    {
        public const float Surface = 0;

        private void Awake() { GetComponent<BoxCollider>().isTrigger = true; }
        private void OnTriggerEnter(Collider other) { Enter(other); }
        private void OnTriggerStay(Collider other) { Enter(other); }
        private static void Enter(Collider other)
        {
            var actor = other.GetComponent<ShipActor>();
            if (actor != null && actor.transform.position.y < -0.15f && !actor.Climbing && !actor.Grounded)
                actor.SetSwimming(true);
        }
        private void OnTriggerExit(Collider other)
        {
            var actor = other.GetComponent<ShipActor>();
            if (actor != null) actor.SetSwimming(false);
        }
    }
}
