using System;
using System.Collections;
using UnityEngine;

namespace TradeWinds
{
    public enum CargoState { Loose, Carried, Secured }
    [Serializable]
    public struct CargoPose
    {
        public int id;
        public Vector3 position;
        public Quaternion rotation;
        public CargoState state;
        public long carrier;
    }

    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class PickableItem : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float weight = 12;
        [SerializeField] private string itemName = "Ящик";
        public float Weight { get { return weight; } }
        public string ItemName { get { return itemName; } }
        public bool isCarried { get { return State == CargoState.Carried; } }
        public CargoState State { get; private set; }
        public PlayerInteraction Carrier { get; private set; }
        public Rigidbody Body { get; private set; }
        public bool IsReplica { get; private set; }
        public int Id { get; private set; }
        private ShipController homeShip;
        private Vector3 initialLocal;
        private Collider ownCollider;
        private float secureAfter;
        private Collider[] shipColliders;
        private Vector3 replicaFromPosition, replicaTargetPosition;
        private Quaternion replicaFromRotation, replicaTargetRotation;
        private float replicaReceived;
        private bool hasReplicaTarget;

        private void Awake()
        {
            Body = GetComponent<Rigidbody>(); ownCollider = GetComponent<Collider>();
            weight = Mathf.Max(0.1f, weight); Body.mass = weight;
        }

        public void Configure(int id, ShipController ship, string title, float mass)
        {
            Id = id; homeShip = ship; itemName = title; weight = Mathf.Max(0.1f, mass);
            Body = GetComponent<Rigidbody>(); ownCollider = GetComponent<Collider>();
            Body.mass = weight; Body.interpolation = RigidbodyInterpolation.Interpolate;
            shipColliders = ship.GetComponentsInChildren<Collider>();
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            initialLocal = ship.transform.InverseTransformPoint(transform.position);
            ResetItem();
        }

        public bool TryPickUp(PlayerInteraction interaction)
        {
            if (IsReplica || isCarried || interaction == null) return false;
            if (Vector3.Distance(interaction.Eye.position, ownCollider.ClosestPoint(interaction.Eye.position)) > 2.5f) return false;
            Carrier = interaction; State = CargoState.Carried;
            transform.SetParent(null, true);
            Body.isKinematic = false; Body.useGravity = false;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            IgnoreShipCollision(false);
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero;
            Body.constraints = RigidbodyConstraints.FreezeRotation;
            Physics.IgnoreCollision(ownCollider, interaction.Actor.Controller, true);
            return true;
        }

        public void Release(Vector3 inheritedVelocity, Vector3 impulse)
        {
            if (IsReplica || !isCarried) return;
            var previous = Carrier; Carrier = null; State = CargoState.Loose;
            transform.SetParent(null, true); Body.isKinematic = false; Body.useGravity = true;
            Body.constraints = RigidbodyConstraints.None;
            Body.linearVelocity = inheritedVelocity;
            Body.AddForce(impulse, ForceMode.Impulse);
            secureAfter = Time.time + 0.45f; // A throw must be able to leave the hold before it can be secured again.
            if (previous != null) StartCoroutine(RestoreCollision(previous.Actor.Controller));
        }

        private IEnumerator RestoreCollision(Collider previous)
        {
            yield return new WaitForSeconds(0.3f);
            if (previous != null && ownCollider != null && (Carrier == null || Carrier.Actor.Controller != previous))
                Physics.IgnoreCollision(ownCollider, previous, false);
        }

        public bool TrySecure(ShipController ship)
        {
            if (IsReplica || isCarried || State == CargoState.Secured || Time.time < secureAfter) return false;
            if ((Body.linearVelocity - ship.GetPointVelocity(transform.position)).sqrMagnitude > 2.25f) return false;
            Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero;
            Body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            Body.isKinematic = true; Body.useGravity = false;
            Body.interpolation = RigidbodyInterpolation.None;
            transform.SetParent(ship.transform, true); State = CargoState.Secured;
            // A secured kinematic crate must not exert infinite-mass contact forces on its own dynamic ship.
            IgnoreShipCollision(true);
            return true;
        }

