using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// The showroom test drive. Each car of the premium collection gets a "Test drive" stand; choosing it fades out,
/// builds a drivable copy of that car (once), parks it on the dealership driveway and stands the visitor beside the
/// driver's door. The visitor opens the door (grip the handle, or A / E), gets in (A / E), drives through the
/// district with the head tracked (Quest) or the mouse (desktop), gets out when stopped (B / E) and holds the menu
/// button (Backspace) to return to the showroom. The Car Explorer and Remy pause while the visitor is out.
/// </summary>
[DefaultExecutionOrder(50)]
public sealed class TestDriveManager : MonoBehaviour
{
    private enum State { Showroom, Busy, Outside, Seated }

    // The plaza in front of the dealership, short of the avenue's sidewalk, nose towards the avenue (+Z).
    private static readonly Vector3 SpawnPosition = new Vector3(1f, 0f, 41.5f);
    private static readonly Vector3 SpawnHeading = Vector3.forward;
    private const float HoldToLeave = 1.2f;
    private const float FixedStepWhileDriving = 1f / 90f;

    private sealed class Entry
    {
        public Transform display;
        public TestDriveCarProfile profile;
        public GameObject template;
        public TestDriveVehicle vehicle;
        public TestDriveDashboard dashboard;
        public TestDriveKiosk kiosk;
    }

    public static TestDriveManager Instance { get; private set; }

    private readonly List<Entry> entries = new List<Entry>();
    private readonly List<Behaviour> paused = new List<Behaviour>();
    private TestDrivePlayer player;
    private TestDriveInput input;
    private TestDriveFader fader;
    private TestDriveHint hint;
    private Transform templates;
    private State state = State.Showroom;
    private Entry current;
    private float holdTimer;
    private float savedFixedStep = -1f;
    private TestDriveDayNight world;
    private TestDriveNight night;
    private bool lastNight;

    public bool Driving => state == State.Seated || state == State.Outside;

