# Test drive

The visitor picks a car of the premium collection, or the hero CarConcept (as the "Aurelle GT Concept"), in the showroom, appears beside it on the dealership plaza, opens the driver's door, gets in and drives through the district, with the head tracked on Meta Quest 3 or the mouse on the desktop. Everything is built at run time from the scene as it is: no scene, prefab or asset changes are needed, and nothing in the existing showroom behaviour changes until a test drive starts.

## Files

| File | Responsibility |
| --- | --- |
| `Assets/Scripts/TestDrive/TestDriveManager.cs` | Installs itself `AfterSceneLoad` when the scene has `Showroom Premium Collection`. Copies every display car at load (before the Quest budget merges or culls them), puts a stand beside each, and runs the states: showroom → beside the car → seated → showroom. Pauses the Car Explorer and Remy while the visitor is out. |
| `TestDriveCarBuilder.cs` | Turns a copy of a display car into a drivable car (once per car): nose along +Z, wheels on y = 0, wheel rig, wheel colliders, door hinge, steering pivot, driver's eye, body colliders, rigid body, headlights. |
| `TestDriveWheelRig.cs` | The traffic agents' wheel rig (tyre per quarter, steer and spin pivots at the hubs, shared meshes split per wheel). |
| `TestDriveMeshCut.cs` | Triangle selection, clipping, connected pieces (islands) and a CPU ray cast. The M4's door is taken as the model's own door pieces (outer panel, frame, inner card, found as islands); the E30's door is cut along straight lines; the Tesla uses its own `door_lf_dummy`. |
| `TestDriveCluster.cs` | A live display laid over the car's own instrument cluster (P R N D, gear, speed, revs), placed by casting rays from the eye past the steering wheel. |
| `TestDriveDriver.cs` | Seated people made from the city's pedestrians: the visitor's own body (head hidden, hands follow the steering wheel, legs to the pedals) and a driver in every traffic car (baked to static meshes). |
| `TestDriveTrees.cs` | Replaces the 47 sphere-and-cylinder street trees with generated trees from `Assets/Resources/TestDrive/Trees` (ez-tree, MIT); cull-only LOD and no leaf shadows on Quest. |
| `TestDriveCity.cs` | The bigger city: Ocean Drive and the beach, the avenue and the south street out to the outer streets at x = ±320, about seventy new blocks with ground-floor shops (clothing, gun shops, cafés, markets, bakeries, books, furniture, pharmacies, surf shops, offices), interior-mapped shop windows everywhere (`Resources/TestDriveShopWindow.shader`), the sea (`Resources/TestDriveWater.shader`), umbrellas, loungers and lifeguard towers, bins and dumpsters, lanterns on every street where it was dark, trees, parks, playgrounds, parking lots, bus stops and hydrants. |
| `TestDriveTrafficVariety.cs`, `TestDriveCurbStops.cs` | Twenty-four kinds of vehicle on the roads at their own pace and spacing, traffic in both directions round every block, and cars that pull in to the kerb while the driver goes into a shop and back. |
| `TestDrivePeople.cs`, `TestDriveShoppers.cs`, `TestDriveIdlePerson.cs`, `TestDriveGait.cs` | Walkers on the promenade, shoppers going in and out of shops, the odd pair chatting by a shop window, people on benches and loungers, office workers at their desks; a stride-based walking gait (foot placement, pelvis, arm swing) for every pedestrian. |
| `TestDriveBirds.cs` | Feral pigeons on the forecourt (head-bob walk, pecking, flock take-off and landing) and gulls over the sea, built from lofted meshes with painted plumage. |
| `TestDriveShopInteriors.cs` | Enterable shops: the nearest ground-floor shops open as you walk up (sliding glass door, tiled entrance level with the sidewalk) into furnished, lit interiors: pharmacy, market, clothing, surf, gun shop, café, bakery, books, furniture and a small office, each with staff. |
| `TestDriveNight.cs` | Headlamps and tail lamps on every car at night, light pools on the road, lanterns as downlights with their own pools. |
| `TestDriveDayNight.cs` | Time of day while driving: sun path, colour, moonlight, sky exposure, ambient, fog, street lanterns at dusk and automatic headlights. 24 real minutes per day, starting at 16:30. |
| `TestDriveVehicle.cs` | Physics and drivetrain: torque curve, automatic gearbox with shift time and kick-down, rev limiter, creep, engine braking or regeneration, ABS, traction control, Ackermann steering with speed-sensitive lock, anti-roll bars, drag and rolling resistance. Moves the wheels (suspension travel, steering, rolling), the steering wheel and the door. |
| `TestDriveCarProfile.cs` | Real figures per car (mass, power, torque, gear ratios, steering ratio, top speed). |
| `TestDriveAudio.cs` | Synthesised sound on the audio thread: engine harmonics from rpm and load (or motor whine), intake, road, wind, tyre squeal, horn, starter, doors, gear shifts. |
| `TestDriveDashboard.cs` | Head-up display 2.3 m ahead of the eye: speed, 60 km/h town limit, P R N D and gear, rev (or power) bar, status, controls card, hold-to-leave bar. |
| `TestDrivePlayer.cs` | Stands the visitor, seats them with the head at the driver's eye (XR Origin parented to the car, locomotion paused) and gets them out; desktop mouse look and V chase camera. |
| `TestDriveKiosk.cs`, `TestDriveHint.cs`, `TestDriveFader.cs`, `TestDriveUi.cs`, `TestDriveInput.cs` | Stands, floating prompts, fades, shared UI look and every control binding. |
| `TestDriveTraffic.cs` | Solid boxes for the traffic and parked cars, and the registry the traffic agents read to yield to the test car. |
| `Assets/Resources/TestDriveOverlay.shader` | Draws the HUD, prompts and fades on top of the cabin (single-pass instanced and multiview). |

