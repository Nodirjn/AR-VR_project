# Third-Party Notices

The MIT License in [LICENSE](LICENSE) covers this project's own code and content. The works listed here come from third parties and keep their own licences; the MIT License does not relicense them. Items marked **Open issue** have an unresolved licensing or provenance question that should be settled before the repository or a build is distributed further.

| Content | Path | Licence | Status |
| --- | --- | --- | --- |
| CarConcept model | `Assets/Models/CarConcept.glb` | CC BY 4.0 | Attribution below; no in-app credit |
| Display posters (renders of the CarConcept) | `Assets/Materials/Realism/Interior/Textures/Interior_Poster_*.jpg` | CC BY 4.0 (adaptation of the CarConcept) | Same attribution as the CarConcept; no in-app credit |
| Planar-UV copies of CarConcept meshes (calipers, windscreen) | `Assets/Materials/Realism/Cars/Meshes/` | CC BY 4.0 (adaptation of the CarConcept) | Same attribution as the CarConcept; no in-app credit |
| BMW M3 E30, BMW M4 Competition, Tesla Model 3 models | `Assets/Models/ShowroomCollection/` | CC BY 4.0 | Attribution below and in `CREDITS.md` |
| Compressed copies of the showroom collection's textures | `Assets/Materials/Realism/Cars/Textures/` | CC BY 4.0 (part of those models) | Same attribution as the models |
| Remy character (Mixamo) | `Assets/Models/Presenter/Remy.fbx` | Adobe Mixamo terms | **Open issue** |
| Textures derived from Remy | `Assets/Models/Presenter/Textures/` | Adobe Mixamo terms | **Open issue** (same as Remy) |
| V6 engine model | `Assets/Models/V6_Engine_Prototype.glb` | Unknown | **Open issue** |
| Merged copies of the V6's meshes | `Assets/Models/CarInternals/V6/` | Unknown (derived from the V6) | **Open issue** (same as the V6) |
| Maya character | `Assets/Models/ExteriorPedestrians/Maya_RelaxedWalk.glb` | CC0 1.0 (embedded metadata) | Not used |
| Poly Haven textures and HDRI | `Assets/Environment/` | CC0 1.0 | No attribution required |
| Poly Haven textures and models for the interior | `Assets/ThirdParty/PolyHaven/` | CC0 1.0 | No attribution required; listed below |
| ambientCG textures for the interior | `Assets/ThirdParty/ambientCG/` | CC0 1.0 | No attribution required; listed below |
| Inter font | `Assets/VRTemplateAssets/Fonts/Inter/` | SIL OFL 1.1 | Licence text missing from the folder |
| Liberation Sans font | `Assets/TextMesh Pro/Fonts/` | SIL OFL 1.1 | Licence text included |
| VR template content | `Assets/VRTemplateAssets/`, `Assets/Scenes/BasicScene.unity` | Unity template terms | See below |
| XR Interaction Toolkit 3.5.1 samples | `Assets/Samples/XR Interaction Toolkit/3.5.1/` | Unity Companion License | — |
| XR Hands 1.8.1 sample | `Assets/Samples/XR Hands/1.8.1/` | Unity Package Distribution License | **Open issue** |
| Planar reflective deck shader (modified copy of URP 17.3's Lit shader) | `Assets/Scripts/Rendering/AurellePlanarReflectiveLit.shader`, `AurellePlanarReflection.hlsl` | Unity Companion License | See below |
| Unity packages | `Packages/manifest.json` (downloaded, not stored in the repository) | Per package | See below |

## 3D models

### CarConcept

- **File:** `Assets/Models/CarConcept.glb`, used as the explorer car `CarConcept` in `Assets/Scenes/SampleScene.unity`. The app calls it "Aurelle CarConcept"; Aurelle is this project's fictional dealership, not the model's author.
- **Author:** Eric Chadwick, Darmstadt Graphics Group GmbH, 2024.
- **Licence:** [Creative Commons Attribution 4.0 International (CC BY 4.0)](https://creativecommons.org/licenses/by/4.0/).
- **Notice embedded in the file (`asset.copyright`):** "© 2015, Khronos Group, Khronos logo. © 2017, Khronos Group, 3D Commerce logo. Asset is CC BY 4.0 International, created by Eric Chadwick of Darmstadt Graphics Group GmbH, 2024"
- **Source:** the CarConcept model of the Khronos [glTF Sample Assets](https://github.com/KhronosGroup/glTF-Sample-Assets/tree/main/Models/CarConcept). The repository copy was processed with RapidPipeline 3D Processor v7.1.0 (its glTF `generator` field) and uses KTX2 textures.
- **Trademarks:** the model shows the Khronos and 3D Commerce logos, and its tyre sidewalls carry "KHRONOS" lettering. CC BY 4.0 does not license trademarks (section 2(b)(2)). In SampleScene the car tool replaces the tyre sidewalls with generated maps carrying AURELLE SPORT lettering, the rear plate's KHRONOS logo atlas with a generated AURELLE / MOTOR HOUSE plate, and the calipers' logo-atlas texture with a generated AURELLE logo. The GLB itself is unchanged and keeps the logos, and the display posters below, rendered with the car's earlier materials (which kept the sidewall's relief), still show the GLB's lettering.
- **Changes in this project:**
  - In SampleScene the realism car tool (`CarsRealism`) overrides its materials with materials from `Assets/Materials/Realism/Cars`. These reuse some of the model's own textures (for example the tread and the second-UV ambient occlusion), drop the paint's tiled noise normal map, and use textures generated by the tool's own code for the paint flakes, the tyre sidewalls, the brake discs, the interior grains, the instrument display, the rear plate, the calipers and the windscreen frit.
  - The tool writes copies of the caliper (`Wheel*BrakePad`) and windscreen (`BodyWindshield`) meshes with planar first UVs to `Assets/Materials/Realism/Cars/Meshes/` and assigns them in the scene. They keep the model's vertices and are adaptations of it, under the same licence and attribution.
  - Two renderers are switched off (`Engine`, and `Axles`, which the generated half shafts replace), the car is positioned on the showroom podium, and `V6_Engine_Prototype(Clone)` and the generated `Internals` subtree are added as children.
  - The render tool rasterises the car's meshes at rest into the blurred contact-shadow texture `Assets/Materials/Realism/Rendering/AurelleHeroContactShadow.png`, a footprint that holds no texture data from the model.
  - At runtime the Car Explorer moves, separates, lays out, darkens and highlights its parts, and in its X-ray view temporarily replaces the body shell's materials with a glass shader (`Assets/Resources/AurelleXRayGhost.shader`); the original materials are put back unchanged. The planar reflection moves the car's renderers to layer 30 while it runs and puts their layers back.

### Display posters (renders of the CarConcept)

- **Files:** `Assets/Materials/Realism/Interior/Textures/Interior_Poster_1.jpg`, `Interior_Poster_2.jpg` and `Interior_Poster_3.jpg`, the emission maps of the three rear-wall display screens in SampleScene.
- **Origin:** studio renders of the CarConcept model, made in this project with `Assets/Editor/AurelleRealism/InteriorPosterRenderer.cs`. Renders 2 and 3 repaint the body silver and blue. They were rendered with the car's earlier materials and have not been rendered again since its current look.
- **Licence:** adaptations of the CarConcept (CC BY 4.0). They need the same attribution, with a note that the model was rendered and modified: "CarConcept" by Eric Chadwick, Darmstadt Graphics Group GmbH, [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The app shows no such credit. For the trademarks in the renders, see the CarConcept entry above.

### Showroom collection

`Assets/Models/ShowroomCollection/CREDITS.md` records the following credits, reproduced unchanged:

> These models are shared under Creative Commons Attribution 4.0 International (CC BY 4.0). The GLB files retain their source `asset.extras` attribution. The scene placement and model presentation may be adapted; attribute the original author, link the source and license, and note modifications.
>
> #### BMW M4 Competition M Package
>
> - Author: 𝙎𝙍𝙏 𝙋𝙚𝙧𝙛𝙤𝙧𝙢𝙖𝙣𝙘𝙚™ ([Sketchfab profile](https://sketchfab.com/TheRealSRT))
> - Source model: [BMW M4 Competition M Package](https://sketchfab.com/3d-models/bmw-m4-competition-m-package-5c0a2dafb1ad408d9fc9eeef9aee531b)
> - License: [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)
> - Downloaded file: `BMW_M4_Competition.glb` (22,739,084 bytes; SHA-256 `913ac951ba2645777c91393b86aed2def2433a807e57e626b203908aa474b8fb`)
> - Changes: none to source GLB; copied from the public derivative repository listed below.
> - Public file source: [lukaizj/car-mod-saas](https://github.com/lukaizj/car-mod-saas/blob/main/public/models/bmw-m4.glb)
>
> #### BMW M3 E30
>
> - Author: Martin Trafas ([Sketchfab profile](https://sketchfab.com/Bexxie))
> - Source model: [BMW M3 E30](https://sketchfab.com/3d-models/free-bmw-m3-e30-ac3c7013434e403e8faff87948caf422)
> - License: [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)
> - Original downloaded file: 2,975,260 bytes; SHA-256 `4806a9b24954a784efc349b6b2ab2b0c347f3802422f1baf1054f45e44a933d1`.
> - Imported file: `BMW_M3_E30.glb` (43,658,244 bytes; SHA-256 `8cb5a6c6651f146e7488848083e218b6c7bb46ac527743c166c3a6a78b7cbaea`).
> - Changes: decoded `EXT_meshopt_compression`, dequantized `KHR_mesh_quantization`, converted the 20 embedded WebP images to PNG, and updated GLB buffer/image/extension references. Kept all 50 nodes, 26 meshes, 10 materials, UV attributes, and embedded source metadata. Conversion used glTF Transform 4.5.1; the original file is retained outside the project during this work.
> - Public file source: [ahnuchen/auto-atlas](https://github.com/ahnuchen/auto-atlas/blob/master/public/models/free_bmw_m3_e30.glb)
>
> #### Tesla Model 3 (2018)
>
> - Author: Ameer Studio ([Sketchfab profile](https://sketchfab.com/uchiha.321abc))
> - Source model: [Tesla 2018 Model 3](https://sketchfab.com/3d-models/tesla-2018-model-3-5ef9b845aaf44203b6d04e2c677e444f)
> - License: [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)
> - Original downloaded file: 2,957,832 bytes; SHA-256 `093aa601ff9e8cdef7046da551cbb7de4f9a35b3023c94e55462f7d21fca8456`.
> - Imported file: `Tesla_Model_3_2018.glb` (11,087,184 bytes; SHA-256 `dee38cea2312074a3fef63201d26f49ad5853873cd3bbc4d6fb12d8bef909ecd`).
> - Changes: decoded `EXT_meshopt_compression`, dequantized `KHR_mesh_quantization`, converted the 17 embedded WebP images to PNG, and updated GLB buffer/image/extension references. Kept all 287 nodes, 162 meshes, 30 materials, UV attributes, and embedded source metadata. Conversion used glTF Transform 4.5.1; the original file is retained outside the project during this work.
> - Public file source: [ahnuchen/auto-atlas](https://github.com/ahnuchen/auto-atlas/blob/master/public/models/tesla_2018_model_3.glb)
>
> The public repositories provide downloadable copies and attribution records. They are distribution mirrors; the model authors and source pages above remain the creators and license sources. These models are independent works and are not endorsements by BMW or Tesla. Only `EXT_meshopt_compression`, `EXT_texture_webp`, and `KHR_mesh_quantization` were removed during conversion; no project package versions were changed.

**Use in this project:**

- The three models stand on the displays of `Showroom Premium Collection` in SampleScene (the Tesla display is turned so the car faces out).
- The exterior builder also wraps each model in a traffic prefab in `Assets/Prefabs/ExteriorTraffic` (scaled to 4.5 m length, the Tesla turned 180°) for the district's moving and parked cars.
- The realism car tool (`CarsRealism`) overrides their materials in the scene and in the traffic prefabs with new paint, glass, tyre, trim and interior materials, and gives some traffic cars other paint colours.
- The materials use resized, compressed copies of the models' own textures (at most 1024 px) in `Assets/Materials/Realism/Cars/Textures/E30`, `M4` and `Tesla`. These copies are modified parts of the CC BY 4.0 models and need the same attribution. The GLB files themselves are unchanged.

### Remy (Mixamo) — open issue

- **File:** `Assets/Models/Presenter/Remy.fbx`, a Mixamo export of the character Remy ("Mixamo, Inc." and "mixamo.com" in its metadata) with embedded textures.
- **Used by:** `Remy Presenter` in SampleScene; the exterior builder also instantiates it for the eight pedestrians and the two cyclists' riders.
- **Licence:** Adobe's Mixamo terms let you use Mixamo characters and animations in your projects, but, according to Adobe's published guidance, not distribute the raw character or animation files.
- **Derived files:** `Assets/Models/Presenter/Textures/` holds 19 textures that the realism people tool (`AurelleRealismPeople`) derives from the maps embedded in `Remy.fbx`. They are converted, partly downsampled and remapped; the crowd's neutral garment and hair textures are made from them too. `Remy.fbx.meta` also remaps the model's materials to `Assets/Materials/Realism/People`.
- **Open issue:** committing the raw FBX to a public repository distributes it, and the derived textures share the issue. Remove the FBX from the public repository (including its history) and have contributors download it from Mixamo, keep the repository private, or replace it with a character whose licence allows redistribution. If `Remy.fbx` is removed, remove `Assets/Models/Presenter/Textures/` as well. The tool derives missing textures again from `Assets/Models/Presenter/Remy.fbx`.

### V6 engine — open issue

- **File:** `Assets/Models/V6_Engine_Prototype.glb`, used as `V6_Engine_Prototype(Clone)` under `CarConcept` in SampleScene (drawn by the Car Explorer in its X-ray and exploded views and when the engine or the front section is taken out).
- **Provenance:** unknown. The file records only its exporter ("Open CASCADE Technology 7.8") and has no author, copyright or licence information; its node names (for example "Horse 1^Engine assembly") look like a CAD assembly export.
- **Derived files:** the car internals tool (`V6MeshMerge`) writes 37 copies of the model's meshes, each merged into one submesh, to `Assets/Models/CarInternals/V6/`, with `V6_mesh_map.txt` recording each copy's source mesh, and the scene's V6 renderers use them. They contain the model's geometry and share its open issue.
- **Open issue:** record the source and licence of the model, or replace it with a model whose licence is known. If the model is removed, remove `Assets/Models/CarInternals/V6/` as well.

### Maya character

- **File:** `Assets/Models/ExteriorPedestrians/Maya_RelaxedWalk.glb`, not referenced by any scene or asset.
- **Licence:** CC0 1.0 according to its embedded metadata (node "Maya character": `source_license` "CC0-1.0 graphical assets; authored rest character", `motion_source` "Original authored mathematical IK recipes; no capture data", `capture_data_used` false). The node extras show it was built from MPFB (MakeHuman) assets and exported with the Khronos glTF Blender I/O add-on.

## Textures and sky

- **Poly Haven surface maps** in `Assets/Environment/Textures/PolyHaven`: [asphalt_02](https://polyhaven.com/a/asphalt_02) and [granular_concrete](https://polyhaven.com/a/granular_concrete), 2K, licensed [CC0 1.0](https://polyhaven.com/license) as recorded in `README.txt` there. The `*_metallic_smoothness.png` files are packing textures derived from them.
- **Poly Haven HDRI** `Assets/Environment/HDRI/Kloppenheim_06_PureSky_2K.hdr`: [Kloppenheim 06 (Pure Sky)](https://polyhaven.com/a/kloppenheim_06_puresky), CC0 1.0. It is the scene's skybox through `Assets/Materials/Exterior/Kloppenheim_Sunset_Skybox.mat`.
- The textures in `Assets/Materials/Exterior/Textures` are generated by `ExteriorEnvironmentBuilder` and are project content.

## Interior textures and models (Poly Haven, ambientCG)

The showroom interior uses the CC0 assets below. They are dedicated to the public domain under [CC0 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/), so no attribution is required. The licence pages are [Poly Haven's](https://polyhaven.com/license) and [ambientCG's](https://docs.ambientcg.com/license/). Each asset has its own folder under `Assets/ThirdParty/PolyHaven/` or `Assets/ThirdParty/ambientCG/`, with a `SOURCE.txt` that records the source page, the licence and the changes.

Changes made in this project:

- The textures are resized to 1K. The granite colour and normal maps and the concrete and black-marble colour maps are 2K.
- The `*_mask_*.png` and `*_Mask.png` maps pack metallic (R), ambient occlusion (G) and smoothness (A, from roughness) for URP Lit.
- The Poly Haven `.gltf` files have their texture references removed, because materials are assigned in Unity; their `.bin` buffers are unchanged.
- Only the maps the interior uses are included; for `modern_arm_chair_01`, that is the leg maps and the cushion normal map.

Poly Haven's preview renders (`*_preview_thumb_*.png`) are not covered by CC0 and are not included.

| Asset | Kind | Licence | Used for | Source |
| --- | --- | --- | --- | --- |
| `granite_tile` | Texture set | CC0 1.0 | Showroom floor | [polyhaven.com/a/granite_tile](https://polyhaven.com/a/granite_tile) |
| `concrete_wall_001` | Texture set | CC0 1.0 | Feature wall | [polyhaven.com/a/concrete_wall_001](https://polyhaven.com/a/concrete_wall_001) |
| `white_stucco` | Texture set | CC0 1.0 | Rear wall, ceiling, planters | [polyhaven.com/a/white_stucco](https://polyhaven.com/a/white_stucco) |
| `synthetic_wood` | Texture set | CC0 1.0 | Slat wall behind the reception desk | [polyhaven.com/a/synthetic_wood](https://polyhaven.com/a/synthetic_wood) |
| `modern_arm_chair_01` | Model (glTF) | CC0 1.0 | Lounge armchairs | [polyhaven.com/a/modern_arm_chair_01](https://polyhaven.com/a/modern_arm_chair_01) |
| `coffee_table_round_01` | Model (glTF) | CC0 1.0 | Lounge coffee table | [polyhaven.com/a/coffee_table_round_01](https://polyhaven.com/a/coffee_table_round_01) |
| `side_table_01` | Model (glTF) | CC0 1.0 | Lounge side table | [polyhaven.com/a/side_table_01](https://polyhaven.com/a/side_table_01) |
| `pachira_aquatica_01` | Model (glTF) | CC0 1.0 | Money trees | [polyhaven.com/a/pachira_aquatica_01](https://polyhaven.com/a/pachira_aquatica_01) |
| `modern_ceiling_lamp_01` | Model (glTF) | CC0 1.0 | Pendant lights | [polyhaven.com/a/modern_ceiling_lamp_01](https://polyhaven.com/a/modern_ceiling_lamp_01) |
| `ceramic_vase_01` | Model (glTF) | CC0 1.0 | Vase on the coffee table | [polyhaven.com/a/ceramic_vase_01](https://polyhaven.com/a/ceramic_vase_01) |
| `ceramic_vase_03` | Model (glTF) | CC0 1.0 | Vases on the reception desk and the side table | [polyhaven.com/a/ceramic_vase_03](https://polyhaven.com/a/ceramic_vase_03) |
| `potted_plant_04` | Model (glTF) | CC0 1.0 | Plant on the reception desk | [polyhaven.com/a/potted_plant_04](https://polyhaven.com/a/potted_plant_04) |
| `Marble016` | Texture set | CC0 1.0 | Podium deck (black marble) | [ambientcg.com/view?id=Marble016](https://ambientcg.com/view?id=Marble016) |
| `Marble012` | Texture set | CC0 1.0 | Reception worktop (white marble) | [ambientcg.com/view?id=Marble012](https://ambientcg.com/view?id=Marble012) |
| `Wood066` | Texture set | CC0 1.0 | Walnut veneer of the reception desk | [ambientcg.com/view?id=Wood066](https://ambientcg.com/view?id=Wood066) |
| `Metal009` | Texture set | CC0 1.0 | Brushed and polished stainless steel, dark bronze | [ambientcg.com/view?id=Metal009](https://ambientcg.com/view?id=Metal009) |
| `Carpet016` | Texture set | CC0 1.0 | Lounge rug, planter soil | [ambientcg.com/view?id=Carpet016](https://ambientcg.com/view?id=Carpet016) |
| `Fabric030` | Texture set | CC0 1.0 | Sofa and cushion fabric | [ambientcg.com/view?id=Fabric030](https://ambientcg.com/view?id=Fabric030) |

The first twelve assets come from Poly Haven and the last six from ambientCG. The materials and meshes that the interior tool generates in `Assets/Materials/Realism/Interior` are project content. The display posters in its `Textures` folder are renders of the CC BY 4.0 CarConcept ([Display posters](#display-posters-renders-of-the-carconcept)).

## Content generated by this project (not third-party)

The editor tools generate the following from this project's own code. Except where noted, it contains no third-party data and is project content under the MIT License:

- **Car internals:** the 522 meshes in `Assets/Models/CarInternals/` outside `V6/`, built procedurally by `Assets/Editor/CarInternals`; the 30 `CI_*` materials in `Assets/Materials/CarInternals`; and the 28 surface maps in `Assets/Materials/Realism/CarInternals`, synthesised in C# (`CarTextureSynth.cs`). The tool measures the CarConcept's geometry to fit the parts but copies none of it. The merged V6 copies in `V6/` are the exception ([V6 engine](#v6-engine--open-issue)).
- **Hero car textures:** the 19 textures in `Assets/Materials/Realism/Cars/Textures/Concept`, generated by `CarsRealismTextures.cs` and `CarsRealismMeshes.cs` with fixed seeds and no third-party pixels. Their lettering is rasterised from Unity's built-in font (`LegacyRuntime.ttf`, part of the Editor) when the tool runs; no font file is added to the repository. AURELLE is this project's fictional brand.
- **Bicycles:** the 11 meshes in `Assets/Models/Bicycle` and the 14 materials in `Assets/Materials/Realism/Bicycle`, built by `ExteriorBicycleBuilder`, with no text or logos.
- **Hero studio:** the softbox and contact-shadow materials and `AurelleHeroContactShadow.png` in `Assets/Materials/Realism/Rendering` (the texture is a blurred footprint rasterised from the CarConcept's meshes, see [CarConcept](#carconcept)), and `Assets/Settings/Rendering/AurelleMirrorRenderer.asset`.
- **Interior:** the generated glazing film texture `Assets/Materials/Realism/Interior/Textures/Interior_Glass_Manifestation.png`, besides the interior materials and meshes described above.

## Fonts

- **Inter** (`Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular.ttf` and its TextMesh Pro font assets, from the VR template): "Copyright 2019 The Inter project authors", licensed under the SIL Open Font License 1.1 (http://scripts.sil.org/OFL) according to the font's name table. The folder has no copy of the licence text, which the OFL asks redistributed copies to carry; add it next to the font, for example as `OFL.txt`.
- **Liberation Sans** (`Assets/TextMesh Pro/Fonts/LiberationSans.ttf`, TextMesh Pro essentials): SIL Open Font License 1.1, licence text in `Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt` ("Digitized data copyright (c) 2010 Google Corporation with Reserved Font Arimo, Tinos and Cousine. Copyright (c) 2012 Red Hat, Inc. with Reserved Font Name Liberation.").

## Unity template content and package samples

- **VR template** (`com.unity.template.vr` 10.0.0, per `templatePackageId` in `ProjectSettings/ProjectSettings.asset`): `Assets/VRTemplateAssets`, `Assets/Scenes/BasicScene.unity` and the XR rig prefabs used by SampleScene. The Unity templates available locally for this Editor version carry the [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license) for Unity-dependent projects; the VR template's own licence file is not part of this repository, so confirm its terms before redistributing this content outside a Unity project.
- **XR Interaction Toolkit 3.5.1 samples** (`Starter Assets`, `Hands Interaction Demo`): [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license) for Unity-dependent projects. The package's `Third Party Notices.md` lists Apache-2.0 gesture code from Google's ARCore Unity examples.
- **XR Hands 1.8.1 `HandVisualizer` sample** — open issue: the XR Hands package is licensed under the [Unity Package Distribution License](https://unity3d.com/legal/licenses/Unity_Package_Distribution_License), which covers distributing the package as part of your built project; this repository publishes the sample's scripts, prefabs, models and scene in source form. The XR rig's hand visuals (`Hands Interaction Demo` prefabs) use its hand models, so removing the sample means replacing those references; otherwise keep the repository private or confirm the terms with Unity.

## Shader code derived from Universal RP

- **Files:** `Assets/Scripts/Rendering/AurellePlanarReflectiveLit.shader` (`Aurelle/Planar Reflective Lit`, used by the podium deck's material) and the fragment function `AurellePlanarLitPassFragment` in `Assets/Scripts/Rendering/AurellePlanarReflection.hlsl`.
- **Origin:** copied pass for pass from the Lit shader of Universal RP 17.3 (`Lit.shader`, and `LitPassFragment` in `LitForwardPass.hlsl`) and modified to swap in the podium's planar reflection. The rest of `AurellePlanarReflection.hlsl` is this project's code.
- **Licence:** "com.unity.render-pipelines.universal copyright © 2020 Unity Technologies ApS. Licensed under the Unity Companion License for Unity-dependent projects" (the package's `LICENSE.md`). The modified copies stay under the [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license), not the MIT License, and may only be used in Unity-dependent projects.

## Unity packages

Packages listed in `Packages/manifest.json` are downloaded by the Unity Package Manager and are not stored in this repository. Their licences, from each package's `LICENSE.md`:

- **Unity Companion License:** Universal RP, Input System, uGUI, XR Plug-in Management, XR Interaction Toolkit, XR Core Utilities, AR Foundation, Android XR (OpenXR), and the source code of OpenXR, Meta OpenXR and XR Composition Layers.
- **Unity Package Distribution License:** XR Hands, and the parts of OpenXR, Meta OpenXR and XR Composition Layers that are not source code.
- **Apache License 2.0:** glTFast (`com.unity.cloud.gltfast`) and KTX for Unity (`com.unity.cloud.ktx`).
- **Unity Terms of Service:** AI Assistant (`com.unity.ai.assistant`) and Unity Pipeline (`com.unity.pipeline`).

Several packages also ship a `Third Party Notices.md` for components they include.
