# Complete C# component system — FirstVoyage
Unity 6000.6, URP, Input System and Netcode for GameObjects. These are complete project files, not snippets. Keep each class in its indicated file; do not paste the whole document into one .cs file. The existing project supplies its scenes, art and package configuration. Full setup instructions follow the code.

## Assets/TradeWinds/Runtime/BuoyantShipBody.cs

```csharp
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

```

## Assets/TradeWinds/Runtime/ShipController.cs

```csharp
using UnityEngine;

namespace TradeWinds
{
    [DefaultExecutionOrder(-100)]
    public sealed class ShipController : MonoBehaviour
    {
        public ShipSimulation State { get; private set; }
        [SerializeField, Range(0, 2), Tooltip("Amplitude multiplier shared by visual water and buoyancy sampling.")] private float seaStrength=.6f;
        public float SeaStrength { get => seaStrength; set => seaStrength=Mathf.Clamp(value,0,2); }
        public float Steering { get; set; }
        public float SailChange { get; set; }
        public bool Paused { get; set; }
        public ShipActor Helmsman { get; private set; }
        public BuoyantShipBody PhysicsBody { get; private set; }
        public bool IsReplica { get; private set; }
        private float receivedPhysicsAt;
        public float WaterRenderClock => Mathf.Max(0, (float)State.Clock + (IsReplica
            ? Mathf.Clamp(Time.unscaledTime-receivedPhysicsAt,0,.1f)
            : Paused ? 0 : -Time.fixedDeltaTime+Mathf.Clamp(Time.time-Time.fixedTime,0,Time.fixedDeltaTime)));
        public void ReceivePhysicsPose(ShipPhysicsPose pose) { PhysicsBody.Receive(pose); receivedPhysicsAt=Time.unscaledTime; }
        public string Notice { get; private set; } = "Подойдите к штурвалу и нажмите E.";
        [SerializeField] private Transform wheel;
        [SerializeField] private Transform sail;
        [SerializeField] private Vector3[] islands;
        // Preserved for existing scene serialization. Physical hull contacts now replace overlap rollback.
        [SerializeField] private Transform[] detailedIslandRoots = System.Array.Empty<Transform>();
        private void Awake() { State = new ShipSimulation(); PhysicsBody=GetComponent<BuoyantShipBody>(); }
        public void Initialize(Transform wheelVisual, Transform sailVisual, Vector3[] obstacles)
        { State=new ShipSimulation(); wheel=wheelVisual; sail=sailVisual; islands=obstacles; }
        public void InitializePhysics()
        {
            PhysicsBody=GetComponent<BuoyantShipBody>();
            if (PhysicsBody==null) PhysicsBody=gameObject.AddComponent<BuoyantShipBody>();
            PhysicsBody.Configure();
            PhysicsBody.Impact+=OnImpact;
        }
        private void OnImpact(Vector3 deltaVelocity)
        { if (deltaVelocity.magnitude>.15f) Notice="Касание корпуса. Уменьшите парус и отойдите от препятствия."; }
        public void SetReplica(bool value) { IsReplica=value; if(PhysicsBody!=null) PhysicsBody.SetReplica(value); }
        private void FixedUpdate()
        {
            if (State==null || PhysicsBody==null) return;
            if (!IsReplica && !Paused)
            {
                State.Step(Time.fixedDeltaTime,Steering,SailChange);
                var state=State.Capture(); var body=PhysicsBody.Body;
                state.x=body.position.x; state.z=body.position.z; state.heading=body.rotation.eulerAngles.y;
                state.speed=Vector3.Dot(body.linearVelocity,body.rotation*Vector3.forward);
                State.Restore(state);
                if (new Vector2((float)state.x,(float)state.z).magnitude>695 && !State.Anchored)
                { State.ToggleAnchor(); Notice="Край тестового моря. Развернитесь к островам."; }
            }
            PhysicsBody.Simulate(Time.fixedDeltaTime,(float)State.Clock,SeaStrength,
                (float)(State.Sail*State.WindEfficiency),(float)State.Rudder,State.Anchored,Paused);
            UpdatePresentation();
        }
        public void InstallLegacyBoundaries(Transform world)
        {
            if(islands==null) return;
            foreach(var island in islands)
            {
                var boundary=new GameObject("Legacy island physical boundary").AddComponent<CapsuleCollider>();
                boundary.transform.SetParent(world,false); boundary.transform.position=new Vector3(island.x,0,island.z);
                boundary.radius=island.y; boundary.height=island.y*2+30; boundary.direction=1;
            }
        }
        public bool TryTakeHelm(ShipActor actor)
        {
            if(Helmsman!=null || actor.Interaction.HeldItem!=null || !actor.NearHelm) return false;
            Helmsman=actor; actor.Respawn(DeckPlayer.HelmPosition); return true;
        }
        public void ReleaseHelm(ShipActor actor) { if(Helmsman!=actor) return; Helmsman=null; Steering=SailChange=0; }
        public Vector3 GetPointVelocity(Vector3 point) => PhysicsBody!=null ? PhysicsBody.PointVelocity(point) : Vector3.zero;
        public Vector3 LocalToPhysics(Vector3 local) => PhysicsBody!=null ? PhysicsBody.LocalToPhysics(local) : transform.TransformPoint(local);
        public Vector3 PhysicsToLocal(Vector3 world) => PhysicsBody!=null ? PhysicsBody.PhysicsToLocal(world) : transform.InverseTransformPoint(world);
        public void ToggleAnchor() { State.ToggleAnchor(); Notice=State.Anchored ? "Якорь опущен. Корабль замедляется." : "Якорь поднят. W — поставить парус."; }
        public void ResetVoyage()
        { State.Reset(); Helmsman=null; Steering=SailChange=0; if(PhysicsBody!=null) PhysicsBody.ResetPose(); Notice="Пробный выход начат заново."; }
        public void UpdatePresentation()
        {
            if(wheel!=null) wheel.localRotation=Quaternion.Euler(0,0,(float)-State.Rudder*85);
            if(sail!=null) sail.localScale=new Vector3(1,Mathf.Lerp(.12f,1,(float)State.Sail),1);
        }
        private void OnDestroy() { if(PhysicsBody!=null) PhysicsBody.Impact-=OnImpact; }
    }
}

```

## Assets/TradeWinds/Runtime/ShipActor.cs

