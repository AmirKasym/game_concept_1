using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using TradeWinds;
public sealed class PhysicsFeelCheck : MonoBehaviour
{
    private string output;
    private ShipController ship;
    private ShipActor actor;
    private Rigidbody body;
    private int failures;
    private readonly StringBuilder report=new StringBuilder();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var args=Environment.GetCommandLineArgs(); int i=Array.IndexOf(args,"--physics-feel-check");
        if(i>=0 && i+1<args.Length) new GameObject("Physics feel checks").AddComponent<PhysicsFeelCheck>().output=args[i+1];
    }
    private IEnumerator Start()
    {
        Application.runInBackground=true; yield return null;
        var player=FindFirstObjectByType<DeckPlayer>(); player.SetPaused(false); player.enabled=false;
        ship=FindFirstObjectByType<ShipController>(); actor=player.OfflineActor; body=ship.PhysicsBody.Body;
        ship.SeaStrength=0;
        yield return Tick(300);
        Check(!body.isKinematic && body.interpolation==RigidbodyInterpolation.Interpolate && body.collisionDetectionMode==CollisionDetectionMode.ContinuousDynamic,"Dynamic hull, interpolation and CCD configured");
        Check(Mathf.Abs(body.position.y)<.4f && Mathf.Abs(body.linearVelocity.y)<.15f,"Calm-water equilibrium",body.position+" velocity="+body.linearVelocity);
        Check(actor.Platform==ship && actor.Grounded,"Standing sailor stays grounded");
        Vector3 local=ship.PhysicsToLocal(actor.transform.position);
        ship.SeaStrength=.6f;
        yield return Tick(300);
        Check(actor.Platform==ship && Vector3.Distance(local,ship.PhysicsToLocal(actor.transform.position))<.3f,"Idle deck grip in waves",ship.PhysicsToLocal(actor.transform.position).ToString());
        body.position=new Vector3(0,0,-80); body.linearVelocity=Vector3.zero;
        body.rotation=Quaternion.Euler(0,0,30); body.angularVelocity=Vector3.zero;
        actor.Respawn(new Vector3(1.5f,2.2f,-4.6f));
        yield return Tick(300);
        Check(Vector3.Dot(body.rotation*Vector3.up,Vector3.up)>.98f,"Recover from 30 degree roll",body.rotation.eulerAngles.ToString());
        ship.SeaStrength=0; ship.ResetVoyage();
        actor.Respawn(new Vector3(1.5f,2.2f,-4.6f)); yield return Tick(120);
        var state=ship.State.Capture(); state.anchored=false; state.sail=0; ship.State.Restore(state);
        body.linearVelocity=new Vector3(3,0,3);
        yield return Tick(50);
        Check(Mathf.Abs(body.linearVelocity.x)<Mathf.Abs(body.linearVelocity.z)*.5f,"Lateral resistance exceeds forward resistance",body.linearVelocity.ToString());
        body.linearVelocity=Vector3.forward*4;
        yield return Tick(3);
        actor.Step(new CrewInput{jump=true},Time.fixedDeltaTime);
        Check(actor.Platform==null && actor.Velocity.y>0 && actor.Velocity.z>2,"Jump inherits ship momentum",actor.Velocity.ToString());
        yield return Tick(90);
        Check(actor.Platform==ship && actor.Grounded,"Jump lands back on moving deck");
        actor.Respawn(new Vector3(1.5f,2.2f,-4.6f)); yield return Tick(10);
        Vector3 before=ship.PhysicsToLocal(actor.transform.position);
        body.linearVelocity=Vector3.zero; yield return Tick(1);
        Check((ship.PhysicsToLocal(actor.transform.position)-before).magnitude>.005f,"Sudden stop transfers inertia to sailor");
        // Isolated CCD test along an empty lane away from the islands.
        body.position=new Vector3(250,0,-100); body.rotation=Quaternion.identity; body.linearVelocity=Vector3.forward*18; body.angularVelocity=Vector3.zero;
        state=ship.State.Capture(); state.anchored=false; state.sail=0; ship.State.Restore(state);
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name="CCD test wall";
        wall.transform.position=new Vector3(250,3,-85); wall.transform.localScale=new Vector3(30,10,.25f);
        Physics.SyncTransforms(); actor.Respawn(new Vector3(0,2.2f,-4.6f));
        yield return Tick(120);
        Check(body.position.z< -91 && body.linearVelocity.magnitude<4,"CCD hull contact absorbs forward motion",body.position+" velocity="+body.linearVelocity);
        Check(ship.PhysicsBody.LastImpactDeltaVelocity.magnitude>.01f,"Mass-normalized impact recorded",ship.PhysicsBody.LastImpactDeltaVelocity.ToString());
        Destroy(wall);
        ship.Paused=true; yield return Tick(3); Vector3 pausedPosition=body.position; yield return Tick(30);
        Check(Vector3.Distance(pausedPosition,body.position)<.001f,"Pause holds dynamic body steady");
        ship.Paused=false; yield return Tick(3); Check(!body.isKinematic,"Resume restores force-driven body");
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output,"physics-feel.txt"),"failures="+failures+"\n"+report);
        Application.Quit(failures==0?0:1);
    }
    private IEnumerator Tick(int count)
    {
        for(int i=0;i<count;i++) { yield return new WaitForFixedUpdate(); if(!ship.Paused) actor.Step(new CrewInput(),Time.fixedDeltaTime); }
    }
    private void Check(bool passed,string label,string details="")
    { if(!passed) failures++; report.AppendLine((passed?"PASS ":"FAIL ")+label+" "+details); Debug.Log("PHYSICS_CHECK "+(passed?"PASS ":"FAIL ")+label+" "+details); }
}