        private void IgnoreShipCollision(bool ignore)
        {
            if(shipColliders==null || homeShip==null) return;
            var shipBody=homeShip.GetComponent<Rigidbody>();
            foreach(var collider in shipColliders)
                if(collider!=null && collider!=ownCollider && !collider.isTrigger && collider.attachedRigidbody==shipBody)
                    Physics.IgnoreCollision(ownCollider,collider,ignore);
        }

        private void FixedUpdate()
        {
            if (Body == null) return;
            if (IsReplica)
            {
                if(hasReplicaTarget && State!=CargoState.Secured)
                {
                    float t=Mathf.Clamp01((Time.unscaledTime-replicaReceived)/.05f);
                    Body.MovePosition(Vector3.Lerp(replicaFromPosition,replicaTargetPosition,t));
                    Body.MoveRotation(Quaternion.Slerp(replicaFromRotation,replicaTargetRotation,t));
                }
                return;
            }
            if (Carrier != null)
            {
                Vector3 inherited = Carrier.Actor.Velocity;
                if (Carrier.Actor.Platform != null) inherited += Carrier.Actor.Platform.GetPointVelocity(Body.position);
                Vector3 desired = inherited + Vector3.ClampMagnitude((Carrier.HoldPoint.position - Body.position) * 12, 8);
                Body.AddForce(Vector3.ClampMagnitude((desired - Body.linearVelocity) * 12, 60), ForceMode.Acceleration);
                Body.MoveRotation(Quaternion.Slerp(Body.rotation, Carrier.HoldPoint.rotation, 0.25f));
            }
            else if (homeShip != null && transform.position.y < -15) ResetItem();
        }

        public void SetReplica(bool replica)
        {
            IsReplica = replica; Carrier = null;
            hasReplicaTarget = false;
            Body.collisionDetectionMode = replica || State == CargoState.Secured ? CollisionDetectionMode.Discrete : CollisionDetectionMode.ContinuousDynamic;
            Body.isKinematic = replica || State == CargoState.Secured;
            Body.useGravity = !Body.isKinematic;
        }

        public CargoPose Capture()
        {
            bool secured = State == CargoState.Secured;
            return new CargoPose { id = Id, position = secured ? transform.localPosition : transform.position,
                rotation = secured ? transform.localRotation : transform.rotation, state = State,
                carrier = Carrier == null ? -1 : (long)Carrier.Actor.OwnerId };
        }

        public void Apply(CargoPose pose)
        {
            if (!IsReplica) return;
            bool changed = State != pose.state;
            State = pose.state;
            Transform parent = State == CargoState.Secured ? homeShip.transform : null;
            if (transform.parent != parent) transform.SetParent(parent, true);
            Body.interpolation=parent!=null ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            if (parent != null) { transform.localPosition = pose.position; transform.localRotation = pose.rotation; hasReplicaTarget=false; }
            else
            {
                if(!hasReplicaTarget || changed || (Body.position-pose.position).sqrMagnitude>100)
                { Body.position=pose.position; Body.rotation=pose.rotation; }
                replicaFromPosition=Body.position; replicaFromRotation=Body.rotation;
                replicaTargetPosition=pose.position; replicaTargetRotation=pose.rotation;
                replicaReceived=Time.unscaledTime; hasReplicaTarget=true;
            }
        }

        public void ResetItem()
        {
            if (homeShip == null) return;
            if (Carrier != null) Carrier.Release(false);
            Carrier = null; State = CargoState.Loose;
            transform.SetParent(null, true);
            Body.isKinematic = false; Body.useGravity = true; Body.constraints = RigidbodyConstraints.None;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = IsReplica ? CollisionDetectionMode.Discrete : CollisionDetectionMode.ContinuousDynamic;
            Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero;
            Body.position = homeShip.transform.TransformPoint(initialLocal); Body.rotation = homeShip.transform.rotation;
            IgnoreShipCollision(false);
            secureAfter = Time.time + 0.2f;
            if (IsReplica) Body.isKinematic = true;
        }
    }
}

