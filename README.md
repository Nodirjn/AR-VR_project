# AR-VR Project: Aurelle Car Showroom

A Unity project for "Aurelle", a fictional car dealership: a showroom with an interactive CarConcept explorer, a procedurally animated presenter (Remy), a premium car collection and a generated city district around the building. The same scene runs as a desktop first-person walkthrough by default and switches to the XR rig when a headset is active.

- **Unity Editor:** 6000.3.12f1, exactly (`ProjectSettings/ProjectVersion.txt`)
- **Rendering:** Universal Render Pipeline 17.3.0
- **XR:** OpenXR 1.17.1 through XR Plug-in Management 4.6.0; XR Interaction Toolkit 3.5.1 rig from the Unity VR template
- **Input:** Input System 1.19.0 only (the legacy Input Manager is disabled)
- **Scene:** `Assets/Scenes/SampleScene.unity`, the only scene in the build list

## Getting started

1. Install Unity **6000.3.12f1** with Unity Hub. For Quest or Android XR builds add Android Build Support (with OpenJDK and the Android SDK and NDK); for desktop builds add the IL2CPP module of your platform. Both targets use IL2CPP.
2. Open the repository folder from Unity Hub with that exact version. Any other version upgrades and re-serializes the project.
3. The first import resolves the packages pinned in `Packages/packages-lock.json` (network access is needed) and imports the GLB models through glTFast. Expect it to take a while.
4. Open `Assets/Scenes/SampleScene.unity` and press Play.

### Desktop or XR

`ExteriorSpawnPlacement` chooses the mode before any other script runs:

- **Desktop** when no XR loader is active, for example in Play Mode on macOS or on a PC without an OpenXR runtime: the camera starts at `Player Spawn` on the arrival plaza in front of the showroom, 1.7 m above the ground and facing the entrance, and `DesktopWalkthroughController` takes over.
- **XR** when an OpenXR loader is active: the VR template rig `XR Origin Hands (XR Rig)` is switched on at `Player Spawn` and the desktop camera is switched off. Once head tracking settles, the rig is moved so that the headset stands at the spawn and faces the showroom.
- If XR was chosen only because a loader was running and no headset display starts within 6 s, the scene falls back to desktop and logs a warning. A headset that starts later needs a scene restart.

"Initialize XR on Startup" is enabled for Standalone and Android, so a desktop build on a PC with an active OpenXR runtime and headset (for example Quest Link or SteamVR) starts in XR.

## What the scene contains

- **Showroom.** The interior with a circular podium carrying the CarConcept, a reception desk, a customer lounge and rear-wall displays, finished with CC0 Poly Haven and ambientCG materials and furniture. Walls, display cars and floor-standing furniture get colliders at runtime (`ShowroomCollisionSetup`).
- **Car Explorer.** `CarExplorerController` on `CarConcept` lets the visitor look inside the car and take it apart, in three views:

  | View | What happens |
  | --- | --- |
  | Normal | The assembled car; the V6 under the closed hood is not rendered. |
  | X-ray | The body shell (body panels, doors, hood, roof, rear section, windows and lights) fades in 0.6 s into light-blue glass with bright edges, and a soft spot light fades in over the car. The V6, seats, dashboard, cabin floor and cage, axles, brakes and wheels show through in their own materials (shader `Aurelle/X-Ray Ghost` in `Assets/Resources`). |
  | Exploded | Every assembly is separated in 3D around the car over 1.5 s, in its own materials: wheels out to the sides with tyre, rim and brake apart along their axles, the V6 forward, seats and cockpit raised out of the cabin, and above them the body frame with the roof and windscreen on top, the doors beside it, the front section over its nose and the rear section behind it. The layout is fitted to the room each time it opens. |

  In the normal or X-ray view, clicking a part takes its whole assembly (16 groups: four wheels, two doors, front and rear sections, roof, windscreen, body frame, seats, cockpit, cabin floor, underbody, V6) out of the car and spreads it into its sub-parts; the cabin floor and the underbody spread in place. The V6, seats, cockpit, body frame and cabin floor are inside the body, so taking one of them out switches the normal view to X-ray. Clicking another part swaps assemblies; clicking empty space puts it back. Every part, car and V6, is selectable: it is highlighted, its readable name and a one-line description of its assembly appear on a card beside Remy, and Remy looks and points at it. Grabbed parts (XR) glide back when released. Panel texts are available in English and Uzbek; part names stay in English.
