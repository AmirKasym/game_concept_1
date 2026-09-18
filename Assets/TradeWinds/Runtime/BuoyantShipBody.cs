using System;
using UnityEngine;

namespace TradeWinds
{
    [Serializable]
    public struct ShipPhysicsPose
    {
        public Vector3 position, velocity, angularVelocity;
        public Quaternion rotation;
    }

    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    public sealed class BuoyantShipBody : MonoBehaviour
    {
        [Header("Hull and displaced volume")]
        [SerializeField, Range(500, 50000), Tooltip("Ship mass in kg, excluding separate cargo rigidbodies.")] private float mass = 10000;
        [SerializeField, Tooltip("Six or more sample points distributed across the waterline. Empty uses the prototype's six hull points.")] private Transform[] floaters;
        [SerializeField, Range(1, 100), Tooltip("Total enclosed displacement volume in cubic metres, divided between all floaters.")] private float displacementVolume = 20;
        [SerializeField, Range(900, 1200), Tooltip("Water density in kg per cubic metre.")] private float waterDensity = 1025;
        [SerializeField, Range(.2f, 3), Tooltip("Depth required to submerge the full volume assigned to each point.")] private float fullSubmersionDepth = 1.2f;
        [SerializeField, Range(.5f, 2), Tooltip("Arcade multiplier on Archimedes buoyancy. One is rho * g * displaced volume.")] private float buoyancyForce = 1;
        [SerializeField, Tooltip("Hull-local centre of mass; keep below the deck.")] private Vector3 centerOfMass = new Vector3(0, -.4f, 0);
        [Header("Hydrodynamics")]
        [SerializeField, Range(0, 10), Tooltip("Vertical water-relative damping rate per second.")] private float heaveDrag = 2.5f;
        [SerializeField, Range(0, 5), Tooltip("Forward linear resistance per second.")] private float forwardDrag = .12f;
        [SerializeField, Range(0, 1), Tooltip("Forward quadratic drag; acceleration is coefficient * speed squared.")] private float forwardQuadraticDrag = .035f;
        [SerializeField, Range(0, 10), Tooltip("Sideways water resistance per second; normally higher than forward drag.")] private float lateralDrag = 2.5f;
        [SerializeField, Range(0, 10), Tooltip("Angular resistance per second, applied independently from linear drag.")] private float angularDrag = 1.2f;
        [SerializeField, Range(0, 10), Tooltip("Additional horizontal resistance with the anchor down.")] private float anchorDrag = 3;
        [Header("Propulsion and steering")]
        [SerializeField, Range(0, 60000), Tooltip("Maximum forward sail force in Newtons; mass controls acceleration.")] private float propulsionForce = 16000;
        [SerializeField, Range(.1f, 5), Tooltip("Seconds to blend engine/sail force commands.")] private float thrustResponse = .6f;
        [SerializeField, Range(3, 60), Tooltip("Minimum steering radius in metres. There is no turn on the spot.")] private float minimumTurnRadius = 12;
        [SerializeField, Range(0, 4), Tooltip("Extra radius per squared metre/second of speed.")] private float speedRadiusGrowth = .45f;
        [SerializeField, Range(.1f, 10), Tooltip("Yaw-rate response; body inertia still governs contacts.")] private float steeringResponse = 2.5f;
        [Header("Stability and impacts")]
        [SerializeField, Range(0, 15), Tooltip("Maximum inward banking angle in degrees.")] private float leaningStrength = 6;
        [SerializeField, Range(0, 20), Tooltip("Uprighting angular acceleration per radian. Acts only while immersed.")] private float stabilization = 5;
        [SerializeField, Range(0, 10), Tooltip("Roll/pitch damping, independent from yaw steering.")] private float stabilizationDamping = 2.5f;
        [SerializeField, Range(.1f, 3), Tooltip("Maximum angular velocity in radians/second.")] private float maximumAngularSpeed = 1.2f;
        [SerializeField, Range(0, .3f), Tooltip("Small hull rebound. PhysX generates mass-dependent impulses; they are not applied twice.")] private float impactRestitution = .06f;
        [SerializeField, Range(6, 20), Tooltip("Per-body position solver iterations.")] private int solverIterations = 12;
        [SerializeField, Range(1, 10), Tooltip("Per-body velocity solver iterations.")] private int solverVelocityIterations = 4;
        public Rigidbody Body { get; private set; }
        public float Immersion { get; private set; }
        public Vector3 LastImpactDeltaVelocity { get; private set; }
        public event Action<Vector3> Impact;
        private Vector3[] localPoints;
        private PhysicsMaterial hullMaterial;
        private float thrust;
        private bool replica, suspended;
        private Vector3 resumeVelocity, resumeAngular;
        private ShipPhysicsPose replicaFrom, replicaTo;
        private float receivedAt;
        private bool hasReplicaPose;

        private void Awake() { Configure(); }
        public void Configure()
        {
            Body = GetComponent<Rigidbody>();
            Body.mass = mass; Body.centerOfMass = centerOfMass;
            Body.useGravity = true; Body.isKinematic = false;
            Body.linearDamping = 0; Body.angularDamping = 0;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.solverIterations = solverIterations; Body.solverVelocityIterations = solverVelocityIterations;
            Body.maxAngularVelocity = maximumAngularSpeed; Body.maxDepenetrationVelocity = 2;
            if (floaters != null && floaters.Length > 0)
            {
                if (floaters.Length < 4) throw new InvalidOperationException("Use at least four buoyancy points.");
                localPoints = new Vector3[floaters.Length];
                for (int i = 0; i < floaters.Length; i++)
                {
                    if (floaters[i] == null) throw new InvalidOperationException("Missing floater transform.");
                    localPoints[i] = transform.InverseTransformPoint(floaters[i].position);
                }
            }
            else localPoints = new[] { new Vector3(-2.3f,-.6f,-5.8f), new Vector3(2.3f,-.6f,-5.8f),
                new Vector3(-2.3f,-.6f,0), new Vector3(2.3f,-.6f,0), new Vector3(-2.3f,-.6f,5.8f), new Vector3(2.3f,-.6f,5.8f) };
            if (hullMaterial == null) hullMaterial = new PhysicsMaterial("Low rebound hull") {
                staticFriction = .45f, dynamicFriction = .3f, bounciness = impactRestitution,
                bounceCombine = PhysicsMaterialCombine.Average, frictionCombine = PhysicsMaterialCombine.Average };
            foreach (var collider in GetComponentsInChildren<Collider>())
                if (!collider.isTrigger && collider.attachedRigidbody == Body) collider.sharedMaterial = hullMaterial;
        }
        public Vector3 LocalToPhysics(Vector3 local) => Body.position + Body.rotation * local;
        public Vector3 PhysicsToLocal(Vector3 world) => Quaternion.Inverse(Body.rotation) * (world - Body.position);
        public void SetReplica(bool value)
        {
            if (replica == value) return;
            replica = value; suspended = false; hasReplicaPose = false;
            if(value) { Body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative; Body.isKinematic=true; }
            else { Body.isKinematic=false; Body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic; }
        }
        public void Receive(ShipPhysicsPose pose)
        {
            if (!Finite(pose.position) || !Finite(pose.velocity) || !Finite(pose.angularVelocity) ||
                !float.IsFinite(pose.rotation.x) || !float.IsFinite(pose.rotation.y) || !float.IsFinite(pose.rotation.z) || !float.IsFinite(pose.rotation.w))
                throw new ArgumentException("Non-finite ship pose");
            float norm=pose.rotation.x*pose.rotation.x+pose.rotation.y*pose.rotation.y+pose.rotation.z*pose.rotation.z+pose.rotation.w*pose.rotation.w;
            if(norm<.5f || norm>1.5f) throw new ArgumentException("Invalid ship rotation");
            pose.rotation=pose.rotation.normalized;
            if (!hasReplicaPose || (Body.position - pose.position).sqrMagnitude > 400)
            { Body.position = pose.position; Body.rotation = pose.rotation; replicaFrom = pose; }
            else replicaFrom = new ShipPhysicsPose { position = Body.position, rotation = Body.rotation };
            replicaTo = pose; receivedAt = Time.unscaledTime; hasReplicaPose = true;
        }
        public ShipPhysicsPose Capture() => new ShipPhysicsPose { position=Body.position, rotation=Body.rotation,
            velocity=replica ? replicaTo.velocity : Body.linearVelocity, angularVelocity=replica ? replicaTo.angularVelocity : Body.angularVelocity };
        public Vector3 PointVelocity(Vector3 world) => replica ? replicaTo.velocity + Vector3.Cross(replicaTo.angularVelocity, world - Body.position) : Body.GetPointVelocity(world);
        public void ResetPose()
        {
            thrust=0; resumeVelocity=resumeAngular=Vector3.zero;
            Body.position=Vector3.zero; Body.rotation=Quaternion.identity;
            if (!Body.isKinematic) { Body.linearVelocity=Vector3.zero; Body.angularVelocity=Vector3.zero; }
            hasReplicaPose=false;
        }
        // Called once per physics tick by the authoritative ShipController.
        public void Simulate(float dt, float clock, float seaStrength, float sail, float rudder, bool anchored, bool paused)
        {
            if (replica)
            {
                if (hasReplicaPose) { float t=Mathf.Clamp01((Time.unscaledTime-receivedAt)/.05f);
                    Body.MovePosition(Vector3.Lerp(replicaFrom.position,replicaTo.position,t));
                    Body.MoveRotation(Quaternion.Slerp(replicaFrom.rotation,replicaTo.rotation,t)); }
                return;
            }
            if (paused != suspended)
            {
                if (paused) { resumeVelocity=Body.linearVelocity; resumeAngular=Body.angularVelocity;
                    Body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative; Body.isKinematic=true; }
                else { Body.isKinematic=false; Body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                    Body.linearVelocity=resumeVelocity; Body.angularVelocity=resumeAngular; Body.WakeUp(); }
                suspended=paused;
            }
            if (paused) return;
            float gravity=Physics.gravity.magnitude, immersion=0;
            float volume=displacementVolume/localPoints.Length;
            foreach (var local in localPoints)
            {
                Vector3 point=LocalToPhysics(local);
                float water=(float)ShipSimulation.WaveHeight(point.x,point.z,clock,seaStrength);
                float wet=Mathf.Clamp01((water-point.y)/fullSubmersionDepth); immersion+=wet;
                if (wet<=0) continue;
                float waterSpeed=seaStrength*(.288f*Mathf.Cos(point.x*.075f+point.z*.12f+clock*.9f)
                    +.234f*Mathf.Cos(-point.x*.16f+point.z*.05f+clock*1.3f));
                float lift=waterDensity*gravity*volume*wet*buoyancyForce;
                float damping=(Body.GetPointVelocity(point).y-waterSpeed)*heaveDrag*mass/localPoints.Length*wet;
                Body.AddForceAtPosition(Vector3.up*(lift-damping),point,ForceMode.Force);
            }
            Immersion=immersion/localPoints.Length;
            float wetness=Mathf.Clamp01(Immersion*2);
            Vector3 localVelocity=Quaternion.Inverse(Body.rotation)*Body.linearVelocity;
            float forwardResistance=forwardDrag*localVelocity.z+forwardQuadraticDrag*localVelocity.z*Mathf.Abs(localVelocity.z);
            Vector3 resistance=new Vector3(localVelocity.x*lateralDrag,0,forwardResistance);
            if (anchored) resistance+=new Vector3(localVelocity.x,0,localVelocity.z)*anchorDrag;
            Body.AddForce(-(Body.rotation*resistance)*mass*wetness,ForceMode.Force);
            thrust=Mathf.MoveTowards(thrust,anchored ? 0 : Mathf.Clamp01(sail)*propulsionForce,propulsionForce*dt/Mathf.Max(.1f,thrustResponse));
            Body.AddForce(Body.rotation*Vector3.forward*thrust*wetness,ForceMode.Force);
            float speed=localVelocity.z;
            float yawRate=rudder*speed/(minimumTurnRadius+speedRadiusGrowth*speed*speed);
            Vector3 localOmega=Quaternion.Inverse(Body.rotation)*Body.angularVelocity;
            Body.AddTorque(-(Body.rotation*localOmega)*angularDrag*wetness,ForceMode.Acceleration);
            Body.AddTorque(Vector3.up*(yawRate-Body.angularVelocity.y)*steeringResponse*wetness,ForceMode.Acceleration);
            float lean=-Mathf.Clamp(yawRate*speed*.15f,-1,1)*leaningStrength;
            Quaternion desired=Quaternion.Euler(0,Body.rotation.eulerAngles.y,lean);
            Vector3 error=Vector3.Cross(Body.rotation*Vector3.up,desired*Vector3.up);
            // Recover from large heel angles without directly overriding rotation.
            if (Vector3.Dot(Body.rotation*Vector3.up,Vector3.up)<0) error+=Body.rotation*Vector3.forward*.5f;
            Vector3 dampingOmega=Body.angularVelocity-Vector3.up*Body.angularVelocity.y;
            Body.AddTorque((error*stabilization-dampingOmega*stabilizationDamping)*wetness,ForceMode.Acceleration);
        }
        private void OnCollisionEnter(Collision collision)
        {
            if (replica || suspended) return;
            Vector3 delta=Vector3.ClampMagnitude(collision.impulse/Body.mass,8);
            if(delta.sqrMagnitude<.0025f) return;
            LastImpactDeltaVelocity=delta;
            Impact?.Invoke(LastImpactDeltaVelocity);
        }
        private static bool Finite(Vector3 v) => float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
        private void OnDestroy() { if (hullMaterial!=null) Destroy(hullMaterial); }
    }
}
