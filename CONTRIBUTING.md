# Contributing

Thank you for considering a contribution. Keep changes focused, preserve Unity's asset references and project configuration, and say what you verified.

## Before changing the project

1. Use Unity **6000.3.12f1** exactly, as recorded in `ProjectSettings/ProjectVersion.txt`. Opening the project in any other version upgrades and re-serializes assets; do not commit such an upgrade unless moving to a new version has been agreed.
2. Read [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md), in particular the naming contracts: several scripts find scene objects by name.
3. The build scene is `Assets/Scenes/SampleScene.unity`. The `SampleScene_Before_*` scenes and the contents of `Assets/Backups` are old snapshots; do not edit them.
4. Unity's generated folders (`Library/`, `Temp/`, `obj/`, `Logs/`, `UserSettings/`, `Build/`, `Builds/` and others) are ignored at the repository root by `.gitignore`. Folders with the same names inside `Assets` are tracked.

## Git workflow

1. Create a focused branch from the default branch, for example `docs/update-setup` or `fix/car-explorer-selection`.
2. Make a small, coherent change. Commit every new asset together with its `.meta` file and keep existing GUIDs; do not delete or regenerate the metadata of assets that are still in use.
3. Review `git status` and `git diff` before committing. Confirm the change contains no generated data, credentials, local editor preferences, absolute local paths (for example `file:///Users/...` URLs in a scene) or unrelated files.
4. Use a concise commit subject in imperative form, such as `Document XR input bindings`.
5. Open a pull request with the purpose, the affected scenes, scripts and settings, the verification performed, and any platform or device limitations.

### Large binary files

The repository uses Git LFS for large binaries added from now on. The root `.gitattributes` sends to LFS:

- textures, HDRIs and model buffers under `Assets/ThirdParty/` (the CC0 Poly Haven and ambientCG content);
- baked lighting under `Assets/Scenes/` (`Lightmap-*`, `ReflectionProbe-*.exr`, `LightingData.asset`);
- the textures that the realism tools write: PNG, JPG and EXR files anywhere under `Assets/Materials/Realism/` (the car texture copies and the display posters) and PNG and JPG files in `Assets/Models/Presenter/Textures/`, about 32 MB together.

`.gltf` files (JSON), `.meta` files and the interior meshes in `Assets/Materials/Realism/Interior/Meshes` (36 `.asset` files, about 4.7 MB) stay in plain Git. Files that were committed before LFS was enabled, for example `Assets/Models/ShowroomCollection/BMW_M3_E30.glb` (43.7 MB), also stay in plain Git; moving them into LFS (`git lfs migrate`) rewrites history and has to be coordinated with everyone who has a clone.

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

`SampleScene.unity` is about 12 MB of YAML, most of it the generated exterior district, and every run of the realism tools rewrites it. Agree on who edits it before starting, and avoid parallel scene changes, especially around a district regeneration or a realism run.

## Unity-specific guidance

