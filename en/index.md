# ShapeSync Tutorial & Documentation

Welcome! This document is the official tutorial for **ShapeSync**, a character outfit deformation toolset for Unity.

Using ShapeSync, you can register and adapt outfits (Outfit) to conform to the changing body shapes of a base character (Figure).

---

## 📦 Tutorial Supplementary Materials

Animation assets used for the initial operation check in Chapter 3.

- [CC0Animation.unitypackage](../CC0Animation.unitypackage) (CC0 1.0)

---

## 📖 Tutorial Structure (Table of Contents)

This tutorial consists of 13 chapters in total. If you are installing ShapeSync for the first time, please proceed in order from Chapter 1.

| # | Chapter | Main Tasks | Status |
| :--- | :--- | :--- | :--- |
| 1 | **[Chapter 1: Installation](./installation.md)** | Package installation. Prerequisites (Unity version, Graphics API, URP, Asset Serialization Mode) presented together | Published |
| 2 | **[Chapter 2: Initial VRM Data Generation](./initialvrm.md)** | Create Base Figure and FBM VRMs | Published |
| 3 | **[Chapter 3: Figure Registration](./figureregistration.md)** | Register Figure on Database, generate, place in Scene, and perform initial operation check | Published |
| 4 | **[Chapter 4: Outfit Registration](./outfitregistration.md)** | Create clothed VRMs, register Outfits, and check operations | Published |
| 5 | **[Chapter 5: Shape Registration](./shaperegistration.md)** | Register Morph / Skin / Hair / Outfit Shapes and check operations | Published |
| 6 | **[Chapter 6: Advanced Figure Registration / PBM](./pbmregistration.md)** | Create PBM VRMs, register to Database, redefine Morph Shape, and check operations | Published |
| 7 | **[Chapter 7: Advanced Outfit Registration / Collection](./outfitcollection.md)** | Create shoe-wearing VRMs, register to Database, configure Projection and Collection, and check operations | Published |
| 8 | **[Chapter 8: Advanced Outfit Registration / Mask](./figuremask.md)** | Create shoe-correction Mask in VRoid Studio, register Mask to Database, and check operations | Published |
| 9 | **[Chapter 9: VRM Integration](./vrmintegration.md)** | Register Expression and Physics VRMs, generate, and check operations | Published |
| 10 | **[Chapter 10: Document Storage](./documentstorage.md)** | Aggregate Shapes created so far and save Document | Published |
| 11 | **[Chapter 11: Humanoid Compiler](./humanoidcompiler.md)** | Generate Humanoid from the created Document | Published |
| 12 | **[Chapter 12: Atlas](./atlas.md)** | Configure settings in Atlas Editor and regenerate Humanoid | Published |
| 13 | **[Chapter 13: Hot Bake](./hotbake.md)** | Runtime Humanoid playback via Figure / Document / Atlas | Published |

---

## 🔗 Chapter Details and Links