```csharp
using UnityEngine;

namespace TradeWinds
{
    [RequireComponent(typeof(CharacterController), typeof(PlayerInteraction))]
    public sealed class ShipActor : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Range(1, 8), Tooltip("Walking speed in metres per second, before cargo weight.")] private float walkSpeed=2.7f;
        [SerializeField, Range(2, 10), Tooltip("Sprint speed in metres per second.")] private float sprintSpeed=4.2f;
        [SerializeField, Range(1, 60), Tooltip("Ground acceleration and braking in metres per second squared.")] private float acceleration=28;
        [SerializeField, Range(0, 15), Tooltip("Air steering acceleration; takeoff momentum is preserved initially.")] private float airAcceleration=3;
        [SerializeField, Range(10, 40), Tooltip("Downward acceleration; does not depend on render frame rate.")] private float gravity=24;
        [SerializeField, Range(.2f, 2), Tooltip("Jump height above stationary support.")] private float jumpHeight=1.05f;
        [SerializeField, Range(0, .25f), Tooltip("Ground-loss grace period, in seconds.")] private float coyoteTime=.09f;
        [Header("Deck contact")]
        [SerializeField, Range(0, 1), Tooltip("Friction coefficient resisting inertia slip; larger values give stronger deck grip.")] private float gripFriction=.35f;
        [SerializeField, Range(0, 6), Tooltip("Small downward speed maintaining contact without parenting the controller.")] private float deckAdhesion=2;
        [SerializeField, Range(0, 1), Tooltip("Fraction of abrupt platform velocity changes transferred to deck slip.")] private float momentumTransfer=.8f;
        [SerializeField, Range(.03f, .3f), Tooltip("Ground probe extension below the feet.")] private float groundProbe=.12f;
        [SerializeField, Tooltip("Solid ground and ship layers. Exclude player/query-only trigger layers.")] private LayerMask groundMask=~(1<<2);
        [Header("Object contacts")]
        [SerializeField, Range(30, 150), Tooltip("Effective player mass for bounded impulses on loose cargo.")] private float characterMass=80;
        [SerializeField, Range(0, 10), Tooltip("Maximum impulse per movement step applied to loose cargo, in Newton seconds.")] private float pushImpulse=3;
        public ShipController Platform { get; private set; }
        public ShipController HomeShip { get; private set; }
        public CharacterController Controller { get; private set; }
        public PlayerInteraction Interaction { get; private set; }
        public LadderInteraction Ladder { get; private set; }
        public bool Climbing => Ladder!=null;
        public bool AtHelm => HomeShip!=null && HomeShip.Helmsman==this;
        public bool NearHelm => Platform==HomeShip && Vector3.Distance(transform.position,HomeShip.LocalToPhysics(DeckPlayer.HelmPosition))<2.4f;
        public bool Grounded => grounded;
        public Vector3 Velocity { get; private set; }
        public ulong OwnerId { get; private set; }
        private readonly RaycastHit[] hits=new RaycastHit[16];
        private Vector3 planarVelocity, slip, localAnchor, previousPointVelocity;
        private Vector3 renderPrevious, renderCurrent;
        private ShipController renderPlatform;
        private float verticalSpeed, jumpQueuedUntil=-1, lastGrounded=-100, ignoreGroundUntil, impulseRemaining;
        private bool grounded;
        public Vector3 RenderPosition
        {
            get
            {
                if(Climbing) return transform.position;
                if(HomeShip!=null && HomeShip.Paused) return Platform!=null ? Platform.transform.TransformPoint(localAnchor) : transform.position;
                float t=Mathf.Clamp01((Time.time-Time.fixedTime)/Time.fixedDeltaTime);
                Vector3 point=Vector3.Lerp(renderPrevious,renderCurrent,t);
                return renderPlatform!=null ? renderPlatform.transform.TransformPoint(point) : point;
            }
        }
        public void Initialize(ShipController ship, ulong ownerId, Vector3 deckPosition)
        {
            HomeShip=ship; OwnerId=ownerId; Controller=GetComponent<CharacterController>();
            Controller.height=1.8f; Controller.radius=.28f; Controller.center=Vector3.up*.9f;
            Controller.skinWidth=.025f; Controller.stepOffset=.22f; Controller.slopeLimit=55; Controller.minMoveDistance=0;
            gameObject.layer=2; Interaction=GetComponent<PlayerInteraction>(); Interaction.Initialize(this); Respawn(deckPosition);
        }
        public void Attach(ShipController ship)
        {
            if(Platform==ship) return;
            transform.SetParent(null,true); Platform=ship;
            localAnchor=ship.PhysicsToLocal(transform.position); previousPointVelocity=ship.GetPointVelocity(transform.position);
            slip=Vector3.zero; ResetRender();
        }
        public void TryAttachToDeck(ShipController ship)
        {
            if(Platform!=null || Climbing || verticalSpeed>0 || Time.time<ignoreGroundUntil) return;
            if(Probe(out var hit) && hit.collider.GetComponentInParent<ShipController>()==ship) Attach(ship);
        }
        public void Detach()
        {
            if(HomeShip!=null) HomeShip.ReleaseHelm(this);
            if(Platform!=null)
            {
                Vector3 inherited=Vector3.ClampMagnitude(Platform.GetPointVelocity(transform.position),15);
                planarVelocity+=new Vector3(inherited.x,0,inherited.z)+slip;
                verticalSpeed+=inherited.y;
            }
            Platform=null; transform.SetParent(null,true); slip=Vector3.zero; ResetRender();
        }
        public void Step(CrewInput input, float dt)
        {
            if(dt<=0 || Controller==null) return;
            renderPrevious=renderCurrent; impulseRemaining=pushImpulse;
            Interaction.SetAim(input.yaw,input.pitch);
            if(input.jump) jumpQueuedUntil=Time.time+.12f;
            if(Climbing) { Ladder.StepClimb(this,input.forward,dt,input.jump); FinishRender(); return; }
            if(Platform!=null)
            {
                Vector3 carried=Platform.LocalToPhysics(localAnchor);
                Controller.Move(carried-transform.position);
                Vector3 pointVelocity=Platform.GetPointVelocity(transform.position);
                Vector3 inertia=(previousPointVelocity-pointVelocity)*momentumTransfer; inertia.y=0;
                slip=Vector3.ClampMagnitude(slip+inertia,8);
                slip=Vector3.MoveTowards(slip,Vector3.zero,gripFriction*gravity*dt);
                previousPointVelocity=pointVelocity;
            }
            grounded=Time.time>=ignoreGroundUntil && verticalSpeed<=0 && Probe(out _);
            if(grounded)
            {
                lastGrounded=Time.time;
                if(Probe(out var ground))
                {
                    var support=ground.collider.GetComponentInParent<ShipController>();
                    if(support!=null && Platform!=support) Attach(support);
                    else if(support==null && Platform!=null) Detach();
                }
                verticalSpeed=-deckAdhesion;
            }
            else if(Platform!=null) Detach();
            if(input.throwItem) Interaction.Release(true);
            if(input.helm || input.cargo)
            {
                if(AtHelm) HomeShip.ReleaseHelm(this);
                else if(!Interaction.TryInteract() && NearHelm && Interaction.HeldItem==null) HomeShip.TryTakeHelm(this);
            }
            if(Climbing) { FinishRender(); return; }
            if(input.jump && AtHelm) HomeShip.ReleaseHelm(this);
            if(AtHelm)
            {
                HomeShip.Steering=input.horizontal; HomeShip.SailChange=input.forward;
                if(input.anchor) HomeShip.ToggleAnchor();
                Controller.Move(Vector3.down*deckAdhesion*dt); Velocity=Vector3.zero; CacheAnchor(); FinishRender(); return;
            }
            Vector3 direction=Quaternion.Euler(0,input.yaw,0)*Vector3.ClampMagnitude(new Vector3(input.horizontal,0,input.forward),1);
            float speed=(input.sprint ? sprintSpeed : walkSpeed)*Interaction.SpeedMultiplier;
            planarVelocity=Vector3.MoveTowards(planarVelocity,direction*speed,(grounded ? acceleration : airAcceleration)*dt);
            if(Time.time<jumpQueuedUntil && Time.time-lastGrounded<=coyoteTime)
            {
                float inheritedY=Platform!=null ? Platform.GetPointVelocity(transform.position).y : 0;
                Detach(); verticalSpeed=Mathf.Sqrt(2*gravity*jumpHeight)+inheritedY;
                grounded=false; lastGrounded=-100; jumpQueuedUntil=-1; ignoreGroundUntil=Time.time+.16f;
            }
            if(!grounded) verticalSpeed=Mathf.Max(verticalSpeed-gravity*dt,-35);
            Vector3 before=transform.position;
            var flags=Controller.Move((planarVelocity+slip+Vector3.up*verticalSpeed)*dt);
            if((flags&CollisionFlags.Above)!=0 && verticalSpeed>0) verticalSpeed=0;
            Velocity=(transform.position-before)/dt;
            transform.rotation=Quaternion.Euler(0,input.yaw,0);
            CacheAnchor(); FinishRender();
            if(transform.position.y < -10) { Interaction.Release(false); Respawn(new Vector3(1.5f,2.2f,-4.6f)); }
        }
        private bool Probe(out RaycastHit best)
        {
            Vector3 origin=transform.position+Vector3.up*.35f;
            int count=Physics.SphereCastNonAlloc(origin,.23f,Vector3.down,hits,.12f+groundProbe,groundMask,QueryTriggerInteraction.Ignore);
            best=default; float distance=float.MaxValue;
            for(int i=0;i<count;i++)
                if(hits[i].collider!=Controller && hits[i].normal.y>=Mathf.Cos(Controller.slopeLimit*Mathf.Deg2Rad) && hits[i].distance<distance)
                { best=hits[i]; distance=hits[i].distance; }
            return distance<float.MaxValue;
        }
        private void CacheAnchor() { if(Platform!=null) localAnchor=Platform.PhysicsToLocal(transform.position); }
        private void ResetRender()
        { renderPlatform=Platform; renderCurrent=Platform!=null ? Platform.PhysicsToLocal(transform.position) : transform.position; renderPrevious=renderCurrent; }
        private void FinishRender()
        { if(renderPlatform!=Platform) ResetRender(); else renderCurrent=Platform!=null ? Platform.PhysicsToLocal(transform.position) : transform.position; }
        public void BeginClimb(LadderInteraction ladder)
        {
            HomeShip.ReleaseHelm(this); Ladder=ladder; Controller.enabled=false; verticalSpeed=0; planarVelocity=slip=Velocity=Vector3.zero;
            if(ladder.Ship!=null) Attach(ladder.Ship);
            transform.SetParent(ladder.transform,true);
        }
        public void EndClimb(Vector3 worldExit, ShipController platform)
        {
            Ladder=null; transform.SetParent(null,true); Platform=null; transform.position=worldExit;
            if(platform!=null) Attach(platform);
            verticalSpeed=0; Controller.enabled=true; ignoreGroundUntil=0; CacheAnchor(); ResetRender();
        }
        public void Respawn(Vector3 deckPosition)
        {
            if(HomeShip==null) return;
            Controller.enabled=false; Ladder=null; Platform=null; transform.SetParent(null,true);
            transform.position=HomeShip.LocalToPhysics(deckPosition); transform.rotation=Quaternion.identity; Attach(HomeShip);
            verticalSpeed=0; planarVelocity=slip=Velocity=Vector3.zero; ignoreGroundUntil=0;
            Controller.enabled=true; grounded=false; ResetRender();
        }
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            var body=hit.rigidbody;
            if(body==null || body.isKinematic || body.GetComponent<ShipController>()!=null || hit.normal.y>.5f || impulseRemaining<=0) return;
            Vector3 direction=-hit.normal; direction.y=0;
            float closing=Mathf.Max(0,Vector3.Dot(planarVelocity-body.GetPointVelocity(hit.point),direction));
            float reducedMass=characterMass*body.mass/(characterMass+body.mass);
            float impulse=Mathf.Min(impulseRemaining,closing*reducedMass*.3f);
            body.AddForceAtPosition(direction*impulse,hit.point,ForceMode.Impulse); impulseRemaining-=impulse;
        }
        private void OnDisable()
        { if(Interaction!=null) Interaction.Release(false); if(HomeShip!=null) HomeShip.ReleaseHelm(this); }
    }
}

```

## Assets/TradeWinds/Runtime/DeckPlayer.cs

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

namespace TradeWinds
{
    public sealed class DeckPlayer : MonoBehaviour
    {
        public static readonly Vector3 HelmPosition = new Vector3(0, 2.15f, -5.3f);
        public ShipActor OfflineActor { get; private set; }
        public bool AtHelm { get { return Session != null && Session.Active ? networkAtHelm : OfflineActor != null && OfflineActor.AtHelm; } }
        public bool Climbing { get { return Session != null && Session.Active ? networkClimbing : OfflineActor != null && OfflineActor.Climbing; } }
        public bool Aboard { get { return Session != null && Session.Active ? networkAboard : OfflineActor != null && OfflineActor.Platform != null; } }
        public bool Paused { get; private set; }
        public bool ExternalView { get; private set; }
        public bool Overview { get; private set; }
        public bool NearHelm { get { return Aboard && Vector3.Distance(WorldPosition, ship.transform.TransformPoint(HelmPosition)) < 2.4f; } }
        public float Sensitivity { get; set; } = 0.12f;
        public float CameraRock { get; set; } = 0.45f;
        public CoopSession Session { get; set; }
        public Vector3 WorldPosition { get { return Session != null && Session.Active ? (Session.LocalActor != null ? Session.LocalActor.transform.position : transform.position) : OfflineActor.transform.position; } }
        public Vector3 DeckPosition { get { return ship.transform.InverseTransformPoint(WorldPosition); } }
        public float LookYaw { get { return yaw; } }
        private ShipController ship;
        private Camera view;
        private float yaw, pitch = 8, previousHeading;
        private bool networkAtHelm, networkClimbing, networkAboard;
        private CrewInput pending;
        private Vector3 networkFrom, networkTarget;
        private float networkReceived;
        private bool hasNetworkPose;
        [SerializeField, Range(0, .15f), Tooltip("Small camera recoil per metre/second of collision impulse divided by ship mass.")] private float impactRecoil=.035f;
        private Vector3 recoilOffset, recoilVelocity;

        public void Initialize(ShipController controller, Camera camera)
        {
            ship = controller; view = camera;
            OfflineActor = new GameObject("Physical local sailor").AddComponent<ShipActor>();
            OfflineActor.Initialize(ship, 0, new Vector3(1.5f, 2.2f, -4.6f));
            previousHeading = (float)ship.State.Heading;
            if(ship.PhysicsBody!=null) ship.PhysicsBody.Impact+=OnShipImpact;
            LockCursor(true);
        }

