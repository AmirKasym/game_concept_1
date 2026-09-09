using System.Collections.Generic;
using UnityEngine;

namespace TradeWinds
{
    public static class ShipPhysicsSetup
    {
        public static PickableItem[] Install(ShipController ship, Transform world, Transform existingCrate, Material material)
        {
            var rigidbody = ship.gameObject.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true; rigidbody.useGravity = false; rigidbody.mass = 10000;
            foreach (Transform child in ship.GetComponentsInChildren<Transform>())
            {
                if (child.name == "Stern rail") { child.gameObject.SetActive(false); continue; }
                if (child.name == "Deck" || child.name == "Mast" || child.name == "Secured cargo" || child.name == "Helm pedestal"
                    || child.name == "Bulwark" || child.name == "Stern rail" || child.name == "Bow rail")
                    if (child.GetComponent<Collider>() == null) child.gameObject.AddComponent<BoxCollider>();
            }
            foreach (int side in new[] { -1, 1 })
                MakeBox("Stern rail beside boarding gap", ship.transform, new Vector3(side * 1.85f, 2.7f, -7.5f), new Vector3(2.3f, 1, 0.2f), material, new Color(0.08f, 0.2f, 0.22f), true);
            var deckZone = new GameObject("ShipPlatformZone").AddComponent<BoxCollider>();
            deckZone.transform.SetParent(ship.transform, false); deckZone.center = new Vector3(0, 4, -0.3f); deckZone.size = new Vector3(6.6f, 6, 15.3f);
            deckZone.gameObject.AddComponent<ShipPlatformZone>().Configure(ship);

            var hold = new GameObject("CargoHoldZone").AddComponent<BoxCollider>();
            hold.transform.SetParent(ship.transform, false); hold.center = new Vector3(0, 2.9f, 5.4f); hold.size = new Vector3(5.1f, 1.7f, 2.1f);
            hold.gameObject.AddComponent<CargoHoldZone>().Configure(ship);
            MakeBox("Marked cargo hold", ship.transform, new Vector3(0, 2.145f, 5.4f), new Vector3(4.9f, 0.025f, 2), material, new Color(0.15f, 0.48f, 0.43f), false);

            var ladder = new GameObject("Кормовая лестница · E, W/S").AddComponent<BoxCollider>();
            ladder.transform.SetParent(ship.transform, false); ladder.transform.localPosition = new Vector3(0, 0, -8.2f);
            ladder.center = new Vector3(0, 1.8f, 0); ladder.size = new Vector3(1.4f, 3.8f, 0.65f);
            ladder.gameObject.AddComponent<LadderInteraction>().Configure(ship);
            for (int i = 0; i < 8; i++) MakeBox("Ladder rung", ladder.transform, new Vector3(0, 0.4f + i * 0.32f, 0), new Vector3(1.1f, 0.1f, 0.12f), material, new Color(0.76f, 0.55f, 0.25f), false);
            foreach (int side in new[] { -1, 1 }) MakeBox("Ladder rail", ladder.transform, new Vector3(side * 0.55f, 1.65f, 0), new Vector3(0.12f, 2.9f, 0.15f), material, new Color(0.42f, 0.27f, 0.14f), false);
            MakeBox("Starter landing pier", world, new Vector3(0, 0.25f, -13.5f), new Vector3(5, 0.5f, 9), material, new Color(0.47f, 0.32f, 0.2f), true);
            MakeBox("Walkable shore", world, new Vector3(0, 0, -26), new Vector3(20, 1, 18), material, new Color(0.39f, 0.48f, 0.29f), true);
            for (int i = 0; i < 5; i++)
                foreach (int side in new[] { -1, 1 }) MakeBox("Pier post", world, new Vector3(side * 2.2f, -0.2f, -10 - i * 1.6f), new Vector3(0.3f, 2.2f, 0.3f), material, new Color(0.3f, 0.21f, 0.12f), true);

            var items = new List<PickableItem>();
            var prefab = Resources.Load<GameObject>("NetworkCargo");
            if (prefab == null) throw new System.InvalidOperationException("NetworkCargo prefab is missing. Run Trade Winds > Prepare cargo prefab.");
            var first = Object.Instantiate(prefab, existingCrate.position, existingCrate.rotation).GetComponent<PickableItem>();
            existingCrate.gameObject.SetActive(false);
            first.Configure(0, ship, "Ящик припасов", 8); items.Add(first);
            var second = Object.Instantiate(prefab, ship.transform.TransformPoint(new Vector3(-1.5f, 2.6f, -1.8f)), ship.transform.rotation).GetComponent<PickableItem>();
            second.Configure(1, ship, "Инструменты", 25); items.Add(second);
            var third = Object.Instantiate(prefab, ship.transform.TransformPoint(new Vector3(0, 2.6f, 5.4f)), ship.transform.rotation).GetComponent<PickableItem>();
            third.Configure(2, ship, "Торговый груз", 18); items.Add(third);
            Physics.SyncTransforms();
            return items.ToArray();
        }

        private static GameObject MakeBox(string title, Transform parent, Vector3 position, Vector3 scale, Material material, Color color, bool collision)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube); item.name = title;
            item.transform.SetParent(parent, false); item.transform.localPosition = position; item.transform.localScale = scale;
            var renderer = item.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            var properties = new MaterialPropertyBlock(); properties.SetColor("_BaseColor", color); renderer.SetPropertyBlock(properties);
            if (!collision) Object.Destroy(item.GetComponent<Collider>());
            return item;
        }
    }
}
