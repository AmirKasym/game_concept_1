# FirstVoyage stylized water

The main game's existing Ocean material (`Assets/TradeWinds/Generated/World-13.asset`) already references `TradeWinds/CoastalSea`. Updating `Assets/TradeWinds/Shaders/CoastalSea.shader` updates FirstVoyage without rebuilding or replacing the scene. The island deployment demo has its own material and shader.

## Appearance and material settings

Select the Ocean renderer's shared material in FirstVoyage. Shader defaults:

| Property | Default | Effect |
| --- | --- | --- |
| Surface opacity | 0.82 | Soft transparency nearby; 1 makes the surface opaque in appearance. |
| Flat face normals | 0.7 | Blends triangle normals with smooth wave normals. |
| Facet color contrast | 2.5 | Controls visible low-poly color variation. |
| Highlight strength | 0.3 | Restrained, broad directional-light highlight. |
| Opaque distance | 160 m | Gradually hides distant underwater geometry. |
| Deep water / Wave crests / Soft highlight | Teal palette | Editable colors. |

The water becomes more opaque at grazing angles and toward the horizon. This is angle/distance transparency, not a depth-based shoreline fade. There is no refraction or underwater rendering pass. Use one water surface; overlapping transparent surfaces add overdraw and can produce sorting artifacts. Setting opacity to 1 does not remove the transparent pass's blending cost.

## Rendering cost

- One forward pass, back-face culling, depth testing, no depth writes, standard alpha blending.
- Zero texture samples: no normal maps, noise textures, scene-depth reads, opaque-texture copies, reflections or reflection cameras.
- Two `sincos` operations per vertex, using the existing voyage clock and exactly the same wave parameters as ship buoyancy at world water level zero. Default sea strength 0.6 gives at most 0.3 m combined displacement.
- No sine/cosine or `pow` in the fragment shader. Facet normals use screen derivatives; highlights use a bounded smoothstep and Fresnel uses multiplication.
- One main light, no additional-light loop, no shadow sampling or shadow pass. Material properties share UnityPerMaterial for SRP batching. Only the existing fog variants are compiled.
- Existing 128 x 128 ocean grid is retained (32,768 triangles); no new geometry, runtime texture generation or per-frame material instances.

Transparency still consumes fill rate and blends against the scene color. These design choices constrain cost; they do not guarantee a particular FPS across GPUs, resolutions, or scenes. No target-device GPU benchmark has been performed.

## Reusing the HLSL shader

Assign `TradeWinds/CoastalSea` to a material on a subdivided, upward-facing water mesh. Keep the material render queue at From Shader (3000). Use one directional light. The game already writes global `_VoyageTime` and `_SeaStrength` in DeckPlayer; outside this game, supply these shader globals from your existing simulation clock. A flat four-vertex plane cannot display the intended geometric wave detail.

## Reproducible validation

`Tools/WaterValidation` contains an isolated build entry point and opt-in Windows capture driver. Copy the build script into the validation project's Editor folder and the runtime script into its Runtime folder. Invoke `WaterValidationBuild.Run` in batch mode, then run the player with `--water-check <output-directory>`. The scripts are kept outside the main Assets tree and do not execute in normal gameplay.

Validated in Unity 6000.6.0f1 / Windows D3D11 on 2026-09-10: shader compilation, main-scene material binding, player build, transparency comparison, wave motion and directional highlight. No shader errors or runtime exceptions were found in the capture log. See Docs/Art/Water/Validation.txt and the adjacent screenshots. This is functional and visual validation, not a GPU performance measurement.
