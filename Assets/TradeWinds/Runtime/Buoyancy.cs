using UnityEngine;

namespace TradeWinds
{
    [RequireComponent(typeof(PickableItem))]
    public sealed class Buoyancy : MonoBehaviour
    {
        [SerializeField] private float liftPerMetre = 28;
        [SerializeField] private float waterDamping = 2.5f;
        [SerializeField] private Vector3 current = new Vector3(0.25f, 0, 0.12f);
        private PickableItem item;
        private float dryDamping, dryAngularDamping;

        private void Start()
        {
            item = GetComponent<PickableItem>();
            dryDamping = item.Body.linearDamping; dryAngularDamping = item.Body.angularDamping;
        }
        private void FixedUpdate()
        {
            if (item == null || item.IsReplica) return;
            var body = item.Body;
            bool wet = item.State != CargoState.Secured && !item.isCarried && body.position.y < WaterZone.Surface;
            item.SetFloating(wet);
            body.linearDamping = wet ? waterDamping : dryDamping;
            body.angularDamping = wet ? 2 : dryAngularDamping;
            if (!wet || body.isKinematic) return;
            float depth = Mathf.Clamp(WaterZone.Surface - body.position.y, 0, 2);
            body.AddForce(Vector3.up * (body.mass * liftPerMetre * depth), ForceMode.Force);
            Vector3 horizontal = new Vector3(body.linearVelocity.x, 0, body.linearVelocity.z);
            body.AddForce((current - horizontal) * body.mass, ForceMode.Force);
        }
        private void OnDisable()
        {
            if (item == null || item.Body == null) return;
            item.Body.linearDamping = dryDamping; item.Body.angularDamping = dryAngularDamping;
        }
    }
}