        private void Update()
        {
            var keyboard = Keyboard.current; var mouse = Mouse.current;
            if (keyboard == null || ship == null) return;
            float heading = ship.transform.eulerAngles.y;
            if (Aboard) yaw += Mathf.DeltaAngle(previousHeading, heading);
            previousHeading = heading;
            if (keyboard.escapeKey.wasPressedThisFrame) SetPaused(!Paused);
            if (Paused) { pending = new CrewInput { yaw = yaw, pitch = pitch }; if (Session != null) Session.SetInput(pending); return; }
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 look = mouse.delta.ReadValue() * Sensitivity;
                yaw += look.x; pitch = Mathf.Clamp(pitch - look.y, -65, 70);
            }
            if (keyboard.tabKey.wasPressedThisFrame) { ExternalView = !ExternalView; Overview = false; }
            if (keyboard.vKey.wasPressedThisFrame) { Overview = !Overview; ExternalView = Overview; }
            var input = new CrewInput {
                horizontal = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0),
                forward = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0),
                yaw = yaw, pitch = pitch, sprint = keyboard.leftShiftKey.isPressed,
                jump = keyboard.spaceKey.wasPressedThisFrame, helm = keyboard.eKey.wasPressedThisFrame,
                anchor = keyboard.bKey.wasPressedThisFrame, cargo = keyboard.fKey.wasPressedThisFrame,
                throwItem = mouse != null && mouse.leftButton.wasPressedThisFrame
            };
            if (Session != null && Session.Active) { Session.SetInput(input); return; }
            input.jump |= pending.jump; input.helm |= pending.helm; input.cargo |= pending.cargo;
            input.anchor |= pending.anchor; input.throwItem |= pending.throwItem; pending = input;
            if (keyboard.rKey.wasPressedThisFrame) ResetPlayerAndShip();
        }

        private void FixedUpdate()
        {
            if (OfflineActor == null || Paused || (Session != null && Session.Active)) return;
            OfflineActor.Step(pending, Time.fixedDeltaTime);
            pending.jump = pending.helm = pending.cargo = pending.anchor = pending.throwItem = false;
        }

        public void ApplyNetworkPose(CrewPose pose)
        {
            bool changedSpace = !hasNetworkPose || networkAboard != pose.aboard;
            networkAtHelm = pose.atHelm; networkClimbing = pose.climbing; networkAboard = pose.aboard;
            Transform parent = pose.aboard ? ship.transform : null;
            if (transform.parent != parent) transform.SetParent(parent, true);
            networkFrom = changedSpace ? pose.position : (pose.aboard ? transform.localPosition : transform.position);
            networkTarget = pose.position; networkReceived = Time.unscaledTime; hasNetworkPose = true;
            if (changedSpace) { if(pose.aboard) transform.localPosition=pose.position; else transform.position=pose.position; }
        }

        public void SetNetworkMode(bool enabled)
        {
            hasNetworkPose = false;
            if (OfflineActor != null) OfflineActor.gameObject.SetActive(!enabled);
            if (!enabled) { transform.SetParent(null, true); networkAtHelm = networkClimbing = false; }
        }

        public void LookAtPoint(Vector3 worldTarget)
        {
            Vector3 direction = worldTarget - (WorldPosition + Vector3.up * 1.65f);
            yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
        }

        private void LateUpdate()
        {
            if (ship == null || OfflineActor == null) return;
            if (Session != null && Session.Active && hasNetworkPose)
            { var p=Vector3.Lerp(networkFrom,networkTarget,Mathf.Clamp01((Time.unscaledTime-networkReceived)/.05f));
                if(networkAboard) transform.localPosition=p; else transform.position=p; }
            Vector3 feet = Session != null && Session.Active ? (Session.LocalActor != null ? Session.LocalActor.RenderPosition : transform.position) : OfflineActor.RenderPosition;
            if (Overview)
            {
                view.transform.position = ship.transform.position + Quaternion.Euler(0, yaw, 0) * new Vector3(17, 12, -24);
                view.transform.LookAt(ship.transform.position + Vector3.up * 3);
            }
            else if (ExternalView)
            {
                Vector3 target = feet + Vector3.up * 1.2f;
                Vector3 offset = Quaternion.Euler(pitch, yaw, 0) * new Vector3(0.75f, 1.1f, -4.5f);
                float length = offset.magnitude;
                if (Physics.SphereCast(target, 0.18f, offset.normalized, out RaycastHit hit, length, ~(1 << 2), QueryTriggerInteraction.Ignore)) length = Mathf.Max(0.25f, hit.distance - 0.1f);
                view.transform.position = target + offset.normalized * length; view.transform.LookAt(target + Quaternion.Euler(0, yaw, 0) * Vector3.forward * 1.4f);
            }
            else
            {
                view.transform.position = feet + Vector3.up * 1.65f;
                float roll = Aboard ? Mathf.DeltaAngle(0, ship.transform.eulerAngles.z) * CameraRock : 0;
                view.transform.rotation = Quaternion.Euler(pitch, yaw, 0) * Quaternion.AngleAxis(roll, Vector3.forward);
            }
            Shader.SetGlobalFloat("_VoyageTime", ship.WaterRenderClock); Shader.SetGlobalFloat("_SeaStrength", ship.SeaStrength);
            float dt=Mathf.Min(Time.unscaledDeltaTime,.033f);
            recoilVelocity-= (recoilOffset*324+recoilVelocity*36)*dt;
            recoilOffset=Vector3.ClampMagnitude(recoilOffset+recoilVelocity*dt,.08f);
            view.transform.position+=recoilOffset;
        }

        private void OnShipImpact(Vector3 deltaVelocity)
        { if(Aboard) recoilVelocity-=Vector3.ClampMagnitude(deltaVelocity*impactRecoil,.35f); }

        public void SetPaused(bool paused)
        {
            if (Paused == paused) return;
            Paused = paused; pending = new CrewInput { yaw = yaw, pitch = pitch };
            ship.Paused = Session != null && Session.Active ? !Session.IsHost : paused;
            if (Session == null || !Session.Active) ship.Steering = ship.SailChange = 0;
            LockCursor(!paused);
        }

        public void ResetPlayerAndShip()
        {
            ship.ResetVoyage(); yaw = 0; pitch = 8;
            OfflineActor.Respawn(new Vector3(1.5f, 2.2f, -4.6f));
            if (Session != null) Session.ResetCargo();
        }
        private void OnApplicationFocus(bool focus) { if (!focus && ship != null) SetPaused(true); }
        private void OnDisable() { LockCursor(false); }
        private void OnDestroy() { if(ship!=null && ship.PhysicsBody!=null) ship.PhysicsBody.Impact-=OnShipImpact; if (OfflineActor != null) Destroy(OfflineActor.gameObject); }
        private static void LockCursor(bool locked)
        { Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !locked; }
    }
}


```

## Assets/TradeWinds/Runtime/ShipPhysicsSetup.cs

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace TradeWinds
{
    public static class ShipPhysicsSetup
    {
        public static PickableItem[] Install(ShipController ship, Transform world, Transform existingCrate, Material material)
        {
            var rigidbody = ship.GetComponent<Rigidbody>();
            if (rigidbody == null) rigidbody = ship.gameObject.AddComponent<Rigidbody>();
            rigidbody.isKinematic = false; rigidbody.useGravity = true; rigidbody.mass = 10000;
            var hull = new GameObject("Physical hull", typeof(BoxCollider));
            hull.transform.SetParent(ship.transform, false);
            var hullCollider = hull.GetComponent<BoxCollider>(); hullCollider.center = new Vector3(0, .65f, -.25f); hullCollider.size = new Vector3(5.7f, 2.4f, 14.1f);
            foreach (Transform child in ship.GetComponentsInChildren<Transform>())
            {
                if (child.name == "Stern rail") { child.gameObject.SetActive(false); continue; }
                if (child.name == "Deck" || child.name == "Mast" || child.name == "Secured cargo" || child.name == "Helm pedestal"
                    || child.name == "Bulwark" || child.name == "Stern rail" || child.name == "Bow rail")
                    if (child.GetComponent<Collider>() == null) child.gameObject.AddComponent<BoxCollider>();
            }
            foreach (int side in new[] { -1, 1 })
                MakeBox("Stern rail beside boarding gap", ship.transform, new Vector3(side * 1.85f, 2.7f, -7.5f), new Vector3(2.3f, 1, 0.2f), material, new Color(0.08f, 0.2f, 0.22f), true);
            var deckZone = new GameObject("ShipPlatformZone").AddComponent<BoxCollider>();
            deckZone.transform.SetParent(ship.transform, false); deckZone.center = new Vector3(0, 4, -0.3f); deckZone.size = new Vector3(6.6f, 6, 15.3f);
            deckZone.gameObject.AddComponent<ShipPlatformZone>().Configure(ship);

            var hold = new GameObject("CargoHoldZone").AddComponent<BoxCollider>();
            hold.transform.SetParent(ship.transform, false); hold.center = new Vector3(0, 2.9f, 5.4f); hold.size = new Vector3(5.1f, 1.7f, 2.1f);
            hold.gameObject.AddComponent<CargoHoldZone>().Configure(ship);
            MakeBox("Marked cargo hold", ship.transform, new Vector3(0, 2.145f, 5.4f), new Vector3(4.9f, 0.025f, 2), material, new Color(0.15f, 0.48f, 0.43f), false);

            var ladder = new GameObject("Кормовая лестница · E, W/S").AddComponent<BoxCollider>();
            ladder.transform.SetParent(ship.transform, false); ladder.transform.localPosition = new Vector3(0, 0, -8.2f);
            ladder.center = new Vector3(0, 1.8f, 0); ladder.size = new Vector3(1.4f, 3.8f, 0.65f);
            ladder.gameObject.AddComponent<LadderInteraction>().Configure(ship);
            for (int i = 0; i < 8; i++) MakeBox("Ladder rung", ladder.transform, new Vector3(0, 0.4f + i * 0.32f, 0), new Vector3(1.1f, 0.1f, 0.12f), material, new Color(0.76f, 0.55f, 0.25f), false);
            foreach (int side in new[] { -1, 1 }) MakeBox("Ladder rail", ladder.transform, new Vector3(side * 0.55f, 1.65f, 0), new Vector3(0.12f, 2.9f, 0.15f), material, new Color(0.42f, 0.27f, 0.14f), false);
            MakeBox("Starter landing pier", world, new Vector3(0, 0.25f, -13.5f), new Vector3(5, 0.5f, 9), material, new Color(0.47f, 0.32f, 0.2f), true);
            MakeBox("Walkable shore", world, new Vector3(0, 0, -26), new Vector3(20, 1, 18), material, new Color(0.39f, 0.48f, 0.29f), true);
            for (int i = 0; i < 5; i++)
                foreach (int side in new[] { -1, 1 }) MakeBox("Pier post", world, new Vector3(side * 2.2f, -0.2f, -10 - i * 1.6f), new Vector3(0.3f, 2.2f, 0.3f), material, new Color(0.3f, 0.21f, 0.12f), true);

            var items = new List<PickableItem>();
            var first = existingCrate.gameObject.AddComponent<PickableItem>();
            first.Configure(0, ship, "Ящик припасов", 8); items.Add(first);
            var secondObject = MakeBox("Тяжёлый ящик", ship.transform, new Vector3(-1.5f, 2.6f, -1.8f), Vector3.one * 0.8f, material, new Color(0.45f, 0.3f, 0.18f), true);
            var second = secondObject.AddComponent<PickableItem>(); second.Configure(1, ship, "Инструменты", 25); items.Add(second);
            var thirdObject = MakeBox("Груз в трюме", ship.transform, new Vector3(0, 2.6f, 5.4f), Vector3.one * 0.8f, material, new Color(0.55f, 0.45f, 0.22f), true);
            var third = thirdObject.AddComponent<PickableItem>(); third.Configure(2, ship, "Торговый груз", 18); items.Add(third);
            ship.InitializePhysics();
            ship.InstallLegacyBoundaries(world);
            Physics.SyncTransforms();
            return items.ToArray();
        }

        private static GameObject MakeBox(string title, Transform parent, Vector3 position, Vector3 scale, Material material, Color color, bool collision)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube); item.name = title;
            item.transform.SetParent(parent, false); item.transform.localPosition = position; item.transform.localScale = scale;
            var renderer = item.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            var properties = new MaterialPropertyBlock(); properties.SetColor("_BaseColor", color); renderer.SetPropertyBlock(properties);
            if (!collision) Object.Destroy(item.GetComponent<Collider>());
            return item;
        }
    }
}


```

## Assets/TradeWinds/Runtime/ShipPlatformZone.cs

```csharp
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
            if (ship != null && actor != null && !actor.Climbing && actor.Platform == null) actor.TryAttachToDeck(ship);
        }
        private void OnTriggerExit(Collider other)
        {
            var actor = other.GetComponent<ShipActor>();
            if (actor != null && !actor.Climbing && actor.Platform == ship) actor.Detach();
        }
    }
}


```

## Assets/TradeWinds/Runtime/PickableItem.cs

```csharp
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


```

## Assets/TradeWinds/Runtime/PlayerInteraction.cs

```csharp
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

```

## Assets/TradeWinds/Runtime/LadderInteraction.cs

```csharp
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

```

## Assets/TradeWinds/Runtime/CargoHoldZone.cs

```csharp
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

```

## Assets/TradeWinds/Runtime/CoopSession.cs

