# Contributing

Thank you for considering a contribution. Keep changes focused, preserve Unity's asset references and project configuration, and say what you verified.

## Before changing the project

1. Use Unity **6000.3.12f1** exactly, as recorded in `ProjectSettings/ProjectVersion.txt`. Opening the project in any other version upgrades and re-serializes assets; do not commit such an upgrade unless moving to a new version has been agreed.
2. Read [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md), in particular the cross-script and naming contracts: several scripts find scene objects by name, and the Car Explorer, its HUD, Remy and the inspect camera talk to each other only through the contracts listed there.
3. The build scene is `Assets/Scenes/SampleScene.unity`. The `SampleScene_Before_*` scenes and the contents of `Assets/Backups` are old snapshots; do not edit them.
4. Unity's generated folders (`Library/`, `Temp/`, `obj/`, `Logs/`, `UserSettings/`, `Build/`, `Builds/` and others) are ignored at the repository root by `.gitignore`. Folders with the same names inside `Assets` are tracked.

## Git workflow

1. Create a focused branch from the default branch, for example `docs/update-setup` or `fix/car-explorer-selection`.
2. Make a small, coherent change. Commit every new asset together with its `.meta` file and keep existing GUIDs; do not delete or regenerate the metadata of assets that are still in use.
3. Review `git status` and `git diff` before committing. Confirm the change contains no generated data that does not belong to it, credentials, local editor preferences, absolute local paths (for example `file:///Users/...` URLs in a scene) or unrelated files.
4. Use a concise commit subject in imperative form, such as `Document XR input bindings`.
5. Open a pull request with the purpose, the affected scenes, scripts and settings, the verification performed, and any platform or device limitations.

### Large binary files

The repository uses Git LFS for large binaries added from now on. The root `.gitattributes` sends to LFS:

- textures, HDRIs and model buffers under `Assets/ThirdParty/` (the CC0 Poly Haven and ambientCG content);
- baked lighting under `Assets/Scenes/` (`Lightmap-*`, `ReflectionProbe-*.exr`, `LightingData.asset`);
- the textures that the editor tools write: PNG, JPG and EXR files anywhere under `Assets/Materials/Realism/` and PNG and JPG files in `Assets/Models/Presenter/Textures/`. That covers the premium cars' texture copies and the hero car's generated textures (`Cars/Textures`), the internals' surface maps (`CarInternals/`), the contact shadow (`Rendering/`), the display posters and the glazing film (`Interior/Textures`) and the textures derived from Remy.

Every generated texture is an LFS file, so a tool run that rewrites one needs `git lfs install` on that machine before committing. The tools write a texture only when its bytes change, so an unchanged run leaves them alone. If `git status` lists textures that you did not regenerate (for example after a checkout without LFS, when they turn into pointer files), restore them with `git lfs pull` or `git checkout -- <path>` instead of committing them.

`.gltf` files (JSON), `.meta` files and the generated meshes stay in plain Git: the interior meshes in `Assets/Materials/Realism/Interior/Meshes` (about 5 MB), the internals' meshes and the V6's merged copies in `Assets/Models/CarInternals` (about 36 MB), the bicycle meshes in `Assets/Models/Bicycle` and the hero car's planar-UV copies in `Assets/Materials/Realism/Cars/Meshes`. Every internals build that changes geometry rewrites these `.asset` files, so keep internals changes deliberate. Files that were committed before LFS was enabled, for example `Assets/Models/ShowroomCollection/BMW_M3_E30.glb` (43.7 MB), also stay in plain Git; moving them into LFS (`git lfs migrate`) rewrites history and has to be coordinated with everyone who has a clone.

Every contributor runs `git lfs install` once per machine before cloning or pulling. On GitHub Free, LFS storage and bandwidth are limited (about 1 GB each per month at the time of writing), and every fresh clone downloads all LFS files, so keep third-party textures at 1K, and use 2K only for hero surfaces. GitHub warns about files over 50 MiB and rejects files over 100 MiB outside LFS.

### Scenes and merges

The project uses text serialization ("Force Text") with visible meta files, so scenes and prefabs can be merged with Unity's Smart Merge tool, UnityYAMLMerge. Configure it in your Git config:

```ini
[merge]
    tool = unityyamlmerge
[mergetool "unityyamlmerge"]
    trustExitCode = false
    cmd = '<path to UnityYAMLMerge>' merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"
```

On macOS the tool is `/Applications/Unity/Hub/Editor/6000.3.12f1/Unity.app/Contents/Helpers/UnityYAMLMerge`; on Windows it is in the Editor's `Data\Tools` folder. Run `git mergetool` on a conflicted scene or prefab instead of editing the conflict markers.

`SampleScene.unity` is a large YAML file, most of it the generated exterior district and the generated internals, and every run of the editor tools rewrites it. Agree on who edits it before starting, and avoid parallel scene changes, especially around a district regeneration, an internals build or a realism run. Do not merge two regenerated scenes: merge the code and run the tools again on the merged code, in their order.

## Unity-specific guidance

- **XR settings** live in `Assets/XR`: `XRGeneralSettingsPerBuildTarget.asset` (loaders and "Initialize XR on Startup" per platform), `Settings/OpenXRPackageSettings.asset` (OpenXR features per platform) and `Loaders/`. Change them through **Project Settings > XR Plug-in Management**, not by hand. `OpenXRPackageSettings.asset` also holds duplicate, unreferenced feature objects that the OpenXR editor maintains, so review its diffs carefully. Player, quality and graphics settings are in `ProjectSettings/`; the URP pipeline assets are in `Assets/Settings/Project Configuration/`.
- **Exterior district.** Change `Assets/Scripts/ExteriorEnvironmentBuilder.cs`, not the generated objects under `Exterior Environment`; manual edits there are lost on the next build. To regenerate, open SampleScene in Unity 6000.3.12f1, leave Play Mode and run **Aurelle > Build Exterior District**. The builder deletes and recreates everything under `Exterior Environment`, updates the assets it owns in `Assets/Materials/Exterior`, `Assets/Models/Exterior` and `Assets/Prefabs/ExteriorTraffic`, and saves the scene. A regeneration rebuilds the traffic prefabs from the raw GLBs and resets the sky lighting, the sun and the street lamps. Afterwards, run **Aurelle > Realism > Cars > Apply Car Materials** and then **Aurelle > Realism > Rendering > Apply Render Setup and Bake Lighting**. The builder builds the cyclists' bicycles through the same `ExteriorBicycleBuilder` as the bicycle tool, so they need no extra run. Commit the builder change together with the regenerated scene and assets.
- **Names.** Keep the object names listed under "Naming contracts" in [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md), such as `CarConcept`, `Internals`, `V6_Engine_Prototype(Clone)`, `Remy Presenter`, `Player Spawn`, the `Showroom ...` roots and the bicycle's `Saddle`, `Handlebar`, wheel and pedal nodes, or update every script that uses them.
- **Package versions and project settings.** Do not change them as incidental cleanup; describe the reason and the impact when a change is needed. Layer 30 (`Aurelle Planar Mirror`) belongs to the planar reflection: keep it free and in every camera's culling mask.
- **Third-party content.** When adding a model, texture, font, sound, sample or code copied from a package, record its source and licence in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Do not add content whose licence forbids redistribution in a public repository, such as raw Mixamo character files.
- **External files.** Do not reference files outside the project (absolute paths or `file://` URLs); import the asset into `Assets/` instead.

## Car Explorer contracts

