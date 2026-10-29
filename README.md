# AR-VR Project: Aurelle Car Showroom

A Unity project for "Aurelle", a fictional car dealership: a glazed showroom whose hero car, the CarConcept, can be taken apart level by level down to single parts of its procedurally built internals, a procedurally animated presenter (Remy), a premium car collection and a generated city district around the building. The same scene runs as a desktop first-person walkthrough by default and switches to the XR rig when a headset is active.

- **Unity Editor:** 6000.3.12f1, exactly (`ProjectSettings/ProjectVersion.txt`)
- **Rendering:** Universal Render Pipeline 17.3.0
- **XR:** OpenXR 1.17.1 through XR Plug-in Management 4.6.0; XR Interaction Toolkit 3.5.1 rig from the Unity VR template
- **Input:** Input System 1.19.0 only (the legacy Input Manager is disabled)
- **Scene:** `Assets/Scenes/SampleScene.unity`, the only scene in the build list

## Getting started

1. Install [Git LFS](https://git-lfs.com) and run `git lfs install` once before cloning. The baked lighting and the textures that the editor tools generate are stored in LFS; without it they check out as small pointer files.
2. Install Unity **6000.3.12f1** with Unity Hub. For Quest or Android XR builds add Android Build Support (with OpenJDK and the Android SDK and NDK); for desktop builds add the IL2CPP module of your platform. Both targets use IL2CPP.
3. Open the repository folder from Unity Hub with that exact version. Any other version upgrades and re-serializes the project.
4. The first import resolves the packages pinned in `Packages/packages-lock.json` (network access is needed) and imports the GLB models through glTFast. Expect it to take a while.
5. Open `Assets/Scenes/SampleScene.unity` and press Play.

### Desktop or XR

`ExteriorSpawnPlacement` chooses the mode before any other script runs:

- **Desktop** when no XR loader is active, for example in Play Mode on macOS or on a PC without an OpenXR runtime: the camera starts at `Player Spawn` on the arrival plaza in front of the showroom, 1.7 m above the ground and facing the entrance, and `DesktopWalkthroughController` takes over.
- **XR** when an OpenXR loader is active: the VR template rig `XR Origin Hands (XR Rig)` is switched on at `Player Spawn` and the desktop camera is switched off. Once head tracking settles, the rig is moved so that the headset stands at the spawn and faces the showroom.
- If XR was chosen only because a loader was running and no headset display starts within 6 s, the scene falls back to desktop and logs a warning. A headset that starts later needs a scene restart.
- **Android (Quest)** has no desktop mode: an Android player always starts in XR mode and waits for the headset display, however long it takes.

"Initialize XR on Startup" is enabled for Standalone and Android, so a desktop build on a PC with an active OpenXR runtime and headset (for example Quest Link or SteamVR) starts in XR.

## Building for Quest

**Aurelle > Build > Quest APK** builds `Builds/Aurelle-Quest.apk` (`Builds/` is git-ignored) for Meta Quest 2, Quest Pro, Quest 3 and Quest 3S. From a terminal, start Unity on Android so that the editor scripts are compiled for it:

```sh
mkdir -p Builds
Unity -batchmode -projectPath . -buildTarget Android -executeMethod AurelleBuild.BuildQuestApk -logFile Builds/build.log
```

`Unity` is the editor binary, `/Applications/Unity/Hub/Editor/6000.3.12f1/Unity.app/Contents/MacOS/Unity` on macOS. The method exits with 0 when the APK was built and 1 otherwise. It writes `Builds/Aurelle-Quest.txt` (the commit, result, size, build time, errors and warnings and the time of each build step) and `Builds/Aurelle-Quest-check.txt` (the Quest settings and the validation results).

- The build switches the editor to Android if needed and applies what every Quest APK needs: IL2CPP, ARM64, ASTC textures and a non-development APK (not an App Bundle). It stops before building when the OpenXR loader is not active for Android or the OpenXR project validation for Android reports errors, and deletes the previous APK first, so a failed build leaves none. **Aurelle > Build > Check Quest Settings** (`AurelleBuild.CheckQuestSettings` in batch mode) writes the check report without changing anything; run it with the editor on Android, because some Meta Quest rules of OpenXR 1.17.1 fail on other build targets.
- Everything else is in the project settings: product name Aurelle Showroom, company Aurelle, application ID `com.aurelle.showroom`, version 1.0 (code 1); minimum and target API level 34 (Android 14, the base of every Horizon OS release since v76); Vulkan; linear colour space; landscape left; Game Activity. OpenXR for Android: Meta Quest Support with the target devices above, the Oculus Touch, Meta Quest Touch Pro and Meta Quest Touch Plus controller profiles, hand tracking and Single Pass Instanced (multiview) rendering. The headset runs quality level Low; Android builds leave out Ultra (the PC look), which the Quest never uses, so the APK carries only the Quest pipeline and its shaders (153.7 MB instead of 244.1 MB).
- On the lab Mac (8 cores, shared with other Unity jobs), switching a desktop-imported project to Android took 2.5 minutes. The first Android build, made while Ultra was still part of Android builds, took 2 h 29 min: 2 h 10 min to compile 23,024 Vulkan shader variants of 114 shaders (URP Lit alone 10,316, the glTF shader graphs 3,482), 10.5 min for IL2CPP and 7 min for Gradle. The compiled variants and the IL2CPP output stay in `Library`: the next build took 2 minutes (Gradle 1 minute), every variant came from the cache, and without Ultra it needed 9,184 variants of 97 shaders, so a first build on another machine now compiles 60 % fewer variants.
- To install on a headset in developer mode: `adb install -r Builds/Aurelle-Quest.apk`.
- Android XR support stays enabled in the OpenXR settings, so the APK also carries the Android XR manifest entries, among them three required Android XR features (`android.software.xr.api.openxr`, `android.software.xr.api.SPATIAL`, `android.hardware.xr.input.hand_tracking`). Android does not check `uses-feature` on install, so `adb install` should not be affected (untested on a headset); stores filter on them, so turn Android XR support off for Android before a Meta Horizon Store upload. The XR Project Validation window lists two Android XR errors (HDR and post-processing on the active URP asset): it checks the Editor's quality level (Ultra), not Android's Low, and does not block the build.
- Android starts at the Low quality level, where the [Quest budget](Docs/ARCHITECTURE.md#quest-budget-low) and the explorer's low proxies cut the draw count (see Known limitations for what is left).

## What the scene contains

- **Showroom.** The interior with a circular podium carrying the CarConcept, a reception desk, a customer lounge and rear-wall displays, finished with CC0 Poly Haven and ambientCG materials and furniture. The front is glazed: four glass bays with mullions, transoms and an entrance frame. Walls, display cars and floor-standing furniture get colliders at runtime (`ShowroomCollisionSetup`).
- **Entrance doors (desktop).** Two hinged glass doors hang in the entrance. They open by themselves when the visitor or Remy comes within 2.5 m of the opening, swinging away from whoever opens them, and close 3 s after the doorway is clear; E opens or closes them by hand. In XR the entrance has no doors.
- **Hero car.** The CarConcept on the podium has its own look: carmine metallic paint with fine flakes under a clear coat and a gloss black second paint (the flakes are a metallic-roughness mask; neither paint has a normal map, so both keep their colour at low and grazing angles), two-tone wheels, tyres with raised AURELLE SPORT sidewall lettering, cross-drilled brake discs, red calipers with an AURELLE logo, a windscreen with a ceramic frit border, HDR LED lamps, leather, Alcantara and carbon inside, and an AURELLE / MOTOR HOUSE rear plate. It is presented like a studio car: a softbox canopy of four light boxes over the podium, its own reflection probe, a contact shadow on the deck that fades out while parts are out of the car, and on PC a planar reflection of the car in the polished podium deck.
- **Car Explorer.** `CarExplorerController` on `CarConcept` shows the car as a structure tree, from the vehicle through 13 systems (Body, Structure, Closures, Interior, Wheels, Brakes, Suspension, Steering, Powertrain, Driveline, Fuel, Cooling, Electrical) and 54 assemblies down to single parts: 1,653 nodes and 1,302 selectable parts in SampleScene (95 of the car's own, the V6's 182 and 1,025 generated internals). It has three views:

  | View | What happens |
  | --- | --- |
  | Normal | The assembled car. The V6 and the internals are not drawn. |
  | X-ray | The body shell fades in 0.6 s into light-blue glass with bright edges, and a soft spot light fades in over the car. The floor tub and the cabin trims become fainter glass, so the suspension, the bulkhead systems, the seat frames and the steering column show. The V6, the internals, the seats, the steering wheel, the pedals and the wheels stay solid in their own materials (shader `Aurelle/X-Ray Ghost` in `Assets/Resources`). |
  | Exploded | Every assembly comes out of the car over 1.5 s: the car's own in three tiers as in a workshop manual (the floor tub with the wheels and the V6, the seats and cockpit above it, the body frame with the roof, windscreen, doors and front and rear sections on top), and the internals outward around it on the podium, on a height tier per system. |

  The car comes apart gradually. A click on a part takes its assembly out of the car in one piece and brings it toward the visitor:
  - **Where it stops.** On the line from the assembly's place in the car to the visitor's eye, so on screen it comes nearer instead of sliding across the view, at about chest height and 1.4–2.6 m from the eye where there is room. It always ends outside the car's footprint (never in, under or over the car, the roof and the bonnet included) and where the car cannot hide it. Where there is no room on that line it first comes nearer, down to 0.5 m from the eye, and only then turns aside, as little as it must to find a place in view and clear of Remy, the other cars, the furniture and the walls. On the desktop, when you stand too close to the car for it to fit in front of you, you glide straight back from the car (up to 1.5 m) while it comes out, and it ends in front of you. In XR the rig never moves by itself (the assembly may come as near as 0.9 m from the eye): close to the car there is often no such place, and it ends beside you, at the edge of the view or out of it (see [Known limitations](#known-limitations)).
  - **How it gets there.** It lifts straight up out of its place until it clears the car's top (a door or a wheel moves straight out sideways instead, unless a visitor stands right there), travels over the car and comes down onto its place, never through the body or other parts; a path that would pass the visitor below head height (checked along the whole path as it runs) goes over their head. Put back runs the same path in reverse. A take-out takes 0.8 s.
  - **Opening levels.** The next click on it makes it come apart one level around the place where it stands, and each further click opens the next level along the clicked part's chain, down to the single part. Where a level would reach into the car, the assembly moves toward the visitor and stays near where it was; only where there is no room for that, or a new place nearly in the same direction (or, when the push leaves less than half of the levels in view, any new place) shows the levels better, does it take a new place by the same rules.
  - **Walking.** What is out stays where it is while the visitor walks round. A click on it or a level opened from a spot where it is out of view or behind the car brings it to the visitor again, the same way.

  A click on a part of another assembly puts the first one back and then takes that one out (0.6 s each); in the exploded view a click opens the assembly in place. Assemblies inside the body (the V6, the seats, the cockpit, the body frame, the cabin floor and the internals) switch the normal view to X-ray when they come out; the cabin floor and the underbody stay in place and only come apart. While anything is apart, the parts outside the deepest open level are darkened. The selected node is highlighted, its card shows its name, system, description, material and the parts it holds, and Remy looks and points at it. Grabbed parts (XR) glide back when released. Texts are in English and Uzbek.
- **Car internals.** The internals are built procedurally by an editor tool (**Aurelle > Car Internals > Build**) in a clean CAD style, measured against the car: double-wishbone front and multi-link rear suspension with anti-roll bars and subframes, rack-and-pinion steering with electric assistance and its column, the brake hydraulics (pedal box, booster and master cylinder, ABS and ESC modulator, lines and hoses, electric parking-brake actuators), the transaxle with clutch, differential and half shafts, an electric rear drive unit and a hybrid battery module, the fuel tank, lines and rail, the radiator module, coolant circuit, belt drive and A/C circuit, the battery, power distribution, engine control unit and main harness, and the body structure (sills, floor rails, cross-car beam, seat frames, roll structure, rear crash structure). That is 1,025 parts in 38 assemblies of 9 systems, about 283,000 triangles. The tool also regroups the V6 into 19 functional groups (crank train, block, heads, valve trains, intake, exhaust manifolds, timing, lubrication) and merges its submeshes. Every part carries English and Uzbek names and descriptions, its system and its material, and the materials have generated surface finishes (cast, machined, e-coated, zinc-plated, rubber, braided and others). An interference check confirms that no part cuts into the car's body, cabin, wheels, V6 or floor, or into another assembly, apart from declared contacts.
- **HUD.** The explorer's only UI is a CAD-style HUD that keeps the middle of the view free: the STRUCTURE tab with the tree and the breadcrumb top left, the view switch (Normal / X-ray / Explode), EN / UZ and `?` on top, the selected node's card on the right (Take out or Explode, Inspect, Collapse, Put back), and a bottom row with the keys for the current state and an eye button that hides the panels. On the desktop the tree stays a closed tab until the visitor opens it, and the card shows only while something is selected. In a small Game view or window (under 1440 × 810 pixels) the HUD switches to a compact layout with a smaller scale, a narrower card and a shorter key hint; at 708 × 440 the panels cover about 20–23 % of the view with a part selected and the tree closed. In XR the same panels, with the tree open and an overview card, stand on a plate beside the car, about 1.06 m wide, pressed with the controller rays.
- **Inspect camera (desktop).** F, the card's Inspect button or a double click on a part flies the camera in 0.6 s to frame the selection in the free part of the view, from the visitor's side and clear of walls, furniture, Remy and other parts. Dragging orbits, right-dragging pans and the wheel zooms; clicks still select and open levels. F, Esc or a walking key flies the camera back to the visitor's eye; the visitor's body never moves while inspecting.
- **Presenter Remy.** `ShowroomPresenterAgent` on `Remy Presenter`. When the visitor enters the showroom, from any side, Remy walks up to them, and his welcome appears on the HUD once he has arrived. He stays with the visitor while the welcome shows or until they start exploring, at most 20 s after arriving, then takes a station beside the car the visitor attends (the hero car, or a premium display they look at for 3 s): never between them and the car, out of their sight lines to it and not behind the parts they are studying, including the node the inspect camera frames. He moves only when he has to, and looks and points at the selected part. Remy has no animation controller: walking, looking and pointing are procedural.
- **Premium collection.** BMW M3 E30, BMW M4 Competition and Tesla Model 3 (2018) on displays behind the podium, placed as instances of the GLBs in `Assets/Models/ShowroomCollection`.
- **Exterior district.** Streets, 18 buildings, street furniture, five signalised junctions, six moving and four parked cars, eight pedestrians, two cyclists, a Poly Haven sky and a low sun. The moving cars' wheels roll about their own hubs by the distance travelled over their radius and the front wheels steer with the path, up to 35°. The cyclists ride a procedural city bicycle (about 25,000 triangles: spoked 700×35c wheels, a 1×8 drivetrain, disc brakes, fenders and a kickstand, and on one of its two colour schemes a rear rack and dynamo lights) whose wheels and cranks turn with the ride, with the rider's feet on the pedals and hands on the grips. The district is generated in the Editor with **Aurelle > Build Exterior District** (`ExteriorEnvironmentBuilder`) and animated at runtime by `ExteriorTrafficSignal`, `ExteriorTrafficCarAgent`, `ExteriorPedestrianAgent` and `ExteriorCyclistAgent`.

## Visual quality

The look depends on the quality level. Standalone builds and the Editor use Ultra; Android (Quest) uses Low, and Android builds leave Ultra out.

| | PC: Ultra (`Quality URP Config`, renderer `Standalone Preset`) | Quest: Low (`Performance URP Config`, renderer `Android Preset`) |
| --- | --- | --- |
| Rendering | HDR, Forward+, MSAA 4× | No HDR, Forward, MSAA 4×, up to 4 per-pixel additional lights per object |
| Post-processing | Global `Aurelle Post Volume`: ACES tonemapping, colour adjustments, white balance, bloom, vignette; SMAA and dithering on the desktop camera | None. The Android renderer has no post-processing data and no SSAO |
| Ambient occlusion | SSAO, plus baked AO in the showroom lightmaps | Baked AO in the showroom lightmaps only |
| Shadows | Soft, 4096 px, four cascades over 90 m; real-time soft shadow from the hero car's key light | Soft, 2048 px, two cascades over 35 m; no additional-light shadows |
| Showroom lighting | Baked: lightmaps, light probes and reflection probes (the hall's probe is box-projected) | The same bake, but without reflection-probe blending or box projection; Android imports lightmaps at up to 1024 px and reflection cubemaps at 128 px |
| Hero car studio | Baked softbox canopy, 512 px hero reflection probe, contact shadow, planar reflection in the podium deck (not in the X-ray view) | The same canopy and contact shadow; the hero probe imports at 256 px on Android; no planar reflection (Ultra only, and never in XR) |
| Car Explorer content | All 1,302 parts; the internals' surface maps at 512 px | Fine-detail parts (fasteners, clips, thin wires, the V6's valve springs) are left out: 819 parts; surface maps at 256 px ASTC 6×6 on Android |

Post-processing is enabled only on the desktop camera; the XR rig's camera keeps it off. LED lamps and light sources bloom on PC and clip to a flat colour on Quest. Because the Editor's saved quality level is Ultra, the Game view and Play Mode show the PC look. To preview Quest, select **Low** in **Project Settings > Quality**, and select Ultra again before committing, because the choice is saved in `ProjectSettings/QualitySettings.asset`. The fine-detail parts are dropped when the explorer starts, so enter Play Mode after switching. The Android texture size limits apply only in Android builds.

### Rebuilding the look and the internals

Editor tools generate the look of the showroom, the cars, the people and the bicycles (`Assets/Editor/AurelleRealism`, menus under **Aurelle > Realism**) and the car's internals (`Assets/Editor/CarInternals`, menu **Aurelle > Car Internals**), and save them into SampleScene and project assets. To rebuild everything, open SampleScene, leave Play Mode and run them in this order:

| Step | Menu | Batch method |
| --- | --- | --- |
| 1. People: presenter and crowd | **Aurelle > Realism > Upgrade People** | `AurelleRealismPeople.Batch` |
| 2. Car and V6 materials, the hero car's generated textures | **Aurelle > Realism > Cars > Apply Car Materials** | `CarsRealism.Batch` |
| 3. Pipeline, lights, probes, post-processing, hero studio | **Aurelle > Realism > Rendering > Apply Render Setup (no bake)** | `AurelleRenderSetup.Batch` |
| 4. Showroom interior and glazing | **Aurelle > Realism > Upgrade Showroom Interior** | `AurelleRealism.InteriorRealismUpgrade.Batch` |
| 5. The cyclists' bicycles | **Aurelle > Realism > Bicycles** | `AurelleBicycles.Batch` |
| 6. Car internals, the V6's groups and merged meshes | **Aurelle > Car Internals > Build** | `AurelleCarInternals.Batch` |
| 7. Render setup again, then bake lighting | **Aurelle > Realism > Rendering > Apply Render Setup and Bake Lighting** | `AurelleRenderSetup.BatchBake` |

- **Batch mode:** close the project in the Editor, then run `<Unity> -batchmode -projectPath <project> -executeMethod <method>`. Each method opens SampleScene, saves the scene and assets, and exits.
- **Saving from the menu:** **Upgrade People** and **Upgrade Showroom Interior** do not save the scene. Save it after running them. The other items save it.
- **Why this order:** the car materials must be in place before the internals tool merges the V6's submeshes, which it does only for renderers whose slots hold one material; the interior places its fixtures from the lights that the render setup has moved, and its glazing probe copies the hall probe; the bake needs the final geometry.
- **Baking:** bake only with step 7. The contact shadows under the premium cars come from stand-in boxes that the tool enables only during its own bake, so a bake from the Lighting window loses them.
- **After Build Exterior District:** run steps 2 and 7 again. The builder rebuilds the traffic prefabs from the raw GLBs and resets the sky ambient and reflection intensities, the sun and the street lamps. It reapplies the crowd's clothing and builds the same bicycles itself.
- **Display posters:** `AurelleRealism.InteriorPosterRenderer.Batch` (batch mode only) renders the three rear-wall posters from the hero car. Run it when the car's look changes.

Each tool is idempotent: a second run writes no asset (the bicycle tool's new materials settle on its second run), and a new bake differs only in lightmap noise. [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md#realism-editor-tools) explains what each tool changes and why the order matters.

## Controls

### Desktop

| Action | Input |
| --- | --- |
| Walk | W A S D or the arrow keys |
| Look around | Mouse, while the cursor is locked |
| Lock or free the cursor (free it to use the HUD) | Tab |
| Select a part and take its assembly out; on the assembly that is out, open the next level that holds the part | Left click on the part: at the crosshair while the cursor is locked, at the pointer when it is free |
| Clear the selection | Left click on empty space, or Esc (both leave the parts where they are) |
| Inspect the selection | F, the card's Inspect button, or a double click on a part |
| While inspecting: orbit, pan, zoom | Left drag, right drag, mouse wheel |
| Stop inspecting and walk on | F, Esc or a walking key; Tab also locks the cursor |
| X-ray on / off (from the exploded view: back to the normal car) | T |
| Exploded view | O, `]` or Page Up |
| Choose a view | Normal, X-ray or Explode on the HUD's view switch |
| One step back: close the deepest open level, then put the assembly back, then exploded → X-ray → normal | R, `[` or Page Down |
| Take out or open the selected node / close its open levels / put the assembly back | The card's Take out or Explode / Collapse / Put back buttons |
| Complete car, nothing selected | Reset on the explore chip, shown when nothing is selected and the car is not whole |
| STRUCTURE tree open / closed | P or the STRUCTURE tab. In the tree a click selects a row, a double click or the row's explode icon opens it, and hovering highlights the part |
| Hide / show the panels | I or the eye button |
| Help with every key | H or `?` |
| English / Uzbek | Q / U, or EN / UZ |
| Open or close the entrance doors by hand | E, within 4.5 m of the entrance (they also open by themselves) |
| Close the welcome, free the cursor | Esc |

A taken-out assembly stays where it stopped while you walk. If you walked to where it is out of view or behind the car, click it again (or open a level) and it comes to you again.

When you stand too close to the car for an assembly to fit between you and it, you glide straight back from the car (up to 1.5 m, never into a wall, a display car or Remy) while it comes out, so it ends in front of you. A walking key or moving the mouse stops the glide.

The bottom row of the HUD shows the keys for the current state (fewer in the compact layout), and H lists them all. Falling below y = −25 returns you to the spawn. The explorer's own mouse orbit, pan and zoom (right drag, middle drag, wheel) are disabled while the desktop walkthrough is active; the inspect camera takes their place.

### XR

| Action | Input |
| --- | --- |
| Select a part and take its assembly out, or open the next level that holds it; press a HUD button | Trigger on either controller, along that controller's ray |
| Grab a part and look at it closely | Grip, through the rig's XR Interaction Toolkit interactors. The part glides back when released; grabs are refused while parts move, and in the X-ray view the glass shell cannot be grabbed |
| Next view: normal → X-ray → exploded | A (right primary button) |
| One step back | B (right secondary button) |
| Complete car | Left Menu button |
| Tree, card buttons, view switch, English / Uzbek | On the HUD plate beside the car, with the trigger |
| Move and turn | Thumbsticks, as set up by the VR template rig (XRI Starter Assets) |

Tips for taking parts out in XR: stand about 1.5–3 m from the car. The rig never moves by itself, so from closer there is often no room between you and the car, and the assembly ends beside you, at the edge of the view or out of it; turn your head toward it, or step back and click it again (it comes to you again when it is out of view or behind the car). From about 3 m most take-outs end in view in front of you.

The rig's jump, which is also on A, is disabled because A advances the explorer. The explorer's A, B, Menu and trigger bindings are controller bindings with no hand-tracking equivalent. The inspect camera, the compact layout and the panel hiding are desktop features. Falling below y = −25 returns you to the spawn.

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
| XR Interaction Toolkit | 3.5.1 | Template rig, locomotion, grab and button interactables, the HUD plate's ray input |
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

- **Not built yet.** Four pieces of the internals clashed with the combined layout and are switched off in the tool: the exhaust (`ExhaustFuel.BuildExhaust`), the front crash structure (`BodyStructure.BuildFrontCrash`), the HVAC module (`CoolingElectrical.BuildHvac`) and the timing-end engine mount (`Drivetrain.BuildTimingEndMount`). The Exhaust system therefore has no parts and is not listed in the tree.
- **Heaviest Quest states.** At Low (Android's level) the [Quest budget](Docs/ARCHITECTURE.md#quest-budget-low) and the explorer's low proxies bring the walking views to 204–694 batches and the X-ray overview to 429 batches and 1.5 million triangles, measured in the Editor with a Quest 3-like stereo view. With the V6 out or apart in X-ray, and in the exploded view, the car is drawn part by part with the V6 and the internals: 700–950 batches and 1.3–1.5 million triangles, plus the transparent glass shell and one real-time spot light in X-ray. That is above the budget the Quest work aimed at for Quest 3 at 72 Hz (about 400 batches and 1 million triangles in the explorer's views). Frame times were measured only in the Editor on a Mac, not on a Quest.
- **XR and the Quest APK are untested on a headset.** The XR paths (rig placement, controller selection and grabbing, the HUD plate, where taken-out assemblies stand) were checked only in Editor simulations with a Quest-like head camera. The APK builds, and its manifest, ABIs and native libraries were checked, but it has not been installed or run on a headset: XR startup, controller and hand input, frame rate and memory on a Quest are unverified.
- **Exterior district is generated.** The district in `SampleScene.unity` comes from **Aurelle > Build Exterior District** (regenerated with the current builder in 6000.3.12f1). After changing `ExteriorEnvironmentBuilder.cs`, run it again in 6000.3.12f1. It rebuilds everything under `Exterior Environment`. Afterwards run steps 2 and 7 of [Rebuilding the look and the internals](#rebuilding-the-look-and-the-internals) again.
- **Lighting is baked for the showroom only.** Changes to showroom geometry, lights or emissive materials need a new bake (step 7). The exterior is lit in real time and uses a coarse grid of light probes. The hero car is not in the bake because it comes apart in the Car Explorer; its contact shadow is a generated texture on a quad, which renders at every quality level.
- **Needs the Editor or a device:**
  - Texture memory: the premium cars' materials use compressed copies of their GLB textures, at most 1024 px (ASTC on Android). The CarConcept keeps its KTX2 textures. Texture memory has not been checked in a build.
  - Quest performance: frame time has not been measured on a device. Android's default quality level (Low, `Performance URP Config`) uses per-pixel additional lights (up to 4 per object) and 35 m two-cascade shadows. Geometry is heavy for Quest:
    - The ten traffic cars total about 1.96 million triangles and have no simplified LODs; at Low the budget switches every second moving car, pedestrian and cyclist off and culls small or distant ones (cull-only LOD groups). The Tesla model alone has about 346,000 triangles and the M4 about 207,000. The two bicycles add about 49,000.
    - The furnished showroom interior has about 200,000 triangles, and each money tree has 20,000–28,000.
    - Plants and vases have cull-only LOD groups, so they disappear sooner at Low (LOD bias 0.4, against 2 at Ultra).
- **Car Explorer.**
  - Where a taken-out assembly stops depends on where the visitor stands. A lab run drove the explorer from 17 visitor spots (each side of the car at 0.8, 1.5 and 3 m from it, the four corners, and 1.5 m off the left side between the door and the front wheel, where the owner stood, plus a walk round the car) for nine assemblies (the V6 out, opened one level and down to the intake and the rocker arms, a front and a rear wheel, a door, the seats, the radiator, the transaxle, the brake booster and the front section), in the normal and X-ray views: on the desktop at 700 × 440 and 1920 × 1080, and in XR with a Quest 3-like head camera (96° × 104°, the eye 1.65 m over the floor). In every XR case no part, and no opened assembly's whole box, ended inside the car's footprint, the car never hid an assembly's centre, no planned path ran into another part after leaving its place or within 0.25 m of the visitor's eye below their head, and every put back restored the car exactly. On the desktop (700 × 440 X-ray and normal, 1080p X-ray) the same held, with the step back gliding the visitor back 56 times in each X-ray run (median 0.75 m, at most 1.5 m); the only exception was the walk round itself: from the far side the visitor saw the taken-out wheel behind the car until they clicked it again.
  - **Close-range placement.** An assembly needs its box 0.5 m from the eye on the desktop (0.35 m in XR) and outside the car's footprint, so it cannot stop between the car and a visitor who stands nearer than about its depth plus that. On the desktop the visitor then glides back. In XR nothing moves the visitor: from 0.8 m only 15 % of the X-ray placements (0 % in the normal view) and from 1.5 m 34 % (19 %) were in view, the rest ending beside the visitor, at the edge of the view or out of it, so the visitor has to turn their head or step back and click again. From 3 m 80 % (X-ray) and 91 % (normal) were in view, from the corners 63 % and 69 %. Stand about 1.5–3 m from the car in XR.
  - Over all spots and assemblies, in XR 48 % (X-ray) and 43 % (normal) of the placements had at least 85 % of their box in the free view, 63 % and 56 % of the take-outs ended within 20° of the line toward the visitor, and 59 % and 55 % of the levels opened with the assembly moving no more than 0.3 m. On the desktop, with the step back: 51–52 % (X-ray) and 48 % (normal) in view, 60–63 % and 51 % within 20°, and 67–71 % and 69 % of the levels opened with the assembly moving no more than 0.3 m.
  - Opening a level computes the new layout on the main thread: in the lab (Editor on a Mac, other Unity instances running) `Open()` took a median of 12 ms in X-ray and 6 ms in the normal view in XR (11–12 ms on the desktop), at most 44 ms in the final runs and up to about 0.5 s once in earlier runs under heavier load; expect a longer hitch on a Quest.
  - In the exploded view all 38 internals assemblies stay on the podium, but the boxes of about 20 pairs of them overlap, most of them the long assemblies over the car.
  - In the X-ray view the XR grip cannot grab the glass shell, and XRI's grab cast often stops at it, so a grip aimed at a seat or the dashboard from outside the car may grab nothing. The trigger reaches them through the glass.
  - Moving assemblies keep their colliders. Planned paths keep 0.35 m from the visitor (over their head where needed), but a desktop visitor who steps into a passing part can still be pushed aside.
- **Presenter.** Remy plans his routes with physics probes, not a NavMesh (straight, round the car, or via one detour point), so he can stop and wait instead of reaching a visitor behind large obstacles. In some inspect views he stands at the edge of the frame or behind the card.
- **Hero studio.** The polished podium deck (smoothness 0.9) shows a spot light's highlight that blooms into a bright patch in some low or close views.
- **Display posters.** The three rear-wall posters were rendered before the hero car's current look and still show its earlier materials. Re-rendering them rewrites binary textures, so it was left for a separate change.
- **Regenerated scene objects.** The bicycle and internals tools recreate their scene objects with new file IDs on every run, so each run changes SampleScene even when the assets come out byte-identical.
- **People.** URP Lit has no subsurface scattering, so skin looks slightly grey in the dim showroom, and a faint lit line can show at the lip seam. The eight pedestrians and two riders are `Remy.fbx` instances: they differ in clothes, hair colour and skin tone but share Remy's face.
- **Leftover assets.** `Assets/Materials/CarNeutralTintedGlass.mat`, the seven flat `Assets/Materials/Remy*.mat` and the `Assets/Materials/Showroom*.mat` materials are no longer used by SampleScene. Some are still used by the old snapshot scenes, and all were left in place.
- **Foveated rendering is off.** The runtime request (`ExteriorSpawnPlacement`) and the SRP foveation API for Android are in place, but the OpenXR Foveated Rendering feature is disabled. In OpenXR 1.17.1 the feature always lists the eye-tracked foveation extension (`XR_META_foveation_eye_tracked`); with Quest Pro among the Meta Quest target devices, the Android manifest would then require eye tracking and request the eye-tracking permission.
- **XR startup.** On desktop platforms, a headset display that starts after the 6 s desktop fallback needs a scene restart. Without an XR rig in the scene, XR falls back to a head-tracked camera with no locomotion or controller interaction.
- **Licensing (open issues, see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)).**
  - `Assets/Models/Presenter/Remy.fbx` is a raw Mixamo export. Mixamo's terms do not allow redistributing raw character files, so it should not be published in a public repository. The textures in `Assets/Models/Presenter/Textures/` are derived from it and share the issue.
  - The provenance and licence of `V6_Engine_Prototype.glb` are unknown. The merged copies of its meshes in `Assets/Models/CarInternals/V6/` share the issue.
  - The XR Hands sample in `Assets/Samples` is under the Unity Package Distribution License, whose grant covers distribution as part of a built project, not publishing its source files.
  - The CarConcept model (CC BY 4.0) is credited in the notices, but the app labels it "Aurelle CarConcept" and has no credits screen. The display posters and the planar-UV copies of some of its meshes are adaptations of it and need the same attribution. The GLB carries Khronos and 3D Commerce logos and "KHRONOS" tyre lettering, trademarks that CC BY 4.0 does not license; the scene's materials replace the tyre lettering, the rear plate and the calipers' texture with generated AURELLE lettering, but the posters still show the earlier tyres.

## Repository layout

```text
Assets/
  Scenes/            SampleScene.unity (build scene) and its bake output, BasicScene.unity, SampleScene_Before_* snapshots
  Scripts/           Project scripts (see Docs/ARCHITECTURE.md); Rendering/: the hero studio's contact shadow and
                     planar reflection with its shader, and the Quest budget (AurelleQuestBudget.cs)
  Editor/            AurelleRealism/: the realism tools (Aurelle > Realism); CarInternals/: the car internals tool
                     (Aurelle > Car Internals) with one file per subsystem in Systems/; AurelleAutoCheck/: the Play
                     Mode check harness; AurelleBuild.cs: the Quest build (Aurelle > Build)
  Resources/         AurelleDepthTestedText.shader and AurelleXRayGhost.shader, loaded at runtime
  Models/            CarConcept.glb, V6_Engine_Prototype.glb, ShowroomCollection/, Presenter/Remy.fbx and
                     Presenter/Textures/ (derived from it), ExteriorPedestrians/ (unused), Exterior/ (generated meshes),
                     CarInternals/ (generated internals meshes, V6/ merged V6 meshes), Bicycle/ (generated bicycle meshes)
  Materials/         Realism/ (Cars, Interior, People, Rendering, Bicycle, CarInternals): materials, meshes and textures
                     written by the realism tools; CarInternals/: the internals' CI_* materials; Exterior/: the
                     builder's materials and textures; older showroom materials
  Prefabs/           ExteriorTraffic/ vehicle prefabs written by the builder
  Environment/       Poly Haven HDRI and surface textures used by the exterior
  ThirdParty/        CC0 Poly Haven and ambientCG textures and models for the interior, one folder per asset
                     with a SOURCE.txt
  Settings/          URP pipeline assets, renderer data and scene templates; Rendering/ holds the lighting
                     settings, the post-processing profile and the planar reflection's renderer
  XR/                XR Plug-in Management and OpenXR settings
  VRTemplateAssets/, Samples/, XRI/, TextMesh Pro/   VR template and package sample content
  Backups/           Old controller and scene backups, not used (as are the CarExplorerController_*.cs.txt files)
Packages/            Package manifest and lock file
ProjectSettings/     Player, quality, graphics, input, tag and layer, and build settings
Docs/                ARCHITECTURE.md
```

## Further documentation

- [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md): scripts, startup order, the Car Explorer, its HUD and their contracts, Remy, the exterior agents, the editor tools and their run order, rendering, naming contracts, measured scale and frame cost
- [CONTRIBUTING.md](CONTRIBUTING.md): Unity version, Git workflow and LFS, regenerating the district, changing the look, changing or adding internals, the explorer's contracts, verification
- [CHANGELOG.md](CHANGELOG.md): history of notable changes
- [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md): third-party models, textures, fonts, shader code and packages

## License

The project's own code and content are under the MIT License; see [LICENSE](LICENSE). Third-party models, textures, fonts, samples, shader code and packages keep their own licences, listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md); some of them do not permit redistribution under MIT terms.