```csharp
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace TradeWinds
{
    [Serializable]
    public struct CrewInput
    {
        public float horizontal, forward, yaw, pitch;
        public bool sprint, jump, helm, anchor, cargo, throwItem;
    }

    [Serializable]
    public struct CrewPose
    {
        public ulong id;
        public Vector3 position;
        public float yaw;
        public bool atHelm, aboard, climbing, grounded;
    }

    [Serializable]
    public sealed class VoyageSnapshot
    {
        public ShipSnapshot ship;
        public ShipPhysicsPose physics;
        public CrewPose[] crew;
        public Vector3 cargo;
        public long carrier = -1;
        public CargoPose[] items;
    }

    // Small LAN prototype: NGO supplies connections and reliable delivery; only the
    // host steps gameplay. Named snapshots avoid parenting NetworkTransforms to a
    // rocking, procedurally generated ship. No client submits a position or ship state.
    public sealed class CoopSession : MonoBehaviour
    {
        private const string InputMessage = "voyage/input-v1", StateMessage = "voyage/state-v1";
        private const float SendInterval = 0.05f;
        private sealed class Sailor
        {
            public ShipActor actor;
            public CrewInput input;
            public float lastInput;
        }
        private readonly Dictionary<ulong, Sailor> sailors = new Dictionary<ulong, Sailor>();
        private readonly Dictionary<ulong, SailorAvatar> avatars = new Dictionary<ulong, SailorAvatar>();
        private SailorAvatar offlineAvatar;
        private readonly HashSet<ulong> visibleIds = new HashSet<ulong>();
        private readonly List<ulong> removedIds = new List<ulong>();
        private NetworkManager network;
        private UnityTransport transport;
        private ShipController ship;
        private DeckPlayer player;
        private PickableItem[] items;
        private Material crewMaterial;
        private CrewInput pending;
        private float nextSend;
        private float connectionDeadline;
        private bool connecting;
        private string address = "127.0.0.1";
        private Vector3 cargoPosition = new Vector3(1.7f, 2.6f, -1.8f);
        private long carrier = -1;
        public bool Active { get { return connecting || (network != null && network.IsListening); } }
        public bool IsHost { get { return network != null && network.IsHost; } }
        public int CrewCount { get { return IsHost ? sailors.Count : avatars.Count; } }
        public string Status { get; private set; } = "E — предмет / лестница / штурвал. ЛКМ — бросить. Зелёная зона — трюм.";
        public VoyageSnapshot LastSnapshot { get; private set; }
        public ulong LocalClientId { get { return network.LocalClientId; } }
        public ShipActor LocalActor => IsHost && sailors.TryGetValue(LocalClientId, out var sailor) ? sailor.actor : null;

        public void Initialize(ShipController controller, DeckPlayer deckPlayer, PickableItem[] cargo, Material material)
        {
            ship = controller; player = deckPlayer; items = cargo; crewMaterial = material;
            Application.runInBackground = true;
            player.Session = this;
            offlineAvatar = new GameObject("Матрос · одиночная игра").AddComponent<SailorAvatar>();
            offlineAvatar.transform.SetParent(ship.transform, false);
            offlineAvatar.Build(crewMaterial, 0);
            var root = new GameObject("Coop connection");
            transport = root.AddComponent<UnityTransport>();
            network = root.AddComponent<NetworkManager>();
            network.NetworkConfig = new NetworkConfig();
            network.NetworkConfig.NetworkTransport = transport;
            network.NetworkConfig.EnableSceneManagement = false;
            network.NetworkConfig.ConnectionApproval = true;
            network.NetworkConfig.ProtocolVersion = 4;
            network.NetworkConfig.TickRate = 20;
            network.ConnectionApprovalCallback = Approve;
            network.OnClientConnectedCallback += Connected;
            network.OnClientDisconnectCallback += Disconnected;
        }

        public void StartHost()
        {
            if (Active) return;
            transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
            PrepareSession();
            foreach (var item in items) item.SetReplica(false);
            if (!network.StartHost()) { ReturnOffline(); Status = "Не удалось создать игру: порт 7777 занят?"; return; }
            RegisterMessages();
            Status = "Хост открыт • порт 7777 • до 4 игроков";
            player.SetPaused(false);
        }

        public void StartClient(string hostAddress = null)
        {
            if (Active) return;
            if (hostAddress != null) address = hostAddress;
            if (string.IsNullOrWhiteSpace(address)) { Status = "Введите адрес хоста."; return; }
            transport.SetConnectionData(address.Trim(), 7777);
            PrepareSession();
            foreach (var item in items) item.SetReplica(true);
            connecting = true;
            connectionDeadline = Time.unscaledTime + 12;
            if (!network.StartClient()) { connecting = false; ReturnOffline(); Status = "Не удалось подключиться."; return; }
            RegisterMessages();
            ship.SetReplica(true); ship.Paused = true;
            Status = "Подключение к " + address + "…";
        }

        private void PrepareSession()
        {
            ClearSailors(); pending = new CrewInput(); carrier = -1; LastSnapshot = null;
            player.SetNetworkMode(true);
            ship.SetReplica(false); ship.ResetVoyage();
            ship.Paused = false;
            cargoPosition = new Vector3(1.7f, 2.6f, -1.8f);
            ResetCargo();
            nextSend = 0;
        }

        private void RegisterMessages()
        {
            network.CustomMessagingManager.RegisterNamedMessageHandler(InputMessage, ReceiveInput);
            network.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, ReceiveState);
        }

        private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = network.ConnectedClientsIds.Count < 4;
            response.CreatePlayerObject = false;
            response.Reason = response.Approved ? "" : "Экипаж уже заполнен (4 игрока).";
            response.Pending = false;
        }

        private void Connected(ulong id)
        {
            if (IsHost)
            {
                var sailor = new Sailor();
                sailor.actor = new GameObject("Authoritative sailor " + id).AddComponent<ShipActor>();
                sailor.actor.Initialize(ship, id, new Vector3(id % 2 == 0 ? 1.5f : -1.5f, 2.2f, -5.7f + (sailors.Count / 2) * 1.1f));
                sailors[id] = sailor;
            }
            if (id == network.LocalClientId)
            {
                connecting = false;
                Status = IsHost ? "Хост открыт • до 4 игроков" : "Вы в экипаже • кораблём управляет хост";
                player.SetPaused(false);
            }
        }

        private void Disconnected(ulong id)
        {
            if (sailors.TryGetValue(id, out Sailor sailor)) Destroy(sailor.actor.gameObject);
            sailors.Remove(id);
            if (carrier == (long)id) carrier = -1;
            if (!IsHost)
            {
                connecting = false;
                Status = "Соединение закрыто. " + network.DisconnectReason;
                ReturnOffline();
            }
        }

        public void StopSession()
        {
            connecting = false;
            network.Shutdown();
            ReturnOffline();
            Status = "Сетевая игра закрыта. Можно создать новую.";
        }

        private void ReturnOffline()
        {
            foreach (var avatar in avatars.Values) if (avatar != null) Destroy(avatar.gameObject);
            avatars.Clear(); ClearSailors(); carrier = -1; pending = new CrewInput();
            foreach (var item in items) item.SetReplica(false);
            ship.SetReplica(false); player.SetNetworkMode(false);
            player.ResetPlayerAndShip();
            player.SetPaused(true);
        }

        public void SetInput(CrewInput input)
        {
            pending.horizontal = input.horizontal; pending.forward = input.forward;
            pending.yaw = input.yaw; pending.pitch = input.pitch; pending.sprint = input.sprint;
            pending.jump |= input.jump; pending.helm |= input.helm;
            pending.anchor |= input.anchor; pending.cargo |= input.cargo;
            pending.throwItem |= input.throwItem;
        }

        private void Update()
        {
            if (connecting && Time.unscaledTime > connectionDeadline)
            {
                StopSession(); Status = "Хост не ответил. Проверьте адрес и что игра создана.";
            }
            if (!Active || Time.unscaledTime < nextSend) return;
            nextSend = Time.unscaledTime + SendInterval;
            if (IsHost)
            {
                AcceptInput(network.LocalClientId, pending);
                ClearActions(ref pending);
                var poses = new CrewPose[sailors.Count];
                int index = 0;
                foreach (var pair in sailors)
                {
                    ShipActor actor = pair.Value.actor;
                    bool aboard = actor.Platform != null;
                    poses[index++] = new CrewPose { id = pair.Key, position = aboard ? ship.PhysicsToLocal(actor.transform.position) : actor.transform.position,
                        yaw = pair.Value.input.yaw, atHelm = actor.AtHelm, aboard = aboard, climbing = actor.Climbing, grounded = actor.Grounded };
                }
                var cargoPoses = new CargoPose[items.Length];
                for (int i = 0; i < items.Length; i++) cargoPoses[i] = items[i].Capture();
                var snapshot = new VoyageSnapshot { ship = ship.State.Capture(), physics = ship.PhysicsBody.Capture(), crew = poses, cargo = items[0].transform.position,
                    carrier = cargoPoses[0].carrier, items = cargoPoses };
                Present(snapshot);
                foreach (ulong id in network.ConnectedClientsIds)
                    if (id != network.LocalClientId) Send(StateMessage, id, snapshot);
            }
            else if (network.IsConnectedClient)
            {
                Send(InputMessage, NetworkManager.ServerClientId, pending);
                ClearActions(ref pending);
            }
        }

        private void FixedUpdate()
        {
            if (!IsHost) return;
            ship.Steering = ship.SailChange = 0;
            foreach (var pair in sailors)
            {
                Sailor sailor = pair.Value;
                CrewInput input = Time.unscaledTime - sailor.lastInput > 0.35f ? new CrewInput { yaw = sailor.input.yaw, pitch = sailor.input.pitch } : sailor.input;
                sailor.actor.Step(input, Time.fixedDeltaTime);
                ClearActions(ref sailor.input);
            }
        }

        private void AcceptInput(ulong id, CrewInput input)
        {
            if (!sailors.TryGetValue(id, out Sailor sailor)) return;
            if (!Finite(input.horizontal) || !Finite(input.forward) || !Finite(input.yaw) || !Finite(input.pitch)) return;
            input.horizontal = Mathf.Clamp(input.horizontal, -1, 1);
            input.forward = Mathf.Clamp(input.forward, -1, 1);
            input.yaw = Mathf.Repeat(input.yaw, 360);
            input.pitch = Mathf.Clamp(input.pitch, -65, 70);
            input.jump |= sailor.input.jump; input.helm |= sailor.input.helm;
            input.anchor |= sailor.input.anchor; input.cargo |= sailor.input.cargo;
            input.throwItem |= sailor.input.throwItem;
            sailor.input = input; sailor.lastInput = Time.unscaledTime;
        }

        private void ReceiveInput(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || reader.Length > 2048) return;
            try { reader.ReadValueSafe(out string json); AcceptInput(sender, JsonUtility.FromJson<CrewInput>(json)); }
            catch (Exception) { Debug.LogWarning("Rejected malformed crew input."); }
        }

        private void ReceiveState(ulong sender, FastBufferReader reader)
        {
            if (IsHost || sender != NetworkManager.ServerClientId || reader.Length > 8192) return;
            try
            {
                reader.ReadValueSafe(out string json);
                var snapshot = JsonUtility.FromJson<VoyageSnapshot>(json);
                if (snapshot == null || snapshot.crew == null || snapshot.crew.Length > 4 || snapshot.items == null || snapshot.items.Length != items.Length) return;
                ship.ReceivePhysicsPose(snapshot.physics); ship.State.Restore(snapshot.ship);
                Present(snapshot);
            }
            catch (Exception) { Debug.LogWarning("Rejected malformed voyage snapshot."); }
        }

        private void Send<T>(string message, ulong target, T payload)
        {
            string json = JsonUtility.ToJson(payload);
            using (var writer = new FastBufferWriter(8192, Allocator.Temp))
            {
                writer.WriteValueSafe(json);
                network.CustomMessagingManager.SendNamedMessage(message, target, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        private void Present(VoyageSnapshot snapshot)
        {
            LastSnapshot = snapshot;
            cargoPosition = snapshot.cargo; carrier = snapshot.carrier;
            if (!IsHost) foreach (CargoPose cargo in snapshot.items)
                if (cargo.id >= 0 && cargo.id < items.Length) items[cargo.id].Apply(cargo);
            visibleIds.Clear();
            foreach (CrewPose pose in snapshot.crew)
            {
                visibleIds.Add(pose.id);
                if (pose.id == network.LocalClientId) player.ApplyNetworkPose(pose);
                if (!avatars.TryGetValue(pose.id, out SailorAvatar avatar))
                {
                    avatar = new GameObject().AddComponent<SailorAvatar>();
                    avatar.name = "Матрос " + (pose.id + 1);
                    avatar.transform.SetParent(ship.transform, false);
                    avatar.Build(crewMaterial, (int)(pose.id % 4));
                    avatars[pose.id] = avatar;
                }
                Transform parent = pose.aboard ? ship.transform : null;
                if (avatar.transform.parent != parent) avatar.transform.SetParent(parent, true);
                bool holdsCargo = false;
                foreach (CargoPose cargo in snapshot.items) holdsCargo |= cargo.carrier == (long)pose.id;
                avatar.SetPose(pose.position, pose.yaw - (pose.aboard ? (float)ship.State.Heading : 0), pose.id == network.LocalClientId && !player.ExternalView, holdsCargo);
            }
            removedIds.Clear();
            foreach (var pair in avatars) if (!visibleIds.Contains(pair.Key)) removedIds.Add(pair.Key);
            foreach (ulong id in removedIds) { Destroy(avatars[id].gameObject); avatars.Remove(id); }
        }

        public void ResetCargo() { foreach (var item in items) item.ResetItem(); }

        private void ClearSailors()
        {
            foreach (var sailor in sailors.Values) if (sailor.actor != null) Destroy(sailor.actor.gameObject);
            sailors.Clear();
        }

        private void LateUpdate()
        {
            offlineAvatar.gameObject.SetActive(!Active);
            if (!Active)
            {
                Transform parent = player.Aboard ? ship.transform : null;
                if (offlineAvatar.transform.parent != parent) offlineAvatar.transform.SetParent(parent, true);
                offlineAvatar.SetPose(player.Aboard ? player.DeckPosition : player.WorldPosition,
                    player.LookYaw - (player.Aboard ? (float)ship.State.Heading : 0), !player.ExternalView, player.OfflineActor.Interaction.HeldItem != null);
            }
        }

        public void DrawLobbyGUI()
        {
            GUI.Box(new Rect(915, 145, 337, 310), "ЭТАП 2 • ЭКИПАЖ");
            GUI.Label(new Rect(932, 180, 300, 45), Active ? "В игре: " + CrewCount + " / 4" : "Локальная сеть или два окна на одном ПК");
            if (!Active)
            {
                GUI.Label(new Rect(932, 232, 300, 24), "Адрес компьютера хоста:");
                address = GUI.TextField(new Rect(932, 260, 300, 30), address, 128);
                if (GUI.Button(new Rect(932, 305, 300, 40), "СОЗДАТЬ ИГРУ")) StartHost();
                if (GUI.Button(new Rect(932, 355, 300, 40), "ПОДКЛЮЧИТЬСЯ")) StartClient();
            }
            else if (GUI.Button(new Rect(932, 260, 300, 45), "ОТКЛЮЧИТЬСЯ")) StopSession();
            GUI.Label(new Rect(932, 408, 300, 40), "Порт 7777 • Steam-лобби ещё нет");
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static void ClearActions(ref CrewInput input) { input.jump = input.helm = input.anchor = input.cargo = input.throwItem = false; }

        private void OnDestroy()
        {
            if (network == null) return;
            network.OnClientConnectedCallback -= Connected;
            network.OnClientDisconnectCallback -= Disconnected;
            ClearSailors();
            network.Shutdown();
            Destroy(network.gameObject);
        }
    }
}


```

