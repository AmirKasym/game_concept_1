# Market Harbor — game exports

The existing build_market_harbor.py generator was executed in Blender 5.2.1. Both formats were exported and re-imported successfully. See ExportValidation.json.

Choose one format:
- FBX: MarketHarbor.fbx + MarketHarbor_Collision.fbx.
- glTF: MarketHarbor.gltf + MarketHarbor.bin, and MarketHarbor_Collision.gltf + MarketHarbor_Collision.bin. Keep each .gltf beside its .bin.

Visual model: 21 meshes, 24,984 triangles, flat normals and basic materials.
Collision: 17 named COL_ meshes, 352 triangles. Collision geometry covers the main static terrain, buildings, docks and stair ramps; small decorative props are excluded.

Both files use the same origin. Import visual and collision roots at identical position, rotation and scale. Collision is supplied separately so it does not cover the visible model with rendered hulls. In Unity, disable renderers on the COL_ objects and add static MeshColliders referencing their meshes. Do not attach a dynamic Rigidbody to the whole island. Material shader conversion to the project's render pipeline may be required after FBX import.

Exports use metres and Y-up. No presentation cameras or lights are included. glTF contains material colors directly and needs no texture images.

MarketHarbor_ExportSource.blend preserves the generated source, materials and separate collision collection. The original MarketHarbor.blend was not overwritten.

Validation: mesh counts, triangle counts, unit scales, material presence, collider names, absence of cameras/lights, and matching FBX/glTF dimensions after round-trip import. Target-engine gameplay and physics behavior were not tested in this export pass.