The explorer, its HUD, Remy and the desktop inspect camera are separate scripts that meet only through the contracts in [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md#cross-script-contracts). Keep them that way:

- **`ICarExplorerModel`** (`CarExplorerModel.cs`) is the only way into the explorer. `CarExplorerController` registers itself in `CarExplorerModels` in `OnEnable` and unregisters in `OnDisable` and `OnDestroy`. It raises `Changed` on every change the HUD shows (view, open path, selection, language, greeting, camera, `IsMoving`). The HUD reads nodes and calls the model's commands (`SetView`, `Select`, `Open`, `CloseTo`, `PutBack`, `Back`, `ResetAll`, `Highlight`, `SetLanguage`, `DismissGreeting`) and never touches the controller's internals. A new UI or tool should do the same.
- **The explorer creates no UI.** Every panel belongs to the HUD (`CarExplorerHud*.cs`); its strings, English and Uzbek, are in `CarExplorerHudText.cs`, including the phrase table for materials. Add the Uzbek phrase for a new material there, or check that the term-by-term fallback reads well.
- **`TryGetNodeBounds`** returns where a node's parts will be when the current move ends, and false for inner content (the V6, the internals) that the current state does not draw. The inspect camera, the HUD's Inspect button and Remy's studied parts depend on that.
- **`CarExplorerModels.HudSafeViewport`** is the part of the desktop view that the HUD leaves free. The explorer places taken-out assemblies and the inspect camera frames nodes inside it, so a HUD layout change must keep publishing it on every layout pass. **`PointerOverHud`** must stay true while the pointer or an XR ray is over a HUD control, or a press on the HUD also clicks the car.
- **`CarExplorerInspect`** carries the inspect requests and state between the HUD, the explorer, the walkthrough and Remy. The walkthrough publishes the state once per change from `LateUpdate`, so every `Update` of a frame sees the same value.
- **`CarExplorerStepBack` / `ICarExplorerStepBackBody`** (`CarExplorerModel.cs`) let the explorer glide the desktop body back from the car while an assembly comes out. The walkthrough provides the body (its `CharacterController` decides how far it can go) and ends the glide when the visitor walks or looks; the explorer never moves the XR rig.
- **One `LateUpdate`.** `CarExplorerLowProxy.cs` declares the controller's only `LateUpdate` (the low proxies at quality levels 0 and 1). A partial that needs one calls into it instead of declaring a second. A new visual state of the car's parts that does not use property blocks must be known to `IsLowProxyClean`, or the proxy can stay on while the parts change under it; keep the `IsHiddenByLowProxy` term in `IsPartVisible`.
- **One placement rule, one kind of path.** Every taken-out assembly comes toward the visitor through `FindVisitorPlace` and travels by `BuildPath` in `CarExplorerLayout.cs`. Change those rather than adding a rule per assembly kind, keep the two hard rules (outside the car's footprint, seen past the car), the path's lift over the car's top and its clearance of the visitor, and check the placement and the paths from several visitor spots (close to the car too), at a small Game view and at 1080p, in the normal and X-ray views, and with an XR-like view (a head camera with the Quest's field of view at a standing eye height), where the rig never moves.
- **CarConcept's names.** The explorer groups CarConcept's own nodes and chooses the X-ray glass shell by their glTF names (`Body*`, `BodyDoor*`, `Wheel*`, `Interior*` and others), with exploded offsets tuned to its proportions. Renaming those nodes, or showing a different car model, needs `CarPartInfo` on its nodes or new rules ([Car Explorer](Docs/ARCHITECTURE.md#car-explorer)).
- In Play Mode the explorer also creates a physics-only scene, `Car Explorer Picking N`, that holds the exact picking meshes and is unloaded with the explorer; it is not a project asset.

## Changing or adding internals

The internals under `CarConcept/Internals` and the V6's grouping are generated by the car internals tool in `Assets/Editor/CarInternals` (**Aurelle > Car Internals > Build**, or `AurelleCarInternals.Batch`). Change the tool, never the generated objects or meshes: every build deletes and recreates `Internals`, regroups the V6 and saves the scene. The report goes to the Console and to `$AAC_OUT/internals_report.txt`.

- **Where things live.** `CarInternalsBuilder.cs` (entry points, `InternalsContext`, the subsystem attribute), `CarLayout.cs` (the measured car: wheels, floor, wheel wells, firewall, the V6's bounds, reserved volumes), `InternalsProbe.cs` (physics copies of the car's meshes for rays and the interference check), `CarMeshKit.cs` (lathe, sweeps, springs, extrusions, drilled discs, bellows, fasteners), `CarPalette.cs` with `CarTextureSynth.cs` and `CarTextures.cs` (the `CI_*` materials and their generated finishes), `V6Regroup.cs` and `V6MeshMerge.cs` (the V6's groups and merged meshes), and one file per subsystem in `Systems/`.
- **A new subsystem** is a new file in `Systems/`: a class implementing `ICarInternalsSubsystem` with `[CarInternalsSubsystem("<system>", <order>)]`. The tool finds it through `TypeCache`, so no shared file needs an edit. Use one of the systems in `CarSystems` (`CarPartInfo.cs`); a new system needs its English and Uzbek name added there.
- **Nodes.** Build the tree with the context: `ctx.System`, `ctx.Assembly`, `ctx.SubAssembly` and `ctx.Part`. Each node gets a `CarPartInfo` with an id, English and Uzbek names and one-line descriptions (`Txt`), its material and optional explode hints; pass `fine: true` for fasteners, clips and thin wires, which Low leaves out. Positions are in the car frame of `Internals` (the ground under the wheelbase centre, +z toward the hood). Node names must not start with `Body` or contain the explorer's shell words (`Panel`, `Door`, `Hood` and the others); the tool warns when one does.
- **Meshes.** `ctx.Mesh(key, build)` builds a key once per run and shares it, so a key must always build the same geometry. Meshes are written in place in `Assets/Models/CarInternals/<system>` (GUIDs kept) and stay readable, because the explorer picks them exactly. `MeshKit` writes UVs in metres and tangents for the surface finishes.
- **Materials.** Use the palette's `CI_*` materials by name; a subsystem may add its own (seven do now). Every material's finish (surface, tile size in metres, normal strength) is looked up by its name in `CarPalette.cs`, so give a new material an entry there; the maps are generated into `Assets/Materials/Realism/CarInternals` (Git LFS).
- **Clearances.** Derive positions from `CarLayout` and the probes, not from fixed numbers, and keep out of the volumes reserved for other subsystems. Declare intended contacts with `ctx.AllowContact(idPrefix, otherPrefix)`. The report must read "interference check of N parts: clear"; a clash is fixed in the geometry, not by declaring a contact that is not real. Pieces that clash and cannot be fixed yet go behind a `static readonly bool` switch named in the report, as the exhaust, the front crash structure, the HVAC module and the timing-end mount are now.
- **Run order.** Run the tool after `CarsRealism` (it merges a V6 renderer's submeshes only when all its slots hold one material) and before the final bake. A second run must give the same report and write no asset; it changes the scene only in the file IDs of the recreated objects.
- **Check the result** in Play Mode: the system in the STRUCTURE tree, its parts' names and cards in both languages, taking the assembly out and opening each level, the X-ray and exploded views, and the Low quality level.

## Changing the look

The editor tools own several parts of the project:

- the showroom's materials, fixtures, glazing and lighting, and the hero car's studio;
- the car, people and bicycle materials and the textures generated for them;
- the PC rendering settings.

They rewrite these on every run, so change the tools rather than their output.

- **Edit the tool code, not the generated assets.** Changes made by hand to materials in `Assets/Materials/Realism`, to the objects the tools restyle in SampleScene, or to the PC pipeline asset are overwritten on the next run. Where to make a change:
  - Interior materials (colours, textures, tiling, smoothness, LED emission): `InteriorPalette.cs`. The interior tool rewrites every property of its `.mat` files on each run. Interior geometry and layout: the other `Interior*.cs` files; the glazed front: `InteriorGlazing.cs`.
  - Car materials: the recipes in `Specs()` and the slot tables in `CarsRealism.cs`; the hero car's generated textures in `CarsRealismTextures.cs` and its planar-UV meshes in `CarsRealismMeshes.cs`.
  - People: the materials and crowd palettes in `PeopleRealism.cs`, and the `PedestrianLooks` and `CyclistLooks` tables in `ExteriorEnvironmentBuilder.cs`. The names in the builder's tables must match the palette names in `PeopleRealism.cs`.
  - Bicycles: the geometry and materials in `Assets/Scripts/ExteriorBicycleBuilder.cs` (shared with the district builder) and the tool in `BicycleRealism.cs`. Keep the node names the cyclist agent reads.
  - Lights, probes, post-processing, pipeline and bake settings and the hero studio (canopy, contact shadow, planar reflection set-up): the constants and values in `RenderSetup.cs`; the runtime side of the studio is in `Assets/Scripts/Rendering`.
- **Re-run the tools in order.** Use the order under [Rebuilding the look and the internals](README.md#rebuilding-the-look-and-the-internals): People, Cars, Apply Render Setup (no bake), Upgrade Showroom Interior, Bicycles, Car Internals, then Apply Render Setup and Bake Lighting. A change to showroom geometry, lights or emissive materials needs the final bake. Bake only with that menu item or `AurelleRenderSetup.BatchBake`, never from the Lighting window: the contact shadows under the premium cars exist only in the tool's bake. When the hero car's look changes, also render the display posters again with `AurelleRealism.InteriorPosterRenderer.Batch`.
- **Keep what the tools rely on.**
  - Keep the object names listed under "Naming contracts" in [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md).
  - Never mark showroom objects `BatchingStatic`; the runtime colliders read `MeshFilter.sharedMesh`.
  - Give emissive materials the Realtime or Baked emissive GI flag, or the bake turns their emission off.
  - Do not assign a single material to the interior's multi-submesh renderers.
  - A URP material whose render queue matters must also set `_QueueOffset`, because URP's material validation recomputes the queue from it (the hero contact shadow uses −10).
  - `Aurelle/Planar Reflective Lit` copies URP 17.3's Lit shader pass for pass; after a URP upgrade, copy Lit's passes again.
- **Third-party assets.** Put new CC0 assets in `Assets/ThirdParty/<source>/<asset id>/` with a `SOURCE.txt` (source URL, licence, changes made). Keep textures at 1K (2K only for hero surfaces), and do not commit Poly Haven preview renders, which are not CC0. List the asset in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Textures the tools generate from code are project content and need no entry, unless they are made from a third-party asset.
- **Commit the tool change with its output**: the scene, the generated assets and their `.meta` files. The bake output in `Assets/Scenes/SampleScene/` (`LightingData.asset`, `Lightmap-*`, `ReflectionProbe-*.exr`) and the generated textures in `Assets/Materials/Realism/` and `Assets/Models/Presenter/Textures/` go to Git LFS through `.gitattributes`; run `git lfs install` before committing them.
- **Expect noise.**
  - Saving SampleScene updates the dependency list in `Assets/Settings/Project Configuration/SampleScene.scenetemplate`, and Unity rewrites `Assets/XR/Settings/OpenXRPackageSettings.asset` when it starts. Leave both out of a commit unless they belong to the change.
  - The bicycle and internals tools recreate their scene objects with new file IDs on every run, so SampleScene changes even when their assets come out byte-identical. Commit such churn only together with a real change.
  - Materials that the bicycle tool has just created settle on its second run, when URP's validation copies `_BaseColor` into `_Color`; run it twice after adding a material.
  - SampleScene stores one material slot per V6 renderer (182) since the internals tool merged the V6's submeshes, so a change to the V6 materials gives a moderate scene diff.

## Verification

- State which checks you performed: Play Mode in desktop mode, Play Mode or a build with a headset, an Android build. Do not claim Editor, build or device verification that was not actually performed.
- The Editor-only harness in `Assets/Editor/AurelleAutoCheck` runs scripted checks in Play Mode. With the project closed in the Editor, run `<Unity> -batchmode -projectPath <project> -executeMethod AurelleAutoCheck.PlayChecks`; it plays SampleScene, writes `checks.txt` with a `pass=… fail=…` line to `$AAC_OUT` (default `<project>/AAC_Out`; do not commit it) and exits. `AurelleAutoCheck.Play` runs a scripted desktop visit and writes `play_log.txt` with screenshots.
- For the Quest, build the APK (**Aurelle > Build > Quest APK** or `AurelleBuild.BuildQuestApk`, see the README) and state whether it was installed and run on a headset; **Aurelle > Build > Check Quest Settings** reports the settings and the OpenXR validation without building.
- When changing interaction behaviour, state which desktop and XR inputs you checked.
- When changing the Car Explorer or the HUD, check the normal, X-ray and exploded views, taking an assembly out and opening it level by level, the inspect camera, and that the car comes back to its exact rest pose and its own materials. Check the HUD at a full-HD Game view and at a small one (for example 708 × 440).
- When changing the look, check it at both quality levels: Ultra (PC) and Low (Quest). At Low the Quest budget and the explorer's low proxies run; check that the car still restores exactly and that a proxy hides as soon as a part is selected, hovered, moved or dimmed.
- When changing the internals, include the tool's report and confirm that the interference check is clear and that a second run writes no asset.

## Reporting a change

Include reproduction steps for behaviour changes, list the Unity version and target platform used for verification, and attach screenshots or logs when they help reviewers understand the result. For documentation-only changes, identify the files reviewed and the statements that were confirmed from repository contents.
