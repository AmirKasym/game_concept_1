using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
public static class WaterValidationBuild
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/TradeWinds/Scenes/FirstVoyage.unity");
        var shader = Shader.Find("TradeWinds/CoastalSea");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Water shader compilation failed");
        var ocean = GameObject.Find("Ocean");
        if (ocean == null) throw new Exception("FirstVoyage Ocean missing");
        var material = ocean.GetComponent<Renderer>().sharedMaterial;
        if (material.shader != shader || material.renderQueue != 3000 || material.passCount != 1)
            throw new Exception("Ocean must use the single-pass transparent CoastalSea shader");
        Directory.CreateDirectory("TestResults/Water");
        File.WriteAllText("TestResults/Water/editor.txt", "PASS FirstVoyage Ocean references CoastalSea; transparent queue 3000; one pass; shader import has no errors.\n");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/TradeWinds/Scenes/FirstVoyage.unity" },
            locationPathName = "Builds/WaterValidation/FirstVoyage.exe",
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Water validation build failed");
        File.AppendAllText("TestResults/Water/editor.txt", "PASS Windows x86_64 Development build.\n");
    }
}
