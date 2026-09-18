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
