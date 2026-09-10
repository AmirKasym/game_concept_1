using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TradeWinds.Editor
{
    // This importer is scoped to this single authored asset, never other FBX files.
    public sealed class LighthouseIslandModelImport : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetPath != LighthouseIslandImport.ModelPath) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = true; // Collider/route validation reads meshes in the Editor.
        }
    }

    public static class LighthouseIslandImport
    {
        public const string Folder = "Assets/TradeWinds/Art/Islands";
        public const string ModelPath = Folder + "/LighthouseIsland.fbx";
        private const string PrefabPath = Folder + "/LighthouseIsland.prefab";

        [Serializable] private sealed class RouteSet { public Route[] routes; }
        [Serializable] private sealed class Route { public string name; public float width; public Vector3[] points; }

        [MenuItem("Trade Winds/Islands/Build Lighthouse prefab")]
        public static void Prepare()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            string texturePath = Folder + "/Textures/Lighthouse_Atlas.png";
            var textureImporter = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            if (textureImporter == null) throw new InvalidOperationException("Lighthouse atlas is missing.");
            textureImporter.textureType = TextureImporterType.Default;
            textureImporter.sRGBTexture = true;
            textureImporter.mipmapEnabled = true;
            textureImporter.wrapMode = TextureWrapMode.Clamp;
            textureImporter.maxTextureSize = 1024;
            textureImporter.anisoLevel = 4;
            textureImporter.SaveAndReimport();
            string materialPath = Folder + "/Lighthouse_Atlas.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.12f);
            EditorUtility.SetDirty(material);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (source == null) throw new InvalidOperationException("Lighthouse FBX failed to import.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = "LighthouseIsland";
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    filter.gameObject.SetActive(true);
                    filter.gameObject.isStatic = true;
                    var renderer = filter.GetComponent<MeshRenderer>();
                    bool collision = filter.name.StartsWith("COL_", StringComparison.Ordinal);
                    if (renderer != null)
                    {
                        renderer.enabled = !collision;
                        renderer.sharedMaterials = Enumerable.Repeat(material, filter.sharedMesh.subMeshCount).ToArray();
                    }
                    if (collision)
                    {
                        var collider = filter.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = filter.sharedMesh;
                        collider.convex = false; // Static island: never attach a dynamic Rigidbody.
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
                AssetDatabase.SaveAssets();
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
            Debug.Log("LIGHTHOUSE_PREFAB_READY " + PrefabPath);
        }

        [MenuItem("Trade Winds/Islands/Add Lighthouse to current scene")]
        public static void AddToScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Prepare(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Add lighthouse island");
            Selection.activeGameObject = instance;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        // Batch entry point: validate the imported meshes and traversable route surfaces.
        public static void PrepareAndValidate()
        {
            Prepare();
            if (!Application.isBatchMode) throw new InvalidOperationException("Run route validation in a separate batch Editor.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
            var report = new StringBuilder();
            int failures = 0, triangles = 0, collisionTriangles = 0;
            Action<bool, string> check = (ok, message) => { report.AppendLine((ok ? "PASS " : "FAIL ") + message); if (!ok) failures++; };
            try
            {
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
                {
                    bool col = filter.name.StartsWith("COL_", StringComparison.Ordinal);
                    var mesh = filter.sharedMesh;
                    if (col) collisionTriangles += mesh.triangles.Length / 3; else triangles += mesh.triangles.Length / 3;
                    check((filter.transform.lossyScale - Vector3.one).sqrMagnitude < 0.0001f, filter.name + " scale 1,1,1");
                    if (!col) check(mesh.uv.Length == mesh.vertexCount && filter.GetComponent<MeshRenderer>().sharedMaterial.mainTexture != null, filter.name + " atlas/UV present");
                }
                check(triangles < 60000, "Visual triangle budget: " + triangles);
                check(collisionTriangles < 6000, "Collider triangle budget: " + collisionTriangles);
                check(instance.GetComponentsInChildren<Rigidbody>().Length == 0, "Static geometry has no dynamic Rigidbody");
                Physics.SyncTransforms();
                var transforms = instance.GetComponentsInChildren<Transform>();
                Func<string, Vector3> marker = name => transforms.First(t => t.name == name).position;
                Vector3 origin = marker("SeaLevel");
                // Derive the actual FBX coordinate conversion instead of assuming importer handedness.
                var from = Matrix4x4.identity;
                from.SetColumn(0, new Vector4(1,-40,0,0));
                from.SetColumn(1, new Vector4(1,-34,1.25f,0));
                from.SetColumn(2, new Vector4(-2,15,11.3f,0));
                var to = Matrix4x4.identity;
                to.SetColumn(0, marker("DockApproach") - origin);
                to.SetColumn(1, marker("DockLanding") - origin);
                to.SetColumn(2, marker("LighthouseView") - origin);
                var conversion = to * from.inverse;
                check(Vector3.Distance(conversion.MultiplyVector(Vector3.forward), Vector3.up) < .001f, "FBX up axis is Unity +Y");
                report.AppendLine("DockLanding Unity: " + marker("DockLanding"));
                var routesPath = Folder + "/LighthouseIsland_Routes.json";
                var routes = JsonUtility.FromJson<RouteSet>(File.ReadAllText(routesPath));
                var physics = scene.GetPhysicsScene();
                foreach (var route in routes.routes)
                {
                    int gaps = 0, blocked = 0;
                    for (int j = 1; j < route.points.Length; j++)
                    {
                        Vector3 a = origin + conversion.MultiplyPoint3x4(route.points[j-1]);
                        Vector3 b = origin + conversion.MultiplyPoint3x4(route.points[j]);
                        int steps = Mathf.CeilToInt(Vector3.Distance(a,b) / .4f);
                        for (int i = 0; i <= steps; i++)
                        {
                            Vector3 p = Vector3.Lerp(a,b,(float)i/steps);
                            if (!physics.Raycast(p + Vector3.up * .35f, Vector3.down, out RaycastHit hit, .8f)) gaps++;
                            // Headroom for a 1.8 m FPS player; upward ray catches overhangs.
                            if (physics.Raycast(p + Vector3.up * .15f, Vector3.up, out _, 1.75f)) blocked++;
                        }
                    }
                    check(gaps == 0 && blocked == 0, route.name + " continuous floor/headroom; gaps=" + gaps + ", blocked=" + blocked);
                    var walker = new GameObject("Temporary FPS traversal probe");
                    SceneManager.MoveGameObjectToScene(walker, scene);
                    var controller = walker.AddComponent<CharacterController>();
                    controller.height = 1.8f; controller.radius = .28f; controller.center = Vector3.up * .9f;
                    controller.skinWidth = .025f; controller.stepOffset = .22f; controller.slopeLimit = 55;
                    controller.enabled = false;
                    walker.transform.position = origin + conversion.MultiplyPoint3x4(route.points[0]) + Vector3.up * .08f;
                    controller.enabled = true;
                    Physics.SyncTransforms();
                    bool walked = true;
                    for (int j = 1; j < route.points.Length && walked; j++)
                    {
                        Vector3 destination = origin + conversion.MultiplyPoint3x4(route.points[j]);
                        int budget = Mathf.CeilToInt(Vector3.Distance(walker.transform.position, destination) / .054f) * 3 + 100;
                        bool reached = false;
                        for (int tick = 0; tick < budget; tick++)
                        {
                            Vector3 delta = destination - walker.transform.position; delta.y = 0;
                            if (delta.magnitude < .16f) { reached = Mathf.Abs(walker.transform.position.y - destination.y) < .4f; break; }
                            controller.Move(Vector3.ClampMagnitude(delta, .054f) + Vector3.down * .045f);
                        }
                        walked &= reached;
                    }
                    check(walked, route.name + " traversed by 1.8 m CharacterController; end=" + walker.transform.position);
                    UnityEngine.Object.DestroyImmediate(walker);
                }
                report.AppendLine("RESULT failures=" + failures);
                Directory.CreateDirectory("TestResults");
                File.WriteAllText("TestResults/lighthouse-unity-validation.txt", report.ToString());
                Debug.Log("LIGHTHOUSE_VALIDATION failures=" + failures + " visualTriangles=" + triangles);
                if (failures > 0) throw new InvalidOperationException("Lighthouse validation failed; see TestResults/lighthouse-unity-validation.txt");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
    }
}
