using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TradeWinds
{
    [DisallowMultipleComponent]
    public sealed class IslandSceneDeployer : MonoBehaviour
    {
        [Serializable]
        public sealed class IslandPlacement
        {
            public GameObject modelPrefab;
            [Tooltip("Separate, pre-decomposed convex collision FBX/prefab. Do not use the full visual model.")]
            public GameObject collisionPrefab;
            public Vector3 position;
            public Vector3 rotation;
            [Min(0.01f)] public float scale = 1;
        }

        [SerializeField] private bool deployOnStart = true;
        [SerializeField] private IslandPlacement lighthouse = new IslandPlacement { position = new Vector3(-85, 0, 70) };
        [SerializeField] private IslandPlacement marketHarbor = new IslandPlacement { position = new Vector3(85, 0, 70) };
        [SerializeField, Range(0, 31)] private int islandLayer;
        [SerializeField] private PhysicsMaterial groundPhysicsMaterial;
        [Header("Water (URP)")]
        [Tooltip("Optional existing water renderer. Leave empty to create a subdivided plane.")]
        [SerializeField] private MeshRenderer waterRenderer;
        [SerializeField] private Material waterMaterial;
        [SerializeField] private Vector3 waterPosition = new Vector3(0, 0, 70);
        [SerializeField, Min(1)] private float waterSize = 420;
        [SerializeField, Range(2, 128)] private int waterResolution = 64;

        private GameObject spawnedRoot;
        private Mesh ownedWaterMesh;
        private Material previousWaterMaterial;
        private MeshRenderer assignedWaterRenderer;
        public bool IsDeployed => spawnedRoot != null;
        public Transform SpawnedRoot => spawnedRoot != null ? spawnedRoot.transform : null;

        private void Start() { if (deployOnStart) Deploy(); }

        public void Deploy()
        {
            if (IsDeployed) return;
            Validate(lighthouse, "Lighthouse");
            Validate(marketHarbor, "Market harbor");
            if (waterMaterial == null) throw new InvalidOperationException("Assign the LowPolyIslandWater material.");
            if (!float.IsFinite(waterSize) || waterSize < 1 || !Finite(waterPosition))
                throw new InvalidOperationException("Water size and position must be finite; size must be at least one metre.");
            if ((transform.lossyScale - Vector3.one).sqrMagnitude > .0001f)
                throw new InvalidOperationException("The deployer and its parents must have unit scale.");
            spawnedRoot = new GameObject("Deployed islands");
            spawnedRoot.transform.SetParent(transform, false);
            try
            {
                Spawn(lighthouse, "Lighthouse island");
                Spawn(marketHarbor, "Market harbor");
                assignedWaterRenderer = waterRenderer != null ? waterRenderer : CreateWater();
                previousWaterMaterial = assignedWaterRenderer.sharedMaterial;
                assignedWaterRenderer.sharedMaterial = waterMaterial;
                Physics.SyncTransforms();
            }
            catch { Clear(); throw; }
        }

        private static void Validate(IslandPlacement placement, string label)
        {
            if (placement == null || placement.modelPrefab == null || placement.collisionPrefab == null)
                throw new InvalidOperationException(label + ": assign both model and convex collision assets.");
            if (!float.IsFinite(placement.scale) || placement.scale <= 0 || !Finite(placement.position) || !Finite(placement.rotation))
                throw new InvalidOperationException(label + ": invalid transform settings.");
            if (placement.modelPrefab.GetComponentInChildren<Rigidbody>(true) != null || placement.collisionPrefab.GetComponentInChildren<Rigidbody>(true) != null)
                throw new InvalidOperationException(label + ": static island assets must not contain a Rigidbody.");
            var meshes = placement.collisionPrefab.GetComponentsInChildren<MeshFilter>(true);
            if (meshes.Length == 0) throw new InvalidOperationException(label + ": no collision meshes found.");
            foreach (var filter in meshes)
                if (!filter.name.StartsWith("COL_", StringComparison.Ordinal) || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                    throw new InvalidOperationException(label + ": collision meshes must use COL_ names and Read/Write Enabled.");
        }

        private void Spawn(IslandPlacement placement, string label)
        {
            var island = new GameObject(label);
            island.transform.SetParent(spawnedRoot.transform, false);
            island.transform.localPosition = placement.position;
            island.transform.localRotation = Quaternion.Euler(placement.rotation);
            island.transform.localScale = Vector3.one * placement.scale;
            var visual = Instantiate(placement.modelPrefab, island.transform, false);
            // Replace the lighthouse prefab's old compound/non-convex collision.
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
                if (filter.name.StartsWith("COL_", StringComparison.Ordinal)) filter.gameObject.SetActive(false);
            var collision = Instantiate(placement.collisionPrefab, island.transform, false);
            foreach (var renderer in collision.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (var previous in collision.GetComponentsInChildren<Collider>(true)) previous.enabled = false;
            foreach (var filter in collision.GetComponentsInChildren<MeshFilter>(true))
            {
                filter.gameObject.SetActive(true);
                Collider collider;
                if (IsAxisAlignedBox(filter.sharedMesh))
                {
                    var box = filter.gameObject.AddComponent<BoxCollider>();
                    box.center = filter.sharedMesh.bounds.center;
                    box.size = filter.sharedMesh.bounds.size;
                    collider = box;
                }
                else
                {
                    var meshCollider = filter.gameObject.AddComponent<MeshCollider>();
                    meshCollider.convex = true;
                    meshCollider.sharedMesh = filter.sharedMesh;
                    collider = meshCollider;
                }
                collider.isTrigger = false;
                collider.sharedMaterial = groundPhysicsMaterial;
            }
            foreach (var child in island.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = islandLayer;
        }

        private static bool IsAxisAlignedBox(Mesh mesh)
        {
            Bounds bounds = mesh.bounds;
            if (Mathf.Min(bounds.size.x, bounds.size.y, bounds.size.z) < .001f) return false;
            int corners = 0;
            foreach (Vector3 vertex in mesh.vertices)
            {
                int corner = 0;
                for (int axis = 0; axis < 3; axis++)
                {
                    if (Mathf.Abs(vertex[axis] - bounds.max[axis]) < .0001f) corner |= 1 << axis;
                    else if (Mathf.Abs(vertex[axis] - bounds.min[axis]) >= .0001f) return false;
                }
                corners |= 1 << corner;
            }
            return corners == 255;
        }

        private MeshRenderer CreateWater()
        {
            int count = Mathf.Clamp(waterResolution, 2, 128), row = count + 1;
            var vertices = new Vector3[row * row];
            var indices = new int[count * count * 6];
            for (int z = 0; z <= count; z++)
                for (int x = 0; x <= count; x++)
                    vertices[z * row + x] = new Vector3(((float)x / count - .5f) * waterSize, 0, ((float)z / count - .5f) * waterSize);
            int index = 0;
            for (int z = 0; z < count; z++)
                for (int x = 0; x < count; x++)
                {
                    int a = z * row + x;
                    indices[index++] = a; indices[index++] = a + row; indices[index++] = a + 1;
                    indices[index++] = a + 1; indices[index++] = a + row; indices[index++] = a + row + 1;
                }
            ownedWaterMesh = new Mesh { name = "Island water grid" };
            ownedWaterMesh.vertices = vertices; ownedWaterMesh.triangles = indices;
            ownedWaterMesh.RecalculateNormals();
            ownedWaterMesh.bounds = new Bounds(Vector3.zero, new Vector3(waterSize, 20, waterSize));
            var water = new GameObject("Low-poly water", typeof(MeshFilter), typeof(MeshRenderer));
            water.transform.SetParent(spawnedRoot.transform, false); water.transform.localPosition = waterPosition;
            water.GetComponent<MeshFilter>().sharedMesh = ownedWaterMesh;
            var renderer = water.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return renderer;
        }

        public void Clear()
        {
            if (assignedWaterRenderer != null && assignedWaterRenderer.sharedMaterial == waterMaterial)
                assignedWaterRenderer.sharedMaterial = previousWaterMaterial;
            assignedWaterRenderer = null;
            if (spawnedRoot != null) { spawnedRoot.SetActive(false); Release(spawnedRoot); spawnedRoot = null; }
            if (ownedWaterMesh != null) { Release(ownedWaterMesh); ownedWaterMesh = null; }
        }
        private void OnDestroy() { Clear(); }
        private static void Release(UnityEngine.Object value)
        { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
