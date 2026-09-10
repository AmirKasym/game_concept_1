using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TradeWinds.Editor
{
    public sealed class IslandIntegrationModelImport : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(IslandIntegrationSetup.Folder + "/", StringComparison.Ordinal)) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.importAnimation = importer.importCameras = importer.importLights = false;
            importer.isReadable = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = assetPath.EndsWith("MarketHarbor.fbx", StringComparison.Ordinal)
                ? ModelImporterMaterialImportMode.ImportStandard : ModelImporterMaterialImportMode.None;
        }
    }

    public static class IslandIntegrationSetup
    {
        public const string Folder = "Assets/TradeWinds/Art/IslandIntegration";
        [Serializable] private sealed class Routes { public Route[] routes; }
        [Serializable] private sealed class Route { public string name; public float width; public Vector3[] points; }

        [MenuItem("Trade Winds/Islands/Prepare both island assets")]
        public static void Prepare()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string name in new[] { "LighthouseCollision", "MarketHarbor", "MarketHarbor_Collision" })
                AssetDatabase.ImportAsset(Folder + "/" + name + ".fbx", ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var market = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/MarketHarbor.fbx"));
            try
            {
                PrefabUtility.UnpackPrefabInstance(market, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var replacements = new Dictionary<Material, Material>();
                foreach (var renderer in market.GetComponentsInChildren<MeshRenderer>())
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        Material source = materials[i];
                        if (source == null) continue;
                        if (!replacements.TryGetValue(source, out Material replacement))
                        {
                            string path = Folder + "/" + source.name + ".mat";
                            replacement = AssetDatabase.LoadAssetAtPath<Material>(path);
                            if (replacement == null) { replacement = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(replacement, path); }
                            // The bpy exporter writes this asset's palette as linear RGB.
                            Color exportedColor = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color;
                            replacement.SetColor("_BaseColor", exportedColor.gamma);
                            replacement.SetFloat("_Smoothness", .12f); EditorUtility.SetDirty(replacement);
                            replacements.Add(source, replacement);
                        }
                        materials[i] = replacement;
                    }
                    renderer.sharedMaterials = materials;
                }
                PrefabUtility.SaveAsPrefabAsset(market, Folder + "/MarketHarbor.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(market); }
            string waterPath = Folder + "/LowPolyIslandWater.mat";
            var water = AssetDatabase.LoadAssetAtPath<Material>(waterPath);
            if (water == null) { water = new Material(Shader.Find("TradeWinds/LowPolyIslandWater")); AssetDatabase.CreateAsset(water, waterPath); }
            string physicsPath = Folder + "/IslandGround.physicMaterial";
            var ground = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(physicsPath);
            if (ground == null)
            {
                ground = new PhysicsMaterial("Island ground") { dynamicFriction = .5f, staticFriction = .6f, bounciness = 0 };
                AssetDatabase.CreateAsset(ground, physicsPath);
            }
            var owner = new GameObject("Island Scene Deployer");
            try
            {
                var component = owner.AddComponent<IslandSceneDeployer>();
                var serialized = new SerializedObject(component);
                SetIsland(serialized.FindProperty("lighthouse"), "Assets/TradeWinds/Art/Islands/LighthouseIsland.prefab", Folder + "/LighthouseCollision.fbx");
                SetIsland(serialized.FindProperty("marketHarbor"), Folder + "/MarketHarbor.prefab", Folder + "/MarketHarbor_Collision.fbx");
                serialized.FindProperty("waterMaterial").objectReferenceValue = water;
                serialized.FindProperty("groundPhysicsMaterial").objectReferenceValue = ground;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(owner, Folder + "/IslandDeployment.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
            AssetDatabase.SaveAssets();
        }

        private static void SetIsland(SerializedProperty island, string visual, string collision)
        {
            island.FindPropertyRelative("modelPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(visual);
            island.FindPropertyRelative("collisionPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(collision);
        }

        public static void PrepareAndValidate()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use a separate batch Editor for validation.");
            Prepare();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/IslandDeployment.prefab"));
            var deployer = instance.GetComponent<IslandSceneDeployer>();
            var report = new StringBuilder(); int failures = 0;
            Action<bool, string> check = (ok, message) => { report.AppendLine((ok ? "PASS " : "FAIL ") + message); if (!ok) failures++; };
            deployer.Deploy();
            Transform initial = deployer.SpawnedRoot; deployer.Deploy();
            check(initial == deployer.SpawnedRoot && initial.childCount == 3, "Repeated Deploy creates no duplicates; two islands and water exist");
            var activeColliders = initial.GetComponentsInChildren<Collider>().Where(c => c.enabled).ToArray();
            check(activeColliders.Length > 17 && activeColliders.All(c => !c.isTrigger && (!(c is MeshCollider m) || m.convex)), "Active collision uses solid BoxCollider / convex MeshCollider only");
            check(initial.GetComponentsInChildren<Rigidbody>().Length == 0, "Islands remain static");
            check(initial.GetComponentsInChildren<MeshCollider>().Where(c => c.enabled).All(c => c.sharedMesh != null), "All convex meshes assigned");
            report.AppendLine("Colliders: " + activeColliders.Length + "; boxes=" + activeColliders.Count(c => c is BoxCollider));
            var routes = JsonUtility.FromJson<Routes>(File.ReadAllText("Assets/TradeWinds/Art/Islands/LighthouseIsland_Routes.json"));
            foreach (Route route in routes.routes)
                check(Walk(initial.Find("Lighthouse island"), route.points), "Lighthouse traversal: " + route.name);
            var market = initial.Find("Market harbor");
            foreach (var points in new[] {
                new[] { new Vector3(-6,-13,1.47f), new Vector3(-4,-6,4.25f) },
                new[] { new Vector3(25,-8,1.47f), new Vector3(21,-1,4.25f) },
                new[] { new Vector3(8,8,4.25f), new Vector3(8,17,7.55f) },
                new[] { new Vector3(29,5,1.47f), new Vector3(21,16,7.55f) } })
                check(Walk(market, points), "Market stair ramp: " + points[0]);
            var water = initial.Find("Low-poly water");
            check(water.GetComponent<Collider>() == null && water.GetComponent<MeshRenderer>().sharedMaterial.shader.name == "TradeWinds/LowPolyIslandWater", "Shared stylized water material assigned; no solid water collider");
            var shader = Shader.Find("TradeWinds/LowPolyIslandWater");
            check(!ShaderUtil.GetShaderMessages(shader).Any(m => m.severity.ToString() == "Error"), "Water shader compilation");
            var camera = new GameObject("Integration camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.position = new Vector3(200, 200, -230); camera.transform.LookAt(new Vector3(0, 4, 70));
            camera.orthographic = true; camera.orthographicSize = 135; camera.farClipPlane = 1500;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.53f,.65f,.70f);
            var sun = new GameObject("Integration sun", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f; sun.transform.rotation = Quaternion.Euler(45,-25,0);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.55f,.6f,.65f);
            ShaderUtil.allowAsyncCompilation = false;
            var target = new RenderTexture(1600, 1000, 24); target.Create();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            var previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(1600,1000,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,1600,1000),0,0); image.Apply();
            Directory.CreateDirectory("TestResults"); File.WriteAllBytes("TestResults/island-integration.png",image.EncodeToPNG());
            RenderTexture.active = previous; target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
            deployer.Clear(); check(!deployer.IsDeployed, "Clear removes owned deployment");
            deployer.Deploy(); check(deployer.IsDeployed, "Deploy works after Clear"); deployer.Clear();
            var external = new GameObject("Temporary external water", typeof(MeshRenderer));
            var externalRenderer = external.GetComponent<MeshRenderer>();
            var originalMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            externalRenderer.sharedMaterial = originalMaterial;
            var settings = new SerializedObject(deployer);
            settings.FindProperty("waterRenderer").objectReferenceValue = externalRenderer; settings.ApplyModifiedPropertiesWithoutUndo();
            deployer.Deploy(); check(externalRenderer.sharedMaterial != originalMaterial, "Existing water receives the assigned shared material");
            deployer.Clear(); check(externalRenderer != null && externalRenderer.sharedMaterial == originalMaterial, "Clear preserves external water and restores its material");
            settings.Update(); settings.FindProperty("waterRenderer").objectReferenceValue = null; settings.ApplyModifiedPropertiesWithoutUndo();
            UnityEngine.Object.DestroyImmediate(external); UnityEngine.Object.DestroyImmediate(originalMaterial);
            var invalidOwner = new GameObject("Temporary invalid deployment");
            var invalid = invalidOwner.AddComponent<IslandSceneDeployer>(); bool rejected = false;
            try { invalid.Deploy(); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && !invalid.IsDeployed, "Missing references fail before any partial deployment");
            UnityEngine.Object.DestroyImmediate(invalidOwner);
            EditorSceneManager.SaveScene(instance.scene, "Assets/TradeWinds/Scenes/IslandIntegrationDemo.unity");
            report.AppendLine("RESULT failures=" + failures);
            File.WriteAllText("TestResults/island-integration-validation.txt",report.ToString());
            Debug.Log("ISLAND_INTEGRATION failures=" + failures);
            if (failures != 0) throw new InvalidOperationException("Island integration validation failed.");
        }

        private static bool Walk(Transform island, Vector3[] points)
        {
            var actor = new GameObject("Temporary traversal probe");
            var controller = actor.AddComponent<CharacterController>();
            controller.height=1.8f; controller.radius=.28f; controller.center=Vector3.up*.9f;
            controller.stepOffset=.22f; controller.skinWidth=.025f; controller.slopeLimit=55;
            Func<Vector3,Vector3> world = p => island.TransformPoint(new Vector3(p.x,p.z,p.y));
            controller.enabled=false; actor.transform.position=world(points[0])+Vector3.up*.08f; controller.enabled=true;
            Physics.SyncTransforms(); bool success=true;
            for (int j=1;j<points.Length && success;j++)
            {
                Vector3 destination=world(points[j]); bool reached=false;
                int budget=Mathf.CeilToInt(Vector3.Distance(actor.transform.position,destination)/.054f)*3+100;
                for (int tick=0;tick<budget;tick++)
                {
                    Vector3 delta=destination-actor.transform.position; delta.y=0;
                    if (delta.magnitude<.16f) { reached=Mathf.Abs(actor.transform.position.y-destination.y)<.4f; break; }
                    controller.Move(Vector3.ClampMagnitude(delta,.054f)+Vector3.down*.045f);
                }
                success &= reached;
            }
            if (!success) Debug.LogWarning("Traversal blocked near " + actor.transform.position);
            UnityEngine.Object.DestroyImmediate(actor); return success;
        }

        public static void BuildRuntimePreview()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/TradeWinds/Scenes/IslandIntegrationDemo.unity" },
                locationPathName = "Builds/IslandIntegration/IslandIntegration.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Island preview build failed.");
        }

        public static void ValidateAndBuild()
        {
            PrepareAndValidate();
            BuildRuntimePreview();
        }
    }
}