- **Presenter Remy.** `ShowroomPresenterAgent` on `Remy Presenter`. When the visitor enters the showroom, Remy walks up to them and greets them with a WELCOME panel; then he follows at about 2.5 m, inside the visitor's view, and stays inside the showroom. Remy has no animation controller: walking, looking and pointing are procedural.
- **Premium collection.** BMW M3 E30, BMW M4 Competition and Tesla Model 3 (2018) on displays behind the podium, placed as instances of the GLBs in `Assets/Models/ShowroomCollection`.
- **Exterior district.** Streets, 18 buildings, street furniture, five signalised junctions, six moving and four parked cars, eight pedestrians, two cyclists, a Poly Haven sky and a low sun. It is generated in the Editor with **Aurelle > Build Exterior District** (`ExteriorEnvironmentBuilder`) and animated at runtime by `ExteriorTrafficSignal`, `ExteriorTrafficCarAgent`, `ExteriorPedestrianAgent` and `ExteriorCyclistAgent`.

## Visual quality

The look depends on the quality level. Standalone builds and the Editor use Ultra; Android (Quest) uses Low.

| | PC: Ultra (`Quality URP Config`, renderer `Standalone Preset`) | Quest: Low (`Performance URP Config`, renderer `Android Preset`) |
| --- | --- | --- |
| Rendering | HDR, Forward+, MSAA 4× | No HDR, Forward, MSAA 4×, up to 4 per-pixel additional lights per object |
| Post-processing | Global `Aurelle Post Volume`: ACES tonemapping, colour adjustments, white balance, bloom, vignette; SMAA and dithering on the desktop camera | None. The Android renderer has no post-processing data and no SSAO |
| Ambient occlusion | SSAO, plus baked AO in the showroom lightmaps | Baked AO in the showroom lightmaps only |
| Shadows | Soft, 4096 px, four cascades over 90 m; real-time soft shadow from the hero car's key light | Soft, 2048 px, two cascades over 35 m; no additional-light shadows |
| Showroom lighting | Baked: lightmaps, light probes and reflection probes (the hall's probe is box-projected) | The same bake, but without reflection-probe blending or box projection; Android imports lightmaps at up to 1024 px and reflection cubemaps at 128 px |

Post-processing is enabled only on the desktop camera; the XR rig's camera keeps it off. Because the Editor's saved quality level is Ultra, the Game view and Play Mode show the PC look. To preview Quest, select **Low** in **Project Settings > Quality**, and select Ultra again before committing, because the choice is saved in `ProjectSettings/QualitySettings.asset`. The Android texture size limits apply only in Android builds.

### Rebuilding the look

Editor tools in `Assets/Editor/AurelleRealism` generate the look and save it into SampleScene and project assets. To rebuild it, open SampleScene, leave Play Mode and run them in this order (the menus are under **Aurelle > Realism**):

| Step | Menu | Batch method |
| --- | --- | --- |
| 1. People: presenter and crowd | **Upgrade People** | `AurelleRealismPeople.Batch` |
| 2. Car and engine materials | **Cars > Apply Car Materials** | `CarsRealism.Batch` |
| 3. Pipeline, lights, probes, post-processing | **Rendering > Apply Render Setup (no bake)** | `AurelleRenderSetup.Batch` |
| 4. Showroom interior | **Upgrade Showroom Interior** | `AurelleRealism.InteriorRealismUpgrade.Batch` |
| 5. Render setup again, then bake lighting | **Rendering > Apply Render Setup and Bake Lighting** | `AurelleRenderSetup.BatchBake` |

- **Batch mode:** close the project in the Editor, then run `<Unity> -batchmode -projectPath <project> -executeMethod <method>`. Each method opens SampleScene, saves the scene and assets, and exits.
- **Saving from the menu:** **Upgrade People** and **Upgrade Showroom Interior** do not save the scene. Save it after running them.
- **Baking:** bake only with step 5. The contact shadows under the premium cars come from stand-in boxes that the tool enables only during its own bake, so a bake from the Lighting window loses them.
- **After Build Exterior District:** run steps 2 and 5 again. The builder rebuilds the traffic prefabs from the raw GLBs and resets the sky ambient and reflection intensities, the sun and the street lamps. It reapplies the crowd's clothing itself.

[Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md#realism-editor-tools) explains what each tool changes and why the order matters.

## Controls

### Desktop

| Action | Input |
| --- | --- |
| Walk | W A S D or the arrow keys |
| Look around | Mouse, while the cursor is locked |
| Select a part or press a panel button | Left click: at the crosshair while the cursor is locked, at the pointer when it is free |
| X-ray on / off (from the exploded view: back to the normal car) | T (panel button X-RAY) |
| Full exploded view | O, `]` or Page Up (panel button EXPLODE) |
| Step back: a part that is out goes back first, then exploded → X-ray → normal | R, `[` or Page Down (panel button BACK) |
| Take a part's assembly out / put it back (normal and X-ray views) | Left click on the part / on empty space |
| Complete vehicle | Panel button RESET |
| English / Uzbek | Q / U (panel buttons EN / UZ) |
| Put a part that is out back, close the greeting, the overview and the part card, free the cursor | Esc |
| Lock or free the cursor | Tab |
| Open or close the entrance doors | E, within 4.5 m of the entrance |

A two-line help bar at the bottom of the screen and the help text on the explorer's control panel summarise these keys. After Esc, the overview and the part card come back with the next selection, step back (R, BACK) or reset. Falling below y = −25 returns you to the spawn. The explorer's mouse orbit, pan and zoom (right drag, middle drag, wheel) are disabled while the desktop walkthrough is active, so they are not available in this scene.

### XR

| Action | Input |
| --- | --- |
| Select a part (takes its assembly out; on empty space, puts it back) or press a panel button | Trigger on either controller, along that controller's ray |
| Grab a part | Grip, through the rig's XR Interaction Toolkit interactors; not while parts are moving, the car body cannot be lifted, and in the X-ray view the glass shell cannot be grabbed |
| Next view: normal → X-ray → exploded | A (right primary button) |
| Step back: a part that is out goes back first, then exploded → X-ray → normal | B (right secondary button) |
| Complete vehicle | Left Menu button |
| English / Uzbek | EN / UZ panel buttons |
| Move and turn | Thumbsticks, as set up by the VR template rig (XRI Starter Assets) |

The rig's jump, which is also on A, is disabled because A advances the explorer. The explorer's A, B, Menu and trigger bindings are controller bindings with no hand-tracking equivalent. Falling below y = −25 returns you to the spawn.

## Packages

All packages are resolved by the Unity Package Manager from `Packages/manifest.json` and `Packages/packages-lock.json`.

| Package | Version | Role in this project |
| --- | --- | --- |
| Universal RP | 17.3.0 | Render pipeline |
| Input System | 1.19.0 | All input |
| XR Plug-in Management | 4.6.0 | Starts OpenXR on Standalone and Android |
| OpenXR | 1.17.1 | XR runtime; Meta Quest and Android XR support, controller profiles and hand tracking are enabled for Android |
| Meta OpenXR | 2.5.1 | Meta-specific OpenXR features; not used by project scripts |
| Android XR (OpenXR) | 1.3.1 | Android XR support |
| XR Interaction Toolkit | 3.5.1 | Template rig, locomotion, grab and button interactables |
| XR Core Utilities | 2.6.0 | `XROrigin`, used to put the head at the spawn |
| XR Hands | 1.8.1 | Hand tracking in the template rig |
| AR Foundation | 6.5.0 | Not used by project scripts |
| XR Composition Layers | 2.5.0 | OpenXR composition layers feature (enabled) |
| glTFast | 6.20.0 | Imports every `.glb` (car, engine, premium collection) and the Poly Haven `.gltf` furniture models |
| KTX for Unity | 3.7.1 | Required by `CarConcept.glb`, which uses KTX2 (Basis Universal) textures |
| AI Assistant | 2.20.0-pre.2 | Pre-release Editor assistant; not used by project scripts |
| Unity Pipeline | 0.8.0-exp.1 | Experimental: lets external tools connect to the Editor or a running player and run commands; not used by project scripts |
| Engineering feature set, Tutorial Framework, Multiplayer Center | 1.0.2, 6.0.6, 1.0.1 | Editor tooling |

Consider removing the pre-release and experimental tooling packages if you do not use them.

## Known limitations

- **Exterior district is generated.** The district in `SampleScene.unity` comes from **Aurelle > Build Exterior District** (regenerated with the current builder in 6000.3.12f1). After changing `ExteriorEnvironmentBuilder.cs`, run it again in 6000.3.12f1. It rebuilds everything under `Exterior Environment`. Afterwards run steps 2 and 5 of [Rebuilding the look](#rebuilding-the-look) again.
- **Lighting is baked for the showroom only.** Changes to showroom geometry, lights or emissive materials need a new bake (step 5). The exterior is lit in real time and uses a coarse grid of light probes. The hero car is not in the bake because it comes apart in the Car Explorer. On Quest it therefore has no contact shadow, since Low has no additional-light shadows.
- **Needs the Editor or a device:**
  - Texture memory: the premium cars' materials use compressed copies of their GLB textures, at most 1024 px (ASTC on Android). The CarConcept keeps its KTX2 textures. Texture memory has not been checked in a build.
  - V6 draw calls: `V6_Engine_Prototype.glb` is a CAD export with 182 renderers, 6,250 submeshes and about 627,000 triangles. The explorer draws it only in the X-ray and exploded views and while it or the front section over it is taken out, and disables its shadows; merging the submeshes needs Editor-side mesh processing. The X-ray view also draws the car's shell as transparent glass and adds one real-time spot light.
  - Quest performance: frame time has not been measured on a device. Android's default quality level (Low, `Performance URP Config`) uses per-pixel additional lights (up to 4 per object) and 35 m two-cascade shadows. Geometry is heavy for Quest:
    - The ten traffic cars total about 1.96 million triangles and have no LODs. The Tesla model alone has about 346,000 triangles and the M4 about 207,000.
    - The furnished showroom interior has about 200,000 triangles, and each money tree has 20,000–28,000.
    - Plants and vases have cull-only LOD groups, so they disappear sooner at Low (LOD bias 0.4, against 2 at Ultra).
- **People.** URP Lit has no subsurface scattering, so skin looks slightly grey in the dim showroom, and a faint lit line can show at the lip seam. The eight pedestrians and two riders are `Remy.fbx` instances: they differ in clothes, hair colour and skin tone but share Remy's face.
- **Leftover assets.**
  - `Assets/Materials/CarNeutralTintedGlass.mat`, the seven flat `Assets/Materials/Remy*.mat` and the `Assets/Materials/Showroom*.mat` materials are no longer used by SampleScene. Some are still used by the old snapshot scenes, and all were left in place.
  - `Assets/Scenes/SampleScene/Lightmap-0_comp_dir.png` and `Lightmap-0_comp_shadowmask.png` are left over from the earlier bake and are unused. The current bake is non-directional and Baked Indirect, so it writes neither.
- **Foveated rendering is off.** The runtime request (`ExteriorSpawnPlacement`) and the SRP foveation API for Android are in place, but the OpenXR Foveated Rendering feature is disabled. In OpenXR 1.17.1 the feature always lists the eye-tracked foveation extension (`XR_META_foveation_eye_tracked`); with Quest Pro among the Meta Quest target devices, the Android manifest would then require eye tracking and request the eye-tracking permission.
- **XR startup.** A headset display that starts after the 6 s desktop fallback needs a scene restart. Without an XR rig in the scene, XR falls back to a head-tracked camera with no locomotion or controller interaction.
- **Car Explorer.**
  - In the X-ray view the XR grip cannot grab the glass shell, and XRI's grab cast often stops at it, so a grip aimed at a seat or the dashboard from outside the car may grab nothing. The trigger reaches them through the glass.
  - Taken-out and exploded assemblies keep their colliders while they move. A desktop visitor standing right beside the car is pushed aside by a part passing them, and a door can briefly pass through a headset user's view; only the end positions are kept clear of the viewer.
  - The underbody and axles assembly does not come apart: a click selects it and names it in the title, but nothing moves.
- **Presenter.** Remy plans direct routes with at most one detour point (there is no NavMesh), so he can stop and wait instead of reaching a visitor behind large obstacles.
- **Licensing (open issues, see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)).**
  - `Assets/Models/Presenter/Remy.fbx` is a raw Mixamo export. Mixamo's terms do not allow redistributing raw character files, so it should not be published in a public repository. The textures in `Assets/Models/Presenter/Textures/` are derived from it and share the issue.
  - The provenance and licence of `V6_Engine_Prototype.glb` are unknown.
  - The XR Hands sample in `Assets/Samples` is under the Unity Package Distribution License, whose grant covers distribution as part of a built project, not publishing its source files.
  - The CarConcept model (CC BY 4.0) is credited in the notices, but the app labels it "Aurelle CarConcept" and has no credits screen. The rear-wall display posters are renders of it and need the same attribution. Its tyre sidewalls carry "KHRONOS" lettering, a trademark that CC BY 4.0 does not license.
- **Project identity.** Product name `UnityProject`, company `DefaultCompany` and Android application ID `com.DefaultCompany.VRTemplate` are still the template defaults.

## Repository layout

```text
Assets/
  Scenes/            SampleScene.unity (build scene), BasicScene.unity, SampleScene_Before_* snapshots
  Scripts/           Project scripts (see Docs/ARCHITECTURE.md)
  Editor/            AurelleRealism/: the realism editor tools (Aurelle > Realism menu)
  Resources/         AurelleDepthTestedText.shader and AurelleXRayGhost.shader, loaded at runtime
  Models/            CarConcept.glb, V6_Engine_Prototype.glb, ShowroomCollection/, Presenter/Remy.fbx and
                     Presenter/Textures/ (derived from it), ExteriorPedestrians/ (unused), Exterior/ (generated meshes)
  Materials/         Realism/ (Cars, Interior, People, Rendering): materials, meshes and textures written by the
                     realism tools; Exterior/ holds the builder's materials and textures; older showroom materials
  Prefabs/           ExteriorTraffic/ vehicle prefabs written by the builder
  Environment/       Poly Haven HDRI and surface textures used by the exterior
  ThirdParty/        CC0 Poly Haven and ambientCG textures and models for the interior, one folder per asset
                     with a SOURCE.txt
  Settings/          URP pipeline assets, renderer data and scene templates; Rendering/ holds the lighting
                     settings and the post-processing profile
  XR/                XR Plug-in Management and OpenXR settings
  VRTemplateAssets/, Samples/, XRI/, TextMesh Pro/   VR template and package sample content
  Backups/           Old controller and scene backups, not used (as are the CarExplorerController_*.cs.txt files)
Packages/            Package manifest and lock file
ProjectSettings/     Player, quality, graphics, input and build settings
Docs/                ARCHITECTURE.md
```

## Further documentation

- [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md): scripts, startup order, cross-script and naming contracts, exterior generation, realism tools, rendering
- [CONTRIBUTING.md](CONTRIBUTING.md): Unity version, Git workflow, regenerating the district, changing the look, merging scenes
- [CHANGELOG.md](CHANGELOG.md): history of notable changes
- [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md): third-party models, textures, fonts and packages

## License

The project's own code and content are under the MIT License; see [LICENSE](LICENSE). Third-party models, textures, fonts, samples and packages keep their own licences, listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md); some of them do not permit redistribution under MIT terms.