    // ------------------------------------------------------------------ install

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (Instance != null) return;
        GameObject collection = GameObject.Find("Showroom Premium Collection");
        if (collection == null) return;
        var go = new GameObject("Test Drive");
        TestDriveManager manager = go.AddComponent<TestDriveManager>();
        manager.Setup(collection.transform);
    }

    private void Awake()
    {
        Instance = this;
        player = new TestDrivePlayer();
        input = new TestDriveInput();
        input.Enable();
        fader = gameObject.AddComponent<TestDriveFader>();
        hint = TestDriveHint.Create(transform);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (input != null) input.Dispose();
        if (savedFixedStep > 0f) Time.fixedDeltaTime = savedFixedStep;
    }

    /// <summary>Copies every display car now, before the Quest budget merges or culls the originals, and puts a
    /// stand beside each.</summary>
    private void Setup(Transform collection)
    {
        var holder = new GameObject("Test Drive Templates");
        holder.SetActive(false);
        holder.transform.SetParent(transform, false);
        templates = holder.transform;
        Material postMaterial = MakePostMaterial();
        for (int i = 0; i < collection.childCount; i++)
        {
            Transform display = collection.GetChild(i);
            if (!display.gameObject.activeInHierarchy || display.GetComponentInChildren<MeshRenderer>() == null) continue;
            var entry = new Entry
            {
                display = display,
                profile = TestDriveCarProfile.ForDisplay(display.name),
                template = Instantiate(display.gameObject, display.position, display.rotation, templates)
            };
            entry.template.name = display.name + " (test drive copy)";
            entry.template.transform.localScale = display.lossyScale;
            entries.Add(entry);
            CreateKiosk(entry, entries.Count - 1, postMaterial);
        }
        // The hero car in the middle of the showroom (the open "Car Concept") can be driven too.
        GameObject hero = GameObject.Find("CarConcept");
        if (hero == null)
        {
            CarExplorerController explorer = Object.FindAnyObjectByType<CarExplorerController>(FindObjectsInactive.Include);
            if (explorer != null) hero = explorer.gameObject;
        }
        if (hero == null) Debug.LogWarning("[Test Drive] The showroom's hero car (CarConcept) was not found; it has no test drive stand.");
        else Debug.Log("[Test Drive] Hero car found for a test drive: " + hero.name);
        if (hero != null && hero.GetComponentInChildren<MeshRenderer>() != null)
        {
            var entry = new Entry
            {
                display = hero.transform,
                profile = TestDriveCarProfile.ForDisplay("CarConcept"),
                template = Instantiate(hero, hero.transform.position, NoseForward(hero), templates)
            };
            entry.template.name = "CarConcept (test drive copy)";
            entry.template.transform.localScale = hero.transform.lossyScale;
            entries.Add(entry);
            CreateKiosk(entry, entries.Count - 1, postMaterial);
        }
        if (entries.Count > 0)
            Debug.Log("[Test Drive] " + entries.Count + " cars ready for a test drive.");
        // People to seat in the cars, copied while every pedestrian is still in the scene; real trees; the sky.
        TestDriveGait.Install();
        TestDriveDriver.CollectTemplates();
        TestDriveTrees.ReplaceStreetTrees();
        world = TestDriveDayNight.Create(transform);
        night = TestDriveNight.Create(transform, world);
        TestDriveCity.Install();
        TestDriveTrafficVariety.Install();
        TestDrivePeople.Install();
    }

    /// <summary>
    /// The hero car's rotation turned about the vertical only, so it stays upright (its import carries its own
    /// tilt correction) with the nose along +Z like the other cars. The nose is where the bonnet is.
    /// </summary>
    private static Quaternion NoseForward(GameObject car)
    {
        Quaternion r = car.transform.rotation;
        Bounds b = new Bounds(car.transform.position, Vector3.zero);
        bool found = false;
        foreach (Renderer rend in car.GetComponentsInChildren<Renderer>())
        {
            if (!found) { b = rend.bounds; found = true; }
            else b.Encapsulate(rend.bounds);
        }
        Vector3 nose = Vector3.zero;
        foreach (Transform t in car.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if ((n.Contains("hood") || n.Contains("bonnet") || n.Contains("headlight")) && t.GetComponent<Renderer>() != null)
            {
                nose = t.GetComponent<Renderer>().bounds.center - b.center;
                break;
            }
        }
        nose.y = 0f;
        if (nose.sqrMagnitude < 0.01f)
        {
            // No bonnet by name: the car's long horizontal axis, pointing the way its local +Z goes.
            nose = b.size.x > b.size.z ? Vector3.right : Vector3.forward;
            Vector3 localZ = r * Vector3.forward; localZ.y = 0f;
            if (Vector3.Dot(nose, localZ) < 0f) nose = -nose;
        }
        return Quaternion.AngleAxis(Vector3.SignedAngle(nose, Vector3.forward, Vector3.up), Vector3.up) * r;
    }

    private void CreateKiosk(Entry entry, int index, Material postMaterial)
    {
        Bounds b = new Bounds(entry.display.position, Vector3.zero);
        bool found = false;
        foreach (Renderer r in entry.display.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            if (!found) { b = r.bounds; found = true; }
            else b.Encapsulate(r.bounds);
        }
        // Beside the driver's door, towards the front, turned to the visitor coming down the hall.
        Vector3 at = new Vector3(b.min.x - 0.42f, b.min.y, b.center.z + b.extents.z * 0.3f);
        RaycastHit hit;
        if (Physics.Raycast(at + Vector3.up * 1.5f, Vector3.down, out hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            at.y = hit.point.y;
        Quaternion facing = Quaternion.LookRotation(new Vector3(-0.45f, 0f, 1f).normalized, Vector3.up);
        entry.kiosk = TestDriveKiosk.Create(transform, index, entry.display, entry.profile, at, facing, postMaterial);
        entry.kiosk.Requested = Request;
    }

    private static Material MakePostMaterial()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) return null;
        var m = new Material(lit) { name = "Test Drive Stand Metal" };
        m.SetColor("_BaseColor", new Color(0.2f, 0.21f, 0.23f));
        m.SetFloat("_Metallic", 0.85f);
        m.SetFloat("_Smoothness", 0.62f);
        return m;
    }

    // ------------------------------------------------------------------ frame

    private void Update()
    {
        player.Resolve();
        if (night != null) night.SetHead(player.Head);
        if (current != null && current.vehicle != null && current.vehicle.CabinLight != null)
            current.vehicle.CabinLight.intensity = world != null && Driving ? Mathf.Lerp(0.22f, 0.7f, world.Daylight) : 0.7f;
        // Headlights follow the time of day.
        if (world != null && current != null && current.vehicle != null && Driving && world.IsNight != lastNight)
        {
            lastNight = world.IsNight;
            current.vehicle.SetLights(lastNight);
            if (current.dashboard != null && state == State.Seated) current.dashboard.Toast(lastNight ? "Night: headlights on" : "Morning: headlights off", 2.5f);
        }
        Transform head = player.Head;
        fader.Follow(head);
        switch (state)
        {
            case State.Showroom: UpdateShowroom(head); break;
            case State.Outside: UpdateOutside(head); break;
            case State.Seated: UpdateSeated(); break;
        }
        if (state != State.Outside) hint.Hide();
        else hint.Track(head);
    }

    private void UpdateShowroom(Transform head)
    {
        if (!player.IsXr) EnsureEventSystem(false);
        if (!TestDriveInput.Pressed(input.SelectCar) || head == null) return;
        RaycastHit hit;
        if (!Physics.Raycast(head.position, head.forward, out hit, 14f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)) return;
        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (hit.transform.IsChildOf(e.display) || (e.kiosk != null && hit.transform.IsChildOf(e.kiosk.transform)))
            {
                Request(i);
                return;
            }
        }
    }

    private void UpdateOutside(Transform head)
    {
        TestDriveVehicle car = current.vehicle;
        if (car == null || head == null) return;
        Vector3 handle = car.HandlePoint.position;
        Vector3 flat = handle - head.position;
        flat.y = 0f;
        bool near = flat.magnitude < (player.IsXr ? 1.6f : 2.6f);
        bool xr = player.IsXr;

        if (UpdateHold(null)) return;

        if (!car.DoorOpen && !car.DoorClosed)
        {
            hint.Show(car.HandlePoint, new Vector3(-0.15f, 0.35f, 0f), "…", "");
            return;
        }
        if (car.DoorClosed)
        {
            hint.Show(car.HandlePoint, new Vector3(-0.15f, 0.35f, 0f), "Open the door",
                xr ? "Grip the handle, or press A" : (near ? "Press E" : "Walk to the driver's door"));
            bool grabbed = xr && (Grabbed(input.LeftGrip, input.LeftHandPosition, handle) || Grabbed(input.RightGrip, input.RightHandPosition, handle));
            if (grabbed || (near && TestDriveInput.Pressed(input.Interact))) car.SetDoor(true);
            return;
        }
        hint.Show(car.HandlePoint, new Vector3(-0.15f, 0.35f, 0f), "Get in",
            xr ? "Press A, or step into the seat" : "Press E");
        bool stepIn = xr && Vector3.Distance(head.position, car.EyeAnchor.position) < 0.55f;
        if (stepIn || (near && TestDriveInput.Pressed(input.Interact))) StartCoroutine(EnterCar());
    }

    private bool Grabbed(UnityEngine.InputSystem.InputAction grip, UnityEngine.InputSystem.InputAction position, Vector3 handle)
    {
        if (TestDriveInput.Read(grip) < 0.6f) return false;
        Vector3 hand;
        return player.TryGetHand(position, out hand) && Vector3.Distance(hand, handle) < 0.32f;
    }

    private void UpdateSeated()
    {
        TestDriveVehicle car = current.vehicle;
        if (car == null) return;
        float steer = TestDriveInput.Read(input.Steer);
        float magnitude = Mathf.Clamp01((Mathf.Abs(steer) - 0.08f) / 0.92f);
        steer = Mathf.Sign(steer) * Mathf.Pow(magnitude, 1.35f);
        car.SetDriverInput(TestDriveInput.Read(input.Throttle), TestDriveInput.Read(input.Brake), steer,
            TestDriveInput.Held(input.Handbrake), TestDriveInput.Held(input.Horn));

        if (TestDriveInput.Pressed(input.Gear))
        {
            if (!car.EngineRunning) car.StartEngine();
            else if (!car.ToggleDirection()) current.dashboard.Toast("Stop the car to change direction", 2f);
        }
        if (TestDriveInput.Pressed(input.Lights)) car.ToggleLights();
        if (TestDriveInput.Pressed(input.Recenter)) player.Recenter(car);
        player.UpdateDesktopSeat(input, car);

        if (UpdateHold(current.dashboard)) return;
        if (TestDriveInput.Pressed(input.Exit))
        {
            if (car.SpeedKmh < 3f) StartCoroutine(ExitCar());
            else current.dashboard.Toast("Stop the car to get out", 2f);
        }
    }

    /// <summary>Holding the menu button (Backspace) returns to the showroom.</summary>
    private bool UpdateHold(TestDriveDashboard dashboard)
    {
        if (TestDriveInput.Held(input.Menu)) holdTimer += Time.deltaTime;
        else holdTimer = 0f;
        if (dashboard != null) dashboard.SetHold(holdTimer / HoldToLeave);
        if (holdTimer < HoldToLeave) return false;
        holdTimer = 0f;
        if (dashboard != null) dashboard.SetHold(0f);
        StartCoroutine(ReturnToShowroom());
        return true;
    }

    // ------------------------------------------------------------------ transitions

    public void Request(int index)
    {
        if (state != State.Showroom || index < 0 || index >= entries.Count) return;
        StartCoroutine(StartTestDrive(entries[index]));
    }

    private IEnumerator StartTestDrive(Entry entry)
    {
        state = State.Busy;
        yield return fader.FadeOut(player.Head, 0.45f);
        PauseShowroom(true);
        // Building takes a moment the first time (wheels, door, cabin); the view stays black meanwhile.
        yield return null;
        if (entry.vehicle == null)
        {
            entry.vehicle = TestDriveCarBuilder.Build(entry.template, entry.profile);
            entry.template = null;
            entry.vehicle.transform.SetParent(transform, false);
            entry.dashboard = TestDriveDashboard.Create(entry.vehicle, player.IsXr);
            entry.vehicle.VisitorBody = TestDriveDriver.SeatVisitor(entry.vehicle);
            if (entry.vehicle.VisitorBody != null) entry.vehicle.VisitorBody.gameObject.SetActive(false);
        }
        if (current != null && current != entry && current.vehicle != null) current.vehicle.gameObject.SetActive(false);
        current = entry;
        TestDriveTraffic.EnsureTrafficColliders();
        TestDriveDriver.PopulateTraffic();
        if (world != null) world.SetRunning(true);
        if (savedFixedStep < 0f) savedFixedStep = Time.fixedDeltaTime;
        Time.fixedDeltaTime = FixedStepWhileDriving;

        TestDriveVehicle car = entry.vehicle;
        car.gameObject.SetActive(true);
        car.Park(GroundedSpawn(), Quaternion.LookRotation(SpawnHeading, Vector3.up));
        if (world != null) { lastNight = world.IsNight; car.SetLights(lastNight); }
        if (night != null) night.SetRunning(true, car);
        entry.dashboard.SetVisible(false);
        yield return new WaitForFixedUpdate();
        player.StandAt(car.ExitPoint.position, car.ExitPoint.forward);
        state = State.Outside;
        yield return null;
        yield return fader.FadeIn(player.Head, 0.6f);
    }

    private static Vector3 GroundedSpawn()
    {
        RaycastHit hit;
        Vector3 p = SpawnPosition;
        if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out hit, 12f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            p.y = hit.point.y;
        return p + Vector3.up * 0.04f;
    }

    private IEnumerator EnterCar()
    {
        state = State.Busy;
        TestDriveVehicle car = current.vehicle;
        yield return fader.FadeOut(player.Head, 0.3f);
        player.Seat(car);
        if (car.VisitorBody != null) car.VisitorBody.gameObject.SetActive(true);
        car.SetDoor(false);
        current.dashboard.SetVisible(true);
        yield return null;
        player.Recenter(car);
        yield return fader.FadeIn(player.Head, 0.45f);
        state = State.Seated;
        yield return new WaitForSeconds(0.6f);
        if (state == State.Seated && !car.EngineRunning) car.StartEngine();
        current.dashboard.Toast(player.IsXr ? "Right trigger: go · Left trigger: brake, hold when stopped to reverse" : "W: go · S: brake, hold when stopped to reverse", 6f);
    }

    private IEnumerator ExitCar()
    {
        state = State.Busy;
        TestDriveVehicle car = current.vehicle;
        car.ClearDriverInput();
        car.SetSelector(TestDriveVehicle.Selector.P);
        car.StopEngine();
        car.SetDoor(true);
        yield return new WaitForSeconds(0.55f);
        yield return fader.FadeOut(player.Head, 0.3f);
        current.dashboard.SetVisible(false);
        if (car.VisitorBody != null) car.VisitorBody.gameObject.SetActive(false);
        player.StandAt(car.ExitPoint.position, car.ExitPoint.forward);
        yield return null;
        yield return fader.FadeIn(player.Head, 0.45f);
        car.SetDoor(false);
        state = State.Outside;
    }

    private IEnumerator ReturnToShowroom()
    {
        state = State.Busy;
        yield return fader.FadeOut(player.Head, 0.5f);
        if (current != null && current.vehicle != null)
        {
            current.vehicle.ClearDriverInput();
            if (current.dashboard != null) current.dashboard.SetVisible(false);
            if (current.vehicle.VisitorBody != null) current.vehicle.VisitorBody.gameObject.SetActive(false);
            Transform kiosk = current.kiosk != null ? current.kiosk.transform : current.display;
            Vector3 front = kiosk.position + kiosk.forward * 1.3f;
            player.StandAt(front, -kiosk.forward);
            current.vehicle.gameObject.SetActive(false);
        }
        if (savedFixedStep > 0f) Time.fixedDeltaTime = savedFixedStep;
        PauseShowroom(false);
        if (world != null) world.SetRunning(false);
        if (night != null) night.SetRunning(false, null);
        state = State.Showroom;
        yield return null;
        yield return fader.FadeIn(player.Head, 0.6f);
    }

    // ------------------------------------------------------------------ showroom systems

    /// <summary>The Car Explorer (its controller buttons would act on the hero car from the city) and Remy (he
    /// would follow the visitor out) pause during the drive.</summary>
    private void PauseShowroom(bool pause)
    {
        if (pause)
        {
            paused.Clear();
            foreach (CarExplorerController explorer in FindObjectsByType<CarExplorerController>(FindObjectsSortMode.None))
                if (explorer.enabled) { explorer.enabled = false; paused.Add(explorer); }
            foreach (ShowroomPresenterAgent presenter in FindObjectsByType<ShowroomPresenterAgent>(FindObjectsSortMode.None))
                if (presenter.enabled) { presenter.enabled = false; paused.Add(presenter); }
            return;
        }
        for (int i = 0; i < paused.Count; i++)
            if (paused[i] != null) paused[i].enabled = true;
        paused.Clear();
    }

    private void EnsureEventSystem(bool xr)
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("Test Drive Event System");
        go.transform.SetParent(transform, false);
        go.AddComponent<EventSystem>().sendNavigationEvents = false;
        if (xr) go.AddComponent<XRUIInputModule>();
        else
        {
            InputSystemUIInputModule module = go.AddComponent<InputSystemUIInputModule>();
            module.cursorLockBehavior = InputSystemUIInputModule.CursorLockBehavior.ScreenCenter;
        }
    }
}
