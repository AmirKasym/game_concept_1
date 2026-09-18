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