## Assets/TradeWinds/Runtime/SailorAvatar.cs

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TradeWinds
{
    // Lightweight, original stylized sailor assembled from shared Unity meshes.
    public sealed class SailorAvatar : MonoBehaviour
    {
        private readonly List<Renderer> renderers = new List<Renderer>();
        private Transform leftArm, rightArm, leftLeg, rightLeg, torso;
        private Vector3 previousPosition, targetPosition;
        private float targetYaw, phase;
        private bool initialized;
        private bool carrying;

        public void Build(Material material, int variant)
        {
            Color skin = variant % 2 == 0 ? new Color(0.72f, 0.46f, 0.3f) : new Color(0.9f, 0.66f, 0.43f);
            Color navy = new Color(0.07f, 0.16f, 0.23f), cream = new Color(0.91f, 0.86f, 0.69f);
            Color coat = variant % 3 == 0 ? new Color(0.64f, 0.22f, 0.12f)
                : variant % 3 == 1 ? new Color(0.12f, 0.39f, 0.4f) : new Color(0.74f, 0.53f, 0.16f);
            torso = new GameObject("Jacket and striped shirt").transform;
            torso.SetParent(transform, false);
            Part("Jacket", torso, PrimitiveType.Capsule, new Vector3(0, 1.1f, 0), new Vector3(0.52f, 0.36f, 0.3f), coat, material);
            Part("Shirt", torso, PrimitiveType.Cube, new Vector3(0, 1.17f, 0.145f), new Vector3(0.22f, 0.4f, 0.025f), cream, material);
            for (int i = 0; i < 4; i++) Part("Shirt stripe", torso, PrimitiveType.Cube,
                new Vector3(0, 1.04f + i * 0.075f, 0.162f), new Vector3(0.225f, 0.025f, 0.012f), navy, material);
            Part("Belt", torso, PrimitiveType.Cube, new Vector3(0, 0.88f, 0), new Vector3(0.48f, 0.08f, 0.3f), navy, material);
            Part("Brass buckle", torso, PrimitiveType.Cube, new Vector3(0, 0.88f, 0.162f), new Vector3(0.095f, 0.09f, 0.028f), new Color(0.9f, 0.66f, 0.2f), material);
            Part("Neck", torso, PrimitiveType.Cylinder, new Vector3(0, 1.44f, 0), new Vector3(0.14f, 0.075f, 0.14f), skin, material);
            Part("Face", torso, PrimitiveType.Sphere, new Vector3(0, 1.61f, 0.02f), new Vector3(0.32f, 0.36f, 0.3f), skin, material);
            Part("Nose", torso, PrimitiveType.Sphere, new Vector3(0, 1.61f, 0.178f), new Vector3(0.065f, 0.075f, 0.06f), skin, material);
            foreach (int side in new[] { -1, 1 })
            {
                Part("Eye", torso, PrimitiveType.Sphere, new Vector3(side * 0.065f, 1.65f, 0.154f), Vector3.one * 0.035f, navy, material);
                Part("Ear", torso, PrimitiveType.Sphere, new Vector3(side * 0.165f, 1.61f, 0.02f), new Vector3(0.06f, 0.095f, 0.07f), skin, material);
            }
            Part("Sailor cap", torso, PrimitiveType.Cylinder, new Vector3(0, 1.79f, 0.015f), new Vector3(0.37f, 0.065f, 0.34f), cream, material);
            Part("Cap band", torso, PrimitiveType.Cylinder, new Vector3(0, 1.735f, 0.015f), new Vector3(0.34f, 0.024f, 0.32f), navy, material);
            Part("Cap visor", torso, PrimitiveType.Cube, new Vector3(0, 1.735f, 0.18f), new Vector3(0.28f, 0.035f, 0.2f), navy, material);
            leftArm = Limb("Left arm", new Vector3(-0.32f, 1.35f, 0), coat, skin, material, true);
            rightArm = Limb("Right arm", new Vector3(0.32f, 1.35f, 0), coat, skin, material, true);
            leftLeg = Limb("Left leg", new Vector3(-0.135f, 0.82f, 0), navy, navy, material, false);
            rightLeg = Limb("Right leg", new Vector3(0.135f, 0.82f, 0), navy, navy, material, false);
        }

        private Transform Limb(string title, Vector3 position, Color cloth, Color skin, Material material, bool arm)
        {
            var pivot = new GameObject(title).transform;
            pivot.SetParent(transform, false); pivot.localPosition = position;
            Part("Sleeve", pivot, PrimitiveType.Capsule, new Vector3(0, arm ? -0.22f : -0.3f, 0),
                arm ? new Vector3(0.17f, 0.25f, 0.18f) : new Vector3(0.22f, 0.33f, 0.23f), cloth, material);
            Part(arm ? "Hand" : "Boot", pivot, arm ? PrimitiveType.Sphere : PrimitiveType.Cube,
                new Vector3(0, arm ? -0.47f : -0.71f, arm ? 0 : 0.05f),
                arm ? new Vector3(0.15f, 0.19f, 0.15f) : new Vector3(0.24f, 0.21f, 0.35f), skin, material);
            return pivot;
        }

        private void Part(string title, Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Color color, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = title; part.transform.SetParent(parent, false);
            part.transform.localPosition = position; part.transform.localScale = scale;
            Destroy(part.GetComponent<Collider>());
            var renderer = part.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            var properties = new MaterialPropertyBlock(); properties.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(properties); renderers.Add(renderer);
        }

        public void SetPose(Vector3 position, float yaw, bool hideBody, bool holdsCargo = false)
        {
            carrying = holdsCargo;
            targetPosition = position; targetYaw = yaw;
            if (!initialized) { previousPosition = position; transform.localPosition = position; initialized = true; }
            foreach (var renderer in renderers)
                renderer.shadowCastingMode = hideBody ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, 1 - Mathf.Exp(-24 * Time.deltaTime));
            transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.Euler(0, targetYaw, 0), 1 - Mathf.Exp(-20 * Time.deltaTime));
            float speed = Vector3.Distance(transform.localPosition, previousPosition) / Mathf.Max(Time.deltaTime, 0.001f);
            previousPosition = transform.localPosition;
            phase += Mathf.Min(speed, 4.2f) * Time.deltaTime * 3.3f;
            float swing = Mathf.Sin(phase) * Mathf.Clamp01(speed) * 27;
            leftLeg.localRotation = Quaternion.Euler(swing, 0, 0); rightLeg.localRotation = Quaternion.Euler(-swing, 0, 0);
            leftArm.localRotation = Quaternion.Euler(carrying ? -65 : -swing, 0, 5);
            rightArm.localRotation = Quaternion.Euler(carrying ? -65 : swing, 0, -5);
        }
    }
}

