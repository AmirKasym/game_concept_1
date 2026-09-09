using System.IO;
using UnityEditor;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace TradeWinds.Editor
{
    public static class CargoPrefabSetup
    {
        [MenuItem("Trade Winds/Prepare cargo prefab")]
        public static void Prepare()
        {
            const string folder = "Assets/TradeWinds/Resources";
            const string path = folder + "/NetworkCargo.prefab";
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            if (!File.Exists(folder + "/Splash.mat"))
            {
                var splash = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                splash.SetColor("_BaseColor", new Color(0.65f, 0.87f, 1));
                AssetDatabase.CreateAsset(splash, folder + "/Splash.mat");
            }
            if (File.Exists(path)) return;
            var cargo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cargo.name = "NetworkCargo"; cargo.transform.localScale = Vector3.one * 0.8f;
            cargo.layer = LayerMask.NameToLayer("Cargo");
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", new Color(0.5f, 0.32f, 0.16f));
            AssetDatabase.CreateAsset(material, folder + "/CargoWood.mat");
            cargo.GetComponent<Renderer>().sharedMaterial = material;
            cargo.AddComponent<NetworkObject>().AutoObjectParentSync = false;
            cargo.AddComponent<NetworkTransform>().Interpolate = true;
            cargo.AddComponent<NetworkRigidbody>();
            cargo.AddComponent<PickableItem>(); cargo.AddComponent<Buoyancy>(); cargo.AddComponent<CargoReplication>();
            PrefabUtility.SaveAsPrefabAsset(cargo, path);
            Object.DestroyImmediate(cargo);
            AssetDatabase.SaveAssets();
        }
    }
}