- **XR settings** live in `Assets/XR`: `XRGeneralSettingsPerBuildTarget.asset` (loaders and "Initialize XR on Startup" per platform), `Settings/OpenXRPackageSettings.asset` (OpenXR features per platform) and `Loaders/`. Change them through **Project Settings > XR Plug-in Management**, not by hand. `OpenXRPackageSettings.asset` also holds duplicate, unreferenced feature objects that the OpenXR editor maintains, so review its diffs carefully. Player, quality and graphics settings are in `ProjectSettings/`; the URP pipeline assets are in `Assets/Settings/Project Configuration/`.
- **Exterior district.** Change `Assets/Scripts/ExteriorEnvironmentBuilder.cs`, not the generated objects under `Exterior Environment`; manual edits there are lost on the next build. To regenerate, open SampleScene in Unity 6000.3.12f1, leave Play Mode and run **Aurelle > Build Exterior District**. The builder deletes and recreates everything under `Exterior Environment`, updates the assets it owns in `Assets/Materials/Exterior`, `Assets/Models/Exterior` and `Assets/Prefabs/ExteriorTraffic`, and saves the scene. A regeneration rebuilds the traffic prefabs from the raw GLBs and resets the sky lighting, the sun and the street lamps. Afterwards, run **Aurelle > Realism > Cars > Apply Car Materials** and then **Aurelle > Realism > Rendering > Apply Render Setup and Bake Lighting**. Commit the builder change together with the regenerated scene and assets. Do not merge two regenerations; merge the builder code and regenerate again.
- **Names.** Keep the object names listed under "Naming contracts" in [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md), such as `CarConcept`, `Remy Presenter`, `Player Spawn` and the `Showroom ...` roots, or update every script that uses them.
- **Car Explorer.** `CarExplorerController.cs` groups the car into assemblies, chooses the X-ray glass shell and spreads the parts by CarConcept's glTF node names (`Body*`, `BodyDoor*`, `Wheel*`, `Interior*` and others), and its exploded and take-out offsets are tuned to CarConcept's proportions. Renaming those nodes, or showing a different car model, needs new tables in the controller ([Car Explorer](Docs/ARCHITECTURE.md#car-explorer)). In Play Mode the explorer also creates a physics-only scene, `Car Explorer Picking N`, that holds the car's picking colliders and is unloaded with the explorer; it is not a project asset.
- **Package versions and project settings.** Do not change them as incidental cleanup; describe the reason and the impact when a change is needed.
- **Third-party content.** When adding a model, texture, font, sound or sample, record its source and licence in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Do not add content whose licence forbids redistribution in a public repository, such as raw Mixamo character files.
- **External files.** Do not reference files outside the project (absolute paths or `file://` URLs); import the asset into `Assets/` instead.

## Changing the look

The realism tools in `Assets/Editor/AurelleRealism` own several parts of the project:

- the showroom's materials, fixtures and lighting;
- the car and people materials;
- the PC rendering settings.

They rewrite these on every run, so change the tools rather than their output.

- **Edit the tool code, not the generated assets.** Changes made by hand to materials in `Assets/Materials/Realism`, to the objects the tools restyle in SampleScene, or to the PC pipeline asset are overwritten on the next run. Where to make a change:
  - Interior materials (colours, textures, tiling, smoothness, LED emission): `InteriorPalette.cs`. The interior tool rewrites every property of its `.mat` files on each run. Interior geometry and layout: the other `Interior*.cs` files.
  - Car materials: the recipes in `Specs()` and the slot tables in `CarsRealism.cs`.
  - People: the materials and crowd palettes in `PeopleRealism.cs`, and the `PedestrianLooks` and `CyclistLooks` tables in `ExteriorEnvironmentBuilder.cs`. The names in the builder's tables must match the palette names in `PeopleRealism.cs`.
  - Lights, probes, post-processing, pipeline and bake settings: the constants and values in `RenderSetup.cs`.
- **Re-run the tools in order.** Use the order under [Rebuilding the look](README.md#rebuilding-the-look): People, Cars, Apply Render Setup (no bake), Upgrade Showroom Interior, then Apply Render Setup and Bake Lighting. A change to showroom geometry, lights or emissive materials needs the final bake. Bake only with that menu item or `AurelleRenderSetup.BatchBake`, never from the Lighting window: the contact shadows under the premium cars exist only in the tool's bake.
- **Keep what the tools rely on.**
  - Keep the object names listed under "Naming contracts" in [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md).
  - Never mark showroom objects `BatchingStatic`; the runtime colliders read `MeshFilter.sharedMesh`.
  - Give emissive materials the Realtime or Baked emissive GI flag, or the bake turns their emission off.
  - Do not assign a single material to the interior's multi-submesh renderers.
- **Third-party assets.** Put new CC0 assets in `Assets/ThirdParty/<source>/<asset id>/` with a `SOURCE.txt` (source URL, licence, changes made). Keep textures at 1K (2K only for hero surfaces), and do not commit Poly Haven preview renders, which are not CC0. List the asset in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
- **Commit the tool change with its output**: the scene, the generated assets and their `.meta` files. The bake output in `Assets/Scenes/SampleScene/` (`LightingData.asset`, `Lightmap-*`, `ReflectionProbe-*.exr`) and the generated textures in `Assets/Materials/Realism/` and `Assets/Models/Presenter/Textures/` go to Git LFS through `.gitattributes`; run `git lfs install` before committing them.
- **Expect noise.** Saving SampleScene updates the dependency list in `Assets/Settings/Project Configuration/SampleScene.scenetemplate`, and Unity rewrites `Assets/XR/Settings/OpenXRPackageSettings.asset` when it starts. Leave both out of a commit unless they belong to the change. SampleScene stores the V6's 6,250 material slots explicitly, so a change to the V6 materials produces a large scene diff.

## Verification

- State which checks you performed: Play Mode in desktop mode, Play Mode or a build with a headset, an Android build. Do not claim Editor, build or device verification that was not actually performed.
- When changing interaction behaviour, state which desktop and XR inputs you checked.
- When changing the Car Explorer, check the normal, X-ray and exploded views and a take-out, and that the car comes back to its exact rest pose and its own materials.
- When changing the look, check it at both quality levels: Ultra (PC) and Low (Quest).

## Reporting a change

Include reproduction steps for behaviour changes, list the Unity version and target platform used for verification, and attach screenshots or logs when they help reviewers understand the result. For documentation-only changes, identify the files reviewed and the statements that were confirmed from repository contents.