```

## Assets/TradeWinds/Runtime/PrototypeWorld.cs

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TradeWinds
{
    // Scene composition only. Generated objects are children and die with this scene.
    public sealed class PrototypeWorld : MonoBehaviour
    {
        [SerializeField] private Shader litShader;
        [SerializeField] private Shader seaShader;
        private readonly List<Object> ownedAssets = new List<Object>();
        [SerializeField] private ShipController ship;
        [SerializeField] private Transform ocean;
        [SerializeField] private Camera sceneCamera;
        [SerializeField] private DeckPlayer scenePlayer;
        [SerializeField] private Transform sceneCargo;
        [SerializeField] private Material crewMaterial;
        public IReadOnlyList<Object> GeneratedAssets { get { return ownedAssets; } }

        public void Configure(Shader lit, Shader sea) { litShader = lit; seaShader = sea; }

        private void Start()
        {
            if (ship == null) BuildSceneContent();
            if (ship == null) return;
            Application.targetFrameRate = 60;
            Time.fixedDeltaTime = 0.02f;
            PickableItem[] cargo = ShipPhysicsSetup.Install(ship, transform, sceneCargo, crewMaterial);
            scenePlayer.Initialize(ship, sceneCamera);
            gameObject.AddComponent<CoopSession>().Initialize(ship, scenePlayer, cargo, crewMaterial);
            gameObject.AddComponent<PrototypeHud>().Initialize(ship, scenePlayer);
            Debug.Log("First Voyage ready: ship, ocean, three islands, walking, jumping and co-op lobby.");
        }

        public void BuildSceneContent()
        {
            if (ship != null) return;
            if (litShader == null || seaShader == null)
            {
                Debug.LogError("Prototype shaders are missing. Run Trade Winds > Prepare prototype.");
                enabled = false;
                return;
            }
            Application.targetFrameRate = 60;
            Time.fixedDeltaTime = 0.02f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 125;
            RenderSettings.fogEndDistance = 425;
            RenderSettings.fogColor = new Color(0.69f, 0.79f, 0.78f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.66f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.44f, 0.5f, 0.51f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.25f, 0.26f);
            Shader skyShader = Shader.Find("TradeWinds/CoastalSky");
            if (skyShader != null)
            {
                var sky = new Material(skyShader) { name = "Coastal sky and clouds" };
                ownedAssets.Add(sky); RenderSettings.skybox = sky;
            }
            var sun = new GameObject("Late afternoon sun").AddComponent<Light>();
            sun.transform.SetParent(transform);
            sun.type = LightType.Directional;
            sun.color = new Color(1, 0.85f, 0.64f);
            sun.intensity = 1.8f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(27, -35, 0);
            RenderSettings.sun = sun;

            Material hull = Material("Midnight teal hull", new Color(0.08f, 0.2f, 0.22f));
            Material wood = Material("Oiled timber", new Color(0.33f, 0.21f, 0.12f));
            Material deck = Material("Honey deck", new Color(0.57f, 0.41f, 0.24f));
            Material lightBoard = Material("Sunlit timber", new Color(0.62f, 0.46f, 0.29f));
            Material brass = Material("Warm brass", new Color(0.76f, 0.52f, 0.22f));
            Material canvas = Material("Ivory canvas", new Color(0.86f, 0.81f, 0.64f));
            canvas.SetFloat("_Cull", 0);
            Material red = Material("Oxide red", new Color(0.55f, 0.16f, 0.1f));
            Material rock = Material("Slate", new Color(0.29f, 0.36f, 0.34f));
            Material grass = Material("Moss", new Color(0.31f, 0.4f, 0.26f));
            Material lamp = Material("Lighthouse lamp", new Color(1, 0.73f, 0.3f));
            lamp.EnableKeyword("_EMISSION");
            lamp.SetColor("_EmissionColor", new Color(1.5f, 0.8f, 0.2f));

            var shipRoot = new GameObject("Marten | coastal trader").transform;
            shipRoot.SetParent(transform);
            MeshObject("Hull", HullMesh(), hull, shipRoot);
            Box("Deck", shipRoot, new Vector3(0, 1.98f, -0.3f), new Vector3(6, 0.3f, 14.5f), deck);
            for (int i = 0; i < 22; i++)
            {
                if (i % 3 == 0) Box("Weathered deck board", shipRoot, new Vector3(0, 2.136f, -6.68f + i * 0.65f), new Vector3(5.97f, 0.01f, 0.61f), lightBoard);
                Box("Plank joint", shipRoot, new Vector3(0, 2.139f, -7 + i * 0.65f), new Vector3(5.98f, 0.008f, 0.025f), wood);
                foreach (int side in new[] { -1, 1 })
                    Box("Deck nail", shipRoot, new Vector3(side * 2.7f, 2.146f, -6.7f + i * 0.65f), new Vector3(0.035f, 0.008f, 0.035f), brass);
            }
            foreach (int side in new[] { -1, 1 })
            {
                Box("Bulwark", shipRoot, new Vector3(side * 3.02f, 2.6f, -0.3f), new Vector3(0.18f, 1, 14.5f), hull);
                Box("Gunwale", shipRoot, new Vector3(side * 3.02f, 3.13f, -0.3f), new Vector3(0.26f, 0.12f, 14.5f), brass);
                for (int i = 0; i < 9; i++)
                    Box("Rail support", shipRoot, new Vector3(side * 2.92f, 2.65f, -6.8f + i * 1.6f), new Vector3(0.16f, 1, 0.16f), wood);
            }
            Box("Stern rail", shipRoot, new Vector3(0, 2.7f, -7.5f), new Vector3(6, 1, 0.2f), hull);
            Box("Bow rail", shipRoot, new Vector3(0, 2.7f, 7), new Vector3(6, 1, 0.2f), hull);
            foreach (int side in new[] { -1, 1 })
            {
                Box("Navigation lamp bracket", shipRoot, new Vector3(side * 2.75f, 3.32f, 5.8f), new Vector3(0.16f, 0.42f, 0.16f), brass);
                Box("Navigation lantern", shipRoot, new Vector3(side * 2.75f, 3.6f, 5.8f), new Vector3(0.3f, 0.34f, 0.3f), lamp);
                Box("Lantern hood", shipRoot, new Vector3(side * 2.75f, 3.83f, 5.8f), new Vector3(0.42f, 0.12f, 0.42f), brass);
            }
            Box("Mast", shipRoot, new Vector3(0, 7.5f, 0), new Vector3(0.55f, 11, 0.55f), wood);
            Box("Yard", shipRoot, new Vector3(0, 11.8f, 0.1f), new Vector3(9.5f, 0.23f, 0.23f), wood);
            var sailPivot = new GameObject("Sail top pivot").transform;
            sailPivot.SetParent(shipRoot, false);
            sailPivot.localPosition = new Vector3(0, 11.65f, 0.25f);
            MeshObject("Billowing sail", SailMesh(), canvas, sailPivot);
            Box("Pennant", shipRoot, new Vector3(0.7f, 13.1f, 0), new Vector3(1.8f, 0.6f, 0.08f), red);
            Beam("Port rigging", shipRoot, new Vector3(-2.8f, 2.7f, -3), new Vector3(0, 12.5f, 0), 0.045f, wood);
            Beam("Starboard rigging", shipRoot, new Vector3(2.8f, 2.7f, -3), new Vector3(0, 12.5f, 0), 0.045f, wood);
            Beam("Forestay", shipRoot, new Vector3(0, 2.8f, 7), new Vector3(0, 12.5f, 0), 0.045f, wood);
            Box("Helm pedestal", shipRoot, new Vector3(0, 2.8f, -3.65f), new Vector3(0.5f, 1.3f, 0.5f), wood);
            var wheel = new GameObject("Wheel").transform;
            wheel.SetParent(shipRoot, false);
            wheel.localPosition = new Vector3(0, 3.5f, -3.95f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6, b = (i + 1) * Mathf.PI / 6;
                Beam("Wheel rim", wheel, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * 0.58f,
                    new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0) * 0.58f, 0.08f, brass);
                if (i % 2 == 0) Beam("Wheel spoke", wheel, Vector3.zero,
                    new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * 0.77f, 0.07f, wood);
            }
            foreach (float x in new[] { -1.7f, 1.75f })
            {
                Box("Secured cargo", shipRoot, new Vector3(x, 2.8f, 3), new Vector3(1.45f, 1.3f, 1.8f), deck);
                foreach (float offset in new[] { -0.5f, 0.5f })
                    Box("Cargo strap", shipRoot, new Vector3(x + offset, 2.81f, 3), new Vector3(0.09f, 1.34f, 1.84f), hull);
            }
            Vector3[] islands = { new Vector3(-85, 26, 90), new Vector3(110, 32, 270), new Vector3(-175, 42, -175) };
            for (int i = 0; i < islands.Length; i++)
                Island(islands[i], i, rock, grass, canvas, red, lamp, wood);
            ship = shipRoot.gameObject.AddComponent<ShipController>();
            ship.Initialize(wheel, sailPivot, islands);

            var oceanMaterial = new Material(seaShader) { name = "Procedural sea" };
            ownedAssets.Add(oceanMaterial);
            ocean = MeshObject("Ocean", OceanMesh(), oceanMaterial, transform);
            ocean.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            sceneCamera = camera;
            camera.gameObject.tag = "MainCamera";
            camera.transform.SetParent(transform);
            camera.fieldOfView = 72;
            camera.nearClipPlane = 0.08f;
            camera.farClipPlane = 450;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = RenderSettings.fogColor;
            var player = new GameObject("Deck sailor").AddComponent<DeckPlayer>();
            scenePlayer = player;
            player.transform.SetParent(transform);
            var cargo = Box("Переносимый ящик · F", shipRoot, new Vector3(1.7f, 2.6f, -1.8f), Vector3.one * 0.8f, deck);
            Box("Cargo band", cargo, Vector3.zero, new Vector3(1.04f, 1.04f, 0.16f), hull);
            sceneCargo = cargo; crewMaterial = red;
            camera.transform.position = ship.transform.TransformPoint(new Vector3(1.5f, 3.8f, -4.6f));
            camera.transform.rotation = Quaternion.Euler(8, 0, 0);
        }

        private void LateUpdate()
        {
            if (ocean != null && ship != null)
                ocean.position = new Vector3(Mathf.Round((float)ship.State.X / 6) * 6, 0, Mathf.Round((float)ship.State.Z / 6) * 6);
        }

        private Material Material(string title, Color color)
        {
            var material = new Material(litShader) { name = title };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.18f);
            ownedAssets.Add(material);
            return material;
        }

        private Transform Box(string title, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = title;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = material;
            // Deck movement uses explicit local bounds, never mesh physics.
            if (Application.isPlaying) Destroy(item.GetComponent<Collider>());
            else DestroyImmediate(item.GetComponent<Collider>());
            return item.transform;
        }

        private void Beam(string title, Transform parent, Vector3 start, Vector3 end, float width, Material material)
        {
            var item = Box(title, parent, (start + end) * 0.5f, new Vector3(width, (end - start).magnitude, width), material);
            item.localRotation = Quaternion.FromToRotation(Vector3.up, end - start);
        }

        private Transform MeshObject(string title, Mesh mesh, Material material, Transform parent)
        {
            ownedAssets.Add(mesh);
            var item = new GameObject(title, typeof(MeshFilter), typeof(MeshRenderer));
            item.transform.SetParent(parent, false);
            item.GetComponent<MeshFilter>().sharedMesh = mesh;
            item.GetComponent<MeshRenderer>().sharedMaterial = material;
            return item.transform;
        }

        private void Island(Vector3 data, int index, Material rock, Material grass, Material wall, Material roof, Material lamp, Material wood)
        {
            var root = new GameObject(index == 0 ? "Lantern island" : "Distant island").transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(data.x, -1, data.z);
            // Fixed seed; layout never depends on frame order or Unity's global RNG.
            var random = new System.Random(71 + index);
            for (int i = 0; i < 11; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2;
                float distance = (float)random.NextDouble() * data.y * 0.5f;
                var stone = Box("Cliff", root, new Vector3(Mathf.Cos(angle) * distance, 1, Mathf.Sin(angle) * distance),
                    new Vector3(data.y * 0.7f, 7 + (float)random.NextDouble() * 11, data.y * 0.7f), i % 3 == 0 ? grass : rock);
                stone.localRotation = Quaternion.Euler(0, angle * Mathf.Rad2Deg, 8);
            }
            if (index != 0) return;
            Box("Lighthouse tower", root, new Vector3(0, 18, 0), new Vector3(5, 24, 5), wall);
            Box("Lighthouse stripe", root, new Vector3(0, 21, 0), new Vector3(5.08f, 3, 5.08f), roof);
            Box("Lantern room", root, new Vector3(0, 31, 0), new Vector3(4, 2, 4), lamp);
            Box("Lantern roof", root, new Vector3(0, 32.8f, 0), new Vector3(7, 1.2f, 7), roof);
            Box("Landing pier", root, new Vector3(22, 2, 0), new Vector3(18, 0.5f, 4), wood);
            for (int i = 0; i < 7; i++)
            {
                foreach (int side in new[] { -1, 1 })
                    Box("Pier piling", root, new Vector3(15 + i * 2.5f, 0.6f, side * 1.8f), new Vector3(0.4f, 5, 0.4f), wood);
            }
            Box("Harbor store", root, new Vector3(11, 5.3f, -6), new Vector3(7, 6, 5), wall);
            var portRoof = Box("Red harbor roof", root, new Vector3(11, 8.6f, -6), new Vector3(8, 1, 6.2f), roof);
            portRoof.localRotation = Quaternion.Euler(0, 0, 8);
            Box("Warehouse door", root, new Vector3(14.55f, 4.4f, -6), new Vector3(0.12f, 3.5f, 2.2f), wood);
            for (int i = 0; i < 3; i++)
                Box("Dock supplies", root, new Vector3(19 + i * 1.3f, 2.85f, -0.8f), new Vector3(1, 1.2f, 1), i % 2 == 0 ? wood : roof);
        }

        private static Mesh HullMesh()
        {
            Vector3[] vertices = {
                new Vector3(-3.15f, 2, -7.7f), new Vector3(3.15f, 2, -7.7f),
                new Vector3(3.15f, 2, 6.9f), new Vector3(0, 2.5f, 9), new Vector3(-3.15f, 2, 6.9f),
                new Vector3(-1.6f, -0.7f, -6.8f), new Vector3(1.6f, -0.7f, -6.8f),
                new Vector3(1.3f, -0.7f, 5.7f), new Vector3(0, -0.3f, 7), new Vector3(-1.3f, -0.7f, 5.7f)
            };
            var triangles = new List<int>();
            for (int i = 0; i < 5; i++)
            {
                int j = (i + 1) % 5;
                triangles.AddRange(new[] { i, i + 5, j, j, i + 5, j + 5 });
            }
            triangles.AddRange(new[] { 5, 7, 6, 5, 9, 7, 7, 9, 8 });
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int swap = triangles[i + 1]; triangles[i + 1] = triangles[i + 2]; triangles[i + 2] = swap;
            }
            var mesh = new Mesh { name = "Low polygon hull", vertices = vertices, triangles = triangles.ToArray() };
            mesh.RecalculateNormals();
            return mesh;
        }

        private static Mesh SailMesh()
        {
            const int columns = 12, rows = 10;
            var vertices = new Vector3[(columns + 1) * (rows + 1)];
            var triangles = new List<int>();
            for (int y = 0; y <= rows; y++)
            for (int x = 0; x <= columns; x++)
            {
                float u = x / (float)columns, v = y / (float)rows;
                vertices[y * (columns + 1) + x] = new Vector3((u - 0.5f) * Mathf.Lerp(9, 7, v), -v * 5.8f,
                    Mathf.Sin(u * Mathf.PI) * Mathf.Sin(v * Mathf.PI) * 1.3f);
                if (x < columns && y < rows)
                {
                    int a = y * (columns + 1) + x;
                    triangles.AddRange(new[] { a, a + columns + 1, a + 1, a + 1, a + columns + 1, a + columns + 2 });
                }
            }
            var mesh = new Mesh { name = "Canvas", vertices = vertices, triangles = triangles.ToArray() };
            mesh.RecalculateNormals();
            return mesh;
        }

        private static Mesh OceanMesh()
        {
            const int cells = 128;
            var vertices = new Vector3[(cells + 1) * (cells + 1)];
            var triangles = new int[cells * cells * 6];
            int cursor = 0;
            for (int z = 0; z <= cells; z++)
            for (int x = 0; x <= cells; x++)
            {
                int a = z * (cells + 1) + x;
                vertices[a] = new Vector3((x - cells / 2) * 6, 0, (z - cells / 2) * 6);
                if (x == cells || z == cells) continue;
                triangles[cursor++] = a; triangles[cursor++] = a + cells + 1; triangles[cursor++] = a + 1;
                triangles[cursor++] = a + 1; triangles[cursor++] = a + cells + 1; triangles[cursor++] = a + cells + 2;
            }
            return new Mesh { name = "Ocean grid", vertices = vertices, triangles = triangles,
                bounds = new Bounds(Vector3.zero, new Vector3(768, 10, 768)) };
        }

        private void OnDestroy()
        {
            if (Application.isPlaying)
                foreach (Object asset in ownedAssets) if (asset != null) Destroy(asset);
            ownedAssets.Clear();
        }
    }
}

```

