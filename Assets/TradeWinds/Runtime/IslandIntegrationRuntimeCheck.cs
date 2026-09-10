#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TradeWinds
{
    public sealed class IslandIntegrationRuntimeCheck : MonoBehaviour
    {
        private string output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--island-check");
            if (index < 0 || index + 1 >= args.Length) return;
            new GameObject("Opt-in integration runtime check").AddComponent<IslandIntegrationRuntimeCheck>().output = args[index + 1];
        }
        private IEnumerator Start()
        {
            Application.runInBackground = true;
            for (int frame = 0; frame < 30; frame++) yield return null;
            var deployer = FindFirstObjectByType<IslandSceneDeployer>();
            var camera = FindFirstObjectByType<Camera>();
            bool success = deployer != null && deployer.IsDeployed && deployer.SpawnedRoot.childCount == 3;
            Directory.CreateDirectory(output);
            if (success && camera != null)
            {
                var target = new RenderTexture(1600, 1000, 24); target.Create();
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                var previous = RenderTexture.active; RenderTexture.active = target;
                var texture = new Texture2D(1600,1000,TextureFormat.RGB24,false);
                texture.ReadPixels(new Rect(0,0,1600,1000),0,0); texture.Apply();
                File.WriteAllBytes(Path.Combine(output,"IslandIntegration.png"),texture.EncodeToPNG());
                RenderTexture.active = previous; target.Release(); Destroy(target); Destroy(texture);
            }
            File.WriteAllText(Path.Combine(output,"runtime-check.txt"), success ? "PASS Start deployed two islands and water in a Windows player." : "FAIL Runtime deployment missing.");
            Application.Quit(success ? 0 : 1);
        }
    }
}
#endif
