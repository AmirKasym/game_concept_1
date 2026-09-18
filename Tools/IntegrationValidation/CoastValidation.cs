using System;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using TradeWinds;
public static class CoastValidation
{
    // Static scene geometry check. Runtime forces/impacts are covered by PhysicsFeelCheck.
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/TradeWinds/Scenes/FirstVoyage.unity");
        var root=GameObject.Find("Completed islands").transform;
        Physics.SyncTransforms();
        bool Overlaps(Vector3 position) => Physics.OverlapBox(position,new Vector3(2.9f,.85f,7.2f),Quaternion.identity,
            Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).Any(c=>c.transform.IsChildOf(root));
        if(Overlaps(new Vector3(0,1,0))) throw new Exception("Starting water blocked");
        string result="PASS Starting water clear of island collision.\n";
        foreach(Transform island in root)
        {
            var colliders=island.GetComponentsInChildren<Collider>();
            var contact=colliders.First(c=>c.bounds.min.y<1.5f && c.bounds.max.y>.4f).bounds.center;
            if(!Overlaps(new Vector3(contact.x,1,contact.z))) throw new Exception("No physical shore at "+island.name);
            var bounds=colliders[0].bounds; foreach(var c in colliders) bounds.Encapsulate(c.bounds);
            if(Overlaps(new Vector3(bounds.center.x,1,bounds.min.z-20))) throw new Exception("Seaward approach blocked");
            result+="PASS "+island.name+": shore geometry present; seaward approach clear.\n";
        }
        Directory.CreateDirectory("TestResults/FirstVoyageIntegration");
        File.WriteAllText("TestResults/FirstVoyageIntegration/coast.txt",result);
    }
}