## Assets/TradeWinds/Runtime/PrototypeHud.cs

```csharp
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TradeWinds
{
    public sealed class PrototypeHud : MonoBehaviour
    {
        private ShipController ship;
        private DeckPlayer player;
        private GUIStyle title, label, muted, number, button;
        private readonly Color ink = new Color(0.035f, 0.09f, 0.11f, 0.93f);
        private readonly Color gold = new Color(0.88f, 0.71f, 0.42f);
        private bool lowQuality;
        private UniversalRenderPipelineAsset pipeline;
        private float originalScale;
        private float originalShadows;
        private bool visitedHelm, sailed, walked;
        private string heading = "000°", speed = "0.0", distance = "0 м";
        private float nextTextUpdate;

        public void Initialize(ShipController controller, DeckPlayer deckPlayer)
        {
            ship = controller;
            player = deckPlayer;
            pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline != null) { originalScale = pipeline.renderScale; originalShadows = pipeline.shadowDistance; }
        }

        private void Update()
        {
            visitedHelm |= player.AtHelm;
            sailed |= ship.State.Distance > 100;
            walked |= visitedHelm && !player.AtHelm && !player.NearHelm;
            if (Time.unscaledTime < nextTextUpdate) return;
            nextTextUpdate = Time.unscaledTime + 0.15f;
            heading = ship.State.Heading.ToString("000") + "°";
            speed = (ship.State.Speed * 1.944).ToString("0.0");
            distance = ship.State.Distance.ToString("0") + " м";
        }

        private void OnGUI()
        {
            if (ship == null) return;
            if (title == null) CreateStyles();
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            Matrix4x4 original = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280 * scale) / 2, (Screen.height - 720 * scale) / 2, 0), Quaternion.identity, Vector3.one * scale);

            Panel(new Rect(28, 28, 320, 86));
            GUI.Label(new Rect(48, 40, 290, 18), "ПЕРВЫЙ РЕЙС   /   ПРОТОТИП 02", muted);
            GUI.Label(new Rect(48, 62, 285, 42), "ПОПУТНЫЙ ВЕТЕР", title);
            Panel(new Rect(520, 28, 240, 76));
            GUI.Label(new Rect(541, 41, 70, 22), "КУРС", muted);
            GUI.Label(new Rect(626, 38, 115, 42), heading, number);
            Panel(new Rect(990, 28, 262, 76));
            GUI.Label(new Rect(1010, 40, 230, 22), "«КУНИЦА»  •  " + (player.Session.Active ? player.Session.CrewCount : 1) + " МАТРОС(А)", label);
            GUI.Label(new Rect(1010, 66, 230, 22), ship.State.Anchored ? "НА ЯКОРЕ" : "ПОД ПАРУСОМ", muted);

            Panel(new Rect(28, 140, 286, 176));
            GUI.Label(new Rect(48, 156, 240, 24), "ПРОБНЫЙ ВЫХОД", label);
            Check(48, 196, visitedHelm, "Встать за штурвал");
            Check(48, 230, sailed, "Пройти 100 метров");
            Check(48, 264, walked, "Пройтись по палубе на ходу");

            Panel(new Rect(28, 534, 286, 158));
            GUI.Label(new Rect(48, 548, 230, 24), player.Climbing ? "НА ЛЕСТНИЦЕ" : player.AtHelm ? "КАПИТАН" : player.Aboard ? "НА ПАЛУБЕ" : "НА СУШЕ", muted);
            GUI.Label(new Rect(48, 576, 120, 45), speed, number);
            GUI.Label(new Rect(156, 590, 135, 25), "узлов  /  " + distance, muted);
            GUI.Label(new Rect(48, 630, 230, 20), "ПАРУС   " + Mathf.RoundToInt((float)ship.State.Sail * 100) + "%", label);
            Fill(new Rect(48, 660, 244, 4), new Color(0.23f, 0.32f, 0.33f));
            Fill(new Rect(48, 660, 244 * (float)ship.State.Sail, 4), gold);

            Panel(new Rect(344, 602, 908, 90));
            string prompt = player.Climbing ? "W / S  Вверх / вниз по лестнице     ПРОБЕЛ  Выйти"
                : player.AtHelm ? "W / S  Парус     A / D  Руль     B  Якорь     E  Отпустить штурвал"
                : "WASD  Ходить     ПРОБЕЛ  Прыгать     E / F  Взаимодействие     ЛКМ  Бросок";
            GUI.Label(new Rect(364, 614, 862, 25), prompt, label);
            GUI.Label(new Rect(364, 649, 860, 25), "TAB  От третьего лица     V  Весь корабль     ESC  Меню / кооп     R  Сначала (соло)", muted);
            if (!player.ExternalView && !player.Paused) GUI.Label(new Rect(634, 350, 20, 24), "+", label);
            GUI.Label(new Rect(354, 550, 880, 42), ship.Notice, label);
            GUI.Label(new Rect(354, 510, 880, 36), player.Session.Status, muted);
            if (player.Paused) DrawPause();
            GUI.matrix = original;
        }

        private void DrawPause()
        {
            Fill(new Rect(0, 0, 1280, 720), new Color(0.02f, 0.055f, 0.07f, 0.85f));
            Panel(new Rect(390, 100, 500, 520));
            GUI.Label(new Rect(426, 130, 440, 42), "ТИХАЯ ГАВАНЬ", title);
            GUI.Label(new Rect(426, 179, 425, 40), player.Session.Active ? "Меню открыто. Сетевая игра продолжается." : "Пауза. Нажмите кнопку ниже или ESC.", muted);
            GUI.Label(new Rect(426, 231, 420, 24), "Чувствительность мыши", label);
            player.Sensitivity = GUI.HorizontalSlider(new Rect(426, 267, 420, 20), player.Sensitivity, 0.04f, 0.3f);
            GUI.Label(new Rect(426, 300, 420, 24), "Качка камеры", label);
            player.CameraRock = GUI.HorizontalSlider(new Rect(426, 336, 420, 20), player.CameraRock, 0, 1);
            if (GUI.Button(new Rect(426, 377, 420, 40), lowQuality ? "Графика: низкая · 30 FPS" : "Графика: обычная · 60 FPS", button))
            {
                lowQuality = !lowQuality;
                Application.targetFrameRate = lowQuality ? 30 : 60;
                if (pipeline != null) { pipeline.renderScale = lowQuality ? 0.7f : originalScale; pipeline.shadowDistance = lowQuality ? 0 : originalShadows; }
            }
            if (GUI.Button(new Rect(426, 429, 420, 40), ship.SeaStrength < 1 ? "Море: спокойное" : "Море: сильная качка (тест)", button))
                ship.SeaStrength = ship.SeaStrength < 1 ? 1.6f : 0.6f;
            if (GUI.Button(new Rect(426, 495, 420, 50), "ВЕРНУТЬСЯ НА ПАЛУБУ", button)) player.SetPaused(false);
            GUI.Label(new Rect(426, 568, 430, 24), "Пробел — прыжок • B — якорь у штурвала", muted);
            player.Session.DrawLobbyGUI();
        }

        private void CreateStyles()
        {
            title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
            title.normal.textColor = gold;
            label = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
            label.normal.textColor = new Color(0.9f, 0.91f, 0.83f);
            muted = new GUIStyle(label) { fontSize = 13 };
            muted.normal.textColor = new Color(0.59f, 0.71f, 0.71f);
            number = new GUIStyle(label) { fontSize = 34, fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { fontSize = 16 };
        }

        private void Check(float x, float y, bool done, string text)
        {
            GUI.Label(new Rect(x, y, 250, 30), (done ? "✓  " : "—  ") + text, done ? muted : label);
        }
        private void Panel(Rect rect) { Fill(rect, ink); Fill(new Rect(rect.x, rect.y, 2, rect.height), gold); }
        private static void Fill(Rect rect, Color color)
        {
            Color original = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = original;
        }
        private void OnDestroy()
        {
            if (pipeline != null) { pipeline.renderScale = originalScale; pipeline.shadowDistance = originalShadows; }
        }
    }
}

```

## Assets/TradeWinds/Core/ShipSimulation.cs

