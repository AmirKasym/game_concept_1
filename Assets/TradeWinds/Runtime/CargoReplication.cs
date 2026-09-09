using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace TradeWinds
{
    public struct CargoAttachment : INetworkSerializable, System.IEquatable<CargoAttachment>
    {
        public int id;
        public CargoState state;
        public long carrier;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref id); serializer.SerializeValue(ref state); serializer.SerializeValue(ref carrier);
            serializer.SerializeValue(ref localPosition); serializer.SerializeValue(ref localRotation);
        }
        public bool Equals(CargoAttachment other)
        {
            return id == other.id && state == other.state && carrier == other.carrier
                && localPosition == other.localPosition && localRotation == other.localRotation;
        }
    }

    [RequireComponent(typeof(NetworkObject), typeof(NetworkTransform), typeof(NetworkRigidbody))]
    public sealed class CargoReplication : NetworkBehaviour
    {
        public NetworkVariable<CargoAttachment> Attachment = new NetworkVariable<CargoAttachment>();
        private PickableItem item;
        private NetworkTransform motion;
        private CoopSession session;

        public void PrepareSpawn()
        {
            item = GetComponent<PickableItem>(); motion = GetComponent<NetworkTransform>();
            Attachment.Value = Capture();
            motion.InLocalSpace = item.State == CargoState.Secured;
        }
        public override void OnNetworkSpawn()
        {
            item = GetComponent<PickableItem>(); motion = GetComponent<NetworkTransform>();
            session = FindFirstObjectByType<CoopSession>();
            if (!IsServer) session.RegisterCargo(Attachment.Value.id, item);
            item.SetReplica(!IsServer);
            Attachment.OnValueChanged += Changed;
            item.Impact += Impact;
            if (!IsServer) Apply(Attachment.Value);
        }
        private CargoAttachment Capture()
        {
            var pose = item.Capture();
            return new CargoAttachment { id = pose.id, state = pose.state, carrier = pose.carrier,
                localPosition = pose.state == CargoState.Secured ? pose.position : Vector3.zero,
                localRotation = pose.state == CargoState.Secured ? pose.rotation : Quaternion.identity };
        }
        private void LateUpdate()
        {
            if (!IsSpawned) return;
            if (IsServer)
            {
                var value = Capture();
                bool changedSpace = motion.InLocalSpace != (value.state == CargoState.Secured);
                Attachment.Value = value;
                motion.InLocalSpace = value.state == CargoState.Secured;
                if (changedSpace) motion.Teleport(motion.InLocalSpace ? transform.localPosition : transform.position,
                    motion.InLocalSpace ? transform.localRotation : transform.rotation, transform.localScale);
            }
            else if (Attachment.Value.state == CargoState.Secured)
            {
                // Exact ship-relative anchoring avoids interpolation drift while the hull rocks.
                transform.localPosition = Attachment.Value.localPosition;
                transform.localRotation = Attachment.Value.localRotation;
            }
        }
        private void Changed(CargoAttachment before, CargoAttachment after) { if (!IsServer) Apply(after); }
        private void Apply(CargoAttachment value)
        {
            item.ApplyNetworkAttachment(value.state);
            motion.InLocalSpace = value.state == CargoState.Secured;
            if (motion.InLocalSpace) { transform.localPosition = value.localPosition; transform.localRotation = value.localRotation; }
        }
        private void Impact(Vector3 position, float strength) { if (IsServer) ImpactRpc(position, strength); }
        [Rpc(SendTo.NotServer)]
        private void ImpactRpc(Vector3 position, float strength)
        {
            if (session != null) session.GetComponent<VoyageFeedback>().PlayImpact(position, strength);
        }
        public override void OnNetworkDespawn()
        {
            Attachment.OnValueChanged -= Changed;
            if (item != null) item.Impact -= Impact;
        }
    }
}
