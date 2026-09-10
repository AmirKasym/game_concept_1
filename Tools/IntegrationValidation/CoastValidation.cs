using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TradeWinds;
public static class CoastValidation
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/TradeWinds/Scenes/FirstVoyage.unity");
        var ship = UnityEngine.Object.FindFirstObjectByType<ShipController>();
        var data = new SerializedObject(ship);
        ship.Initialize((Transform)data.FindProperty("wheel").objectReferenceValue,
            (Transform)data.FindProperty("sail").objectReferenceValue, new[] { new Vector3(-175,42,-175) });
        var root = GameObject.Find("Completed islands").transform;
        Physics.SyncTransforms();
        string result = "";
        ship.State.Restore(new ShipSnapshot { speed=3, sail=.5, anchored=false });
        ship.SendMessage("FixedUpdate");
        if (ship.State.Anchored || ship.State.Speed == 0) throw new Exception("Starting water blocked");
        result += "PASS Ship can move through starting water.\n";
        foreach (Transform island in root)
        {
            var colliders = island.GetComponentsInChildren<Collider>();
            var contact = colliders.First(c => c.bounds.min.y < 1.5f && c.bounds.max.y > .4f).bounds.center;
            ship.State.Restore(new ShipSnapshot { x=contact.x,z=contact.z,speed=3,sail=.5,anchored=false });
            ship.SendMessage("FixedUpdate");
            if (!ship.State.Anchored || ship.State.Speed != 0) throw new Exception("Ship failed to stop at " + island.name);
            var bounds = colliders[0].bounds; foreach(var c in colliders) bounds.Encapsulate(c.bounds);
            ship.State.Restore(new ShipSnapshot { x=bounds.center.x,z=bounds.min.z-20,speed=3,sail=.5,anchored=false });
            ship.SendMessage("FixedUpdate");
            if (ship.State.Anchored) throw new Exception("Seaward approach blocked at " + island.name);
            result += "PASS " + island.name + ": hull stops at solid shore; seaward approach remains open.\n";
        }
        File.WriteAllText("TestResults/FirstVoyageIntegration/coast.txt", result);
    }
}