```csharp
using System;

namespace TradeWinds
{
    [Serializable]
    public struct ShipSnapshot
    {
        public double x, z, heading, speed, sail, rudder, distance, clock, wind;
        public bool anchored;
    }
    // Plain C# authoritative state. Input and rendering never own a second copy.
    public sealed class ShipSimulation
    {
        public double X { get; private set; }
        public double Z { get; private set; }
        public double Heading { get; private set; }
        public double Speed { get; private set; }
        public double Sail { get; private set; }
        public double Rudder { get; private set; }
        public bool Anchored { get; private set; }
        public double Distance { get; private set; }
        public double Clock { get; private set; }
        public double WindEfficiency { get; private set; }
        public const double WorldRadius = 700;

        public ShipSimulation() { Reset(); }

        public void Reset()
        {
            X = Z = Heading = Speed = Sail = Rudder = Distance = Clock = 0;
            Anchored = true;
            WindEfficiency = 1;
        }

        public void ToggleAnchor() { Anchored = !Anchored; }

        public ShipSnapshot Capture()
        {
            return new ShipSnapshot { x = X, z = Z, heading = Heading, speed = Speed, sail = Sail,
                rudder = Rudder, distance = Distance, clock = Clock, wind = WindEfficiency, anchored = Anchored };
        }

        public void Restore(ShipSnapshot state)
        {
            if (!Finite(state.x) || !Finite(state.z) || !Finite(state.heading) || !Finite(state.speed)
                || !Finite(state.sail) || !Finite(state.rudder) || !Finite(state.distance)
                || !Finite(state.clock) || !Finite(state.wind)) throw new ArgumentException("Invalid snapshot");
            X = state.x; Z = state.z; Heading = state.heading; Speed = state.speed; Sail = state.sail;
            Rudder = state.rudder; Distance = state.distance; Clock = state.clock;
            WindEfficiency = state.wind; Anchored = state.anchored;
        }

        public void Step(double dt, double steering, double sailChange)
        {
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt <= 0 || dt > 0.1)
                throw new ArgumentOutOfRangeException("dt");
            if (!Finite(steering) || !Finite(sailChange))
                throw new ArgumentException("Ship commands must be finite.");
            Clock += dt;
            Sail = Clamp(Sail + Clamp(sailChange, -1, 1) * dt * 0.3, 0, 1);
            Rudder = Approach(Rudder, Clamp(steering, -1, 1), dt * 1.8);
            WindEfficiency = 0.6 + 0.4 * Math.Cos((Heading - 35) * Math.PI / 180);
            double target = Anchored ? 0 : Sail * 8 * WindEfficiency;
            Speed = Approach(Speed, target, dt * (Anchored ? 2.5 : 0.6));
            Heading = (Heading + Rudder * 19 * Math.Min(Speed / 3, 1) * dt + 360) % 360;
            double radians = Heading * Math.PI / 180;
            double nextX = X + Math.Sin(radians) * Speed * dt;
            double nextZ = Z + Math.Cos(radians) * Speed * dt;
            if (nextX * nextX + nextZ * nextZ <= WorldRadius * WorldRadius)
            {
                X = nextX;
                Z = nextZ;
                Distance += Speed * dt;
            }
            else
            {
                Speed = 0;
                Sail = 0;
                Anchored = true;
            }
        }

        public void StopAtObstacle(double previousX, double previousZ)
        {
            X = previousX;
            Z = previousZ;
            Speed = Sail = 0;
            Anchored = true;
        }

        public static double WaveHeight(double x, double z, double time, double strength)
        {
            return strength * (Math.Sin(x * 0.075 + z * 0.12 + time * 0.9) * 0.32
                + Math.Sin(x * -0.16 + z * 0.05 + time * 1.3) * 0.18);
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(max, value)); }
        private static double Approach(double value, double target, double step)
        {
            return value < target ? Math.Min(value + step, target) : Math.Max(value - step, target);
        }
    }
}

```

## Assets/TradeWinds/Core/DeckMotor.cs

```csharp
using System;

namespace TradeWinds
{
    // Deck-relative feet position. The host can run exactly the same movement rules.
    public sealed class DeckMotor
    {
        public const float Floor = 2.15f;
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }
        public float VerticalSpeed { get; private set; }
        public bool Grounded { get; private set; }
        public DeckMotor() { Reset(); }

        public void Reset(float x = 1.5f, float z = -4.6f)
        {
            X = x; Y = Floor; Z = z; VerticalSpeed = 0; Grounded = true;
        }

        public void Step(float dt, float horizontal, float forward, float yaw, bool sprint, bool jump)
        {
            if (dt <= 0 || dt > 0.1f || float.IsNaN(dt) || float.IsNaN(yaw) || float.IsInfinity(yaw)
                || float.IsNaN(horizontal) || float.IsInfinity(horizontal)
                || float.IsNaN(forward) || float.IsInfinity(forward)) throw new ArgumentException("Invalid movement input");
            float magnitude = (float)Math.Sqrt(horizontal * horizontal + forward * forward);
            if (magnitude > 1) { horizontal /= magnitude; forward /= magnitude; }
            float angle = yaw * (float)Math.PI / 180;
            float speed = sprint ? 4.2f : 2.7f;
            float dx = ((float)Math.Cos(angle) * horizontal + (float)Math.Sin(angle) * forward) * speed * dt;
            float dz = ((float)Math.Cos(angle) * forward - (float)Math.Sin(angle) * horizontal) * speed * dt;
            if (Grounded && jump) { VerticalSpeed = 6; Grounded = false; }
            float nextX = Math.Max(-2.5f, Math.Min(2.5f, X + dx));
            if (Surface(nextX, Z) <= Y + 0.02f) X = nextX;
            float nextZ = Math.Max(-6.5f, Math.Min(6.3f, Z + dz));
            if (Surface(X, nextZ) <= Y + 0.02f) Z = nextZ;
            float floor = Surface(X, Z);
            VerticalSpeed -= 16 * dt;
            Y += VerticalSpeed * dt;
            if (Y <= floor) { Y = floor; VerticalSpeed = 0; Grounded = true; }
            else Grounded = false;
        }

        private static float Surface(float x, float z)
        {
            if (x > -0.65f && x < 0.65f && z > -0.65f && z < 0.65f) return 14;
            if (x > -0.7f && x < 0.7f && z > -4.4f && z < -3.1f) return 3.7f;
            if (z > 1.7f && z < 4.3f && ((x > -2.8f && x < -0.65f) || (x > 0.75f && x < 2.8f))) return 3.45f;
            return Floor;
        }
    }
}

```

# Setup instructions

# Ship and moving-deck game feel — Unity 6000.6 / URP / NGO

This system extends the existing FirstVoyage project. The host owns physics; clients receive complete rigidbody poses. It uses a dynamic Rigidbody ship and a swept CharacterController sailor. Do not add a Rigidbody to the sailor or run another movement script on the ship.

## Components

- `BuoyantShipBody`: sampled displaced volume, water-relative heave damping, independent forward/lateral resistance and angular damping, force-driven thrust, speed-dependent turning radius, banking, uprighting torque, interpolation, CCD and impact notification.
- `ShipController`: existing helm/sail/anchor API, authoritative physics timing, legacy island boundaries, interpolated visual-water clock and pose replication bridge. ShipSimulation remains the command/clock/HUD model; it no longer owns the physical world position.
- `ShipActor`: capsule movement, ground sphere cast, buffered jump/coyote time, acceleration, deck-frame displacement, grip-limited inertia slip, inherited launch velocity and mass-dependent loose-object impulses.
- `DeckPlayer`: frame-interpolated camera target and bounded impact recoil. Camera motion does not modify the authoritative controller.
- `CoopSession`: host-only simulation and protocol 4 full ship-pose snapshots. Both peers must run this version. Clients interpolate authoritative snapshots; this does not remove network latency and does not implement client prediction/rollback.
- `PickableItem`: force-driven carrying, CCD, ship-point velocity on throw and collision filtering for secured cargo. A kinematic secured crate must not collide with its own dynamic ship; otherwise it can inject contact energy into the hull.
- `ShipPhysicsSetup`, `ShipPlatformZone`: generated compound hull and deck attachments. Normal walking does not parent the authoritative controller to the interpolated ship. Ladder mode still uses ladder-local coordinates while the controller is disabled.

## Unity setup — follow in order

1. Open `FirstVoyage.unity`. Use a ship root with local/world scale **(1,1,1)**, metres as units, **Y up / Z forward**. Keep render meshes, primitive hull colliders, the existing ShipController and BuoyantShipBody on the same hierarchy. Rigidbody belongs on the root. Do not add non-convex MeshColliders to a dynamic body. In this prototype ShipPhysicsSetup creates the box hull and deck/rail colliders once at startup.
2. On BuoyantShipBody, assign six floater Transforms. For the current approximately 6 x 14 metre hull, use X = **-2.3 / +2.3**, Y = **-0.6**, Z = **-5.8 / 0 / +5.8**. Place pairs symmetrically at stern, midship and bow, below the desired rest waterline. The points define sample locations, not collision geometry. Their local positions are cached during configuration; reconfigure after moving them. At least four non-null points are required if the array is populated; an empty array uses these six prototype coordinates.
3. Starting buoyancy values: **10,000 kg**, **20 m³** displacement volume, **1,025 kg/m³** water density, **1.2 m** full submersion, force multiplier **1**, center of mass **(0,-0.4,0)**. Equilibrium requires displaced volume of mass/density (about 9.76 m³), so this hull rests at roughly half of its total sampled volume. Do not blindly reuse the numbers for another hull size. Distribute points across both length and beam, and verify that all points agree with the water surface used by the shader.
4. In **Edit > Project Settings > Time**, use **Fixed Timestep 0.02 s** (50 Hz); use **Maximum Allowed Timestep 0.1 s** as a starting cap after profiling your target. PrototypeWorld already selects 0.02 at runtime. A smaller timestep increases cost and requires another validation run. Sample input in Update, consume it in FixedUpdate, and move the camera in LateUpdate.
5. In **Project Settings > Physics**, use gravity **(0,-9.81,0)**. Keep a small positive default contact offset (normally **0.01**). Global solver defaults can remain unchanged: BuoyantShipBody explicitly uses **12 position / 4 velocity iterations** and maximum depenetration speed **2 m/s** on the ship. Start loose cargo at **8 / 4** if contact stacks need extra stability, then profile. Avoid enabling global Auto Sync Transforms to hide synchronization problems.
6. Set the dynamic hull to **Interpolate**, **Continuous Dynamic**, gravity enabled and no frozen rotation axes. The component configures these settings. Replica ships use **kinematic + Continuous Speculative** and Rigidbody.MovePosition/MoveRotation. Do not select Extrapolate for a ship carrying passengers: it may predict through contacts. Interpolation intentionally renders a physics tick behind; it is not a guarantee of zero latency. Never write the dynamic hull Transform every render frame.
7. In **Physics > Layer Collision Matrix**, allow **ship ↔ environment**, **ship ↔ loose cargo**, **player ↔ ship/environment/cargo**, and the required player/zone trigger pair. FirstVoyage currently uses layer 0 for hull/environment and layer 2 (Ignore Raycast) for the character and platform/hold triggers. Layer 2 still needs physical collision with layer 0; the name affects query defaults, not collision permission. ShipActor's Ground Mask excludes layer 2 and queries ignore triggers. For a larger project, create explicit Ship/Player/Cargo/Environment/InteractionTrigger layers and update masks together; this change does not silently rewrite your project collision matrix.
8. Keep the sailor's CharacterController upright: height **1.8**, radius **0.28**, center Y **0.9**, skin width **0.025**, step offset **0.22**, slope limit **55**, min move distance **0**. CharacterControllers do not expose Rigidbody CCD modes; their Move operation sweeps the capsule. The added sphere probe establishes support and slope validity. Do not parent a normal walking sailor to the interpolated rigidbody; that would apply platform motion twice. Visual avatars and camera use the interpolated deck frame.
9. Tune game feel in this order: buoyancy equilibrium → heave/roll damping → forward/lateral drag → thrust and turn radius → deck grip and momentum transfer → camera recoil. Defaults intentionally resist sideways drift more than forward motion. Lower grip allows stronger inertia slip; higher grip suppresses it. Jump height is derived from gravity rather than an unrelated upward speed. Test minimum/maximum cargo mass and representative frame rates after changing parameters.
10. Run host and client with matching **protocol 4**. Only the host/offline game applies forces and controller motion. Clients must not run another buoyancy solver. Test disconnect, late join, loss/latency and four-player sessions before release; local interpolation cannot promise flawless behavior on an arbitrary connection.

## Collision energy

PhysX computes the mass/inertia-dependent collision impulse for dynamic bodies. The hull uses low restitution to dissipate energy. Impact callbacks expose impulse divided by ship mass for camera micro-recoil; the same impulse is never reapplied as a second force. CharacterController contacts use a bounded reduced-mass impulse only on non-kinematic loose bodies and never push the ship. Stabilization is a submerged angular spring/damper, not a Transform rotation clamp; it cannot guarantee recovery from arbitrary external forces or invalid overlapping geometry.

## Cost and limitations

Six buoyancy evaluations per ship per fixed tick; no LINQ/allocations in the force loop; non-alloc ground/contact queries. Collider complexity, transparent water fill rate, networking serialization and solver settings still need target-device profiling. Water is an analytic two-wave surface shared with the current shader, not a fluid simulation or water-depth/shore model. This is an arcade sampled-volume buoyancy approximation, not exact integration of a closed hull mesh. Do not claim a universal FPS, zero jitter or zero network latency from the component alone.

Unity references: [Rigidbody interpolation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody-interpolation.html), [CharacterController.Move](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CharacterController.Move.html), [Rigidbody collision modes](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody-collisionDetectionMode.html).