Two small additions to existing scripts: `DesktopWalkthroughController.PlaceBody`, `ReattachCamera` and `Body`, and five lines in `ExteriorTrafficCarAgent.Update` that make traffic yield to the test car.

## Controls

| Action | Meta Quest 3 | Desktop |
| --- | --- | --- |
| Choose a car | Trigger on the stand's "Start test drive" | Look at the car or stand, press G |
| Open the door | Grip the door handle, or A near the door | E near the door |
| Get in | A (door open) | E |
| Accelerate / brake | Right trigger / left trigger | W / S (arrows) |
| Steer | Left or right thumbstick | A / D (arrows) |
| Reverse | Hold the left trigger when stopped (then the left trigger drives backwards, the right trigger brakes and, when stopped, returns to D); or A | Hold S when stopped; or R |
| Handbrake | Right grip | Space |
| Get out | B (below 3 km/h) | E |
| Horn / lights | Y / X | H / L |
| Recentre the seat | Left thumbstick click | C |
| Camera | Head tracking | Mouse; V toggles the chase camera |
| Back to the showroom | Hold the menu button 1.2 s | Hold Backspace |

The engine starts by itself after the door closes. Pressing the accelerator in P selects D. The glass of the test car is made clear so the city is visible from the seat. The engine sound is modelled from per-cylinder firing pulses through exhaust pipe resonances (four-cylinder rasp on the E30; inline six with turbo whistle, blow-off and overrun crackle on the M4).

## Tuning

- Car figures: `TestDriveCarProfile`. Grip: the friction curves in `TestDriveCarBuilder.BuildPhysics`. Steering feel: `TestDriveVehicle.Steer` and the response curve in `TestDriveManager.UpdateSeated`.
- Spawn point: `TestDriveManager.SpawnPosition` (the plaza, nose towards the avenue). The physics step is 1/90 s while a car is out and goes back afterwards.
- If the seat height feels wrong on the headset, click the left thumbstick to recentre; the driver's eye comes from the seat cushion and headlining of each model.

The scripts compile against Unity 6000.3.12f1's assemblies and were tried in Play Mode in the Editor on macOS during development; the test drive has not yet been run on a headset.