### [Chapter 1: Installation](./installation.md) (Published)
* Operating environment requirements for ShapeSync (Unity 6 / URP / DX12, etc.)
* OpenUPM registry and NuGetForUnity configuration
* Installation steps for required packages (R3, ShapeSync Core)
* Initial project settings (Color Space: Linear, Asset Serialization Mode: Mixed)
* Optional: Installing VRM integration packages (UniVRM / Companion)
* **[Common Issues and Solutions (Troubleshooting)](./installation.md#6-common-issues-and-solutions-troubleshooting)**

### [Chapter 2: Initial VRM Data Generation](./initialvrm.md) (Published)
* Preparing Base VRM (`BasicFemale`) and FBM VRM (`SampleI`) using VRoid Studio
* Removing all hair, outfits, and accessories to maintain identical topology
* VRM 1.0 export and matching polygon, material, and bone counts
* Terms of use for VRoid Studio sample models
* **[Common Issues and Solutions (Troubleshooting)](./initialvrm.md#6-common-issues-and-solutions-troubleshooting)**

### [Chapter 3: Figure Registration](./figureregistration.md) (Published)
* Launching ShapeSync Editor and creating Database (`.prefab`)
* Registering Base VRM (`BasicFemale.vrm`) in the Figure section
* Naming 9 Material Entries in the Materials section
* Registering FBM axis (`SampleI` / `SampleI.vrm`) in the FBMs section (Enabling `Import All Materials and Textures`)
* Generating Figure in the Generation section (Generate)
* Placing in Scene and verifying FBM weight operations under CC0 Animation (`Walking.controller`) playback
* **[Common Issues and Solutions (Troubleshooting)](./figureregistration.md#8-common-issues-and-solutions-troubleshooting)**

### [Chapter 4: Outfit Registration](./outfitregistration.md) (Published)
* Preparing Base / FBM VRMs for custom hair (`Hair1`) and preset dress (`Dress1`) using VRoid Studio
* Registering `Hair1` / `Dress1` in the Outfits section (Mesh Outfits) of ShapeSync Editor
* Material classification in Mesh Outfit Materials (`Include` / `Exclude`)
* Assigning VRMs for the `SampleI` axis in the FBMs section
* Generating Outfit Prefabs in the Generation section (Generate)
* Configuring `OutfitAttacher` on the Scene-placed Figure and verifying attach/detach and body shape deformation tracking in Play Mode
* **[Common Issues and Solutions (Troubleshooting)](./outfitregistration.md#7-common-issues-and-solutions-troubleshooting)**

### [Chapter 5: Shape Registration](./shaperegistration.md) (Published)
* Exporting stocking texture from VRoid Studio (`Stocking.png`) and configuring `Alpha is Transparency` in Unity
* Registering Material Outfit (`Stocking`) in the Outfits section (Assigning texture name `Body`)
* Registering various Shapes in the Shapes section (Morph: `morphSampleI`, Hair: `hairSampleI`, Skin: `skinSampleI`, Outfit: `outfitSampleI`)
* Generating Shape Template asset group and catalog in the Generation section (Generate)
* Registering Templates to Figure's `ShapeDirector` and verifying linked control and operations in Play Mode (`Walking.controller`)
* **[Common Issues and Solutions (Troubleshooting)](./shaperegistration.md#10-common-issues-and-solutions-troubleshooting)**

### [Chapter 6: Advanced Figure Registration / PBM](./pbmregistration.md) (Published)
* Creating Base / FBM `.vroid` data for localized body deformation (`BreastSize`) using VRoid Studio
* Exporting PBM VRMs for base body (`BreastSizeBasicFemale.vrm`, `BreastSizeSampleI.vrm`) and outfit (`Dress1BreastSizeBasicFemale.vrm`, `Dress1BreastSizeSampleI.vrm`)
* Registering PBM (`BreastSize`) and assigning Base / FBM VRMs in the Figure section
* Configuring PBM tracking for Dress in the Outfits section (`Follow BreastSize`)
* Overriding and redefining Morph Shape (`morphSampleI`) in the Shapes section (`SampleI = 1`, `BreastSize = 0.8`)
* Executing re-Generate in the Generation section
* Verifying PBM chest deformation tracking in Play Mode using Figure's `ShapeDirector`
* **[Common Issues and Solutions (Troubleshooting)](./pbmregistration.md#10-common-issues-and-solutions-troubleshooting)**

### [Chapter 7: Advanced Outfit Registration / Collection](./outfitcollection.md) (Published)
* Customizing shoes into custom items, equipping to base body (Base / FBM), and exporting VRMs using VRoid Studio
* Checking position and posture discrepancies between base body and shoes in Scene, and overview of Collection feature
* Registering shoes (`Shoes1`) in the Outfits section and classifying body materials as `Projection`
* Exporting base body Prefab from Figure (`Collection/Shoes1`), adjusting posture (Hip Y / Foot X rotation) in Scene, and saving Overrides
* Configuring `Full` and `Use Projection for Full Collection` in the Collections section
* Registering Outfit Shape (`outfitShoes1`), generating, and verifying shoe Fit operation and remaining Poke via `ShapeDirector`
* **[Common Issues and Solutions (Troubleshooting)](./outfitcollection.md#9-common-issues-and-solutions-troubleshooting)**

### [Chapter 8: Advanced Outfit Registration / Mask](./figuremask.md) (Published)
* Checking remaining toe/sole Poke from Chapter 7 and mechanism of Figure Mask
* Creating and saving toe/sole mask texture (`Shoes1Mask.png`) using 3D Paint in VRoid Studio (`Assets/Texture/`)
* Mask polarity (Black = Hide, White = Show) and visual guide to prevent overpainting
* Registering Figure Mask in the Outfits section (`Figure Material Entry: Body`, `Mask Texture: Shoes1Mask.png`)
* Explanation of design eliminating need for individual registration in Textures section
* Re-generating in the Generation section and confirming Poke resolution in Play Mode (Before / After)
* **[Common Issues and Solutions (Troubleshooting)](./figuremask.md#5-common-issues-and-solutions-troubleshooting)**

### [Chapter 9: VRM Integration](./vrmintegration.md) (Published)
* Prerequisites for VRM integration (UniVRM package and `SHAPESYNC_USE_UNIVRM`) and criteria for skipping
* Registering Expression Reference (Base: `BasicFemale.vrm`, FBM: `SampleI.vrm`) and Physics Reference (`Hair1BasicFemale.vrm`) in the Figure section
* Explanation of optional nature of Figure-side Physics Reference (Any VRM with spring bones is acceptable)
* Configuring Physics Reference VRMs for each outfit (`Hair1` / `Dress1`) in the Outfits section
* Executing Generate in the Generation section and explanation of automatic post-processing (Expression Bake / Physics transfer)
* Verifying facial expression changes via `UniversalExpressionProxy` and spring bone (SpringBone) physics tracking under walking playback in Play Mode (Isolated component testing)
* **[Common Issues and Solutions (Troubleshooting)](./vrmintegration.md#7-common-issues-and-solutions-troubleshooting)**

### [Chapter 10: Document Storage](./documentstorage.md) (Published)
* Exporting and registering separate outfits (4 types of `Tops1`, 2 types of `Skirt1`) using VRoid Studio
* Creating Outfit Tags (`upperchest`, `lowerchest`) and registering vocabulary
* Setting Priority / Tags in Outfit Shapes (Updating `outfitTops1`, `outfitSkirt1`, `outfitSampleI`)
* Exclusive control via Priority and Tags (Displaying Dress1 alone when 3 Templates registered, displaying separate outfits when Dress is removed)
* Saving Document A (`ShapeSyncDocumentA`) and Document B (`ShapeSyncDocumentB`) via ShapeDirector (`Assets/ShapeSync`)
* Clearing Template List, Loading Document A / B, and verifying state restoration
* **[Common Issues and Solutions (Troubleshooting)](./documentstorage.md#7-common-issues-and-solutions-troubleshooting)**

### [Chapter 11: Humanoid Compiler](./humanoidcompiler.md) (Published)
* Overview of Pure Humanoid generation using Document A (`ShapeSyncDocumentA`) as input
* Launching Humanoid Compiler window and configuring inputs (Figure: `BasicFemale.prefab`, Document: `ShapeSyncDocumentA.asset`, Atlas Schema: Blank)
* Setting `Transport VRM Physics` toggle in VRM integration environment
* Specifying output destination folder (`Assets/ShapeSync/Compiler/DocumentA/`) and executing Generate
* Verifying generated assets with `DocumentA` prefix (`DocumentA.prefab`, `DocumentA.asset`, `DocumentA_avatar.asset`, Materials / Textures, etc.)
* Placing in Scene and verifying Pure Humanoid structure (standard Unity Animator / Avatar configuration without ShapeSync runtime components)
* **[Common Issues and Solutions (Troubleshooting)](./humanoidcompiler.md#6-common-issues-and-solutions-troubleshooting)**

### [Chapter 12: Atlas](./atlas.md) (Published)
* Purpose of Atlas (reducing texture count, VRAM usage, and Draw Calls) and specifications
* Launching Atlas Editor and specifying Figure (`BasicFemale.prefab`) / Document (`ShapeSyncDocumentA.asset`)
* Allocating Page 0 (body parts) and Page 1 (outfit parts)
* Configuring `ignore` setting for Hair1 considering UV overlap and checking aspect ratio warnings
* Verifying placement via `Dry Run` and saving Atlas Schema (`Assets/ShapeSync/AtlasSchema.asset`)
* Applying Atlas Schema in Humanoid Compiler and regenerating to a new folder (`Assets/ShapeSync/Compiler/AtlasA/`)
* Verifying generated assets with `AtlasA` prefix and explanation of Main Texture consolidation effect
* **[Common Issues and Solutions (Troubleshooting)](./atlas.md#7-common-issues-and-solutions-troubleshooting)**

### [Chapter 13: Hot Bake](./hotbake.md) (Published)
* Mechanism of Hot Bake (Difference between Editor pre-build and Runtime dynamic assembly)
* Adding `Animator` and `HotBake Figure` components to an Empty GameObject
* Configuring 1st character (`HotBakeFigureA`) (Figure: `BasicFemale.prefab`, Document: `ShapeSyncDocumentA.asset`, Atlas: `AtlasSchema.asset`, `Require Atlas: ON`, `Physics Transport: ON`)
* Setting `Walking.controller` and verifying 1st character solo walking in Play Mode
* Duplicating 2nd character (`HotBakeFigureB`), placing at Position `(1,0,0)`, and configuring (Document: `ShapeSyncDocumentB.asset`, Atlas: `None`, `Require Atlas: OFF`, `Physics Transport: ON`)
* Verifying simultaneous dynamic generation and parallel walking (2-character simultaneous Walking) with different outfits
* **[Common Issues and Solutions (Troubleshooting)](./hotbake.md#43-checking-when-generation-fails-troubleshooting)**

---

## 💡 Basic Terms to Know First

* **Figure**: The base 3D character model.
* **Outfit**: Outfits and accessories worn by characters.
* **ShapeSync Core**: The core package providing base features for outfit deformation tracking.
* **VRM Integration Companion**: An extension package for using ShapeSync with VRM 1.0 format avatar models (Optional).

---

*© 2026 zgock999. Released under the MIT License.*
