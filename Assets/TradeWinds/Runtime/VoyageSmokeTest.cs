#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace TradeWinds
{
    // Opt-in test driver. Exercises the actual keyboard controller in separate players.
    public sealed class VoyageSmokeTest : MonoBehaviour
    {
        private string role, output;
        private Keyboard keyboard;
        private CoopSession session;
        private DeckPlayer player;
        private bool sawPeer, sawJump, sawLanding, sawSailing, sawCarrier, sawDrop;
        private float highest;

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
            if (role == "host") session.StartHost(); else session.StartClient("127.0.0.1");
            float deadline = Time.realtimeSinceStartup + 20;
            while (session.LastSnapshot == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (session.LastSnapshot == null) { Finish("FAIL: no connected snapshot"); yield break; }
            if (role == "host")
            {
                yield return Keys(0.15f, Key.E);
                yield return Keys(0.15f);
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
                yield return Keys(0.15f, Key.F);
                yield return Keys(0.6f, Key.W);
                yield return Keys(0.15f, Key.F);
                yield return Keys(0.7f);
                yield return Capture("client-deck.png");
                yield return Keys(2f);
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
                sawLanding |= sawJump && Mathf.Abs(pose.position.y - DeckMotor.Floor) < 0.01f;
            }
        }

        private IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); texture.Apply();
            Directory.CreateDirectory(output); File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
            Destroy(texture);
        }

        private void Finish(string result)
        {
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, role + "-smoke.txt"), result);
            Debug.Log("SMOKE " + result);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            Application.Quit(result.StartsWith("PASS") ? 0 : 1);
        }
    }
}
#endif
