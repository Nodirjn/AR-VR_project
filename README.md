# AR-VR Project

A Unity project containing an interactive automotive showroom and a progressive car component explorer. The configured build scene is `Assets/Scenes/SampleScene.unity`.

## Project status and requirements

- **Unity Editor:** 6000.3.12f1 (recorded in `ProjectSettings/ProjectVersion.txt`)
- **Rendering:** Universal Render Pipeline (URP) 17.3.0
- **Input:** Input System 1.19.0
- **XR packages:** XR Interaction Toolkit 3.5.1, OpenXR 1.17.1, XR Management 4.6.0, AR Foundation 6.5.0, XR Hands 1.8.1, Android XR OpenXR 1.3.1, and Meta OpenXR 2.5.1
- The project includes Android and standalone presets and XR/OpenXR settings. Actual device support and build behavior have not been verified here.

Use the Unity Editor version above to open the project. Unity Package Manager resolves the dependencies listed in `Packages/manifest.json` and `Packages/packages-lock.json`. Open `Assets/Scenes/SampleScene.unity` to view the configured project scene. This scene is the only scene enabled in `ProjectSettings/EditorBuildSettings.asset`.

## What is in the project

The active sample scene contains a showroom with a `CarConcept` model and a presenter asset. `CarExplorerBootstrap` finds the named car after scene load and attaches `CarExplorerController` when needed. The controller finds named model assemblies and provides selection, component descriptions, highlighting, and animated progressive disassembly through three levels, with a restore-to-assembled action. It can play an optional greeting or component voice clips when those clips are configured.

The repository also contains `BasicScene.unity`, earlier `SampleScene` snapshots, XR Interaction Toolkit sample assets, project settings, and an additional `PlayerMovement` script. Historical snapshots are not configured as build scenes.

## Controls

| Action | Desktop | XR controller |
| --- | --- | --- |
| Select a car part or panel control | Left mouse click | Right trigger (ray from view center) |
| Advance one explode level | `O` | Right primary button |
| Restore assembled state | `R` | Right secondary button |
| Reset selection and explorer state | `T` | Left menu button |
| Dismiss the information panel | `Esc` | — |

`PlayerMovement.cs` reads WASD and arrow keys, but it is a separate script and is not part of the car explorer control mapping above.

## Repository layout

```text
Assets/             Scenes, scripts, models, materials, XR settings and sample content
Packages/           Unity Package Manager manifest and lock file
ProjectSettings/    Editor, build, graphics and XR project settings
Docs/               Architecture notes
```

See [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md) for script relationships and scene behavior, and [CONTRIBUTING.md](CONTRIBUTING.md) for the Git workflow.

## Screenshots

No screenshots are currently included in the repository. Add captured images under `Docs/Screenshots/` and replace these placeholders when suitable screenshots are available.

<!-- Screenshot placeholder: active SampleScene showroom -->
*Screenshot placeholder — active showroom scene.*

<!-- Screenshot placeholder: car explorer interaction -->
*Screenshot placeholder — car component selection or exploded view.*

## License

The repository includes an MIT License; see [LICENSE](LICENSE). It permits use, modification, redistribution, sublicensing, and commercial use provided the copyright and permission notice is retained. The software is provided without warranty, and the license limits the authors' liability. The repository-level license does not establish the licensing terms for third-party Unity packages, samples, or imported model and media assets; check their source licenses before redistributing them.
