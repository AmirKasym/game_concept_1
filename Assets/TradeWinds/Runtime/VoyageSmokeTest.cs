#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TradeWinds
{
    // Opt-in test driver. Exercises the actual keyboard controller in separate players.
    public sealed class VoyageSmokeTest : MonoBehaviour
    {
        private string role, output;
        private Keyboard keyboard;
        private Mouse testMouse;
        private CoopSession session;
        private DeckPlayer player;
        private bool sawPeer, sawJump, sawLanding, sawSailing, sawCarrier, sawDrop;
        private float highest;
        private readonly StringBuilder systemResults = new StringBuilder();
        private int systemFailures;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--voyage-smoke");
            if (index < 0 || index + 2 >= args.Length) return;
            var runner = new GameObject("Opt-in voyage smoke test").AddComponent<VoyageSmokeTest>();
            runner.role = args[index + 1]; runner.output = args[index + 2];
        }

        private IEnumerator Start()
        {
            yield return null;
            session = FindFirstObjectByType<CoopSession>(); player = FindFirstObjectByType<DeckPlayer>();
            if (session == null || player == null) { Debug.LogError("SMOKE: world did not start"); Application.Quit(2); yield break; }
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
            player.SetPaused(false);
            if (role == "systems") { yield return RunSystems(); yield break; }
            if (role == "host") session.StartHost(); else session.StartClient("127.0.0.1");
            float deadline = Time.realtimeSinceStartup + 20;
            while (session.LastSnapshot == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (session.LastSnapshot == null) { Finish("FAIL: no connected snapshot"); yield break; }
            yield return Keys(0.4f);
            if (role == "host")
            {
                yield return Keys(0.15f, Key.E);
                yield return Keys(0.15f);
                float pickupDeadline = Time.realtimeSinceStartup + 30;
                while (!sawCarrier && Time.realtimeSinceStartup < pickupDeadline) yield return Keys(0.1f);
                yield return Keys(0.15f, Key.B);
                yield return Keys(3.5f, Key.W);
                yield return Keys(0.15f, Key.E);
                yield return Keys(0.15f);
                yield return Keys(0.15f, Key.Space);
                yield return Keys(3f);
                yield return Keys(0.15f, Key.Tab);
                yield return Keys(0.5f);
                yield return Capture("host-overview.png");
                yield return Keys(13f);
            }
            else
            {
                yield return Keys(0.15f, Key.Space);
                yield return Keys(1f);
                yield return Keys(1.2f, Key.W);
                yield return Keys(1.1f, Key.D);
                var target = Array.Find(FindObjectsByType<PickableItem>(FindObjectsSortMode.None), item => item.Id == 0);
                float aimUntil = Time.realtimeSinceStartup + 12;
                while (!sawCarrier && Time.realtimeSinceStartup < aimUntil)
                {
                    player.LookAtPoint(target.transform.position);
                    Vector3 offset = target.transform.position - player.WorldPosition;
                    offset.y = 0;
                    if (offset.magnitude > 1.5f) yield return Keys(0.1f, Key.W);
                    else
                    {
                        yield return Keys(0.15f);
                        yield return Keys(0.1f, Key.F);
                        yield return Keys(0.2f);
                    }
                }
                if (!sawCarrier) Debug.Log("SMOKE pickup missed: player=" + player.WorldPosition + " cargo=" + target.transform.position);
                yield return Keys(5f);
                yield return Keys(0.15f, Key.F);
                yield return Keys(0.7f);
                yield return Capture("client-deck.png");
                yield return Keys(5f);
            }
            bool pass = sawPeer && sawJump && sawLanding && sawSailing && sawCarrier && sawDrop;
            Finish((pass ? "PASS" : "FAIL") + ": role=" + role + " peer=" + sawPeer + " jump=" + sawJump
                + " landing=" + sawLanding + " sailing=" + sawSailing + " carry=" + sawCarrier + " drop=" + sawDrop + " peak=" + highest);
        }

        private IEnumerator Keys(float duration, params Key[] keys)
        {
            float end = Time.realtimeSinceStartup + duration;
            while (Time.realtimeSinceStartup < end)
            {
                if (player.Paused) player.SetPaused(false);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
                Observe();
                yield return null;
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        }

        private void Observe()
        {
            var snapshot = session.LastSnapshot;
            if (snapshot == null) return;
            sawPeer |= snapshot.crew.Length >= 2; sawSailing |= snapshot.ship.speed > 0.1;
            sawCarrier |= snapshot.carrier >= 0; sawDrop |= sawCarrier && snapshot.carrier == -1;
            foreach (var pose in snapshot.crew)
            {
                if (pose.id != session.LocalClientId) continue;
                highest = Mathf.Max(highest, pose.position.y);
                sawJump |= pose.position.y > DeckMotor.Floor + 0.3f;
                sawLanding |= sawJump && pose.grounded;
            }
        }

        private IEnumerator Capture(string name)
        {
            yield return null;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            target.Create();
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            RenderPipeline.SubmitRenderRequest(Camera.main, request);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            Directory.CreateDirectory(output); File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
            Destroy(texture);
            RenderTexture.active = previous; target.Release(); Destroy(target);
        }

        private IEnumerator RunSystems()
        {
            var actor = player.OfflineActor; var ship = actor.HomeShip;
            var cargo = Array.Find(FindObjectsByType<PickableItem>(FindObjectsSortMode.None), item => item.Id == 0);
            var holdCargo = Array.Find(FindObjectsByType<PickableItem>(FindObjectsSortMode.None), item => item.Id == 2);
            var ladder = FindFirstObjectByType<LadderInteraction>();
            yield return Keys(1.2f);
            Check(actor.Platform == ship && actor.transform.parent == ship.transform, "Player attaches to the ship");
            Vector3 worldBefore = actor.transform.position; actor.Detach();
            Check(actor.transform.parent == null && Vector3.Distance(actor.transform.position, worldBefore) < 0.001f, "Detach preserves world position");
            actor.Attach(ship);
            Check(Vector3.Distance(actor.transform.position, worldBefore) < 0.001f, "Attach preserves world position");
            Check(holdCargo.State == CargoState.Secured && holdCargo.Body.isKinematic && holdCargo.transform.parent == ship.transform, "Loose cargo automatically secures in hold");

            actor.Respawn(new Vector3(1.7f, 2.2f, -3.6f));
            yield return Keys(0.3f); player.LookAtPoint(cargo.transform.position); yield return Keys(0.2f);
            yield return Keys(0.15f, Key.E); yield return Keys(0.4f);
            Check(actor.Interaction.HeldItem == cargo && cargo.isCarried && !cargo.Body.isKinematic && !cargo.Body.useGravity && cargo.transform.parent == null, "E raycast picks up a dynamic item");
            Check(actor.Interaction.SpeedMultiplier < 1, "Weight reduces walking speed");
            Vector3 carriedStart = actor.transform.position;
            yield return Keys(0.45f, Key.S);
            float carriedDistance = Vector3.Distance(carriedStart, actor.transform.position);
            Check(carriedDistance > 0.5f && carriedDistance < 1.22f, "Weighted movement is applied to the controller");
            yield return Keys(0.15f, Key.E); yield return Keys(0.1f);
            Check(!cargo.isCarried && cargo.Body.useGravity && !cargo.Body.isKinematic && cargo.Body.linearVelocity.magnitude > 1, "E releases and throws with gravity and impulse");

            cargo.ResetItem(); actor.Respawn(new Vector3(1.7f, 2.2f, -3.6f)); yield return Keys(0.5f);
            player.LookAtPoint(cargo.transform.position); yield return Keys(0.2f); yield return Keys(0.15f, Key.E); yield return Keys(0.4f);
            testMouse = InputSystem.AddDevice<Mouse>(); testMouse.MakeCurrent();
            InputSystem.QueueStateEvent(testMouse, new MouseState().WithButton(MouseButton.Left)); yield return null;
            InputSystem.QueueStateEvent(testMouse, new MouseState()); yield return Keys(0.1f);
            Check(!cargo.isCarried && cargo.Body.useGravity && cargo.Body.linearVelocity.magnitude > 1, "Left mouse button throws the carried item");

            actor.Respawn(new Vector3(0, 2.2f, 3.6f)); yield return Keys(0.3f);
            player.LookAtPoint(holdCargo.transform.position); yield return Keys(0.2f);
            yield return Keys(0.15f, Key.E); yield return Keys(0.4f);
            Check(actor.Interaction.HeldItem == holdCargo && !holdCargo.Body.isKinematic && holdCargo.transform.parent == null, "Secured cargo can be picked up again");
            actor.Interaction.Release(false); yield return Keys(2.5f);
            Check(holdCargo.State == CargoState.Secured && holdCargo.Body.isKinematic && holdCargo.transform.parent == ship.transform, "Dropped cargo settles and reattaches to the hold");

            actor.Respawn(new Vector3(0, 2.2f, -6.3f)); yield return Keys(0.3f);
            player.LookAtPoint(ladder.transform.TransformPoint(new Vector3(0, 2.1f, 0))); yield return Keys(0.2f);
            yield return Keys(0.15f, Key.E); yield return Keys(0.1f);
            Check(actor.Climbing && !actor.Controller.enabled, "E enters climbing mode");
            yield return ClimbKey(Key.S); yield return Keys(0.4f);
            Check(!actor.Climbing && actor.Controller.enabled && actor.Platform == null && actor.transform.parent == null, "Ladder bottom restores world controller on shore");
            Check(actor.transform.position.z < -8.5f && actor.transform.position.y > 0.35f, "Player lands on the pier");
            player.LookAtPoint(ladder.transform.TransformPoint(new Vector3(0, 1.3f, 0))); yield return Keys(0.2f);
            yield return Keys(0.15f, Key.E); yield return ClimbKey(Key.W); yield return Keys(0.2f);
            Check(!actor.Climbing && actor.Controller.enabled && actor.Platform == ship && actor.transform.parent == ship.transform, "Ladder top restores ship parenting");
            yield return Keys(0.15f, Key.Tab); yield return Keys(0.3f); yield return Capture("systems-character-and-pier.png");
            Finish((systemFailures == 0 ? "PASS" : "FAIL") + ": systems failures=" + systemFailures + "\n" + systemResults);
        }

        private void Check(bool passed, string name)
        {
            if (!passed) systemFailures++;
            systemResults.AppendLine((passed ? "PASS " : "FAIL ") + name);
        }

        private IEnumerator ClimbKey(Key key)
        {
            float deadline = Time.realtimeSinceStartup + 3;
            while (player.Climbing && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
                yield return null;
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        private void Finish(string result)
        {
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, role + "-smoke.txt"), result);
            Debug.Log("SMOKE " + result);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (testMouse != null) InputSystem.RemoveDevice(testMouse);
            Application.Quit(result.StartsWith("PASS") ? 0 : 1);
        }
    }
}
#endif
