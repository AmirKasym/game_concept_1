using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using TradeWinds;
public static class PhysicsFeelBuild
{
    public static void Run()
    {
        var scene=EditorSceneManager.OpenScene("Assets/TradeWinds/Scenes/FirstVoyage.unity");
        var ship=UnityEngine.Object.FindFirstObjectByType<ShipController>();
        if(ship.GetComponent<BuoyantShipBody>()==null) ship.gameObject.AddComponent<BuoyantShipBody>();
        var data=new SerializedObject(ship.GetComponent<BuoyantShipBody>());
        var points=data.FindProperty("floaters");
        if(points.arraySize==0)
        {
            points.arraySize=6; int i=0;
            foreach(float z in new[]{-5.8f,0,5.8f}) foreach(float x in new[]{-2.3f,2.3f})
            {
                var point=new GameObject("Buoyancy point "+i).transform; point.SetParent(ship.transform,false);
                point.localPosition=new Vector3(x,-.6f,z); points.GetArrayElementAtIndex(i++).objectReferenceValue=point;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{scene.path},
            locationPathName="Builds/PhysicsFeel/FirstVoyage.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development });
        if(result.summary.result!=BuildResult.Succeeded) throw new Exception("Physics build failed");
    }
}
