# Architecture

## Scope

This is a Unity 6 project configured around an automotive showroom scene and an interactive car component explorer. The project uses URP and includes XR packages and OpenXR settings. Package presence and XR configuration do not by themselves establish that every target device or AR/XR workflow is implemented or validated.

## Scene and startup

`ProjectSettings/EditorBuildSettings.asset` enables only `Assets/Scenes/SampleScene.unity`. That scene contains the active showroom and `CarConcept` model. Several `SampleScene_Before_*.unity` files are retained as historical scene snapshots, and `Assets/Scenes/BasicScene.unity` is present but is not enabled in the build scene list.

After a scene loads, `CarExplorerBootstrap.InstallCarExplorer` looks up the active GameObject named `CarConcept`. If found, it adds `CarExplorerController` unless that component is already present. The controller also resolves the car root by name in `Awake`, so the expected model root name is part of the current scene/script contract. If the root is missing, the controller logs an error and disables itself.

## Runtime components

### `Assets/Scripts/CarExplorerBootstrap.cs`

Uses `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` to install the explorer without a serialized controller component being required on the car. It depends on the active scene containing a GameObject named `CarConcept`.

### `Assets/Scripts/CarExplorerController.cs`

Owns the car exploration behavior. During initialization it:

1. Resolves the car root, camera, and optional `Remy Presenter` root/audio source.
2. Registers supported model transforms by their authored node names and groups them into three explode levels.
3. Creates selection colliders where needed, builds a world-space information/control panel, and creates/enables its input actions.

At runtime it raycasts to select registered car parts or its own command controls. Selection displays a part name and description, highlights renderer materials with property blocks, and can play a matching configured voice cue. Explode/restore operations interpolate local transforms; returning to level zero explicitly restores each registered transform's captured position, rotation, and scale. The controller caps disassembly at level 3.

Input actions are created in code. Desktop behavior also reads `Keyboard.current` and `Mouse.current`: left-click selects, `O` advances, `R` restores, `T` resets, and `Escape` dismisses the info panel. XR bindings use the right-hand trigger for selection, right primary/secondary buttons for explode/restore, and the left menu button for reset. XR selection rays from the center of the view.

The controller can show a proximity greeting for the presenter when configured. Greeting and per-part audio clips are serialized fields; they are optional and depend on scene configuration.

### `Assets/Scripts/PlayerMovement.cs`

Independent Rigidbody-based keyboard movement using WASD or arrow keys and a smoothed camera follow. It is not called by `CarExplorerBootstrap`; its presence in the repository alone does not mean it is active in the configured sample scene.

## Content and packages

- `Assets/Models/CarConcept.glb` supplies the car hierarchy addressed by the controller's named-node registry.
- `Assets/Models/Presenter/Remy.fbx` supplies the presenter asset referenced optionally by the explorer.
- `Assets/Materials/` and scene lighting assets support the showroom presentation.
- `Assets/VRTemplateAssets/` and `Assets/Samples/XR Interaction Toolkit/3.5.1/` contain template and package sample content.
- `Packages/manifest.json` declares URP, Input System, XR Interaction Toolkit, OpenXR, AR Foundation, XR Hands, platform XR packages and other dependencies. The lock file records resolved package versions.
- `ProjectSettings/` stores graphics, build, input and XR configuration. The enabled build scene list contains only `SampleScene`.

## Maintenance notes

The controller's expected GameObject names and model node names couple it to the current imported GLB hierarchy. Changes to those imported node names require corresponding updates to the registration table in `CarExplorerController`. This document describes repository configuration and source inspection; it does not claim a successful Editor run, device build, or runtime test.
