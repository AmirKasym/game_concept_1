# Two-island Unity integration

Runtime component: `Assets/TradeWinds/Runtime/IslandSceneDeployer.cs`. Target: Unity 6000.6.0f1 with URP. The component uses serialized asset references, so no Resources folders, runtime file paths, or AssetDatabase calls are required in a player build.

## Ready-to-use setup

1. Drag `Assets/TradeWinds/Art/IslandIntegration/IslandDeployment.prefab` into the scene once.
2. Set Lighthouse and Market Harbor positions, Euler rotations and uniform scales in the Inspector. Values are local to the deployer. Defaults: lighthouse (-85, 0, 70), market (85, 0, 70), rotations zero, scales one. The deployer and all its parents must have unit scale.
3. Assign an existing water MeshRenderer if required, or leave it empty to create the subdivided water plane. The prefab already references `LowPolyIslandWater.mat` and the ground physics material.
4. Enter Play. `Start` deploys both islands once. Public `Deploy()` is idempotent; `Clear()` removes the owned objects and restores an externally assigned water renderer's previous material.

Alternatively, create an empty GameObject and attach the component manually. Assign these assets:

| Inspector field | Asset |
|---|---|
| Lighthouse → Model Prefab | Art/Islands/LighthouseIsland.prefab |
| Lighthouse → Collision Prefab | Art/IslandIntegration/LighthouseCollision.fbx |
| Market Harbor → Model Prefab | Art/IslandIntegration/MarketHarbor.prefab |
| Market Harbor → Collision Prefab | Art/IslandIntegration/MarketHarbor_Collision.fbx |
| Water Material | Art/IslandIntegration/LowPolyIslandWater.mat |
| Ground Physics Material | Art/IslandIntegration/IslandGround.physicMaterial |

All table paths are relative to `Assets/TradeWinds`. The scene `Assets/TradeWinds/Scenes/IslandIntegrationDemo.unity` contains the configured deployer, camera and light. It is a deployment preview, not a replacement for FirstVoyage or a full playable voyage scene.

## Collision design

The lighthouse's original collision meshes combine disconnected buildings and curved routes. Simply enabling Convex on those aggregates would fill channels and block passages. `Tools/Blender/export_lighthouse_convex.py` prepares 216 small convex pieces instead. Market Harbor uses 20 pieces, including three corrected stair transitions. Its integration copy reaches terrace height before the wall and routes the eastern staircase clear of the house; the original Blender model/export remain intact. Box-shaped pieces become BoxColliders; all other active MeshColliders have Convex enabled. The root islands have no Rigidbody and remain static physics geometry.

Only the supplied collision geometry is used. The visual model's old colliders are disabled on the spawned copy. `COL_` renderers are hidden, and any colliders inherited from the collision prefab are replaced on that copy. The source assets remain unchanged. Collision meshes require Read/Write Enabled; the asset-specific Editor importer sets this automatically. Do not substitute a complete visual model for a convex collision asset.

These colliders represent terrain, structures, docks and walkways. Decorative foliage, small cargo and boats are not automatically turned into physical gameplay objects. Island-layer assignment uses the project's existing physics collision matrix; the component does not modify project-wide settings.

## Water

`Shaders/LowPolyIslandWater.shader` is an opaque URP shader with two vertex waves and a flat normal derived per triangle in the fragment stage. It uses no texture samples, depth textures, transparency, reflection cameras or per-frame C# updates. The generated 64 × 64 grid has 8,192 triangles and no collider. All renderers share the assigned material; `.material` instantiation is avoided.

The shader respects the water mesh's world height and exposes colors, amplitude, wavelength scale, speed and facet contrast. These are visual waves, separate from the existing ship simulation's buoyancy. An externally supplied water mesh should already have sufficient subdivisions and no unwanted solid collider. The existing CoastalSea shader and FirstVoyage water are not overwritten automatically.

## Authoring and validation

`Trade Winds → Islands → Prepare both island assets` rebuilds the generated market materials and deployment prefab. It intentionally replaces those generated assets; keep manual prefab variants separate. Blender sources and original island exports remain unchanged.

The validation entry point is `TradeWinds.Editor.IslandIntegrationSetup.PrepareAndValidate`, intended for an isolated batch Editor. It verifies duplication prevention, active convex/box physics, static roots, material assignment, shader compilation, cleanup/redeployment, and CharacterController traversal of nine lighthouse routes and four market stair ramps. See `Docs/Art/IslandIntegrationValidation.txt` for the actual results. A Unity-rendered overview is saved as `Docs/Art/IslandIntegration.png`.

These static islands should be deployed once per scene on each peer with identical placement settings. Do not also spawn a second networked copy. Network player ownership, ship navigation, purchases and dynamic cargo registration remain responsibilities of the existing gameplay systems. Target-device frame rates and a four-computer session are not certified by the Editor checks.

Final validation (2026-09-10): `ValidateAndBuild` passed in Unity 6000.6.0f1, with 236 active colliders (11 boxes), all 13 traversal routes, lifecycle/reference checks and water shader compilation passing. The Windows x86_64 Development player launched and deployed both islands and water through `Start`; its captured image was visually inspected for the corrected market palette and faceted water. See `Docs/Art/IslandIntegrationRuntimeValidation.txt`. The preview executable is under `TestResults/ValidationProject/Builds/IslandIntegration/IslandIntegration.exe`. This validates the deployment preview; multiplayer and target-device performance were not tested.
