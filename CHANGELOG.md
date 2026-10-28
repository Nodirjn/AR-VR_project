# Changelog

Notable changes to this project are recorded here. The project has no tagged releases; dated entries name the commit that introduced them.

## [Unreleased]

### Realism

Four editor tools under **Aurelle > Realism** rebuilt the look of the showroom, the cars and the people. The run order is in the README.

#### Rendering & lighting

- PC (quality level Ultra, now also the Editor's default) renders in HDR with Forward+ and SSAO. A global post-processing volume adds ACES tonemapping, colour adjustments, white balance, bloom and a light vignette, and the desktop camera adds SMAA and dithering.
- PC shadows are soft (high quality), 4096 px, in four cascades over 90 m (previously 50 m). Additional-light shadows are enabled, and reflection probes blend and use box projection.
- Quest (Low) keeps its pipeline: no HDR, no post-processing and no SSAO.
- The showroom has baked lighting: Baked Indirect, GPU lightmapper and baked ambient occlusion.
  - The six recessed spots, 21 new ceiling downlights (each with a visible lens) and seven wall washers, now mounted at the ceiling, are baked.
  - The hero car's key and rim spots are Mixed, and the key casts a real-time soft shadow on PC.
  - The fill light is off. The showroom's real-time lights drop from 13 to 2.
- Reflection probes are baked: a box-projected probe for the hall and one for each car on display (the hero car and the three premium cars). About 1,500 light probes cover the showroom, with a coarse grid over the district.
- The floor has soft contact shadows under the premium cars.
- Sky ambient light (0.62 → 1.0) and sky reflections (0.55 → 0.8) are stronger, so shaded facades stay bright without the fill light. Street lamps are dimmed for daylight (30 → 12).
- On Android, lightmaps import at up to 1024 px and reflection cubemaps at 128 px.

#### Cars

- CarConcept:
  - Carmine metallic paint with a clear coat, without the tiled noise normal map that made it look speckled, and a gloss black second paint.
  - Transparent tinted glass that keeps full reflections, so the interior is visible; mirror glass.
  - Gunmetal rims with machined faces, rubber tyres, metal brake discs and red calipers.
  - Black and carmine leather, alcantara, carpet and brushed aluminium inside.
  - LED headlights and lit tail lights. The fake white glow on the rims, calipers, trims and hardware is gone.
- V6: nine materials by part instead of one flat material: cast and polished aluminium, machined and dark steel, exhaust iron, black valve covers, rubber hoses and red pleated air filters.
- BMW M3 E30: Brilliantrot clear-coat paint, transparent tinted glass and rubber tyres.
- BMW M4: Alpine White paint and livery with a clear coat, a clear-coated carbon roof, gloss black trim, smoked headlight lenses, gold calipers and transparent glass.
- Tesla Model 3: pearl white clear coat, tinted glass, a darker glass roof, satin chrome and white leather.
- Traffic cars use the same materials. E30s are red, blue, black or silver; Teslas are white, midnight silver or blue.
- The premium cars use compressed texture copies of at most 1024 px instead of the uncompressed RGBA32 textures that glTFast imported. The texture memory of the car materials, measured in the Editor, dropped from 907.5 MB to 88.5 MB.
- The Car Explorer highlight still works on every part.

#### Interior

- Floor: polished anthracite granite in 1.2 m tiles inside the glass, and the same stone honed on the forecourt.
- Walls and ceiling:
  - A board-marked concrete feature wall with a shadow-gap skirting.
  - A white plaster rear wall and ceiling. The ceiling no longer glows.
  - Low-iron clear glazing, black powder-coated mullions, sills and beams, and stainless reveals.
- Podium: a polished black marble deck, a brushed stainless band, a steel inlay and a flush LED line.
- Reception:
  - A white marble worktop and a fluted walnut front over a recessed kick.
  - One walnut slat wall instead of 14 oak battens, and a dark bronze brand plaque.
  - Three opal globe pendants, a desk plant, a vase and a money tree.
- Lounge: more compact, so it no longer blocks the way to the reception desk.
  - A modelled fabric sofa, two black leather armchairs, a round coffee table and a side table with vases.
  - A wool rug, two pendants and money trees in fibre-cement planters.
- Rear-wall displays: slim black lightbox frames with backlit studio renders of the CarConcept in red, silver and blue (GT COUPE, S EXECUTIVE, ELECTRA GT). They replace the cartoon pictograms and coloured accent bars.
- Ceiling fixtures: suspended LED ring luminaires, surface-mounted downlight cans, a wall-wash track with spot heads, a pendant head for the rim spot and four linear LED lines.
- The textures and the furniture and plant models are CC0 assets from Poly Haven and ambientCG (`Assets/ThirdParty`).

#### People

- Remy's materials are built from the maps embedded in `Remy.fbx`, which were never extracted before. He has textured skin, hair, eyelashes and clothes, and his eyes no longer glow. His eyes and eyelashes no longer cast shadows.
- The eight pedestrians and the two cyclists' riders are no longer pale and untextured. Each one has its own top, bottoms, shoes, hair colour and skin tone.

### XR and desktop startup

- The scene chooses between desktop and XR before any other script starts. With an active headset or XR loader, the VR template rig is switched on at the exterior spawn and the desktop camera is switched off; once tracking settles, the headset is placed at the spawn facing the showroom. Previously the rig stayed inactive and the headset showed an untracked view.
- If an XR loader is running but no headset display starts within 6 s, the scene falls back to desktop mode.
- Without an XR rig in the scene, XR mode falls back to a head-tracked camera.
- In XR mode the Android XR hand-tracking permission is requested, and the rig's jump is disabled because A advances the explorer.
- Falling below y = −25 returns the player to the spawn, on desktop and in XR.
- Foveated rendering is requested at runtime; it takes effect only once the OpenXR Foveated Rendering feature is enabled (see the README).

### Input

- Arrow keys only walk; the explorer uses T, O, R, `[`, `]`, Page Up and Page Down (see Car Explorer).
- A crosshair is shown while the cursor is locked, and clicks select at the screen centre.
- Esc frees the cursor, puts a part that is out back, and closes the greeting, the overview and the part card.
- The on-screen help and the explorer's help panel match the active mode (desktop or XR) and language.
- In XR, the trigger on either controller selects along that controller's ray, and the grip grabs parts.

### Collisions

- Walls, the three premium cars and floor-standing furniture get colliders at runtime in both modes, so neither the visitor nor Remy walks through them.
- The podium's colliders follow its shape instead of forming 3 m domes.
- The showroom floor gets a solid 1 m deep collider under its thin plane, so characters can no longer sink through it; if anything still pushes Remy below the floor, he is put back on it.
- The desktop eye height no longer drops when looking down.
- The entrance door (E) swings open and shut over 1.6 s instead of snapping.

### Car Explorer

- The seven-stage explorer (stages 1–7 on O / R / T, ending in the stage 7 atlas board) is replaced by three views and a take-out per assembly:
  - Normal: the complete car.
  - X-ray: the body shell fades in 0.6 s into light-blue glass with a bright rim (new shader `Aurelle/X-Ray Ghost` in `Assets/Resources`), and a soft spot light fades in over the car. The V6, the cabin, the axles, the brakes and the wheels show through in their own materials.
  - Exploded: the car's 16 assemblies come apart over 1.5 s in three tiers around it (wheels, V6, cabin floor and underbody; seats and cockpit; body frame, roof, windscreen, doors, front and rear sections), fitted to the room each time.
  - In the normal and X-ray views, clicking a part takes its assembly out of the car (0.8 s) and spreads it into its parts: a wheel along its axle, a door into panel, window, mirror and trim, the V6 into its sub-assemblies. Clicking another assembly swaps them; clicking empty space puts it back.
- Controls: T switches X-ray on and off (from the exploded view it reassembles the normal car); O, `]` or Page Up opens the exploded view; R, `[` or Page Down steps back (a part that is out goes back first, then exploded → X-ray → normal); Esc also puts the part back. The console's cards are X-RAY, EXPLODE, BACK and RESET. In XR, A shows the next view, B steps back and the left Menu button resets; the trigger takes parts out and the grip grabs them.
- The car's parts are picked by their exact meshes, cooked into a separate physics scene at start; in the X-ray view a click passes through the glass to the part behind it. Walls, glazing, other cars and furniture in front block the selection, and clicks on the explorer's panels no longer select what is behind them.
- Taken-out assemblies and the exploded layout are placed to stay above the floor and below a height limit, clear of the viewer, the other display cars and the furniture; a taken-out assembly also keeps clear of Remy, and an exploded assembly that would land where the visitor stands moves on until it is clear of them.
- XR grabs: in the X-ray view the glass shell cannot be grabbed, and a part that is held or still gliding back when a move starts (also a V6 component or a steering-wheel rim, which have no move of their own) returns to its place instead of staying where it was dropped.
- The X-ray fade starts from a stand-in lit like the paint (real-time lights, blended reflection probes, clear coat), so the body no longer darkens for a moment; windows cast no shadow while they fade; a missing glass shader logs one warning instead of one per renderer per frame.
- After the X-ray view, a take-out or the exploded view, the car returns exactly to its rest pose and its own materials, also when the explorer is disabled or destroyed. A command given during a move continues from where the parts are.
- The highlight is visible on the glTFast materials of the car and the engine, and on the X-ray glass.
- The V6 is drawn only in the X-ray and exploded views and while it or the front section is taken out, and never casts shadows.
- V6 parts show readable names instead of raw CAD export names.
- The controls and the overview form one console at the edge of the view, on the side away from Remy. It is placed from where the visitor stands, not where they look, stays above the floor, and moves closer instead of being hidden by the car or its parts.
- The part card and the WELCOME panel appear next to Remy where the visitor can see them and keep clear of the console; WELCOME waits for Remy to arrive and is no longer cancelled by the first click.
- Car parts show readable names (for example Hood instead of BodyHood). Remy's nameplate is smaller, sits above his head and hides while he is near or talking.
- Selection colliders fit each part's mesh, and the bootstrap no longer logs an error in scenes without the car.

### Presenter (Remy)

- Remy starts his greeting whenever the visitor enters the showroom, from any side.
- He walks to a reachable spot in front of the visitor instead of walking in place, then follows inside the visitor's view and inside the showroom, and avoids standing in front of the explorer's panels.
- He looks and points at the selected part's centre; head tilt and arm swing are corrected.

### Exterior agents

- Cars, cyclists and signals rebuild their runtime state when the scene loads, so cars no longer start their first lap the wrong way and signal lamps are correct from the first frame.
- Cars round corners, stop at stop lines, keep braking distance and yield to cyclists, pedestrians and crossing cars; a deadlock breaker resolves standoffs at junctions.
- Pedestrians no longer walk in a T-pose; the crossing pedestrian waits only at the curb and always finishes a crossing.
- Cyclists sit on the saddle, lean forward with their hands on the handlebar and keep their feet on the turning pedals; the wheels roll and the tyres stay on the cycle track's surface.

### Exterior generator (Aurelle > Build Exterior District)

- Trees, lamps, signal heads, signs and street furniture are placed individually instead of being stacked at one point.
- Traffic loops follow the streets' right-hand lanes as straight lines between the corners.
- The ground steps down under the showroom floor, so grass no longer shows through the marble.
- Curbs and sidewalks stop at cross streets and buildings, with dropped curbs at driveways and crossings. Trees and lamps keep clear of roads, paths and buildings, walk-signal posts stand on sidewalks, and lane arrows and stop lines are corrected.
- Bicycles are built for a seated rider: a handlebar within reach, a seat post and saddle, and crank arms with pedals; riders are sized to fit.
- Tesla traffic cars drive nose first, moving parts are never static-batched, storefront canopies and pylons no longer hide the signs' depth-tested labels, and street lamps are dimmer.
- The builder saves the scene that contains it, also finds an inactive district root, and records Undo for its lighting changes.
- The builder ends the traffic build by dressing every pedestrian and rider in a saved crowd look (`ApplyCrowdLooks`). It no longer tints the cyclists' shirts with a property block that the scene did not keep, and pedestrians no longer tint textured outfits at runtime.
- The district in SampleScene was regenerated with this builder in Unity 6000.3.12f1; the regeneration also rewrote the exterior materials and the traffic prefabs it owns and added tiled sidewalk materials.

### Rendering

- Text labels no longer show through walls, buildings and the car (a depth-tested text shader in `Assets/Resources`).
- Additional lights (showroom spots, street lamps) render at quality level Low, Android's default, and shadows at that level reach 35 m with two cascades. For Ultra, see Realism.
- The five emissive showroom materials have their emission keyword on (SampleScene no longer uses them after the realism pass); the exterior signal and lamp materials are flagged so that the material inspector no longer strips their emission.
- The Android OpenXR settings use the SRP foveation API; the Foveated Rendering feature stays disabled.

### Scene

- The premium cars are placed from the imported GLBs instead of being loaded from an absolute local path, so they appear on every machine; the Tesla faces out and the M4 is centred on its display.
- The V6 is mounted upright and centred in the car.
- The showroom floor is level, and the district ground lies below it.
- Traffic waypoints follow straight lanes and the six moving cars start on them; the Tesla traffic prefab is turned so those cars drive nose first; the cyclists' pedals are no longer static-batched.
- Storefront canopies, the pylon displays, the reception desk mark and seven sign texts were adjusted so the depth-tested labels stay visible.

### Documentation

- Rewrote the README, architecture notes, contributing guide and changelog for the current project, added third-party notices, and anchored the `.gitignore` patterns to the repository root.
- Added a root `.gitattributes` that stores new third-party textures, model buffers, baked lighting and the textures that the realism tools generate in Git LFS.
- Documented the realism tools and their run order, the visual quality per platform, the new third-party assets and the new known limitations.
- Documented the Car Explorer's views, assemblies, picking scene and glass shader, and the CarConcept node names it depends on.

## 2026-10-21: Expanded showroom and exterior environment (`65e4de9`)

- Added the generated exterior district around the showroom (`ExteriorEnvironmentBuilder`, menu **Aurelle > Build Exterior District**): streets, buildings, landscaping, street furniture, signalised junctions, moving and parked traffic, pedestrians, cyclists, an exterior spawn point, the Kloppenheim HDRI sky and Poly Haven asphalt and concrete textures. Runtime behaviour lives in `ExteriorTrafficSignal`, `ExteriorTrafficCarAgent`, `ExteriorPedestrianAgent`, `ExteriorCyclistAgent` and `ExteriorSpawnPlacement`.
- Added the premium collection: BMW M3 E30, BMW M4 Competition and Tesla Model 3 GLBs with `CREDITS.md`, loaded in the scene by glTFast runtime loaders from absolute local file paths.
- Added the V6 engine model inside the CarConcept and rewrote `CarExplorerController`: seven stages instead of three, every mesh selectable, English and Uzbek panels, XR grabbing and integration with Remy.
- Added `ShowroomPresenterAgent` (Remy's greeting and follow behaviour) and `DesktopWalkthroughController` (first-person desktop controls with an entrance door).
- Added the unused `EnginePartsExplorer` prototype, the unused `Maya_RelaxedWalk.glb` and the scene snapshot `SampleScene_Before_ShowroomCollection_20261019.unity`.
- The documentation was not updated in this commit.

## 2026-10-11: Project documentation (`24572b4`)

- Added the README, architecture notes, contributing guide and changelog, describing the project as of `8f8b55a`.

## 2026-10-10: Unity project (`8f8b55a`)

- Imported the Unity project created from the VR template (`com.unity.template.vr` 10.0.0): the showroom scene with the CarConcept and the Remy presenter, `CarExplorerController` with three disassembly levels, XR packages and settings, and earlier scene snapshots.
