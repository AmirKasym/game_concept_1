using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TradeWinds.Editor
{
    [InitializeOnLoad]
    public static class PrototypeSetup
    {
        private const string ScenePath = "Assets/TradeWinds/Scenes/FirstVoyage.unity";
        private const string SettingsPath = "Assets/TradeWinds/Settings";
        static PrototypeSetup() { EditorApplication.delayCall += AutoPrepare; }

        private static void AutoPrepare()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += AutoPrepare;
                return;
            }
            if (!File.Exists(ScenePath)) Prepare();
        }

        [MenuItem("Trade Winds/Prepare prototype")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before preparing assets.");
            Directory.CreateDirectory(SettingsPath);
            Directory.CreateDirectory("Assets/TradeWinds/Scenes");
            AssetDatabase.Refresh();
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(SettingsPath + "/CoastalPipeline.asset");
            if (pipeline == null)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, SettingsPath + "/CoastalRenderer.asset");
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.renderScale = 1;
                pipeline.msaaSampleCount = 2;
                pipeline.shadowDistance = 70;
                pipeline.supportsHDR = false;
                AssetDatabase.CreateAsset(pipeline, SettingsPath + "/CoastalPipeline.asset");
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            QualitySettings.vSyncCount = 0;
            PlayerSettings.companyName = "Harbor Workshop";
            PlayerSettings.productName = "Trade Winds - First Voyage";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            if (!File.Exists(ScenePath))
            {
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                Shader sea = Shader.Find("TradeWinds/CoastalSea");
                if (lit == null || sea == null) throw new InvalidOperationException("Wait for shader/package import, then prepare again.");
                Scene previous = SceneManager.GetActiveScene();
                bool replaceEmpty = previous.IsValid() && previous.path == "" && !previous.isDirty;
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                new GameObject("First Voyage | procedural scene").AddComponent<PrototypeWorld>().Configure(lit, sea);
                EditorSceneManager.SaveScene(scene, ScenePath);
                if (replaceEmpty) EditorSceneManager.CloseScene(previous, true);
                else
                {
                    if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
            // This is a new project. Preserve additional scenes if the project is extended later.
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == ScenePath)) scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("First Voyage prepared. Open Assets/TradeWinds/Scenes/FirstVoyage.unity and press Play.");
        }

        [MenuItem("Trade Winds/Build Windows prototype")]
        public static void BuildWindows()
        {
            Prepare();
            Directory.CreateDirectory("Builds/Windows");
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/FirstVoyage.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build failed: " + report.summary.result);
        }
    }
}
