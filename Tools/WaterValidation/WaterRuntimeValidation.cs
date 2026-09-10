using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
public sealed class WaterRuntimeValidation : MonoBehaviour
{
    private string output;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, "--water-check");
        if (i < 0 || i + 1 >= args.Length) return;
        new GameObject("Water validation").AddComponent<WaterRuntimeValidation>().output = args[i + 1];
    }
    private IEnumerator Start()
    {
        Application.runInBackground = true;
        yield return new WaitForSecondsRealtime(2);
        Directory.CreateDirectory(output);
        FindFirstObjectByType<TradeWinds.DeckPlayer>().SetPaused(false);
        yield return new WaitForSecondsRealtime(1);
        var ocean = GameObject.Find("Ocean");
        var material = ocean.GetComponent<Renderer>().sharedMaterial;
        if (!material.shader.isSupported || material.renderQueue != 3000) { Application.Quit(2); yield break; }
        Capture(Camera.main, "FirstVoyage-water.png");
        var camera = new GameObject("Water detail camera").AddComponent<Camera>();
        camera.CopyFrom(Camera.main); camera.enabled = false;
        camera.transform.position = new Vector3(26, 12, -26);
        camera.transform.LookAt(new Vector3(0, 0, 0));
        Capture(camera, "Water-overview.png");
        camera.transform.position = new Vector3(25, 9, 10);
        camera.transform.LookAt(new Vector3(25, 0, 23));
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = new Vector3(25, -.8f, 23); cube.transform.localScale = new Vector3(5, .6f, 5);
        var marker = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        marker.color = new Color(1, .8f, .3f); cube.GetComponent<Renderer>().sharedMaterial = marker;
        Capture(camera, "Water-transparency.png");
        material.SetFloat("_Opacity", 1);
        Capture(camera, "Water-opaque-comparison.png");
        material.SetFloat("_Opacity", .82f);
        yield return new WaitForSecondsRealtime(2);
        Capture(camera, "Water-motion.png");
        var sun = Array.Find(FindObjectsByType<Light>(FindObjectsSortMode.None), light => light.type == LightType.Directional);
        if (sun != null) { var center = new Vector3(30,0,30); camera.transform.position = center + Vector3.Reflect(sun.transform.forward, Vector3.up) * 25; camera.transform.LookAt(center); Capture(camera, "Water-highlight.png"); }
        File.WriteAllText(Path.Combine(output,"runtime.txt"), "PASS FirstVoyage water rendered with supported transparent shader in Windows D3D11; captured gameplay, overview, submerged marker, opaque comparison and later wave frame. Visual inspection required.\n");
        Destroy(cube); Destroy(marker); Destroy(camera.gameObject);
        Application.Quit(0);
    }
    private void Capture(Camera camera, string name)
    {
        var target = new RenderTexture(1280,720,24); target.Create();
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        var previous = RenderTexture.active; RenderTexture.active = target;
        var texture = new Texture2D(1280,720,TextureFormat.RGB24,false);
        texture.ReadPixels(new Rect(0,0,1280,720),0,0); texture.Apply();
        File.WriteAllBytes(Path.Combine(output,name),texture.EncodeToPNG());
        RenderTexture.active = previous; Destroy(texture); target.Release(); Destroy(target);
    }
}

