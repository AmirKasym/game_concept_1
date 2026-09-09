using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEditor.Build.Reporting;

namespace TradeWinds.Editor
{
    public static class FeatureValidation
    {
        public static void Run()
        {
            CargoPrefabSetup.Prepare();
            EditorSceneManager.OpenScene("Assets/TradeWinds/Scenes/FirstVoyage.unity");
            EditorApplication.isPlaying = true;
        }
        public static void BuildMac()
        {
            CargoPrefabSetup.Prepare();
            Directory.CreateDirectory("Builds/macOS");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/TradeWinds/Scenes/FirstVoyage.unity" },
                locationPathName = "Builds/macOS/FirstVoyage.app", target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("macOS validation build failed");
        }
    }
}
