using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TradeWinds
{
    // Scene composition only. Generated objects are children and die with this scene.
    public sealed class PrototypeWorld : MonoBehaviour
    {
        [SerializeField] private Shader litShader;
        [SerializeField] private Shader seaShader;
        private readonly List<Object> ownedAssets = new List<Object>();
        private ShipController ship;
        private Transform ocean;

        public void Configure(Shader lit, Shader sea) { litShader = lit; seaShader = sea; }

        private void Start()
        {
            if (litShader == null || seaShader == null)
            {
                Debug.LogError("Prototype shaders are missing. Run Trade Winds > Prepare prototype.");
                enabled = false;
                return;
            }
            Application.targetFrameRate = 60;
            Time.fixedDeltaTime = 0.02f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 70;
            RenderSettings.fogEndDistance = 340;
            RenderSettings.fogColor = new Color(0.46f, 0.59f, 0.6f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.53f, 0.62f, 0.66f);
            var sun = new GameObject("Late afternoon sun").AddComponent<Light>();
            sun.transform.SetParent(transform);
            sun.type = LightType.Directional;
            sun.color = new Color(1, 0.85f, 0.64f);
            sun.intensity = 1.8f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(27, -35, 0);

            Material hull = Material("Midnight teal hull", new Color(0.08f, 0.2f, 0.22f));
            Material wood = Material("Oiled timber", new Color(0.33f, 0.21f, 0.12f));
            Material deck = Material("Honey deck", new Color(0.57f, 0.41f, 0.24f));
            Material brass = Material("Warm brass", new Color(0.76f, 0.52f, 0.22f));
            Material canvas = Material("Ivory canvas", new Color(0.86f, 0.81f, 0.64f));
            canvas.SetFloat("_Cull", 0);
            Material red = Material("Oxide red", new Color(0.55f, 0.16f, 0.1f));
            Material rock = Material("Slate", new Color(0.29f, 0.36f, 0.34f));
            Material grass = Material("Moss", new Color(0.31f, 0.4f, 0.26f));
            Material lamp = Material("Lighthouse lamp", new Color(1, 0.73f, 0.3f));
            lamp.EnableKeyword("_EMISSION");
            lamp.SetColor("_EmissionColor", new Color(1.5f, 0.8f, 0.2f));

            var shipRoot = new GameObject("Marten | coastal trader").transform;
            shipRoot.SetParent(transform);
            MeshObject("Hull", HullMesh(), hull, shipRoot);
            Box("Deck", shipRoot, new Vector3(0, 1.98f, -0.3f), new Vector3(6, 0.3f, 14.5f), deck);
            for (int i = 0; i < 22; i++)
                Box("Plank joint", shipRoot, new Vector3(0, 2.139f, -7 + i * 0.65f), new Vector3(5.98f, 0.008f, 0.025f), wood);
            foreach (int side in new[] { -1, 1 })
            {
                Box("Bulwark", shipRoot, new Vector3(side * 3.02f, 2.6f, -0.3f), new Vector3(0.18f, 1, 14.5f), hull);
                Box("Gunwale", shipRoot, new Vector3(side * 3.02f, 3.13f, -0.3f), new Vector3(0.26f, 0.12f, 14.5f), brass);
                for (int i = 0; i < 9; i++)
                    Box("Rail support", shipRoot, new Vector3(side * 2.92f, 2.65f, -6.8f + i * 1.6f), new Vector3(0.16f, 1, 0.16f), wood);
            }
            Box("Stern rail", shipRoot, new Vector3(0, 2.7f, -7.5f), new Vector3(6, 1, 0.2f), hull);
            Box("Bow rail", shipRoot, new Vector3(0, 2.7f, 7), new Vector3(6, 1, 0.2f), hull);
            Box("Mast", shipRoot, new Vector3(0, 7.5f, 0), new Vector3(0.55f, 11, 0.55f), wood);
            Box("Yard", shipRoot, new Vector3(0, 11.8f, 0.1f), new Vector3(9.5f, 0.23f, 0.23f), wood);
            var sailPivot = new GameObject("Sail top pivot").transform;
            sailPivot.SetParent(shipRoot, false);
            sailPivot.localPosition = new Vector3(0, 11.65f, 0.25f);
            MeshObject("Billowing sail", SailMesh(), canvas, sailPivot);
            Box("Pennant", shipRoot, new Vector3(0.7f, 13.1f, 0), new Vector3(1.8f, 0.6f, 0.08f), red);
            Beam("Port rigging", shipRoot, new Vector3(-2.8f, 2.7f, -3), new Vector3(0, 12.5f, 0), 0.045f, wood);
            Beam("Starboard rigging", shipRoot, new Vector3(2.8f, 2.7f, -3), new Vector3(0, 12.5f, 0), 0.045f, wood);
            Beam("Forestay", shipRoot, new Vector3(0, 2.8f, 7), new Vector3(0, 12.5f, 0), 0.045f, wood);
            Box("Helm pedestal", shipRoot, new Vector3(0, 2.8f, -3.65f), new Vector3(0.5f, 1.3f, 0.5f), wood);
            var wheel = new GameObject("Wheel").transform;
            wheel.SetParent(shipRoot, false);
            wheel.localPosition = new Vector3(0, 3.5f, -3.95f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6, b = (i + 1) * Mathf.PI / 6;
                Beam("Wheel rim", wheel, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * 0.58f,
                    new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0) * 0.58f, 0.08f, brass);
                if (i % 2 == 0) Beam("Wheel spoke", wheel, Vector3.zero,
                    new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * 0.77f, 0.07f, wood);
            }
            foreach (float x in new[] { -1.7f, 1.75f })
            {
                Box("Secured cargo", shipRoot, new Vector3(x, 2.8f, 3), new Vector3(1.45f, 1.3f, 1.8f), deck);
                foreach (float offset in new[] { -0.5f, 0.5f })
                    Box("Cargo strap", shipRoot, new Vector3(x + offset, 2.81f, 3), new Vector3(0.09f, 1.34f, 1.84f), hull);
            }
            Vector3[] islands = { new Vector3(-85, 26, 90), new Vector3(110, 32, 270), new Vector3(-175, 42, -175) };
            for (int i = 0; i < islands.Length; i++)
                Island(islands[i], i, rock, grass, canvas, red, lamp, wood);
            ship = shipRoot.gameObject.AddComponent<ShipController>();
            ship.Initialize(wheel, sailPivot, islands);

            var oceanMaterial = new Material(seaShader) { name = "Procedural sea" };
            ownedAssets.Add(oceanMaterial);
            ocean = MeshObject("Ocean", OceanMesh(), oceanMaterial, transform);
            ocean.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.gameObject.tag = "MainCamera";
            camera.transform.SetParent(transform);
            camera.fieldOfView = 72;
            camera.nearClipPlane = 0.08f;
            camera.farClipPlane = 450;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = RenderSettings.fogColor;
            var player = new GameObject("Deck sailor").AddComponent<DeckPlayer>();
            player.transform.SetParent(transform);
            player.Initialize(ship, camera);
            gameObject.AddComponent<PrototypeHud>().Initialize(ship, player);
        }

        private void LateUpdate()
        {
            if (ocean != null && ship != null)
                ocean.position = new Vector3(Mathf.Round((float)ship.State.X / 6) * 6, 0, Mathf.Round((float)ship.State.Z / 6) * 6);
        }

        private Material Material(string title, Color color)
        {
            var material = new Material(litShader) { name = title };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.18f);
            ownedAssets.Add(material);
            return material;
        }

        private Transform Box(string title, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = title;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = material;
            // Deck movement uses explicit local bounds, never mesh physics.
            Destroy(item.GetComponent<Collider>());
            return item.transform;
        }

        private void Beam(string title, Transform parent, Vector3 start, Vector3 end, float width, Material material)
        {
            var item = Box(title, parent, (start + end) * 0.5f, new Vector3(width, (end - start).magnitude, width), material);
            item.localRotation = Quaternion.FromToRotation(Vector3.up, end - start);
        }

        private Transform MeshObject(string title, Mesh mesh, Material material, Transform parent)
        {
            ownedAssets.Add(mesh);
            var item = new GameObject(title, typeof(MeshFilter), typeof(MeshRenderer));
            item.transform.SetParent(parent, false);
            item.GetComponent<MeshFilter>().sharedMesh = mesh;
            item.GetComponent<MeshRenderer>().sharedMaterial = material;
            return item.transform;
        }

        private void Island(Vector3 data, int index, Material rock, Material grass, Material wall, Material roof, Material lamp, Material wood)
        {
            var root = new GameObject(index == 0 ? "Lantern island" : "Distant island").transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(data.x, -1, data.z);
            // Fixed seed; layout never depends on frame order or Unity's global RNG.
            var random = new System.Random(71 + index);
            for (int i = 0; i < 11; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2;
                float distance = (float)random.NextDouble() * data.y * 0.5f;
                var stone = Box("Cliff", root, new Vector3(Mathf.Cos(angle) * distance, 1, Mathf.Sin(angle) * distance),
                    new Vector3(data.y * 0.7f, 7 + (float)random.NextDouble() * 11, data.y * 0.7f), i % 3 == 0 ? grass : rock);
                stone.localRotation = Quaternion.Euler(0, angle * Mathf.Rad2Deg, 8);
            }
            if (index != 0) return;
            Box("Lighthouse tower", root, new Vector3(0, 18, 0), new Vector3(5, 24, 5), wall);
            Box("Lighthouse stripe", root, new Vector3(0, 21, 0), new Vector3(5.08f, 3, 5.08f), roof);
            Box("Lantern room", root, new Vector3(0, 31, 0), new Vector3(4, 2, 4), lamp);
            Box("Lantern roof", root, new Vector3(0, 32.8f, 0), new Vector3(7, 1.2f, 7), roof);
            Box("Landing pier", root, new Vector3(22, 2, 0), new Vector3(18, 0.5f, 4), wood);
        }

        private static Mesh HullMesh()
        {
            Vector3[] vertices = {
                new Vector3(-3.15f, 2, -7.7f), new Vector3(3.15f, 2, -7.7f),
                new Vector3(3.15f, 2, 6.9f), new Vector3(0, 2.5f, 9), new Vector3(-3.15f, 2, 6.9f),
                new Vector3(-1.6f, -0.7f, -6.8f), new Vector3(1.6f, -0.7f, -6.8f),
                new Vector3(1.3f, -0.7f, 5.7f), new Vector3(0, -0.3f, 7), new Vector3(-1.3f, -0.7f, 5.7f)
            };
            var triangles = new List<int>();
            for (int i = 0; i < 5; i++)
            {
                int j = (i + 1) % 5;
                triangles.AddRange(new[] { i, i + 5, j, j, i + 5, j + 5 });
            }
            triangles.AddRange(new[] { 5, 7, 6, 5, 9, 7, 7, 9, 8 });
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int swap = triangles[i + 1]; triangles[i + 1] = triangles[i + 2]; triangles[i + 2] = swap;
            }
            var mesh = new Mesh { name = "Low polygon hull", vertices = vertices, triangles = triangles.ToArray() };
            mesh.RecalculateNormals();
            return mesh;
        }

        private static Mesh SailMesh()
        {
            const int columns = 12, rows = 10;
            var vertices = new Vector3[(columns + 1) * (rows + 1)];
            var triangles = new List<int>();
            for (int y = 0; y <= rows; y++)
            for (int x = 0; x <= columns; x++)
            {
                float u = x / (float)columns, v = y / (float)rows;
                vertices[y * (columns + 1) + x] = new Vector3((u - 0.5f) * Mathf.Lerp(9, 7, v), -v * 5.8f,
                    Mathf.Sin(u * Mathf.PI) * Mathf.Sin(v * Mathf.PI) * 1.3f);
                if (x < columns && y < rows)
                {
                    int a = y * (columns + 1) + x;
                    triangles.AddRange(new[] { a, a + columns + 1, a + 1, a + 1, a + columns + 1, a + columns + 2 });
                }
            }
            var mesh = new Mesh { name = "Canvas", vertices = vertices, triangles = triangles.ToArray() };
            mesh.RecalculateNormals();
            return mesh;
        }

        private static Mesh OceanMesh()
        {
            const int cells = 128;
            var vertices = new Vector3[(cells + 1) * (cells + 1)];
            var triangles = new int[cells * cells * 6];
            int cursor = 0;
            for (int z = 0; z <= cells; z++)
            for (int x = 0; x <= cells; x++)
            {
                int a = z * (cells + 1) + x;
                vertices[a] = new Vector3((x - cells / 2) * 6, 0, (z - cells / 2) * 6);
                if (x == cells || z == cells) continue;
                triangles[cursor++] = a; triangles[cursor++] = a + cells + 1; triangles[cursor++] = a + 1;
                triangles[cursor++] = a + 1; triangles[cursor++] = a + cells + 1; triangles[cursor++] = a + cells + 2;
            }
            return new Mesh { name = "Ocean grid", vertices = vertices, triangles = triangles,
                bounds = new Bounds(Vector3.zero, new Vector3(768, 10, 768)) };
        }

        private void OnDestroy()
        {
            foreach (Object asset in ownedAssets) if (asset != null) Destroy(asset);
            ownedAssets.Clear();
        }
    }
}
