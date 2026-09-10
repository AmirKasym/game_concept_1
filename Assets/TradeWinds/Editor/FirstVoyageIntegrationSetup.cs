using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TradeWinds.Editor
{
    public static class FirstVoyageIntegrationSetup
    {
        private const string ScenePath = "Assets/TradeWinds/Scenes/FirstVoyage.unity";
        private const string RootName = "Completed islands";

        [MenuItem("Trade Winds/Islands/Add completed islands to FirstVoyage")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the open scene before installing islands.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            if (GameObject.Find(RootName) != null) { Validate(); return; }
            var world = UnityEngine.Object.FindFirstObjectByType<PrototypeWorld>();
            var ship = UnityEngine.Object.FindFirstObjectByType<ShipController>();
            var ocean = GameObject.Find("Ocean").GetComponent<MeshRenderer>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(IslandIntegrationSetup.Folder + "/IslandDeployment.prefab");
            if (world == null || ship == null || prefab == null) throw new InvalidOperationException("FirstVoyage or prepared island assets missing.");
            var temporary = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var deployer = temporary.GetComponent<IslandSceneDeployer>();
            var settings = new SerializedObject(deployer);
            settings.FindProperty("waterRenderer").objectReferenceValue = ocean;
            settings.FindProperty("waterMaterial").objectReferenceValue = ocean.sharedMaterial;
            settings.ApplyModifiedPropertiesWithoutUndo();
            try
            {
                deployer.Deploy();
                var root = new GameObject(RootName).transform;
                foreach (var child in deployer.SpawnedRoot.Cast<Transform>().ToArray()) child.SetParent(root, true);
                foreach (var collider in root.GetComponentsInChildren<Collider>(true).Where(c => !c.enabled).ToArray())
                    UnityEngine.Object.DestroyImmediate(collider);
                foreach (Transform child in world.transform.Cast<Transform>().ToArray())
                    if (child.name == "Lantern island" || (child.name == "Distant island" && child.localPosition.z > 0))
                        UnityEngine.Object.DestroyImmediate(child.gameObject);
                var shipSettings = new SerializedObject(ship);
                var circles = shipSettings.FindProperty("islands"); circles.arraySize = 1;
                circles.GetArrayElementAtIndex(0).vector3Value = new Vector3(-175,42,-175);
                var roots = shipSettings.FindProperty("detailedIslandRoots"); roots.arraySize = 2;
                for (int i=0; i<2; i++) roots.GetArrayElementAtIndex(i).objectReferenceValue = root.GetChild(i);
                shipSettings.ApplyModifiedPropertiesWithoutUndo();
            }
            finally { deployer.Clear(); UnityEngine.Object.DestroyImmediate(temporary); }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Validate();
        }

        public static void Validate()
        {
            var root = GameObject.Find(RootName);
            if (root == null || root.transform.childCount != 2) throw new Exception("Two completed islands required");
            var colliders = root.GetComponentsInChildren<Collider>();
            if (colliders.Length != 236 || colliders.Any(c => c.isTrigger || (c is MeshCollider m && !m.convex)))
                throw new Exception("Island physics invalid");
            if (UnityEngine.Object.FindObjectsByType<IslandSceneDeployer>(FindObjectsSortMode.None).Length != 0)
                throw new Exception("Baked scene must not also spawn another island set");
            var ocean = GameObject.Find("Ocean").GetComponent<Renderer>();
            if (ocean.sharedMaterial.shader.name != "TradeWinds/CoastalSea") throw new Exception("Main-game water shader lost");
            if (RenderSettings.skybox == null || RenderSettings.skybox.shader.name != "TradeWinds/CoastalSky")
                throw new Exception("CoastalSky material reference is missing");
            Directory.CreateDirectory("TestResults/FirstVoyageIntegration");
            File.WriteAllText("TestResults/FirstVoyageIntegration/editor.txt", "PASS Two baked completed islands; 236 solid box/convex colliders; no runtime duplicate deployer; main CoastalSea water preserved.\n");
            foreach (Transform island in root.transform)
            {
                var bounds = island.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).Select(r => r.bounds).ToArray();
                var total = bounds[0]; foreach (var b in bounds) total.Encapsulate(b);
                File.AppendAllText("TestResults/FirstVoyageIntegration/editor.txt", island.name + " bounds: " + total + "\n");
            }
        }

        public static void InstallAndBuild()
        {
            Install();
            EditorSceneManager.OpenScene(ScenePath); Validate();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { ScenePath }, locationPathName = "Builds/IntegratedVoyage/FirstVoyage.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Integrated voyage build failed");
            File.AppendAllText("TestResults/FirstVoyageIntegration/editor.txt", "PASS Windows x86_64 build.\n");
        }
    }
}
