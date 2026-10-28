# Architecture

This document describes how the project is put together: the build scene, the scripts in `Assets/Scripts`, the startup sequence, the contracts between scripts and the object names they depend on, and the realism editor tools that produce the current look. It is written from the source and scene files; it does not claim Editor, build or device verification.

## Scene

`Assets/Scenes/SampleScene.unity` is the only scene in the build list (`ProjectSettings/EditorBuildSettings.asset`). Its root objects:

| Root | Active at load | Contents |
| --- | --- | --- |
| `Exterior Environment` | yes | Generated district: `Land and Arrival`, `Connected Road Network`, `Commercial Blocks`, `Street Trees and Landscape`, `Street Furniture and Signs`, `Traffic, Pedestrians and Cyclists`, `Exterior Spawn/Player Spawn`, `Exterior Daylight`. Components `ExteriorEnvironmentBuilder` and `ExteriorSpawnPlacement`. |
| `Main Camera` | yes | Desktop camera: `Camera`, `AudioListener`, URP camera data, `DesktopWalkthroughController`. |
| `Showroom Environment` | yes | `Showroom Floor`, `Showroom Architecture` (including the six `Recessed Ceiling Spotlight N` lights), `Circular Display` (podium with mesh colliders), `Showroom Presenter/Remy Presenter` (`ShowroomPresenterAgent`), and `Showroom Ceiling Fixtures` and `Showroom Interior Finishes`, added by the interior tool. The interior tool also builds the glazed front under `Showroom Architecture` (`Front Glass Wall Section 0`–`3`, `Entrance Transom Glass`, mullions, transoms, sills and the entrance frame) and `Showroom Interior Finishes/Glazing details` (silicone joints, manifestation films and the `Glazing Reflection Probe`). |
| `Showroom Interior Zones` | yes | Reception, customer lounge and rear-wall displays, furnished by the interior tool. |
| `Showroom Lighting` | yes | `Showroom Key Spot` and `Showroom Rim Spot` (Mixed), seven baked `Rear Wall Wash Spotlight N`, `Aurelle Downlights` (baked downlights with lens discs), `Showroom Fill Light` (light disabled), five baked reflection probes, `Light Probe Group` and `Exterior Light Probe Grid`, the global `Aurelle Post Volume`, the inactive `Aurelle Bake Occluders` and the old, inactive `Post Process Volume`. |
| `Showroom Premium Collection` | yes | One display per car; each has a `<model>.glb Loader` child holding a prefab instance of the GLB. |
| `CarConcept` | yes | Prefab instance of `Assets/Models/CarConcept.glb` with `CarExplorerController`, the added child `V6_Engine_Prototype(Clone)` (regrouped into tagged functional groups) and the generated `Internals` subtree (suspension, steering, brakes, transaxle and driveline, fuel, cooling, electrical and body structure), both from the [car internals tool](#car-internals-aurellecarinternals). The internals tool switches off the renderer of CarConcept's `Axles` bar, which the half shafts replace. |
| `XR Origin Hands (XR Rig)` | no | VR template rig (`Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Hands Variant.prefab`); activated in XR mode. |
| `Hands Permissions Manager` | no | VR template prefab; activated in XR mode. |
| `EventSystem`, `UI`, `Interactables`, `Environment`, `Teleport Area Setup` | no | Unused VR template content. |
| `Prototype Player (Disabled)`, `Prototype Obstacle 1`–`5 (Disabled)` | no | Early prototype (`PlayerMovement`). |

`Assets/Scenes/BasicScene.unity` comes from the VR template. The eight `Assets/Scenes/SampleScene_Before_*.unity` files and `Assets/Backups/CarExplorer/SampleScene_PreCarExplorer_20261009.unity` are historical snapshots of SampleScene; none is in the build.

## Scripts

| Script | Runs on | Responsibility |
| --- | --- | --- |
| `ExteriorSpawnPlacement.cs` | `Exterior Environment` | Chooses desktop or XR mode before any other `Awake`, places the desktop camera or the XR rig at `Player Spawn`, falls back from XR to desktop, aligns the XR head to the spawn, respawns XR players who fall below y = −25, requests foveated rendering. |
| `ShowroomCollisionSetup.cs` | static class | Runtime colliders shared by the desktop body, the XR rig and Remy ([Collision](#collision)); `TryGetFloorHeight` and `RespawnBelowHeight`. |
| `DesktopWalkthroughController.cs` | `Main Camera` | Desktop first-person controls, the automatic entrance doors and the inspect camera ([Desktop mode](#desktop-mode)). |
| `CarExplorerController.cs`, `CarExplorerStructure.cs`, `CarExplorerLayout.cs` | `CarConcept` | Explorer of the car, its V6 and the generated internals as a structure tree: normal, X-ray and exploded views and a gradual drill-down; implements `ICarExplorerModel` ([Car Explorer](#car-explorer)). |
| `CarExplorerModel.cs` | static class, types | `CarExplorerView`, `CarStructureNode`, `ICarExplorerModel` and the `CarExplorerModels` registry (`Current`, `CurrentChanged`, `PointerOverHud`): the contract between the explorer and the HUD. |
| `CarPartInfo.cs` | internals and V6 nodes | `CarPartInfo` metadata (kind, id, English and Uzbek names and descriptions, system, material, explode hints, `fineDetail`), `CarNodeKind`, and `CarSystems` (system names in English and Uzbek). |
| `CarExplorerHud*.cs` | runtime `Car Explorer HUD` | The explorer's CAD-style HUD on the desktop and on an XR plate ([Car Explorer HUD](#car-explorer-hud)). |
| `CarExplorerBootstrap.cs` | static class | `AfterSceneLoad`: adds `CarExplorerController` to `CarConcept` when it is missing and returns silently when the scene has no `CarConcept`. In SampleScene the controller is already serialized on the car. |
| `ShowroomPresenterAgent.cs` | `Remy Presenter` | Remy's greeting trip, his station beside the car the visitor looks at, procedural gait, gaze and pointing ([Presenter](#presenter-remy)). |
| `DepthTestedText.cs` | static class | Gives every legacy `TextMesh` a depth-tested font material ([Rendering](#rendering)). |
| `ExteriorEnvironmentBuilder.cs` | `Exterior Environment` | Editor-only district generator ([Exterior district](#exterior-district)), plus the static `ApplyCrowdLooks` that dresses pedestrians and riders ([People](#people-aurellerealismpeople)). All of its logic is inside `#if UNITY_EDITOR`; in a player `BuildEnvironment` only logs a warning. |
| `ExteriorTrafficSignal.cs` | 5 junction roots | Timed signal phases, lamp materials, vehicle and pedestrian queries. |
| `ExteriorTrafficCarAgent.cs` | 6 moving cars | Loop driving with runtime cornering, signal stops, following gaps and yielding. |
| `ExteriorPedestrianAgent.cs` | 8 pedestrians | Sidewalk loops, curb wait at a signal, procedural gait; tints untextured tops and bottoms at runtime. |
| `ExteriorCyclistAgent.cs` | 2 cyclists | Cycle-track loop, signal stops, yielding, rider pose and pedalling. |
| `EnginePartsExplorer.cs` | nothing | Earlier V6 explorer prototype, superseded by the Car Explorer, where the V6 is one of the assemblies. Not referenced by any scene or prefab, but compiled into the player. |
| `PlayerMovement.cs` | `Prototype Player (Disabled)` (inactive) | Early Rigidbody prototype; never runs in SampleScene. |

`Assets/Resources/AurelleDepthTestedText.shader` belongs to `DepthTestedText`, and `Assets/Resources/AurelleXRayGhost.shader` (`Aurelle/X-Ray Ghost`) to `CarExplorerController`. The Editor-only tools in `Assets/Editor/AurelleRealism` are described under [Realism editor tools](#realism-editor-tools).

## Startup sequence

1. Before the scene loads, XR Plug-in Management initializes and starts OpenXR: "Initialize XR on Startup" is on for Standalone and Android (`Assets/XR/XRGeneralSettingsPerBuildTarget.asset`). If no OpenXR runtime can start, no loader is active. Some runtimes start the loader before, or without, a headset display; the 6 s fallback below covers that case.
2. `ExteriorSpawnPlacement.Awake` runs before every other `Awake` (`[DefaultExecutionOrder(-10000)]`):
   - `ShowroomCollisionSetup.Apply()` adds the runtime colliders (both modes).
   - `Player Spawn` is taken from the serialized field, or found by name among the component's descendants.
   - XR mode when `XRSettings.isDeviceActive` is true or an XR loader is active; desktop mode otherwise.
3. The remaining `Awake` calls run in no defined order. `CarExplorerController` builds its part registry, structure tree, colliders, interactables and input actions, hides the inner content (V6 and internals) and starts cooking the car's and the internals' meshes for exact picking on worker threads; its `OnEnable` registers it in `CarExplorerModels`, which makes the HUD create `Car Explorer HUD` (the HUD hooked `CarExplorerModels.CurrentChanged` at `BeforeSceneLoad`). `ShowroomPresenterAgent` sets up Remy's `CharacterController`, finds the visitor camera and caches the rest pose.
4. `RuntimeInitializeOnLoadMethod(AfterSceneLoad)`: `CarExplorerBootstrap` (a no-op in SampleScene) and `DepthTestedText`, which swaps the font material of every `TextMesh` in the loaded scenes and hooks `SceneManager.sceneLoaded` for scenes loaded later.
5. `Start`: `DesktopWalkthroughController` (desktop only), `ShowroomPresenterAgent` (showroom interior bounds; the hero car, its podium and the premium displays for his station) and the exterior agents (runtime state, see [Runtime agents](#runtime-agents)).
6. Every frame: `ExteriorSpawnPlacement.Update` waits for the headset display, falls back to desktop, retries the foveation request, aligns the head and handles the XR respawn. `CarExplorerController` and `ShowroomPresenterAgent` re-resolve the active camera, so they follow a switch between the desktop camera and the rig camera. By its third `Update` at the latest, `CarExplorerController` creates its picking scene from the cooked meshes ([Car Explorer](#car-explorer)).

### Desktop mode

- `ExteriorSpawnPlacement` moves the desktop camera to `Player Spawn` + 1.7 m with the spawn's yaw and makes it clear to the skybox.
- `DesktopWalkthroughController.Start` runs only when no XR display is active. It calls `ShowroomCollisionSetup.Apply()` again (idempotent), creates `Desktop Player Body`, a yaw-only object with a `CharacterController` (height 1.8 m, radius 0.32 m, step 0.32 m, slope 48°), and parents the camera to it at eye height 1.7 m, so only the camera pitches. It then locks the cursor, snaps the body to the floor below, stores the respawn pose and builds `Showroom Entrance Hinged Doors` at (1, 0, 18).
- Walking speed is 4.2 m/s with gravity; below y = −25 the body returns to the stored pose. A crosshair is drawn while the cursor is locked (not under the HUD's help overlay); the key hint is the [HUD](#car-explorer-hud)'s, and the walkthrough's own IMGUI help line shows only while no HUD draws it.
- The entrance doors exist only in desktop mode; in XR the opening has no doors. They hang in the opening of the glazed front (x −0.64 to 2.64) and open by themselves when the visitor's body (not the camera, so inspecting does not count) or Remy comes within `autoDoorDistance` (2.5 m) of the opening between the hinges, on either side. Closed leaves swing away from whoever opens them (into the showroom for someone outside, onto the plaza for someone inside) with the 1.6 s smoothstep swing; leaves already ajar reopen the way they are turned. They close `autoDoorCloseDelay` (3 s) after the zone is clear, so they stay open while Remy is in it. E toggles them by hand (measured from the body's eye): a manual close holds until somebody next comes into the zone, and a manual open from outside the zone closes after 3 s if nobody comes through. Each leaf has one `BoxCollider` on its `Left Door Hinge` / `Right Door Hinge` covering the glass, stiles, rails and handle at every swing angle; the glass cubes have no collider, and the closed leaves meet with a 6 cm gap.
- **Inspect camera.** F (the selection, else the deepest open level, else the taken-out assembly), `CarExplorerInspect.Request` (the HUD's Inspect) or a double click on a part (0.4 s, 6 px, not over the HUD; the explorer picks only on the first press, so the double click inspects what that press selected) takes the camera off the body and flies it there in `inspectFlightSeconds` (0.6 s, smoothstep):
  - Framing: the node's planned box (`TryGetNodeBounds`) fills `inspectFill` (37.5 %) of the view height and stays within 85 % of the height and 75 % of the width of `CarExplorerModels.HudSafeViewport`, centred in that rect with a roll-free aim. The camera starts from the visitor's side at 12–55° elevation, at least 0.35 m above the feet, and takes the first of 12 nearby angles whose line from the node's centre is clear of walls, furniture, Remy and other parts (the node's own boxes and the visitor's capsule do not count); otherwise it shortens the boom.
  - Controls: left drag orbits and right drag pans (both from 4 px of travel, so a click stays a click; the explorer then picks on release), the wheel zooms between 125 % fill and 3× the framing distance, and the near plane comes in to 0.02 m at the closest. The cursor is free and the crosshair hidden.
  - A new selection, or new planned bounds of the inspected node (checked on `Changed` and when a move ends), re-frames it from the current angle; zoom and pan are kept otherwise. A node the current state does not draw has no bounds (`TryGetNodeBounds` is false for the V6 or the internals in the closed car), so it cannot be inspected, and when the inspected node loses its bounds (a Reset, or R putting the V6 back and leaving X-ray) the camera flies back to walking.
  - F, Esc, a walking key or `RequestExit` flies back (0.6 s) to the body's eye and restores the cursor state from before; walking keys walk at once, and Tab also locks the cursor. The body never moves while inspecting, and `IsDesktopActive` stays true.
  - The walkthrough publishes `CarExplorerInspect.SetState` once per change, in `LateUpdate`. XR ignores requests; a headset start, `OnDisable` or `OnDestroy` puts the camera back at once.

### XR mode

- The rig is a scene root, active or not, whose name contains `XR Origin` or `XR Rig`; a root with an `XROrigin` component wins.
- Every camera outside the rig that is tagged `MainCamera` or carries `DesktopWalkthroughController` is deactivated, leaving one MainCamera and one AudioListener. The walkthrough therefore never starts.
- The rig's `JumpProvider` components are disabled, because the right A button advances the explorer.
- The rig root is placed on the floor below `Player Spawn` with the spawn's yaw and activated; its camera clears to the skybox. The inactive `Hands Permissions Manager` root is activated; it requests `android.permission.HAND_TRACKING` on Android XR.
- Once the head has been reported as tracked for 0.25 s (or 3 s after the display started, if the runtime never reports tracking), `XROrigin.MatchOriginUpCameraForward` and `XROrigin.MoveCameraToWorldLocation` move the rig so the head stands at the spawn and faces the spawn heading; the rig root keeps its floor height. That pose becomes the respawn pose for y < −25, which also resets the rig's gravity providers.
- No rig found: the desktop camera is parented to `Fallback Head Tracking Space` at the spawn and gets a `TrackedPoseDriver` on the HMD centre eye. The walkthrough is disabled, and there is no locomotion or controller interaction; a warning is logged.
- Foveation: `TryEnableFoveation` sets `foveatedRenderingFlags = GazeAllowed` and `foveatedRenderingLevel = xrFoveationLevel` (serialized, default 1) on every running `XRDisplaySubsystem`, and retries each frame until a display runs. See [Rendering](#rendering) for why it has no effect at the moment.

### XR to desktop fallback

When XR mode was chosen only because a loader was active (`XRSettings.isDeviceActive` still false), `ExteriorSpawnPlacement` waits 6 s of unscaled time, counted from the first frame, for the display. If none starts, `RevertToDesktop` deactivates the rig and the `Hands Permissions Manager` it activated, reactivates the hidden desktop cameras (their walkthrough then starts as in desktop mode), undoes the no-rig fallback and places the camera at the spawn. A display that starts after the fallback only logs a warning; the scene must be restarted to use XR.

## Collision

`ShowroomCollisionSetup.Apply()` runs from `ExteriorSpawnPlacement.Awake` in both modes and again from the walkthrough's `Start`. Each step skips objects that already have a collider.

- **Walls** (`Showroom Architecture`): a non-convex `MeshCollider` on every enabled mesh that is at least 1.15 m tall and at least 1.4 m long in x or z (15 in SampleScene, the four front glass panes and the door transom glass included).
- **Display cars** (`Showroom Premium Collection`): one `BoxCollider` per display (each direct child) around all of its enabled meshes, measured in the display's own space. A display that already has a collider anywhere below it is skipped.
- **Furniture** (`Showroom Interior Zones`): a `BoxCollider` sized to the mesh on floor-standing pieces: top more than 0.25 m above the floor and bottom less than 0.6 m above it, or a slab at most 0.3 m thick that starts less than 1.3 m above the floor. The floor height comes from the `Showroom Floor` renderer.
- **Safety ground** (`Desktop Walkable Exterior Ground`): a 100 × 0.22 × 100 m box with its top at y = −0.10 under the lawns around the showroom. The generated ground's mesh collider stays the walking surface. The box is created in both modes despite its name.

**Static batching.** Never mark objects under `Showroom Architecture` or `Showroom Interior Zones` `BatchingStatic`. Static batching replaces `MeshFilter.sharedMesh` with the combined batch mesh at runtime, and `Apply` builds the wall mesh colliders and sizes the furniture boxes from `sharedMesh`. The realism tools set only `ContributeGI` and `ReflectionProbeStatic` on showroom objects.

The scene itself provides mesh colliders on `Showroom Floor` and the podium (`Raised Circular Display Plinth`, `Inset Display Surface`); the interior tool replaced their render meshes but not their colliders, so the floor's collider is still the built-in plane. The builder gives the city ground, sidewalks and building masses colliders. The explorer adds a box collider and a kinematic rigidbody to every car part, so the car blocks walking; the boxes move with the parts, and the boxes of the V6 and the internals are off while they are hidden. Remy's planning treats all of these colliders as obstacles, and the explorer's selection rays all but the car's own. The exact mesh colliders that the explorer uses for picking live in a separate physics scene, `Car Explorer Picking N`, so nothing in the main scene collides with them.

## Cross-script contracts

| Contract | Provided by | Used by | Rules |
| --- | --- | --- | --- |
| `DepthTestedText.Apply(TextMesh)` | `DepthTestedText` | the `AfterSceneLoad` and `sceneLoaded` sweeps | Play Mode only. Replaces only the font's own material or a material using `GUI/Text Shader`, so custom label materials are kept. One material per font, kept in sync with the dynamic font atlas. Call it for every `TextMesh` created at runtime. |
| `SetExplanationTarget(Transform)` and `SetExplanationTarget(Transform, Vector3 worldFocusPoint)` | `ShowroomPresenterAgent` | `CarExplorerController` on selection and deselection | Remy looks and points at the focus point, which is stored in the target's local space so it follows the moving part. The one-argument overload estimates the geometry centre; `null` clears. |
| `HasArrived` | `ShowroomPresenterAgent` | `CarExplorerController` greeting | True once Remy has come to the visitor and stood close by in view; a timed-out approach counts only after he has caught up. |
| `ICarExplorerModel`, `CarExplorerModels.Register` / `Unregister` / `Current` / `CurrentChanged` | `CarExplorerController` | `CarExplorerHud`, `ShowroomPresenterAgent` (`GreetingShown`), the harness | The explorer registers in `OnEnable` and unregisters in `OnDisable` / `OnDestroy`, and raises `Changed` on any change the HUD shows. The HUD only reads nodes and calls the model's commands; it never touches the controller's internals. Remy stays with the visitor while `GreetingShown` is true (null-safe without a model). |
| `CarExplorerModels.PointerOverHud` (static) | `CarExplorerHud`, every frame | `CarExplorerController` | True while the free mouse or an XR ray is over a HUD control; the explorer then ignores clicks and trigger presses on the car. Reset when the HUD goes away. |
| `CarPartInfo` on scene nodes | car internals tool (and any future generated content) | `CarExplorerController` | Defines the tree under tagged nodes ([Car Explorer](#car-explorer)). Generated meshes must be readable for exact picking; `fineDetail` parts are not drawn at Low. |
| `CarExplorerHud.DrawsDesktopHint`, `CarExplorerHud.HelpShown` (static) | `CarExplorerHud` | `DesktopWalkthroughController.OnGUI` | While the HUD draws the hint, the walkthrough draws no help line; no crosshair under the help overlay. |
| `SetViewKeepOut(Transform panel, float halfWidth)` | `ShowroomPresenterAgent` | nothing at the moment (the explorer has no world-space panels; the HUD's XR plate, `CarExplorerHud.Instance.WorldCanvas`, about 0.53 m half-width, could register) | Spots that would hide the panel cost more but are not forbidden. `halfWidth <= 0` or a destroyed panel removes the entry; inactive panels are ignored. Call again after rebuilding a panel. |
| `IsDesktopActive` (static) | `DesktopWalkthroughController` | `CarExplorerController` | True while the walkthrough has started in desktop mode and no XR display is active; it re-arms after a disable and enable. The explorer's own mouse orbit, pan and zoom run only while it is false. |
| `ICarExplorerModel.TryGetNodeBounds(node, out bounds)` | `CarExplorerController` | `DesktopWalkthroughController` (inspect framing; inspecting ends when it turns false), `CarExplorerHud` (Inspect enabled), `ShowroomPresenterAgent` (the studied parts) | World bounds of the node's parts where they will be when the current move ends (where they are when nothing moves). False for a node without parts and for inner content (the V6, the internals) that the current state does not draw. |
| `CarExplorerModels.HudSafeViewport` (static) | `CarExplorerHud` | `CarExplorerController` (placement), `DesktopWalkthroughController` (inspect framing) | Viewport rectangle (origin bottom-left) of the full-screen desktop camera that the HUD panels (and Remy's greeting toast while it shows) leave free; the whole view (default) in XR, with no model and without a HUD; with the panels hidden away from the car, the whole view less the toast while it shows. Read when a command runs or a node is framed; under 5 % of the view counts as the whole view. |
| `CarExplorerInspect` (`Request`, `RequestExit`, `Requested`, `ExitRequested`, `SetState`, `Active`, `Target`, `ActiveChanged`) | `DesktopWalkthroughController` (answers the requests, publishes the state) | `CarExplorerHud` (Inspect / Exit inspect, hint, card), `CarExplorerController` (click on release, Esc keeps the selection, no re-pick on a double click's second press, placements redone for the walking eye after inspecting), `ShowroomPresenterAgent` (the inspected node is the studied parts; only the inspect camera's view of it counts, once the camera is steady) | `SetState` is called once per change from `LateUpdate` (at once on disable or an XR start), so every `Update` of a frame sees the same `Active`. Requests are ignored in XR. |
| `Apply()`, `TryGetFloorHeight(origin, maxDistance, out floorY)`, `RespawnBelowHeight` | `ShowroomCollisionSetup` | `ExteriorSpawnPlacement`, `DesktopWalkthroughController` | `Apply` is idempotent. `TryGetFloorHeight` returns the highest non-trigger surface below a point, ignoring character controllers. |
| `Configure(Transform point, float eyeHeight)` | `ExteriorSpawnPlacement` | builder | Spawn point and desktop eye height. |
| `ApplyCrowdLooks(Transform root)` (static, Editor-only) | `ExteriorEnvironmentBuilder` | the builder's traffic build; `AurelleRealismPeople` | Dresses pedestrians and riders with the materials in `Assets/Materials/Realism/People` and returns the number of renderers changed. A missing material keeps the model's own. The people tool calls it directly, so change the two together. |
| `Configure(...)` and `SetStartIndex(int)` on `ExteriorTrafficCarAgent` and `ExteriorCyclistAgent`; `Configure(...)` on `ExteriorPedestrianAgent` and `ExteriorTrafficSignal` | agents | builder, at edit time | The configuration fields are serialized. The start index, wheel caches, rider bones and lamp state are not, so each agent rebuilds them in `Start` or `OnEnable`; without a runtime `SetStartIndex` call, the start index is the end of the nearest route segment ahead. |
| Traffic routes | builder | `ExteriorTrafficCarAgent` | Closed loops of waypoint transforms forming straight polylines between corner anchors. Cars round the corners at runtime, so do not bake curves into the waypoints: a curve through only the corners swings out of the streets. |
| `Active` registries, `ExteriorTrafficSignal.NextOnApproach`, `AllowsVehicle`, `AllowsPedestrian`, `PedestrianMayStart`, `StopLineDistance`, `ExteriorTrafficCarAgent.NearestTargetIndex` | exterior agents | each other | Internal static lists and queries used for yielding, signal stops and start indices. |

## Car Explorer

`CarExplorerController` on `CarConcept` (a partial class in `CarExplorerController.cs`, `CarExplorerStructure.cs` and `CarExplorerLayout.cs`) shows the car as a structure tree (vehicle, systems, assemblies, sub-assemblies, parts) in three views (normal, X-ray and exploded) and lets the visitor take the car apart gradually: an assembly comes out of the car in one piece, then opens one level at a time down to a single part. It implements `ICarExplorerModel` and registers itself in `CarExplorerModels`; every on-screen panel belongs to the [Car Explorer HUD](#car-explorer-hud), and the explorer itself creates no UI. At rest in the normal view, every transform is back in its authored pose and every material as it was.

**Registry.** In `Awake`, every enabled renderer with a mesh under `CarConcept` becomes a part; renderers on the same transform form one part. In SampleScene that is 1,302 parts: 95 of the car's own (CarConcept's `Engine` mesh is switched off in the scene, and the internals tool switches off the `Axles` bar), the V6's 182 and the 1,025 generated internals. Each part gets:

- a `BoxCollider` fitted to its mesh bounds in its own space, unless the node already has a collider;
- a kinematic `Rigidbody`;
- an `XRGrabInteractable` (dynamic attach, no throw, hierarchy kept while held) on the scene's `XRInteractionManager` or a new `Car Explorer XR Interaction Manager`.

A released part glides back in 0.35 s to where it was grabbed; grabs are refused while parts move, and the X-ray glass cannot be grabbed.

**Structure tree.** `CarExplorerStructure.cs` builds the tree once, in `Awake`. In SampleScene: 13 systems, 54 assemblies (15 of CarConcept's own, the V6 and 38 internals assemblies), 1,653 nodes, at most 6 levels deep.

- Nodes that carry `CarPartInfo` define the tree under them. `kind = Assembly` attaches to the system named in `system` (empty: Structure under `Internals`, Powertrain under the V6); a `SubAssembly` or `Part` attaches to its nearest tagged ancestor. Untagged transforms between tagged nodes become generated sub-assemblies (one-child ones collapse); a transform with its own mesh and children gets a "(main part)" leaf; a `Part` with a mesh hierarchy is one leaf. `explodeDirection` is read in the node's parent's local space and `explodeDistance` scales the spread; a zero direction lets the explorer choose. `id` is used when set, otherwise a slug path is generated.
- CarConcept's own glTF nodes are grouped by name rules. The top-level node (the direct child of `CarConcept`) picks the assembly; group rules (matched by sub-part name terms, for example `Rim`, `Disc`, `Caliper`, `Window`, `Mirror`, `Handle`, `InteriorDoor<n>`) nest the parts below it, with English and Uzbek names and descriptions:

  | System | Assemblies (top-level nodes) |
  | --- | --- |
  | Body | Front Section (`BodyHood*`), Roof Panel (`BodyRoof*`), Windscreen (`BodyWindshield*`), Rear Section (`BodyRear*`, `License*`) |
  | Structure | Body Frame (other `Body*`, `InteriorCage*`, `InteriorPillar*`), Underbody and Axles (`CarConcept` itself, `Axles*`, `Engine*`) |
  | Closures | Left Door, Right Door (`BodyDoor*`) |
  | Interior | Seats (`InteriorSeats*`), Cockpit (`InteriorDash*`, `InteriorSteering*`, `InteriorPedal*`), Cabin Floor (`InteriorFloor*`, `InteriorMid*`) |
  | Wheels | one per wheel (`Wheel*`): road wheel (tyre, rim) and disc brake |

- The V6 is tagged by the car internals tool (Assembly `powertrain.engine` with 19 functional sub-assemblies, [Car internals](#car-internals-aurellecarinternals)). An untagged V6 falls back to name rules (short block, cylinder heads, valve train with camshafts, caps, rocker arms, valves and springs, intake, exhaust, accessory drive), and a tagged V6 root keeps the engine's placement rules.
- Everything under `CarConcept/Internals` or the V6 is inner content (below).

**Drill-down.** The model's commands, used by clicks and by the HUD:

- A click on a part (or `Open` on an assembly from the tree) takes an assembly that is in the car out in one piece. The next click on it makes it come apart one level, and every further click opens the next level along the clicked part's chain, down to the single part. A click on a part of another assembly takes that one out instead; the first goes back on the same move. `Open` on a deeper node opens every level above it in one move; `Open` on a system or the vehicle only selects.
- `Select` highlights a node and shows its card without moving anything; `Highlight` (the HUD's hover) is a weaker tint.
- `CloseTo(k)` keeps the first k levels of `OpenPath` apart and closes the deeper ones (`CloseTo(0)` leaves the assembly out in one piece); `PutBack` puts the taken-out assembly back (and clears a selection inside it); `Back` closes the deepest level, then puts the assembly back, then steps the view back (exploded, X-ray, normal); `ResetAll` gives the complete car in the normal view with nothing selected.
- Assemblies inside the body (the V6, the seats, the cockpit, the body frame, the cabin floor, the internals) switch the normal view to X-ray when they come out, and the view stays X-ray when they go back. The cabin floor and the underbody carry the car: they stay in place and only come apart.

**Placement.** A taken-out assembly goes out of the car in its own direction (a door or wheel to its side, the V6 forward) at a comfortable height, or up above the car (the front and rear sections like a raised hood, the cabin assemblies, the roof, the windscreen). Each kind has three candidates in order (front and rear: raised over their end, out of the car on that end, then that end and aside from the viewer; the V6: forward, forward and aside from the viewer, lifted out of the bay and forward), and the first valid one wins:

- A valid place stays under 3.3 m, at least 1.3 m horizontally from the viewer's eye (or over their head), out of the keep-outs and the colliders around the car (the other display cars, walls, furniture and people, Remy's `CharacterController` included; the floor, the podium, the car itself and the rig are skipped), and clear of every other drawn part: boxes first, and where boxes overlap, the exact pick meshes at their planned poses decide.
- At least 85 % of its screen box lies in `CarExplorerModels.HudSafeViewport` on the desktop (the central 70 % of the view in XR), for the view camera as it is when the command runs (the inspect camera while inspecting).
- Above-car places keep their centre at least 0.25 m below the eye; they come down (up to three 0.1 m steps) and slide out along their own direction (0.1 m steps, up to 1.5 m) until clear and in view.
- If none of its own candidates is valid, 16 bearings round the car at 1.1 m and 0.8 m height are tried, and the valid one nearest the middle of the free view wins (with a penalty beyond 3 m from the eye); with none valid, the best score wins.
- While the assembly is in one piece or one level apart, every change re-checks it, and it moves when it is no longer clear or in view; from two open levels down it stays. When opening a level makes it move, only places at most 1 m (`RefitFurther`) further from the eye than where it stands are tried, so an opened level never sends it away behind the car; without a valid one, a nearer place is taken only when at least 5 % more of the box is in the free view there, and otherwise it comes apart where it is.
- Places are computed for the view camera, which is the inspect camera while inspecting. Once inspecting ends and the camera has flown back (0.7 s), placements made while inspecting are redone for the walking eye: the assembly moves only if its place is no longer valid for that eye, and its open levels come toward that eye again.

The cockpit turns its dashboard, and the seats their fronts, toward the viewer. When a node comes apart, its anchor (the marked child, or the largest one without a direction of its own) stays and the other children move along their direction (`CarPartInfo`, a rule's, the wheel's own axle, or away from the anchor), scaled by the node's size, until no two children's boxes are within 3 cm; coaxial stacks (tyre and rim, disc and caliper) spread along their axle. An open level comes toward the viewer's eye: about 1.3 m away horizontally (further for a big level), between floor + 0.8 m and eye − 0.25 m, in view, clear of every other part, the room and the keep-outs, and 0.6 m from the eye; otherwise 0.25 m nearer, ±0.25 m in height, 0.35 m or 0.7 m further, then turned 12–50° round the eye toward the middle of the view. Its travel is limited to 2.5 m only in the exploded view; a taken-out assembly's level comes to the visitor from any distance. While anything is apart, the parts outside the deepest open level are darkened by 40 % with property blocks, which are removed at rest.

**Exploded view.** `ExplodeAll` (O) takes every assembly out of the car as one unit over `explodeDuration` (1.5 s), the roof and body first and the engine and floor last. CarConcept's own assemblies keep three tuned tiers, as in a workshop manual: the floor tub with the wheels out along their axles and the V6 forward; the seats and cockpit lifted out of the tub; the body frame above them with the roof and windscreen on top, the doors beside it and the front and rear sections over its ends. Tagged assemblies (the internals) go outward from the car in their own direction, just clear of its footprint, on a height tier by system (running gear on the floor, powertrain a little higher, cabin in the middle, body on top), in the first free place (further out, turned aside or a tier up): 12 turns up to 180° and 6 tiers 0.6 m apart, with each assembly's footprint at least 0.2 m inside the podium's edge (`Circular Display`, radius 3.0 m in SampleScene), where Remy does not walk. With all 38 internals assemblies the podium has no free place left for the ones that span the car (sills, seat frames, roll structure, brake and fuel lines, parking brake, power distribution, harness); they take the valid place on the podium that overlaps the others least (straight up over the car's middle, or centred on the podium, in 0.3 m steps included); only without any valid place on the podium does the one that reaches least past its edge win. In SampleScene all 38 stay on the podium, but the boxes of about 20 pairs of assemblies overlap (most of them with the long ones over the car). CarConcept's own tuned tiers are not bound to the podium. The layout is fitted to the room each time the view opens: under 3.95 m, off the floor, out of the display cars and furniture and off the viewer. A click in the exploded view opens the clicked assembly in place.

**Moves.** Every node's offset is its assembly's move plus each level's spread; a part under another moving part keeps its offset relative to that one. The assembly travels first and spreads on the way (0.8 s in SampleScene, `transitionDuration`; the script default is 0.9 s); going back it closes first. Every command starts a new move from wherever the parts are, also during a move or after a grab, so no input is ignored. Moves use smoothstep easing, and a move that ends with nothing out in the normal or X-ray view gives every animated transform its authored local position, rotation and scale back exactly.

**Inner content.** The V6 (182 draws: the internals tool merged each renderer's submeshes into one) and everything under `CarConcept/Internals` are drawn only where they can be seen: in the X-ray and exploded views, while their own assembly is out, while the front section over the bay is out, and during a move that starts or ends in such a state. They never cast shadows, and their selection boxes are off while they are hidden. `CarPartInfo.fineDetail` parts (fasteners, clips, thin wires) are switched off in `Awake` at quality levels 0 and 1 (Very Low and Low, the Quest's level) and left out of the tree (819 parts instead of 1,302 at Low, 1,170 nodes instead of 1,653; the V6 keeps 170 of its 182, without the 12 valve springs). `OnDisable` and `OnDestroy` put back the renderer states and shadow modes captured at start.

**Scale.** Measured with the full internals in Play Mode in the Editor (batch mode, so main-thread times exclude rendering the Game view):

- Draws (renderer submeshes, before batching) and triangles of the car, every view: normal 107 draws, 213k triangles (inner content hidden), at Ultra and at Low. X-ray, exploded or an inner assembly out: 1,328 draws and 1.12 M triangles at Ultra (the car 107 / 213k, the V6 182 / 627k, the internals 1,039 / 283k), 845 draws and 819k triangles at Low (the V6 170 / 389k, the internals 568 / 216k). With the front section out, the content in the bay: 611 internals draws (176k triangles) at Ultra, 299 (131k) at Low.
- Meshes: the internals' 522 meshes take 16.1 MB, the V6's 37 merged copies 9.3 MB and the car's own 97 meshes 16.7 MB (all readable, for exact picking).
- The 1,302 exact pick meshes (819 at Low) were cooked and the picking scene was ready on the first Play Mode frame.
- Main thread per frame (medians of two runs): 2.8–3.1 ms idle in the normal view or X-ray, 3.0–3.5 ms walking round the car in X-ray, 1.9–3.1 ms orbiting the inspect camera, 2.4 ms idle in X-ray at Low. GC allocations per frame (Editor included): 136 B idle, 204 B walking and 368 B orbiting, the lab's own probes included (the Editor alone allocates about 90 B a frame).
- HUD tree with every branch open: 1,653 rows on a pool of 15 row objects; opening every branch (350 toggles) took 50 ms, and scrolling three rows a frame cost 4.7 ms median (6.2 ms at the 95th percentile), allocating while names are measured for the first time (up to 48 KB in a frame) and about 200 B a frame otherwise.

**X-ray glass.** The body shell is every part under a top-level `Body*` or `License*` node, door, hood and hatch trims included. When `CarConcept/Internals` exists, the floor tub (the underbody on `CarConcept` itself and the `InteriorFloor*` pan, mats included) is shell too, so the suspension inside the wheel wells shows, and so are the cabin trims: the dash (`InteriorDashMid`, `InteriorDashSides`), the instrument binnacle and column shroud (`InteriorSteeringDash`, `InteriorSteeringDashColumn`), the centre console (`InteriorMid`), the cage and the inner pillars (`InteriorCage`, `InteriorPillar`), so the bulkhead systems, the cross-car beam, the seat frames and the steering column show: 62 shell renderers in SampleScene. The seats, the steering wheel (`InteriorSteeringBase`, `-Cylinder` with its wheel, `-Handle*`), the pedals, the wheels, the axles, the V6 and the internals stay solid; the assembly that is out is shown solid. For another car model, parts named like a window, glass, windshield, door, hood, bonnet, bumper, fender, roof, mirror, trunk or panel also count, unless their top-level node is `Interior*`, `Axle*` or `Engine*` or they belong to a tagged assembly. Layers inside the skin (names containing `Interior`, `Under`, `Gasket` or `Base`, so also every cabin trim) and the underbody are fainter glass (30 %).

Entering X-ray fades each shell renderer over `ghostFadeDuration` (0.6 s):

- At the start of the fade the explorer reads the renderer's current `sharedMaterials` and puts a runtime material of `Aurelle/X-Ray Ghost` into every slot. Per-slot property blocks carry the fade and a solid stand-in for the slot's own material: base colour and texture, metallic, smoothness and the glTF clear coat, read from the glTFast, URP or legacy property names. The stand-in is lit like URP Lit, so the paint does not darken when the fade starts.
- While fading, the material `Car Explorer X-Ray (fading)` writes depth and culls back faces. At full fade the renderer switches to `Car Explorer X-Ray (glass)`, which is two-sided and writes no depth, so the far side of the body shows faintly through the near side.
- The shell keeps casting its shadow until the fade passes half way. Fading back ends by removing the property blocks and putting the captured material array back as it was.

If the shader is missing or unsupported, one warning is logged and the shell is hidden past half way instead. A spot light, `Car Explorer X-Ray Light`, fades in and out with the X-ray view (1.1 m above the car over the front of the cabin, 105° cone, no shadows, `xrayLightIntensity` 2.4).

**Picking.** In `Awake` the explorer schedules `Physics.BakeMesh` jobs for every part's readable mesh (the car's, the V6's merged copies and the internals'; a mesh shared by several parts once). When they finish, by its third `Update` at the latest, it creates a physics-only scene, `Car Explorer Picking N`, with one `MeshCollider` per such part, moved to the parts' current poses before each selection ray. Until then, and for any part without a readable mesh, the parts' boxes are used; in SampleScene all 1,302 parts (819 at Low) are picked by their meshes, and in the measurements the picking scene was ready on the first Play Mode frame (the cooking jobs finish within it). A ray (up to `rayDistance`, 20 m) takes the nearest visible part; in the X-ray view the glass lets it through to a solid part behind and is chosen only when nothing solid follows. Any other non-trigger collider in front blocks the selection; a world-space UI in front (UI layer) takes the click. A ray that finds no part only clears the selection; nothing moves.

The second press of a double click (within 0.4 s and 6 px of the first, the walkthrough's window) does not pick again, so a double click on the bonnet inspects the Front Section it raised rather than picking the V6 that the raised section uncovered. While the inspect camera is active (`CarExplorerInspect.Active`), a left press only arms the click: it picks on release, and only when the pointer moved less than 4 px (the walkthrough orbits from 4 px). Mouse clicks are ignored while `CarExplorerModels.PointerOverHud` is true or a free cursor is over EventSystem UI, and a controller's trigger while `PointerOverHud` is true or that hand's `XRRayInteractor` is over UI, so pressing the HUD never also clicks the car.

**Selection.** The selected node's parts are tinted per material slot through a `MaterialPropertyBlock` (`baseColorFactor` for glTFast materials, else `_BaseColor`, else `_Color`); the HUD's hover is a weaker tint. Dark colours take more of the accent (up to 0.62 for the selection and 0.3 for the hover), and the selection lowers metallic to at most 0.2 (`metallicFactor` or `_Metallic`, the hover halfway), so it shows on dark metal. Clearing them removes the property blocks, which restores SRP Batcher compatibility. `ShowroomPresenterAgent.SetExplanationTarget` gets the selected node's first part and its visible-bounds centre on every selection change, and `null` when the selection clears.

**Input.** Actions are created in code: both triggers, the right `primaryButton` and `secondaryButton`, the left `menuButton`, and an aim pose and a device pose per hand. Keyboard and mouse are read directly. The HUD's buttons call the same model methods.

| Action | Desktop | XR | Method |
| --- | --- | --- | --- |
| Select a part; take its assembly out, or open the next level that holds it | Left click: at the screen centre while the cursor is locked, else at the pointer | Trigger on either controller, along its aim pose (else its device pose, else the view centre) | `Open` / `Select` |
| X-ray on / off; from the exploded view, the normal car | T | — | `ToggleXRay()` |
| Exploded view from any view | O, `]`, Page Up | — | `ExplodeAll()` |
| Next view: normal → X-ray → exploded | — | A (right `primaryButton`) | `Explode()` |
| One step back | R, `[`, Page Down | B (right `secondaryButton`) | `Back()` (`Restore()`) |
| Complete car, nothing selected | — | Left `menuButton` | `ResetAll()` (`ResetExplorer()`) |
| English / Uzbek | Q / U | — | `SetLanguage` |
| Clear the selection and the greeting | Esc (while inspecting it only takes the camera back to walking; the selection stays) | — | `Select(null)`, `DismissGreeting()` |

The explorer's own mouse orbit, pan and zoom (right drag, middle drag, wheel) run only while `DesktopWalkthroughController.IsDesktopActive` is false and the view is not XR, so they are inactive in SampleScene.

**Public methods.** `Explode`, `ExplodeAll`, `ToggleXRay`, `Restore` (= `Back`), `ResetExplorer` (= `ResetAll`) and `ReturnFocus` (= `PutBack` and clear the selection) do nothing until `Awake` has finished. The private `currentLevel` (0 normal, 1 X-ray, 2 exploded) is kept for tools that read the explorer's state by reflection.

**Greeting and audio.** `GreetingShown` turns true once per session when Remy has arrived (`HasArrived` and within `greetingDistance` + 1.7 m) or stands within `greetingDistance` (2.8 m), and is inside the camera's horizontal view. It waits while something is selected and turns false after `greetingDisplayDuration` (7 s) or on `DismissGreeting`. The HUD shows it as Remy's toast; Remy keeps the visitor company while it is shown. `greetingClip` and `partVoiceCues` are optional; none is assigned in SampleScene.

**Lifecycle.** `OnEnable` registers the explorer in `CarExplorerModels` (and hides the inner content again); `OnDisable` unregisters it, clears the selection and Remy's target, lets go of held parts, snaps the car back to the normal view at rest in its own materials and puts back the inner content's renderer states. `OnDestroy` restores the car the same way, unloads the picking scene and destroys the selection boxes, the interaction manager it added (only when no interactor uses it), its materials and the X-ray light. `Changed` is raised on any change of the view, the open path, the selection, the language, the greeting, the camera and `IsMoving`.

**Other car models.** Untagged nodes follow CarConcept's names and proportions ([Naming contracts](#naming-contracts)); a different car needs `CarPartInfo` on its nodes or new rules. Without either, its top-level nodes that match no rule become generic assemblies and its glass shell is guessed from part names.

## Car Explorer HUD

`CarExplorerHud.cs` (with `CarExplorerHudTree`, `CarExplorerHudButton`, `CarExplorerHudRow`, `CarExplorerHudHover`, `CarExplorerHudScaler`, `CarExplorerHudStyle` and `CarExplorerHudText`) is the explorer's only UI, a CAD-style workspace: the STRUCTURE tree on the left, the selected node's card on the right, the breadcrumb, the view switch (Normal / X-ray / Explode), EN / UZ and `?` on top, and a key hint at the bottom. It is created at runtime and never saved in the scene.

- **Bootstrap.** `BeforeSceneLoad` hooks `CarExplorerModels.CurrentChanged`; when a model registers, the HUD creates `Car Explorer HUD` and binds to it, and it hides when `Current` becomes null. `Changed` is coalesced to once per frame and diffed against what is shown, so nothing is rebuilt (or allocated) when nothing differs. The HUD calls `GameObject.Find("CarConcept")` at bind for the car's rest centre and radius.
- **Desktop.** `Car Explorer HUD (Screen)` is a Screen Space - Overlay canvas with `CarExplorerHudScaler` (1920 × 1080 reference, match 0.5, scale floor min(0.75, width / 1200)). The tree, the card and the top bar show within 8 m of the car (hidden beyond 9.5 m), or while something is selected or out, or in X-ray or Exploded; the hint always shows (away from the car only the walking keys; the help keeps E as the manual door toggle). H toggles the help overlay. Panels are opaque and fade in 0.15 s.
- **XR.** The same panels sit on `Car Explorer HUD (XR)`, a world-space plate 1120 × 760 units at 0.95 mm per unit (1.06 m wide) with a `TrackedDeviceGraphicRaycaster`, placed 1.15 m from the head and 0.3 m below the eye, yawed to the left just clear of the car (34–62°) and facing the visitor. It glides to a new spot only when the visitor turns more than 80° from it or moves away, and shows once placed. Remy's toast stays outside the plate.
- **Tree.** Virtualised (pooled rows); it opens the path to the selection by itself, and amber dots mark nodes that are apart. It is 22.5 % of the canvas width (384–448 units; `SetWidth` refits the names) and indents 14 units per level down to level 2, 10 below. A name that does not fit is cut by `CarExplorerHudTree.FitName` so it still tells it from its siblings: it keeps the start when the names differ near the start, else it keeps the end from the word where they differ behind a middle ellipsis ("Rocker Arm Asse… 12"); siblings with different names never get the same label. Names are measured with a hidden probe text and cached per node, language and width. A single click selects (`Select`), a double click (0.4 s) or the row's explode icon opens (`Open`), hovering highlights (`Highlight`); chevrons only expand rows. Systems without parts are not listed.
- **Card.** Two action rows. The primary button reads Take out for an assembly still in the car, else Explode, and calls `Open`; a single part's card has none; beside it Inspect (desktop only, when something is selected; enabled when `TryGetNodeBounds` succeeds) calls `CarExplorerInspect.Request`, and on the inspected node's card it reads Exit inspect (`RequestExit`). The second row: Collapse (`CloseTo`) on an open level, Put back (`PutBack`) while something is out, and Reset (`ResetAll`) on the overview card when something is out or the view is not normal. Status chips: OUT OF THE CAR, APART. While inspecting the card shows `CarExplorerInspect.Target` and the panels stay up. The material reads in Uzbek through `CarExplorerHudText.MaterialUzbek`: a phrase table for the scene's materials, else a term-by-term translation (lists, "and", "in", "with", plurals); grades and codes such as 6082-T6 or 10.9 stay as they are.
- **Breadcrumb.** The path to the selection (else the deepest open level, else the taken-out assembly, else the vehicle); a crumb closes the deeper levels (`CloseTo`) and selects that node, the vehicle or a system crumb puts the assembly back first, and folded middle levels show as `…`. Middle crumbs are capped at 280 units; the last one gets the room up to the view switch and is fitted like a tree name.
- **EventSystem.** The HUD adds `Car Explorer HUD Event System` only when no EventSystem is active: `InputSystemUIInputModule` on the desktop (navigation events off), `XRUIInputModule` in XR after waiting 1.5 s for XRI's own. After the XR-to-desktop fallback it turns on mouse input of an `XRUIInputModule` left on the active EventSystem.
- **Hint and free view.** The hint set follows the state: `HintNearSelected` adds F (inspect), `HintInspect` replaces it while inspecting (drag orbit, right-drag pan, wheel zoom, F / Esc walk, H help), and `HintFar` has no E. The HUD publishes `CarExplorerModels.HudSafeViewport` on every layout pass: the band between the tree and the card, under the top bar and above the hint, 16 units from each panel (a collapsed tree frees the left side; at least 20 % × 30 % of the view), about x 0.246–0.754, y 0.074–0.919 at 1080p. While Remy's greeting toast shows, the band starts below it, also while the panels are hidden away from the car.
- **Pointer.** The HUD writes `CarExplorerModels.PointerOverHud` every frame (the free mouse or an XR ray over any HUD control); the explorer ignores clicks and trigger presses on the car while it is true.
- **Texts.** All strings are in `CarExplorerHudText` (English and Uzbek); node names and descriptions come from the model. `DesktopWalkthroughController.OnGUI` draws its own hint line only while `CarExplorerHud.DrawsDesktopHint` is false, and no crosshair under the help overlay (`HelpShown`).

## Presenter (Remy)

`Remy Presenter` is the Mixamo `Remy.fbx` humanoid scaled to 0.4145 with no Animator Controller; the pose is written procedurally in `LateUpdate`. `Awake` adds and sizes a `CharacterController` (1.72 m by 0.27 m in world units).

- **Idle.** Remy waits at his post near the reception desk; torso and head follow the visitor.
- **Greeting trip.** It starts once the visitor's camera is inside the showroom interior, entered from any side. The interior is the footprint of the floor-standing parts of `Showroom Architecture`, narrowed to the inner faces of the side glazing and clipped to `Showroom Floor`.
- **Approach.** Remy first uses the aisle waypoints that bring him closer, then plans a free, reachable spot 1.6–1.9 m from the visitor at about 35° on his own side of the visitor's view line. Physics probes check the route; after 1.5 s without progress he tries another spot, and after 15 s he stops approaching and starts following from where he stands. `HasArrived` turns true only once he has also stood close to the visitor and in view.
- **Greeting.** After arriving he stays with the visitor (the follow rules below) until he has met them, 8 s have passed and the explorer no longer shows the welcome (`CarExplorerModels.Current.GreetingShown`, when an explorer is registered); sooner once he has met a visitor who has started exploring (a node selected or out of the car, or a view other than normal); at the latest 20 s after arriving, and at once when the visitor leaves the showroom. Then he goes to his station and does not come back for a welcome shown later.
- **Station.** Remy stands beside the car the visitor attends, never between them and it, and moves only when he has to:
  - The car's footprint, seen from above, covers every shown renderer under `CarConcept` wherever the explorer has moved it (taken out, spread, exploded) and is read every 0.5 s, together with where the studied parts are heading (below). Stations lie on two rings 0.6 m apart just outside that footprint and outside the podium (`Circular Display`), each grown by Remy's body and `stationClearance` (0.45 m), every 10° around the car's rest centre.
  - Spots cost least at a bearing of `stationBearingMin`–`stationBearingMax` (60–110°) from the visitor's line to the car's centre, on Remy's current side, a short walk away, in view of a visitor looking at the car and not hidden behind it; hiding a registered panel costs extra, and so do the stations ahead of a visitor who is walking round the car (Remy gives way to the other side rather than walk on ahead of them); a spot in the visitor's sight lines to the parts (the region between the visitor and the footprint, seen from above) costs most.
  - The parts the visitor studies are the node the desktop inspect camera frames (`CarExplorerInspect.Target`), else the whole car in the exploded view (the model's `Root`), else the assembly that is out (`TakenOut`). Their bounds come from `ICarExplorerModel.TryGetNodeBounds`, where the current move will leave them, re-read when the node, the view or the open levels change and every 0.5 s, and join the footprint at once, so Remy plans clear of where they are going. A spot whose bearing from the eye, seen from above, lies within their angular span widened by 6° (at least his silhouette plus 2°) and that is farther from the eye than their nearest point would show him among or behind them, unless it is out of the view of a visitor looking at the parts (an exploded car can span more than the view): planned spots cost extra within 4° more, and a station that falls inside the 6° band makes him re-plan after 0.35 s (1.5 s while the eye is moving, so he does not chase a walking visitor). The eye counts as moving until it has stayed within 0.2 m, its view turned by at most 4°, for 0.5 s. While parts are studied, "in view" is judged toward them rather than toward the car's centre. When no clear station exists, the cheapest one is kept until the parts change or the visitor moves 1 m.
  - While the desktop inspect camera frames a node (`CarExplorerInspect.Active`), that camera is the eye and the node the studied parts, and only the camera's view of the node counts: Remy re-plans when his station lies within the node's span as the camera sees it (behind the node or in front of it, with the same margins), once the camera has been steady for 0.5 s and he has stood there for 0.35 s. He never re-plans while the camera flies, orbits, zooms or pans, so he does not shuffle round while the visitor turns the node over; the band, personal-space and sight-line reasons wait until the camera is back on the body. Parts that come over his station still move him at once.
  - He re-plans when parts settle over his station (at once when they come up to his body), when he has stood in the sight lines or behind the studied parts for 0.35 s, when the station's bearing has stayed outside 35–135° (or he stands far out after the parts went back) for `stationRetargetDwell` (2 s), when the visitor walks up to within 0.9 m, and when the visitor attends another car. Apart from the parts and the visitor walking up, no reason re-plans within 1 s of the last plan, and a spot that still qualifies is kept.
  - The visitor attends a premium display (a child of `Showroom Premium Collection`) within 5.5 m of them and 50° of their view direction, unless the hero car is nearer and in view, after `displayVisitDwell` (3 s); Remy then takes a station at that display's side. Otherwise, after 2 s, and at once when the visitor leaves the showroom, it is the hero car; while the visitor is outside he waits at its station.
  - Routes to a station are straight, else round the car just outside the station ring with a corner every 25° (of the two ways round, the one through fewer of the visitor's sight lines, then the shorter; to a premium display also round the hero car), else via one detour point. A straight walk (or a short cut) through the visitor's sight lines goes round the far side instead when that way is clear of them. After the greeting no route crosses the podium.
  - Without `CarConcept` he keeps following the visitor after the greeting.
- **Follow.** While greeting (and after it in a scene without the hero car) he holds a spot 2.2–2.8 m away (the greeting distance for the first 8 s), preferably about 35° (at least 12°) off the view centre and inside the view. He re-plans when the visitor moves more than 1 m or walks up to him, and when he has been out of view for 0.6 s after a turn of more than 30° (2 s otherwise). When the visitor leaves the showroom, Remy waits just inside near the exit point.
- **Planning.** The visitor's eye is the explorer's `ViewCamera` (the desktop camera, the XR head or the desktop inspect camera while it orbits a node), read every frame; without an explorer, `Camera.main`. Candidate spots are scored by angle, distance and how much of a registered panel (`SetViewKeepOut`) they would hide. Spots and routes are checked with physics probes against the colliders around them; there is no NavMesh.
- **Gesture.** `SetExplanationTarget` makes Remy look at the focus point and point with the arm on its side; head and torso pitch follow the target's height. At his station he turns to the visitor between explanations.

## Exterior district

### Generation workflow

The district is generated in the Editor and saved into SampleScene; at runtime nothing is generated.

- Run **Aurelle > Build Exterior District** (or the component's context menu **Build Aurelle Exterior District**) outside Play Mode. The menu uses the active scene's `Exterior Environment` root (active or not), else the first object in the scene that carries the builder, else it creates the root.
- The builder destroys every child of the root (with Undo) and rebuilds: ground and hills, 18 buildings, streets with curbs, sidewalks, crossings and markings, the arrival plaza and dealership sign, trees, lamps, street furniture and signs, five signalised junctions, two traffic loops with six moving cars, four parked cars, two cyclists, eight pedestrians, `Player Spawn` (and `ExteriorSpawnPlacement.Configure` on the root), and the daylight setup (Kloppenheim HDRI skybox, ambient and reflections from the sky, fog, the `Exterior Daylight` sun).
- It writes or updates assets: materials in `Assets/Materials/Exterior` (including one `Sidewalk_Concrete_<length>m` or `Sidewalk_Concrete_Vertical_<length>m` material per sidewalk length), procedural textures in `Assets/Materials/Exterior/Textures` (only when missing), meshes in `Assets/Models/Exterior`, the traffic prefabs `Assets/Prefabs/ExteriorTraffic/TrafficVehicle_E30|M4|Tesla.prefab` (each GLB scaled to 4.5 m length; the Tesla is turned 180° so it drives nose first) and `Kloppenheim_Sunset_Skybox.mat`.
- It reads `Assets/Models/ShowroomCollection/*.glb`, `Assets/Models/Presenter/Remy.fbx` (pedestrians and riders), the crowd materials in `Assets/Materials/Realism/People` (a missing material keeps the model's own), `Assets/Environment/HDRI/Kloppenheim_06_PureSky_2K.hdr` and the Poly Haven maps in `Assets/Environment/Textures/PolyHaven` (smoothness maps from its `Imported` subfolder).
- The traffic build ends with `ApplyCrowdLooks`, so every pedestrian and rider keeps its crowd look after a rebuild.
- It then saves the assets and the scene that contains the builder.

Regenerate only with Unity 6000.3.12f1. Every rebuild replaces several thousand objects with new file IDs, so it produces a very large SampleScene diff, discards manual edits under `Exterior Environment` and conflicts with any parallel scene change. Commit a regeneration together with the builder change that caused it.

A rebuild also undoes part of the realism pass:

- It rebuilds the traffic prefabs from the raw GLBs, which drops the car materials and the traffic paint variety.
- It recreates the sun as a real-time light and sets the sky ambient and reflection intensities back to 0.62 and 0.55.
- It recreates the street lamps at intensity 30.

Run `CarsRealism` and then `AurelleRenderSetup.BatchBake` afterwards ([Run order](#run-order)).

### Runtime agents

- **Signals.** `Phase` is a pure function of `Time.time` plus the per-junction offset (0, 5, 10, 15, 20 s): east-west green 18 s, amber 3 s, north-south green 18 s, amber 3 s, all red 1 s (43 s cycle). Stop lines are 9 m from the junction centre. Lamps are set in `OnEnable`, so the first frame shows the right state.
- **Cars.** The two loops (`Perimeter Traffic Loop`, four cars; `Counterflow Traffic Loop`, two cars) are straight polylines between four corner anchors with eight waypoints per leg. Cars round the corners at runtime by pure pursuit (3 m look-ahead, speed-limited turn rate) and shift right where an opposite-direction loop runs closer than 2.8 m. They stop at red or amber stop lines they can still stop for, keep a braking-distance gap to cars ahead, yield to crossing cars (a deadlock breaker ignores them briefly after 4 s), cyclists and pedestrians, and spin wheel nodes whose names contain `tire` (else `wheel`).
- **Cyclists** ride the `Avenue Cycle Track Route`, stop at signals and yield to pedestrians and cars. In `Start` the rider (a `Remy.fbx` instance) is seated on `Bicycle/Saddle`, leans only as far as it needs to reach the grips read from the `Bicycle/Handlebar` mesh, and holds them through a two-bone arm solve. While riding, the cranks turn, the feet stay on the pedals and the wheels roll.
- **Pedestrians** are `Remy.fbx` instances at 0.42 scale moved by `CharacterController` without gravity. Arms are lowered from the T-pose and swung procedurally. `Pedestrian 3` walks the signalised crossing in front of the dealership; it waits for the walk phase only at the curb and always finishes a crossing it has started. In `Start`, `ApplyAppearance` tints tops and bottoms through a property block only when their material has no base map, so the textured crowd looks keep their colours and stay SRP Batcher compatible.
- Each agent type keeps an internal static `Active` list. Cars check cyclists and pedestrians, cyclists check pedestrians and cars, pedestrians check cars.

## Realism editor tools

`Assets/Editor/AurelleRealism` holds four Editor-only tools that produce the current look of the showroom, the cars and the people, and `Assets/Editor/CarInternals` the car internals tool. They change SampleScene and project assets in place; none of their code is in a player.

- Each tool finds its objects by name.
- Each batch method opens SampleScene, applies the tool, saves the scene and assets, and exits.
- All of them are idempotent: a second run changes nothing (the internals tool recreates its objects with new scene file IDs but byte-identical assets), and a new bake differs only in lightmap noise.

| Tool | Class (file) | Menu under **Aurelle > Realism** | Batch methods |
| --- | --- | --- | --- |
| People | `AurelleRealismPeople` (`PeopleRealism.cs`) | **Upgrade People**; **Upgrade People (Rebuild Textures)** | `Batch`; `BatchRebuildTextures` |
| Cars | `CarsRealism` (`CarsRealism.cs`) | **Cars > Apply Car Materials**; **Cars > Report Car Materials** (log only) | `Batch` |
| Rendering | `AurelleRenderSetup` (`RenderSetup.cs`) | **Rendering > Apply Render Setup (no bake)**; **Rendering > Apply Render Setup and Bake Lighting** | `Batch` (no bake); `BatchBake` |
| Interior | `AurelleRealism.InteriorRealismUpgrade` (`InteriorRealismUpgrade.cs` and the other `Interior*.cs`) | **Upgrade Showroom Interior** | `Batch` |
| Car internals | `AurelleCarInternals` (`Assets/Editor/CarInternals/CarInternalsBuilder.cs` and the files beside it) | **Aurelle > Car Internals > Build**; **Aurelle > Car Internals > Restore V6 glTF Meshes** (puts the V6 back on its glTF meshes until the next build) | `Batch` |

From the menu:

- **Upgrade People** works on the scenes that are open.
- **Upgrade People** and **Upgrade Showroom Interior** leave the scene unsaved.
- The Cars and Rendering items open SampleScene when needed and save it.

`AurelleRealism.InteriorPosterRenderer.Batch` runs in batch mode only and has no menu item. It re-renders the three display posters from the CarConcept in a temporary studio set-up and never saves the scene. Run it only when the hero car's look changes.

### Run order

1. `AurelleRealismPeople.Batch`
2. `CarsRealism.Batch`
3. `AurelleRenderSetup.Batch`
4. `AurelleRealism.InteriorRealismUpgrade.Batch`
5. `AurelleCarInternals.Batch`
6. `AurelleRenderSetup.BatchBake`

Why this order:

- **People and Cars** do not depend on the other tools. Cars must also run after every district regeneration, because the builder rebuilds the traffic prefabs from the raw GLBs, and after anything else that replaces car instances.
- **The render setup runs before the interior** because it moves the lights. The interior places its fixtures (downlight cans, the wall-wash track and its spot heads, the rim-spot pendant) from the current transforms of `Recessed Ceiling Spotlight N`, `Rear Wall Wash Spotlight N` and `Showroom Rim Spot`.
- **The car internals** only touch `CarConcept` (the `Internals` subtree, the V6's groups and the V6's meshes, and the `Axles` renderer they switch off), so their place before the bake is not critical; `CarsRealism` finds the V6's renderers by name at any depth.
- **Cars before the car internals.** The internals tool merges a V6 renderer's submeshes only when all its slots hold one material, so it should see the V6 materials `CarsRealism` assigns. A later `CarsRealism` run is safe too: it only rewrites the slots a renderer has, so each merged V6 renderer keeps its single slot, and the next internals build trims any array that another tool filled up again.
- **The render setup runs again last** because the static and GI flags, the light-probe positions and the bake depend on the final geometry. The interior tool's `Glazing Reflection Probe` copies the hall probe's capture point and box, so rerun the interior tool before the final bake when a render setup run has moved the hall probe.

### Generated assets

| Folder | Written by | Contents |
| --- | --- | --- |
| `Assets/Materials/Realism/People` | People | 7 `People_Remy_*` materials; `Crowd/`: 28 `Crowd_*` materials |
| `Assets/Models/Presenter/Textures` | People | 19 textures derived from `Remy.fbx` |
| `Assets/Materials/Realism/Cars` | Cars | 87 car materials; `Textures/E30`, `Textures/M4`, `Textures/Tesla`: 44 compressed copies of the GLBs' textures |
| `Assets/Materials/Realism/Rendering` | Rendering | `AurelleDownlightLens.mat`, `AurelleBakeOccluder.mat` |
| `Assets/Settings/Rendering` | Rendering | `AurelleLighting.lighting` (lighting settings), `AurellePostProfile.asset` (volume profile) |
| `Assets/Scenes/SampleScene/` | Rendering (bake) | `LightingData.asset`, `Lightmap-*_comp_light.exr`, `ReflectionProbe-0`–`6.exr` (6 is the glazing probe), stored in Git LFS |
| `Assets/Materials/Realism/Interior` | Interior | 42 materials (`Interior_*`, `PH_*`); `Meshes/`: 52 meshes; `Textures/`: `Interior_Poster_1.jpg` to `_3.jpg` and the generated `Interior_Glass_Manifestation.png` |
| `Assets/Models/CarInternals` | Car internals | 522 readable meshes in `Brakes/`, `Cooling/`, `Driveline/`, `Electrical/`, `Fuel/`, `Powertrain/`, `Steering/`, `Structure/` and `Suspension/`, overwritten in place (GUIDs kept, stale meshes deleted); `V6/`: 37 readable one-submesh copies of the V6's glTF meshes, written only when their content changes, and `V6_mesh_map.txt` (each copy's source mesh: glb, GUID, local file ID, name, size) |
| `Assets/Materials/CarInternals` | Car internals | 30 flat URP Lit `CI_*` materials |
| `Assets/ThirdParty/PolyHaven`, `Assets/ThirdParty/ambientCG` | Added by hand; the interior tool sets their import settings | CC0 textures and glTF models, one folder per asset with a `SOURCE.txt` |

Git LFS stores the bake output and the generated textures: PNG, JPG and EXR files under `Assets/Materials/Realism` and PNG and JPG files in `Assets/Models/Presenter/Textures`. The materials, the interior meshes and the settings assets stay in plain Git.

The tools also change other assets:

- Rendering: `Quality URP Config.asset`, `Standalone Preset.asset` and `ProjectSettings/QualitySettings.asset`.
- People: `Remy.fbx.meta`.
- Cars: the three `TrafficVehicle_*.prefab`.

### Rendering and lighting (`AurelleRenderSetup`)

The tunable values (light intensities, texel density, AO, SSAO and post-processing settings) are constants and literals in `RenderSetup.cs`.

Project settings:

- **`Quality URP Config` (PC).**
  - HDR (R11G11B10), HDR colour grading and MSAA 4×.
  - Main-light shadows: 4096 px, four cascades over 90 m, soft shadows at High.
  - Per-pixel additional lights with shadows (4096 px atlas).
  - Reflection-probe blending and box projection.
- **`Standalone Preset` (PC renderer).** Forward+ and an SSAO renderer feature (depth normals, bilateral blur). Forward+ removes the per-object light limit on PC but adds shader variants to PC builds.
- **`Android Preset` and `Performance URP Config` (Quest).** Only checked, not changed. The Android renderer has no post-processing data and no SSAO feature. In URP 17.3 a renderer without post-processing data never builds the post-processing passes, whatever a camera requests.
- **`ProjectSettings/QualitySettings.asset`.** The Editor's current level becomes Ultra (5). Per-platform defaults stay Ultra for Standalone and Low for Android.

Scene:

- **Environment.** Ambient light and reflections come from the sky at intensities 1.0 and 0.8. The builder's 0.62 and 0.55 left shaded facades dark without the fill light. Reflections bounce twice, and `Exterior Daylight` is the sun, now Mixed.
- **Lights** (under `Showroom Lighting` unless noted):
  - `Showroom Fill Light` is disabled; baked bounce light replaces it.
  - The six `Recessed Ceiling Spotlight N` (under `Showroom Architecture`) are baked, as are the downlights under `Aurelle Downlights`. These are rows towards the entrance and over the premium row, plus one over each display car, the reception desk and Remy's post. Grid positions under ceiling fixtures in `Showroom Interior Zones` are skipped.
  - Each added downlight has a lens disc with the HDR emissive `AurelleDownlightLens.mat`. While the `UseHdrLensMaterial` constant is on, the tool also puts that material into the last material slot of every renderer named `Recessed Spotlight Lens*`, turns off its shadows and keeps it out of the bake. After the interior pass these renderers are the three-part downlight cans (housing, inner wall, lens disc), so the lens material sits on the lens disc and the housing in slot 0 keeps its white powder coat.
  - The wall washers `Rear Wall Wash Spotlight N` are baked: one centred on each wall display plus fillers, hung at the ceiling 2.2 m in front of the feature wall.
  - `Showroom Key Spot` and `Showroom Rim Spot` light the hero car and are Mixed. The key casts a real-time soft shadow on PC, and its range keeps the premium cars out of its shadow map; the rim has no shadow.
  - The street lamps (`Warm Road LED`) stay real time, dimmed to intensity 12.
- **Static flags.** The tool sets them by rule on the renderers under `Showroom Environment` (except `Showroom Presenter`) and `Showroom Interior Zones`.
  - Renderers get `ContributeGI` and `ReflectionProbeStatic`. They are lightmapped when the mesh has usable lightmap UVs and is large enough, and lit by light probes otherwise.
  - These stay out of GI: text, lens discs, transparent surfaces (glass keeps `ReflectionProbeStatic`), LOD levels above 0, and anything animated, scripted (`Assembly-CSharp` components, interactables) or physics-driven.
  - `CarConcept`, Remy and the premium cars are fully dynamic.
- **`Aurelle Bake Occluders`.** Two boxes per premium car (body and cabin) are active only during the tool's bake, so the floor gets soft contact shadows while the cars stay dynamic.
- **Reflection probes.** All are baked, HDR and 256 px: `Showroom Reflection Probe` (box-projected to the hall), `Hero Car Reflection Probe` and one `Premium Car Reflection Probe - <display>` per display. The car probes are captured from each car's centre.
- **Light probes.** `Light Probe Group` covers the showroom: a grid at four heights, denser around the cars, with no probes inside solid geometry or cars. `Exterior Light Probe Grid` is a coarse grid over the district plus a ring around the pavilion, so exterior objects do not sample the showroom's interior probes.
- **`Aurelle Post Volume`.** Global, priority 10, with profile `AurellePostProfile.asset`: ACES tonemapping, colour adjustments, white balance, bloom and vignette.
- **`Main Camera` (desktop).** HDR, post-processing on, SMAA High and dithering. The XR rig's camera is not changed and keeps post-processing off.
- **`AurelleLighting.lighting`.** Assigned to SampleScene: baked GI only (no realtime GI), Mixed lights as Baked Indirect, the Progressive GPU lightmapper, non-directional lightmaps in atlases of at most 2048 px, baked ambient occlusion and three bounces.

Bake (`BatchBake`, or the menu item with bake):

- **Occluders.** The tool enables `Aurelle Bake Occluders`, runs `Lightmapping.Bake()` synchronously and disables them again. A bake from the Lighting window, or a bare `Lightmapping.Bake()`, leaves the occluders off, and the premium cars lose their contact shadows.
- **Output.** The bake writes `LightingData.asset`, the lightmaps (`Lightmap-<n>_comp_light.exr`) and the reflection cubemaps (`ReflectionProbe-<n>.exr`) to `Assets/Scenes/SampleScene/`, about 26 MB in the current bake. The tool then caps the Android import size of the lightmaps at 1024 px and of the reflection cubemaps at 128 px.
- **Emission.** In URP 17.3 a bake turns emission off on materials whose GI flag is None or EmissiveIsBlack, so emissive materials need the Realtime or Baked emissive flag. Baked-emissive materials on GI-contributing renderers, such as the interior's LED fixtures, add light to the bake.

### Cars (`CarsRealism`)

- **Overrides instead of remapping.** glTFast 6.20's importer has no material remapping, so the tool cannot change the GLBs' imported materials. Instead it assigns override materials:
  - on the scene's prefab instances: `CarConcept` and the GLB instances under `Showroom Premium Collection`;
  - on the unpacked V6 objects under `V6_Engine_Prototype(Clone)`;
  - inside the three `TrafficVehicle_*` prefabs, whose scene instances follow the prefabs.
- **Slot mapping.** A slot is identified by its GLB path and imported material name, found through `PrefabUtility.GetCorrespondingObjectFromOriginalSource`. V6 slots are identified by the part's name. File IDs are never used. A slot the tool does not manage keeps, or gets back, its imported material.
- **Materials.** The 87 materials in `Assets/Materials/Realism/Cars` stay on glTFast's shader graphs (`glTF-pbrMetallicRoughness` and its clear-coat variant). As a result:
  - the CarConcept's second-UV ambient occlusion and texture transforms keep working;
  - no shader is added to the build;
  - the Car Explorer highlight (`baseColorFactor` through a property block) still works.

  72 materials are tuned recipes in `Specs()`. The other 15 are exact copies of imported E30 and Tesla materials whose only change is their textures.
- **Compressed texture copies.** glTFast imports the premium GLBs' PNG textures as uncompressed RGBA32 on every platform. The tool copies the GLB textures its car materials use to `Assets/Materials/Realism/Cars/Textures/<E30|M4|Tesla>`:
  - at most 1024 px, colour maps as JPG, and colour maps with alpha, data maps and normal maps as PNG;
  - imported with high-quality compression on PC and ASTC 6×6 on Android (1024 px, or 512 px for small props);
  - existing copies are reused, so their GUIDs stay stable.

  The CarConcept keeps its KTX2 textures, which are already block-compressed.
- **Traffic variety.** The traffic instances, ordered by prefab and hierarchy, cycle through E30 paints (red, blue, black, silver) and Tesla paints (white, midnight silver, blue). The paints are stored as prefab-instance overrides in SampleScene; the M4 keeps its paint. The builder's own colour tint never had an effect: it set `_BaseColor`, which the glTFast shaders do not have, through property blocks that are not saved.
- **Scene diffs.** SampleScene stores one material slot per V6 renderer (182) since the internals tool merged the V6's submeshes; before that it stored 6,250.

### Interior (`AurelleRealism.InteriorRealismUpgrade`)

| File | Responsibility |
| --- | --- |
| `InteriorRealismUpgrade.cs` | Menu and batch entry, step order; `InteriorContext` (find, create and restyle objects, `MarkStatic`, `CullBelow`) |
| `InteriorAssets.cs` | Import settings for the CC0 textures and glTF models; the URP Lit material library |
| `InteriorPalette.cs` | Every interior material by role: textures, tiling, colour, smoothness, emission |
| `InteriorMeshes.cs` | Procedural meshes with UVs in metres and generated lightmap UVs, saved to `Meshes/` |
| `InteriorModels.cs` | Places Poly Haven glTF models as unpacked scene objects and assigns their materials |
| `InteriorArchitecture.cs` | Floor, walls, ceiling, glazing (through `InteriorGlazing`), and the podium (`InteriorPodium`) |
| `InteriorGlazing.cs` | Side glass, the glazed front with the entrance frame (the desktop doors hang in its opening), silicone joints, manifestation films and their generated texture, and the `Glazing Reflection Probe` (the hall probe's capture, a box out to the glass mid-planes, 0.03 m blend, importance 0) that makes the glass reflect the room from inside |
| `InteriorReception.cs`, `InteriorLounge.cs`, `InteriorDisplays.cs` | Reception, lounge, rear-wall displays |
| `InteriorCeilingFixtures.cs` | Ring luminaires, downlight cans, linear LED lines, wall-wash track, rim-spot pendant: emissive geometry only; no `Light` is changed |
| `InteriorPosterRenderer.cs` | Optional poster renderer (batch mode only) |

- **Materials.** All are URP Lit, SRP Batcher compatible, with GPU instancing. The clear glass uses the specular workflow (F0 about 0.09, alpha 0.3, preserve specular, receives shadows; `InteriorLitDef.Specular` and `PreserveSpecular`). Every run rewrites every property from `InteriorPalette.cs` and a few inline definitions in the step files, so edits made directly to the `.mat` files are lost.
- **Restyled objects.** They keep their names, transforms, colliders and static flags. The tool changes their meshes and materials, and turns off shadow casting on some thin trims, light fixtures and glass. New meshes are stored in the object's unscaled local space.
- **Multi-submesh renderers.** Several renderers have two or three submeshes: the floor, `Rear Feature Wall`, the LED rings, the downlight cans (`Recessed Spotlight Lens N`), the podium band and LED line, the display screens and the slat wall. A later tool that assigns a single material to such a renderer breaks it; setting `sharedMaterial`, for example, replaces only the first submesh's material. `Rear Feature Wall` keeps black powder coat in slot 0 because the desktop entrance doors copy that slot.
- **Removed and kept objects.** The tool removes the primitive-built lounge pieces (only objects with Unity's built-in primitive meshes), the 14 oak battens, the display pictograms and the accent bars. The new sofa seat and coffee table reuse the old names `Sofa • seat cushion` and `Coffee table • rounded stone top`.
- **Static flags and colliders.** New objects get `ContributeGI` and `ReflectionProbeStatic`, never `BatchingStatic` ([Collision](#collision)). The final render setup decides again, by its own rule, whether each renderer is lightmapped or lit by light probes. Plants and vases get cull-only LOD groups, so they disappear sooner at Low (LOD bias 0.4, against 2 at Ultra). New furniture stays under `Showroom Interior Zones`, so `ShowroomCollisionSetup` gives it box colliders.
- **Emission.** The LED materials are baked-emissive (values in `InteriorPalette.cs`) and light the bake. The poster screens are emissive but not baked-emissive.
- **Texture sizes.** They are set per platform in `InteriorAssets.cs`: 2K on PC for the floor, the concrete wall, the black marble and the posters; at most 1K on Android. The Poly Haven `.gltf` files have their texture references removed, and materials are assigned in Unity.

### People (`AurelleRealismPeople`)

- **Textures.** `Remy.fbx` embeds Mixamo maps (diffuse, normal, specular, gloss, opacity) that were never extracted, so the model's own materials imported untextured. The tool extracts them to a temporary folder, which it deletes afterwards, and derives 19 textures in `Assets/Models/Presenter/Textures`:
  - albedo maps saved as JPG, and normal maps copied unchanged;
  - `SpecGloss` maps: specular colour (F0) in RGB and smoothness in alpha, remapped per part from Mixamo's legacy specular and gloss values;
  - the hair albedo with its opacity in alpha, and a 256 px eyelash crop of the body atlas;
  - neutral grey top, bottom and hair textures that the crowd materials tint.

  Each run derives only missing textures. **Upgrade People (Rebuild Textures)** re-derives all of them in place, keeping the files and GUIDs.
- **Materials.** All use URP Lit in the Specular workflow, with keywords set by URP's own Lit inspector code.
  - Seven `People_Remy_*` materials: skin, eyes, eyelashes, hair, top, bottom and shoes.
  - 28 `Crowd_*` materials in `Crowd/`: 10 tops, 5 bottoms, 3 shoes, 6 hair colours and 4 skin tones.
  - Hair and eyelashes are alpha-clipped and double-sided; the eyes have no emission.
- **Model remap.** `Remy.fbx.meta` maps the model's six materials (`Bodymat`, `Topmat`, `Bottommat`, `Shoesmat`, `Hairmat`, `Eyelashmat`) to the Remy materials, so new instances are textured.
  - Only `externalObjects` changes; the generated humanoid mapping stays out of the meta.
  - The `Eyes` mesh shares `Bodymat`, so scenes assign `People_Remy_Eyes` to it explicitly.
- **Scene.** Every `Remy Presenter` gets the Remy materials by renderer name, and its eyes and eyelashes stop casting shadows. The tool then calls `ExteriorEnvironmentBuilder.ApplyCrowdLooks` for each builder in the open scenes. It calls the builder directly, so the two must change together.
- **`ApplyCrowdLooks(Transform root)`.** A public, static, Editor-only method of the builder.
  - It takes the `ExteriorPedestrianAgent` objects in hierarchy order and the `Cyclist Rider` child of each `ExteriorCyclistAgent`, and assigns saved materials by `SkinnedMeshRenderer` name.
  - `Tops`, `Bottoms`, `Shoes`, `Hair` and `Body` (skin tone) get `Crowd_<Top|Bottoms|Shoes|Hair|Skin>_<name>` from the `PedestrianLooks` and `CyclistLooks` tables.
  - `Eyes` and `Eyelashes` get the shared Remy materials and cast no shadows.
  - A missing material keeps the model's own. The method returns the number of renderers it changed.

### Car internals (`AurelleCarInternals`)

The tool builds the car's detailed internals procedurally, in a clean CAD style, and tags them (and the V6) with `CarPartInfo` for the [Car Explorer](#car-explorer). Run it from **Aurelle > Car Internals > Build** or with `-executeMethod AurelleCarInternals.Batch`; the report goes to the log and to `$AAC_OUT/internals_report.txt`.

| File | Responsibility |
| --- | --- |
| `CarInternalsBuilder.cs` | Menu and batch entry; the `[CarInternalsSubsystem(system, order)]` attribute and `ICarInternalsSubsystem` (subsystems are found through `TypeCache`, so a new `Systems/*.cs` file needs no shared edit); `InternalsContext` (system, assembly, sub-assembly and part nodes with `CarPartInfo`, the mesh cache saved in place, declared contacts, the report) |
| `CarLayout.cs` | The measured car: wheels (steered frame, tyre, rim, disc and caliper extents), floor, wheel-well walls, arch roof, firewall, the V6's bounds, the steering wheel's axis, reserved volumes for future subsystems |
| `InternalsProbe.cs` | Temporary physics copies of the car's meshes on a free layer, for rays and the interference check |
| `CarMeshKit.cs` | `MeshKit` (lathe, tube sweeps, helix springs, polygon sweeps, box tubes, extrusions, rounded boxes, drilled discs, bellows), profiles and fasteners |
| `CarPalette.cs` | The 23 base `CI_*` materials (the subsystems add 7 more of their own: 30 in all) |
| `V6Regroup.cs` | Regroups `V6_Engine_Prototype(Clone)` into 19 functional groups (crank train, block, heads, valve trains with camshafts, rockers and valves, intake with superchargers and filters, exhaust manifolds, timing, lubrication) and tags the root (`powertrain.engine`), the groups, 5 CAD group nodes and the 182 parts. The 12 valve springs (the CAD's `Rocker arm backing spring`, 19,822 triangles each, 38 % of the V6's triangles) are `fineDetail`, so Low leaves them out. It dissolves earlier groups first and keeps every name, material and world pose. |
| `V6MeshMerge.cs` | Called by `V6Regroup`: gives each of the V6's 182 renderers a merged copy of its glTF mesh (identical vertex streams, one triangle submesh: the source's index lists with base vertices applied, in submesh order) and its one material in a single slot, so the V6 draws 182 submeshes instead of 6,250. Renderers that share a glTF mesh share its copy (37 copies in `Assets/Models/CarInternals/V6`, named after the glTF meshes, readable for exact picking). Copies are rebuilt from the glTF meshes on every run and written only when they differ, in place (GUIDs kept); `V6_mesh_map.txt` records each copy's source, and **Restore V6 glTF Meshes** uses it to go back. A renderer whose slots hold more than one material is left alone and reported. |
| `Systems/FrontChassisGeometry.cs`, `Systems/FrontSuspension.cs`, `Systems/Steering.cs` | Front hardpoints from the layout and probe rays; two double-wishbone corners (arms, knuckle, hub, coilover), the anti-roll bar, subframe brackets and tie bar; rack and pinion with EPS, tie rods and a column with two universal joints |
| `Systems/RearChassisGeometry.cs`, `Systems/RearSuspension.cs` | Rear hardpoints and interfaces (caliper lugs, drive-shaft joints, hose brackets); two multi-link corners (upper arm, spring link, toe link, trailing arm, carrier and hub, coilover), the rear anti-roll bar and the rear subframe |
| `Systems/BrakeHydraulics.cs` | Pendant pedal box with a cross-shaft, electromechanical booster and tandem master cylinder, ABS and ESC modulator, brake lines (two diagonal circuits, braided hoses) and the electric parking-brake actuators |
| `Systems/Drivetrain.cs` | Clutch and flywheel, the transaxle (case, gear train, final drive and differential, shift actuator), engine and transmission mounts, front half shafts, the electric rear drive unit, rear half shafts and the hybrid battery module; switches off the renderer of CarConcept's `Axles` bar |
| `Systems/ExhaustFuel.cs` | Fuel tank, fuel lines and rail (the exhaust is behind the `BuildExhaust` switch, off) |
| `Systems/CoolingElectrical.cs` | Radiator module, coolant circuit, accessory belt drive and A/C circuit; battery, power distribution, charging and starting, engine control unit and the main harness (the HVAC module is behind `BuildHvac`, off) |
| `Systems/BodyStructure.cs` | Sills and floor rails, the cross-car beam, seat frames, the roll structure and the rear crash structure (the front crash structure is behind `BuildFrontCrash`, off) |

- **Output.** It deletes and recreates `CarConcept/Internals` and saves the scene: 1,025 parts (471 of them fine detail) in 38 assemblies of 9 systems, 283,268 triangles (66,942 of them fine detail) and 522 meshes. By system (parts / triangles): Suspension 363 / 85,800, Steering 50 / 16,344, Brakes 140 / 30,836, Powertrain (beside the V6) 83 / 26,322, Driveline 84 / 23,442, Fuel 72 / 20,916, Cooling 57 / 25,316, Electrical 81 / 28,916, Structure 95 / 25,376. `Internals` sits at the car frame (the ground under the wheelbase centre, +z toward `BodyHood`), which cancels CarConcept's −90° X rotation; `explodeDirection` is in car space there, and in the V6 root's own glTF space under the V6.
- **Checks.** Every part's edges are raycast against the car's body, cabin, wheels, V6 and floor meshes (back faces included) and the other way round, and between assemblies; only declared contacts may touch ("interference check of 1025 parts: clear"). Node names avoid the explorer's shell words (`Panel`, `Door`, `Hood`, …) and never start with `Body`; the tool warns if one does. A second run gives the same report, writes no asset and changes the scene only in its file IDs.
- **Left out.** Four pieces clashed with the combined layout and are behind `static readonly` switches, each named in the report: the exhaust (`ExhaustFuel.BuildExhaust`), the front crash structure (`BodyStructure.BuildFrontCrash`), the HVAC module (`CoolingElectrical.BuildHvac`) and the timing-end engine mount (`Drivetrain.BuildTimingEndMount`).
- **Mesh keys** are shared by all subsystems (`ctx.Mesh(key, …)` builds a key once per run), so a key must always build the same geometry; a part whose key no other part builds needs its own builder.

## Rendering

- **Pipeline assets** (URP 17.3.0, `Assets/Settings/Project Configuration`):

  | Quality level | Pipeline asset | Default for |
  | --- | --- | --- |
  | Very Low, Low | `Performance URP Config` | Android (Low) |
  | Medium, High, Very High | none assigned, so the Graphics default `Performance URP Config` | — |
  | Ultra | `Quality URP Config` | Standalone; also the Editor's saved level (`m_CurrentQuality: 5`) |

  - **`Performance URP Config`** (renderer `Android Preset`: Forward, no post-processing data, no renderer features):
    - HDR off and MSAA 4×.
    - Per-pixel additional lights (4 per object, no additional-light shadows).
    - Main-light shadows: 2048 px, 2 cascades, 35 m, soft shadows.
    - No reflection-probe blending or box projection.
  - **`Quality URP Config`** (renderer `Standalone Preset`: Forward+ with SSAO):
    - HDR with HDR colour grading, and MSAA 4×.
    - Main-light shadows: 4096 px, 4 cascades, 90 m, soft shadows at High.
    - Additional-light shadows.
    - Reflection-probe blending and box projection.

  The render tool maintains the PC assets ([Rendering and lighting](#rendering-and-lighting-aurellerendersetup)). `Android Preset` and `Standalone Preset` are URP Universal Renderer Data assets, not build presets.
- **Post-processing.** On PC, the global `Aurelle Post Volume` (priority 10) applies to the desktop `Main Camera`, the only camera with post-processing on. The XR rig's camera has it off, and the Android renderer cannot run it. The old `Post Process Volume` under `Showroom Lighting` stays inactive.
- **Depth-tested text.** Legacy `TextMesh` uses the built-in `GUI/Text Shader`, which draws with `ZTest Always`, so labels showed through walls and the car. `DepthTestedText` replaces the font's own material (or any material using `GUI/Text Shader`) with a copy using `Aurelle/Depth Tested Text` (`Assets/Resources/AurelleDepthTestedText.shader`: `ZTest LEqual`, `ZWrite Off`, `Offset -1, -1`, single-pass instanced and multiview variants). The shader is loaded with `Resources.Load`, so it must stay in a `Resources` folder to be included in builds; if it is missing or unsupported, a warning is logged and the built-in shader stays.
- **X-ray glass.** `Aurelle/X-Ray Ghost` (`Assets/Resources/AurelleXRayGhost.shader`) draws the car's shell in the explorer's X-ray view ([Car Explorer](#car-explorer)). It is a URP transparent shader with premultiplied blending: a faint light-blue film with a fresnel rim and crease lines, blended through `_Fade` from a solid stand-in lit by the main light, the real-time additional lights (including the Forward+ light loop), ambient light and the reflection probes (blended and box-projected as in URP Lit), with a clear coat when the original material has one. `ZWrite` and `Cull` come from the material. It has single-pass instanced and multiview variants, and its shadow pass casts only while `_Fade` is below 0.5. It is loaded with `Resources.Load`, so it must stay in a `Resources` folder; without it the explorer hides the shell instead. While the X-ray view is on, the shell's renderers carry property blocks and leave the SRP Batcher, the V6 and the internals are drawn, and `Car Explorer X-Ray Light` adds one real-time light.
- **Emission.** Emissive materials need the Realtime or Baked emissive GI flag, or URP's material validation, which a bake also runs, turns `_EMISSION` off. The builder's emissive exterior materials and the interior's LED materials are `BakedEmissive`. The car lamps, the display posters and the downlight lens discs are `RealtimeEmissive`.
- **Foveated rendering.** Android OpenXR uses the SRP foveation API and `ExteriorSpawnPlacement` requests a foveation level, but the OpenXR Foveated Rendering feature is disabled. In OpenXR 1.17.1 the feature always lists `XR_META_foveation_eye_tracked`; with Quest Pro among the Meta Quest target devices, the Android manifest then requires eye tracking (`oculus.software.eye_tracking`, required) and adds `com.oculus.permission.EYE_TRACKING`. Enable the feature in the OpenXR settings only once that is acceptable.
- **Lighting.** The showroom's lighting is baked by `AurelleRenderSetup.BatchBake` with the lighting settings `Assets/Settings/Rendering/AurelleLighting.lighting` ([Rendering and lighting](#rendering-and-lighting-aurellerendersetup)). The exterior is lit in real time and takes ambient light from the exterior light-probe grid. The bake output in `Assets/Scenes/SampleScene/` is stored in Git LFS. Re-bake after changing showroom geometry, lights or emissive materials, and after a district regeneration.

## Naming contracts

These names are looked up at runtime, by the builder or by the realism tools. Renaming them breaks the feature named.

| Name | Used by | Purpose |
| --- | --- | --- |
| `CarConcept` | `CarExplorerBootstrap`, `CarExplorerController`, `CarExplorerHud`, `ShowroomPresenterAgent`, `AurelleCarInternals` | Car root (`GameObject.Find`, so it must be active); Remy's station keeps clear of its shown renderers; the HUD's presence and XR plate placement use its rest bounds |
| `Internals` (direct child of `CarConcept`) | `CarExplorerController`, `AurelleCarInternals` | Root of the generated internals: inner content, picked exactly; while it exists the floor tub turns to glass in X-ray |
| `BodyHood` (direct child of `CarConcept`) | `CarExplorerController` | The car's front: the explorer's forward axis points from the car's centre to the hood (else to the V6, else along `CarConcept`'s forward) |
| `V6_Engine_Prototype(Clone)` (direct child of `CarConcept`) | `CarExplorerController`, `AurelleCarInternals` | V6 root (regrouped and tagged by the internals tool). Without the exact name, the first descendant whose name contains `V6` and has a renderer is used. |
| Top-level glTF nodes of `CarConcept`: `Wheel*`, `BodyDoor*`, `BodyHood*`, `BodyRear*`, `License*`, `BodyRoof*`, `BodyWindshield*`, other `Body*`, `InteriorSeats*`, `InteriorDash*`, `InteriorSteering*`, `InteriorPedal*`, `InteriorFloor*`, `InteriorMid*`, `InteriorCage*`, `InteriorPillar*`, `Axles*`, `Engine*` | `CarExplorerController` | Assemblies; the `Body*` and `License*` subtrees (and, with `Internals`, `InteriorFloor*`, the underbody and the cabin trims `InteriorDash*`, `InteriorSteeringDash*`, `InteriorMid*`, `InteriorCage*`, `InteriorPillar*`) form the X-ray glass shell ([Car Explorer](#car-explorer)) |
| Sub-part names in the car's assemblies (for example `Rim`, `Disc`, `Pad`, `Caliper`, `Window`, `Mirror`, `Handle`, `InteriorDoor<n>`, `Headlight`, `Taillights`, `SteeringHandle`, `Floormats`) and the words `Interior`, `Under`, `Gasket` and `Base` in shell part names | `CarExplorerController` | The groups inside each assembly and how they spread; fainter glass for the layers inside the skin |
| `Node-*` under `WheelFront*` / `WheelRear*` (suffix `L` = left) | `CarExplorerController` | Wheel part names |
| Raw V6 node names (block, oil pan, crank, piston, cylinder head, valve cover, cam, valve, rocker, intake, air filter, hose, exhaust, …) | `V6Regroup` (car internals tool); `CarExplorerController` for an untagged V6 | The V6's functional groups |
| `Remy Presenter` | `CarExplorerController`; `AurelleRealismPeople`, `AurelleRenderSetup` | Presenter root (`GameObject.Find`, must be active at load); presenter materials; downlight over Remy's post |
| `Showroom Floor` | `CarExplorerController`, `ShowroomPresenterAgent`, `ShowroomCollisionSetup` | Floor height, interior bounds |
| `Showroom Architecture` | `ShowroomCollisionSetup`, `ShowroomPresenterAgent` | Wall colliders, interior bounds |
| `Showroom Premium Collection` (each child is one display) | `ShowroomCollisionSetup`, `CarExplorerController`, `ShowroomPresenterAgent` | Display car colliders; keep-outs for the explorer's layouts; the displays a visitor can attend, with Remy at their side |
| `Circular Display` (under `Showroom Environment`) | `ShowroomPresenterAgent` | The hero car's podium: Remy's stations and his routes after the greeting keep outside it |
| `Showroom Interior Zones` | `ShowroomCollisionSetup` | Furniture colliders |
| `Player Spawn` (descendant of the `ExteriorSpawnPlacement` object) | `ExteriorSpawnPlacement`, builder | Start point when the serialized reference is empty |
| Root name containing `XR Origin` or `XR Rig` | `ExteriorSpawnPlacement`, `CarExplorerController` | XR rig lookup; the explorer treats cameras under such a node as XR views |
| `Hands Permissions Manager` (root) | `ExteriorSpawnPlacement` | Activated in XR mode |
| `Exterior Environment` (root) | builder menu | District root |
| `Exterior Daylight` | builder, `AurelleRenderSetup` | Sun light: found by name, created under the root when missing; the render setup makes it Mixed |
| `Bicycle/Saddle`, `Bicycle/Handlebar`, `Bicycle/Front Alloy Wheel`, `Bicycle/Rear Alloy Wheel`, and the `Pedal` child of each crank | `ExteriorCyclistAgent` | Seat, grips, wheels and pedals (built by the builder) |
| Mixamo bone names (`Hips`, `Spine`, `Neck`, `LeftUpLeg`, `LeftLeg`, `LeftFoot`, `LeftToeBase`, `LeftArm`, `LeftForeArm`, `LeftHand`, `LeftHandMiddle1` and the right-hand ones) | `ExteriorCyclistAgent` | Fallback when the rider's humanoid mapping is missing |
| `Rear Feature Wall`, `Smoked Glass Wall Section 0 0` | `DesktopWalkthroughController` | Door materials (with fallbacks) |
| `Showroom Entrance Hinged Doors`, `Desktop Walkable Exterior Ground`, `Car Explorer X-Ray Light`, `Car Explorer HUD`, `Car Explorer HUD (Screen)`, `Car Explorer HUD (XR)`, `Car Explorer HUD Event System` | runtime | Created at runtime; the names guard against duplicates |
| `Showroom Lighting/Showroom Reflection Probe`, `Graphite Glass Mullion i k` | interior tool (`InteriorGlazing`) | The glazing probe copies the hall probe; the mullions' positions set the glazing bays and the front line |
| `Body`, `Eyes`, `Eyelashes`, `Hair`, `Tops`, `Bottoms`, `Shoes` (the skinned mesh renderers of `Remy.fbx`) | `AurelleRealismPeople`, `ExteriorEnvironmentBuilder.ApplyCrowdLooks` | Material per body part |
| `Cyclist Rider` (child of each cyclist root) | `ExteriorEnvironmentBuilder.ApplyCrowdLooks` | Rider lookup |
| `CarConcept`, `V6_Engine_Prototype(Clone)`, `Showroom Premium Collection` | `CarsRealism`, `AurelleRenderSetup` | Car materials; probes, occluders and downlights per car |
| Imported material names of the four car GLBs (for example `Paint 1 Carmine`, `BMW_E30_M3_PAINT`) and the V6 part names | `CarsRealism` | Material slot mapping |
| `Showroom Lighting` and its children (`Showroom Key Spot`, `Showroom Rim Spot`, `Showroom Fill Light`, `Rear Wall Wash Spotlight N`, `Showroom Reflection Probe`, `Light Probe Group`, `Aurelle Downlights`, `Aurelle Bake Occluders`, `Aurelle Post Volume`) | `AurelleRenderSetup`; the interior reads the wall washers and the rim spot | Found by name or created |
| `Recessed Ceiling Spotlight N`, `Recessed Spotlight Lens N` (under `Showroom Architecture`) | `AurelleRenderSetup`, interior tool | Downlight columns, lens material, downlight cans |
| `Ceiling Canopy`, `Rear Feature Wall`, `Rear Graphite Feature Wall`, `Smoked Glass Wall Section 0 0` and `1 0`, `Ceiling Front Beam` | `AurelleRenderSetup` | Hall bounds for lights, probes and the box projection |
| Pieces of `Showroom Architecture` and `Circular Display`, and the children of `Reception Zone`, `Customer Lounge Zone` and `Rear Wall Advertising Displays` | interior tool | Objects to restyle, replace or remove (names in the `Interior*.cs` files) |
| `Warm Road LED`, `Main Camera` (root) | `AurelleRenderSetup` | Street lamps; desktop camera |

The scripts also depend on asset paths and shader names:

- the builder's input paths listed above;
- the realism folders under [Generated assets](#generated-assets);
- `Aurelle/Depth Tested Text`, `Aurelle/X-Ray Ghost` (the resources `AurelleDepthTestedText` and `AurelleXRayGhost`), `GUI/Text Shader`, `Universal Render Pipeline/Lit` and `/Unlit`, `Skybox/Panoramic`;
- the glTFast shader graphs `Shader Graphs/glTF-pbrMetallicRoughness` and `-Clearcoat`, the glTFast material property `baseColorFactor` (highlight), and `baseColorTexture`, `metallicFactor`, `roughnessFactor`, `clearcoatFactor` and `clearcoatRoughnessFactor` (the X-ray fade's stand-in).

## Content and packages

- `Assets/Models/CarConcept.glb` (requires `KHR_texture_basisu`, KTX2 textures) and every other `.glb` are imported by glTFast (`com.unity.cloud.gltfast` 6.20.0); KTX2 decoding needs `com.unity.cloud.ktx` 3.7.1. CarConcept's glTF node names drive the Car Explorer's assemblies and glass shell ([Naming contracts](#naming-contracts)). glTFast also imports the Poly Haven `.gltf` models in `Assets/ThirdParty/PolyHaven`, with generated lightmap UVs.
- `Assets/Models/V6_Engine_Prototype.glb` is a CAD export: 182 mesh nodes, 6,250 submeshes that share one material in the file, and about 627,000 triangles; glTFast imports 37 distinct meshes (identical parts share one). In the scene, `CarsRealism` gives its parts nine materials by part name, and the car internals tool puts each renderer on a merged one-submesh copy ([Car internals](#car-internals-aurellecarinternals)).
- `Assets/Models/Presenter/Remy.fbx` drives Remy, the pedestrians and the cyclists' riders. Its materials are remapped to `Assets/Materials/Realism/People`, and `Assets/Models/Presenter/Textures` holds textures derived from it. `Assets/Models/ExteriorPedestrians/Maya_RelaxedWalk.glb` is not referenced.
- `Assets/ThirdParty` holds the CC0 Poly Haven and ambientCG content used by the interior, one folder per asset with a `SOURCE.txt`.
- XR settings live in `Assets/XR` (`XRGeneralSettingsPerBuildTarget.asset`, `Settings/OpenXRPackageSettings.asset`, loaders); player, quality and graphics settings in `ProjectSettings`.
- Third-party content and licences: [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).
