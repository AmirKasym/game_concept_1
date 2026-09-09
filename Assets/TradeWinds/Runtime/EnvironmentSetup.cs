using UnityEngine;

namespace TradeWinds
{
    public static class EnvironmentSetup
    {
        public static void Install(Transform world, ShipController ship, Material material)
        {
            int cargo = LayerMask.NameToLayer("Cargo");
            Physics.IgnoreLayerCollision(cargo, cargo, true);
            foreach (Transform root in world)
            {
                if (root.name != "Lantern island" && root.name != "Distant island") continue;
                foreach (var mesh in root.GetComponentsInChildren<MeshFilter>())
                {
                    mesh.gameObject.layer = LayerMask.NameToLayer("Island");
                    if (mesh.GetComponent<Collider>() == null)
                        mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
                }
                Point(root, "DockPoint", new Vector3(29, 2.3f, 0));
                Point(root, "VendorSpawnPoint", new Vector3(19, 2.3f, 0));
                Point(root, "CargoSpawnPoint", new Vector3(23, 2.8f, 0));
                if (root.Find("Landing pier") == null)
                {
                    var pier = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    pier.name = "Landing pier"; pier.transform.SetParent(root, false);
                    pier.transform.localPosition = new Vector3(22, 2, 0);
                    pier.transform.localScale = new Vector3(18, 0.5f, 4);
                    pier.layer = LayerMask.NameToLayer("Island");
                    pier.GetComponent<Renderer>().sharedMaterial = material;
                }
            }
            foreach (Transform child in world)
                if (child.name == "Walkable shore") child.gameObject.layer = LayerMask.NameToLayer("Island");
            var water = Volume(world, "WaterZone", new Vector3(0, -25, 0), new Vector3(20000, 50, 20000));
            water.gameObject.layer = LayerMask.NameToLayer("Water"); water.gameObject.AddComponent<WaterZone>();
            var kill = Volume(world, "KillZone", new Vector3(0, -55, 0), new Vector3(20000, 10, 20000));
            kill.gameObject.layer = 2; kill.gameObject.AddComponent<KillZone>();
            Point(ship.transform, "DeckSpawnPoint", new Vector3(1.5f, 2.2f, -4.6f));
            var rope = Volume(ship.transform, "Rescue rope", new Vector3(3.6f, 0, -4.8f), new Vector3(0.8f, 6, 0.8f));
            rope.center = Vector3.up;
            rope.gameObject.AddComponent<LadderInteraction>().ConfigureRescue(ship);
            for (int side = -1; side <= 1; side += 2)
            {
                var line = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                line.name = "Rope strand"; line.transform.SetParent(rope.transform, false);
                line.transform.localPosition = new Vector3(0, 0.85f, side * 0.28f);
                line.transform.localScale = new Vector3(0.055f, 2.65f, 0.055f);
                line.GetComponent<Renderer>().sharedMaterial = material;
                Object.Destroy(line.GetComponent<Collider>());
            }
            for (int i = 0; i < 16; i++)
            {
                var rung = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rung.name = "Rope foothold"; rung.transform.SetParent(rope.transform, false);
                rung.transform.localPosition = new Vector3(0, -1.7f + i * 0.34f, 0);
                rung.transform.localScale = new Vector3(0.09f, 0.06f, 0.65f);
                rung.GetComponent<Renderer>().sharedMaterial = material;
                Object.Destroy(rung.GetComponent<Collider>());
            }
            var helm = Volume(ship.transform, "Helm interaction", new Vector3(0, 3.4f, -3.95f), new Vector3(1.5f, 1.5f, 0.25f));
            helm.gameObject.AddComponent<HelmInteraction>();
        }

        private static BoxCollider Volume(Transform parent, string name, Vector3 position, Vector3 size)
        {
            var box = new GameObject(name).AddComponent<BoxCollider>();
            box.transform.SetParent(parent, false); box.transform.localPosition = position;
            box.size = size; box.isTrigger = true; return box;
        }
        private static void Point(Transform parent, string name, Vector3 position)
        {
            if (parent.Find(name) != null) return;
            var point = new GameObject(name).transform;
            point.SetParent(parent, false); point.localPosition = position;
        }
    }
}
