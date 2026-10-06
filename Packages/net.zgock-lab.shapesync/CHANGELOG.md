# Changelog

## [0.2.2] - 2026-10-07

- Outfits whose Extra Bone roots match exactly in structure, rest pose, FBM poses, and physics share those roots instead of being rejected. Shared roots stay until their last reference detaches.
- Database Full Collection generation reproduces the worn body: the vertex correction is built by inverse skinning under the corrected skeleton. With the projection switch off, the Collection Prefab is used when it is set.
- User-authored Database names (Figure name, Outfit Id, Material Entry names, axis names, Shape Id) must contain neither whitespace nor '_' and are rejected at save time. Mesh Outfit Material Entry names default to MaterialEntry-<slot>.
- Collection Outfit bindposes follow the corrected Figure rig.
- Outfit skinning bones are preserved, non-Root skeletons are registered, and unreadable Outfit textures are cloned instead of failing.
- Database navigation tree parent references are fixed, and saving a Collection again no longer loses its reference Meshes.
- Removing a Database Outfit also removes the Outfit references held by an optional integration.

## [0.2.1] - 2026-09-16

- Shape Director accepts an Outfit priority cutoff that hides outer Outfit layers without changing the logical Shape list.
- Morph Shape follows the Figure axis set.
- The Texture StackMachine host is reorganized into request-hall planning, admission, submission, completion, and GPU retirement stages.

## [0.2.0] - 2026-09-05

- Ship the default Texture StackMachine Factory Settings asset in the Core package.
- Removed the retired embedded net.zgock-lab.shapesync.r3-core dependency.
- Third-party binaries are not redistributed; R3 remains an external package dependency.
- Publish the resolved FBM/BCP Humanoid rest pose for Pure and Hot Bake outputs while retaining the Runtime/DDB animation contract.
