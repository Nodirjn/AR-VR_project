using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Desktop first-person controls. The scene camera pitches under a yaw-only body that carries the
/// CharacterController. The XR rig remains untouched.
///
/// Inspect camera: F, CarExplorerInspect.Request (the HUD) or a double click on a part takes the camera off the
/// body to frame the Car Explorer's selection; it orbits (left drag), pans (right drag) and zooms (wheel) around the
/// node until F, Esc, Tab or a walking key flies it back to the body's eye. The body stays where it was.
///
/// Entrance doors: they open by themselves when the visitor or Remy comes within autoDoorDistance of the doorway,
/// swinging away from whoever opens them, and close autoDoorCloseDelay after the zone is clear; E toggles them.
/// </summary>
[DisallowMultipleComponent]
public sealed class DesktopWalkthroughController : MonoBehaviour
{
    [SerializeField] private float walkingSpeed = 4.2f;
    [SerializeField] private float mouseSensitivity = 0.12f;
    [SerializeField] private float eyeHeight = 1.7f;
    [SerializeField] private float gravity = -22f;
    [SerializeField] private Transform entrance;
    [SerializeField] private float doorSwingSeconds = 1.6f;
    [SerializeField] private float autoDoorDistance = 2.5f;
    [SerializeField] private float autoDoorCloseDelay = 3f;
    [SerializeField] private float inspectFlightSeconds = 0.6f;
    [SerializeField, Range(0.2f, 0.6f)] private float inspectFill = 0.375f;

    private const float DoorAngle = 92f;
    private const float DoorHalfWidth = 1.65f;
    private const float DoubleClickSeconds = 0.4f;
    private const float DoubleClickPixels = 6f;
    private const float DragThresholdPixels = 4f;
    private const float OrbitDegreesPerPixel = 0.3f;
    private const float CameraClearance = 0.15f;
    private const float MinInspectNearClip = 0.02f;

    private enum InspectPhase { Off, Orbit, Return }

    private Transform body;
    private CharacterController character;
    private Vector3 spawnPosition;
    private float spawnYaw;
    private float spawnPitch;
    private float verticalSpeed;
    private float yaw;
    private float pitch;
    private Transform leftDoor;
    private Transform rightDoor;
    private bool doorOpen;
    private float doorSwing;
    // +1 swings the leaves into the showroom (away from a visitor outside), -1 out onto the plaza.
    private float doorSide = 1f;
    private bool visitorInDoorZone;
    private bool presenterInDoorZone;
    private float doorZoneClearSince = float.NegativeInfinity;
    private Transform presenter;
    private static bool active;

    // Inspect camera.
    private Camera viewCamera;
    private InspectPhase inspectPhase;
    private CarStructureNode inspectNode;
    private Bounds inspectBounds;
    private Vector3 orbitPivot;
    private Vector3 orbitPan;
    private float orbitYaw;
    private float orbitPitch;
    private float orbitDistance;
    private float orbitMinDistance;
    private float orbitMaxDistance;
    private float orbitPanLimit;
    private float inspectBlend = 1f;
    private Vector3 flightFromPosition;
    private Quaternion flightFromRotation;
    private Vector3 walkLocalPosition;
    private bool walkCursorLocked;
    private float walkNearClip;
    private bool inspectStateDirty;
    private bool modelChanged;
    private bool modelWasMoving;
    private ICarExplorerModel boundModel;
    private Action modelChangedHandler;
    private Action<CarStructureNode> inspectRequestedHandler;
    private Action exitRequestedHandler;
    private int dragButton;
    private bool dragOverHud;
    private float dragTravel;
    private float lastClickTime = float.NegativeInfinity;
    private Vector2 lastClickPosition;
    private bool doubleClickPending;
    // Large enough that the node's own parts, which a ray from its centre crosses first, cannot crowd out a blocker.
    private readonly RaycastHit[] rayHits = new RaycastHit[128];

    public static bool IsDesktopActive => active && !XRSettings.isDeviceActive;

    private void Awake()
    {
        modelChangedHandler = OnModelChanged;
        inspectRequestedHandler = OnInspectRequested;
        exitRequestedHandler = OnInspectExitRequested;
    }

    private void Start()
    {
        active = !XRSettings.isDeviceActive;
        if (!active) return;

        Camera camera = GetComponent<Camera>();
        if (camera == null) camera = Camera.main;
        if (camera == null) { enabled = false; return; }
        if (camera.transform != transform) { enabled = false; return; }
        viewCamera = camera;

        yaw = transform.eulerAngles.y;
        pitch = transform.eulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        pitch = Mathf.Clamp(pitch, -80f, 80f);
        ShowroomCollisionSetup.Apply();
        CreateBody();
        LockGameplayCursor();
        SetSpawnHeight();
        spawnPosition = body.position;
        spawnYaw = yaw;
        spawnPitch = pitch;
        BuildEntranceDoor();
        ShowroomPresenterAgent agent = FindAnyObjectByType<ShowroomPresenterAgent>();
        if (agent != null) presenter = agent.transform;
    }

    private void OnEnable()
    {
        // Start runs only once; re-arm the walkthrough after a disable/enable cycle.
        if (character != null) active = !XRSettings.isDeviceActive;
        CarExplorerInspect.Requested -= inspectRequestedHandler;
        CarExplorerInspect.Requested += inspectRequestedHandler;
        CarExplorerInspect.ExitRequested -= exitRequestedHandler;
        CarExplorerInspect.ExitRequested += exitRequestedHandler;
    }

    private void OnDisable()
    {
        CarExplorerInspect.Requested -= inspectRequestedHandler;
        CarExplorerInspect.ExitRequested -= exitRequestedHandler;
        StopInspectNow();
        BindModel(null);
        if (active)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        active = false;
    }

    private void Update()
    {
        if (character == null) return;
        if (!IsDesktopActive)
        {
            // A headset display that starts after a desktop start (for example after the XR fallback) takes over
            // the view and these controls stop; the inspect camera goes back and the cursor is handed back.
            StopInspectNow();
            if (active && Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            return;
        }
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        BindModel(CarExplorerModels.Current);

        bool inspecting = inspectPhase == InspectPhase.Orbit;
        if (inspecting)
        {
            UpdateInspectInput(keyboard, mouse);
        }
        else
        {
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else
                {
                    LockGameplayCursor();
                }
            }
            if (keyboard != null && keyboard.fKey.wasPressedThisFrame) BeginInspect(InspectCandidate());
            DetectDoubleClick(mouse);
        }

        if (inspectPhase != InspectPhase.Orbit)
        {
            if (Cursor.lockState == CursorLockMode.Locked && mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw += delta.x * mouseSensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, -80f, 80f);
                if (inspectPhase == InspectPhase.Off) ApplyLook();
            }

            Vector3 movement = Vector3.zero;
            if (keyboard != null)
            {
                Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) movement += forward;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) movement -= forward;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) movement += right;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) movement -= right;
            }
            if (movement.sqrMagnitude > 1f) movement.Normalize();

            if (character.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
            verticalSpeed += gravity * Time.deltaTime;
            character.Move((movement * walkingSpeed + Vector3.up * verticalSpeed) * Time.deltaTime);
            if (body.position.y < ShowroomCollisionSetup.RespawnBelowHeight) Respawn();
        }

        if (keyboard != null && keyboard.eKey.wasPressedThisFrame) ToggleEntranceIfNear();
        UpdateAutomaticDoor();
        SwingEntranceDoor();
        UpdateInspectCamera();
    }

    private void LateUpdate()
    {
        // After every Update of the frame: a double click opens the inspect camera on what the explorer selected
        // for it, and the inspect state is published once, so all scripts saw the same state during the frame.
        if (doubleClickPending)
        {
            doubleClickPending = false;
            if (inspectPhase != InspectPhase.Orbit && boundModel != null) BeginInspect(boundModel.Selected);
        }
        if (inspectStateDirty)
        {
            inspectStateDirty = false;
            CarExplorerInspect.SetState(inspectPhase == InspectPhase.Orbit, inspectNode);
        }
    }

    private void OnDestroy()
    {
        StopInspectNow();
        BindModel(null);
    }

    private static void LockGameplayCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void CreateBody()
    {
        // The capsule stays upright at the feet; only the camera pitches, so looking down keeps the eye height.
        body = new GameObject("Desktop Player Body").transform;
        body.SetPositionAndRotation(transform.position - Vector3.up * eyeHeight, Quaternion.Euler(0f, yaw, 0f));
        transform.SetParent(body, false);
        transform.localPosition = new Vector3(0f, eyeHeight, 0f);
        ApplyLook();

        CharacterController cameraCapsule = GetComponent<CharacterController>();
        if (cameraCapsule != null) Destroy(cameraCapsule);
        character = body.gameObject.AddComponent<CharacterController>();
        character.height = 1.8f;
        character.radius = 0.32f;
        character.center = new Vector3(0f, character.height * 0.5f, 0f);
        character.skinWidth = 0.06f;
        character.stepOffset = 0.32f;
        character.slopeLimit = 48f;
        character.minMoveDistance = 0f;
    }

    private void ApplyLook()
    {
        body.rotation = Quaternion.Euler(0f, yaw, 0f);
        transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void SetSpawnHeight()
    {
        Vector3 p = body.position;
        // Without a floor below, fall back to the showroom floor level.
        p.y = ShowroomCollisionSetup.TryGetFloorHeight(p + Vector3.up * (eyeHeight + 0.25f), 8f, out float floorY)
            ? floorY : -0.05f;
        body.position = p;
        Physics.SyncTransforms();
    }

    private void Respawn()
    {
        verticalSpeed = 0f;
        yaw = spawnYaw;
        pitch = spawnPitch;
        body.position = spawnPosition;
        ApplyLook();
        Physics.SyncTransforms();
    }

    // ---------------------------------------------------------------- entrance doors

    private void BuildEntranceDoor()
    {
        if (GameObject.Find("Showroom Entrance Hinged Doors") != null) return;
        GameObject rootObject = new GameObject("Showroom Entrance Hinged Doors");
        entrance = rootObject.transform;
        entrance.position = new Vector3(1f, 0f, 18f);
        Material frame = FindSceneMaterial("Rear Feature Wall") ?? MakeMaterial(new Color(0.035f, 0.045f, 0.055f), 0.72f);
        Material glass = FindSceneMaterial("Smoked Glass Wall Section 0 0") ?? MakeMaterial(new Color(0.1f, 0.16f, 0.2f), 0.5f);
        Material brass = MakeMaterial(new Color(0.72f, 0.48f, 0.19f), 0.68f);
        CreateDoorLeaf("Left", -1f, 1f, frame, glass, brass);
        CreateDoorLeaf("Right", 1f, -1f, frame, glass, brass);
        CreateCube("Entrance Header", entrance, new Vector3(0f, 3.28f, 0f), new Vector3(3.55f, 0.16f, 0.18f), frame, false);
    }

    private void CreateDoorLeaf(string side, float pivotX, float direction, Material frame, Material glass, Material brass)
    {
        Transform pivot = new GameObject(side + " Door Hinge").transform;
        pivot.SetParent(entrance, false);
        pivot.localPosition = new Vector3(pivotX * DoorHalfWidth, 0f, 0f);
        if (side == "Left") leftDoor = pivot; else rightDoor = pivot;
        float center = direction * 0.825f;
        CreateCube(side + " Door Glass", pivot, new Vector3(center, 1.58f, 0f), new Vector3(1.55f, 3.12f, 0.09f), glass, false);
        CreateCube(side + " Door Stile", pivot, new Vector3(center - direction * 0.75f, 1.58f, -0.07f), new Vector3(0.09f, 3.2f, 0.16f), frame, false);
        CreateCube(side + " Door Stile", pivot, new Vector3(center + direction * 0.75f, 1.58f, -0.07f), new Vector3(0.09f, 3.2f, 0.16f), frame, false);
        CreateCube(side + " Door Rail", pivot, new Vector3(center, 0.04f, -0.07f), new Vector3(1.58f, 0.09f, 0.16f), frame, false);
        CreateCube(side + " Door Rail", pivot, new Vector3(center, 3.12f, -0.07f), new Vector3(1.58f, 0.09f, 0.16f), frame, false);
        CreateCube(side + " Door Handle", pivot, new Vector3(center - direction * 0.17f, 1.05f, -0.14f), new Vector3(0.045f, 0.28f, 0.1f), brass, false);
        // One box over the whole leaf (glass, stiles, rails and handle) on the hinge, so the collider is exactly
        // where the leaf is drawn at every angle of the swing.
        BoxCollider leaf = pivot.gameObject.AddComponent<BoxCollider>();
        leaf.center = new Vector3(center, 1.58f, -0.0725f);
        leaf.size = new Vector3(1.59f, 3.2f, 0.235f);
    }

    private static GameObject CreateCube(string name, Transform parent, Vector3 localPosition, Vector3 size, Material material, bool collide)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = localPosition;
        cube.transform.localScale = size;
        cube.GetComponent<Renderer>().sharedMaterial = material;
        if (!collide) UnityEngine.Object.Destroy(cube.GetComponent<Collider>());
        return cube;
    }

    private static Material FindSceneMaterial(string objectName)
    {
        GameObject candidate = GameObject.Find(objectName);
        Renderer renderer = candidate != null ? candidate.GetComponent<Renderer>() : null;
        return renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial : null;
    }

    private static Material MakeMaterial(Color color, float metallic)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new Material(shader) { color = color };
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.72f);
        return material;
    }

    private void ToggleEntranceIfNear()
    {
        if (entrance == null || Vector3.Distance(EyePosition(), entrance.position) > 4.5f) return;
        if (doorOpen)
        {
            // Held shut until somebody next comes into the zone.
            doorOpen = false;
            return;
        }
        OpenEntrance(body != null ? body.position : transform.position);
        // Opened from outside the zone, the doors still close once nobody has come through for the delay.
        doorZoneClearSince = Time.time;
    }

    // Automatic doors: whoever comes into the zone (the visitor's body, or Remy) opens them; they close once the
    // zone has been clear for autoDoorCloseDelay. Opening reacts only to arrivals, so E can still shut them.
    private void UpdateAutomaticDoor()
    {
        if (entrance == null) return;
        bool visitor = InDoorZone(body.position);
        bool remy = presenter != null && presenter.gameObject.activeInHierarchy && InDoorZone(presenter.position);
        bool visitorArrived = visitor && !visitorInDoorZone;
        bool remyArrived = remy && !presenterInDoorZone;
        visitorInDoorZone = visitor;
        presenterInDoorZone = remy;
        if (visitor || remy) doorZoneClearSince = Time.time;
        if (visitorArrived || remyArrived)
        {
            if (!doorOpen) OpenEntrance(visitorArrived ? body.position : presenter.position);
        }
        else if (doorOpen && !visitor && !remy && Time.time - doorZoneClearSince >= autoDoorCloseDelay)
        {
            doorOpen = false;
        }
    }

    // Within autoDoorDistance of the doorway (the opening between the hinges), on either side of the glass.
    private bool InDoorZone(Vector3 worldPosition)
    {
        Vector3 local = entrance.InverseTransformPoint(worldPosition);
        float along = Mathf.Max(0f, Mathf.Abs(local.x) - DoorHalfWidth);
        return along * along + local.z * local.z <= autoDoorDistance * autoDoorDistance &&
               local.y > -1f && local.y < 4f;
    }

    // Closed leaves swing away from whoever opens them; leaves already ajar reopen the way they are turned.
    private void OpenEntrance(Vector3 from)
    {
        if (doorSwing <= 0f) doorSide = entrance.InverseTransformPoint(from).z >= 0f ? 1f : -1f;
        doorOpen = true;
    }

    // The leaves swing over doorSwingSeconds with an ease-in-out; a reversal mid-swing starts from where they are.
    private void SwingEntranceDoor()
    {
        float target = doorOpen ? 1f : 0f;
        if (Mathf.Approximately(doorSwing, target)) return;
        doorSwing = Mathf.MoveTowards(doorSwing, target, Time.deltaTime / Mathf.Max(0.05f, doorSwingSeconds));
        float angle = doorSide * DoorAngle * Mathf.SmoothStep(0f, 1f, doorSwing);
        if (leftDoor != null) leftDoor.localRotation = Quaternion.Euler(0f, angle, 0f);
        if (rightDoor != null) rightDoor.localRotation = Quaternion.Euler(0f, -angle, 0f);
    }

    private Vector3 EyePosition()
    {
        return body != null ? body.TransformPoint(inspectPhase == InspectPhase.Off ? transform.localPosition : walkLocalPosition)
            : transform.position;
    }

    // ---------------------------------------------------------------- inspect camera

    private void BindModel(ICarExplorerModel model)
    {
        if (model == boundModel) return;
        if (boundModel != null) boundModel.Changed -= modelChangedHandler;
        boundModel = model;
        modelChanged = true;
        modelWasMoving = false;
        if (boundModel != null) boundModel.Changed += modelChangedHandler;
        else if (inspectPhase == InspectPhase.Orbit) EndInspect(false);
    }

    private void OnModelChanged() => modelChanged = true;

    // The HUD's Inspect button; asked again for the node already inspected, it frames it afresh.
    private void OnInspectRequested(CarStructureNode node)
    {
        if (character == null || !IsDesktopActive) return;
        BeginInspect(node);
    }

    private void OnInspectExitRequested()
    {
        if (inspectPhase == InspectPhase.Orbit) EndInspect(false);
    }

    // F frames the selection, else the deepest level that is apart, else the assembly that is out.
    private CarStructureNode InspectCandidate()
    {
        ICarExplorerModel model = CarExplorerModels.Current;
        if (model == null) return null;
        if (model.Selected != null) return model.Selected;
        IReadOnlyList<CarStructureNode> open = model.OpenPath;
        if (open != null && open.Count > 0) return open[open.Count - 1];
        return model.TakenOut;
    }

    private void DetectDoubleClick(Mouse mouse)
    {
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        // Clicks on the HUD are the HUD's (its tree has its own double click).
        if (!locked && CarExplorerModels.PointerOverHud)
        {
            lastClickTime = float.NegativeInfinity;
            return;
        }
        Vector2 at = locked ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) : mouse.position.ReadValue();
        float now = Time.unscaledTime;
        if (now - lastClickTime <= DoubleClickSeconds && (at - lastClickPosition).sqrMagnitude <= DoubleClickPixels * DoubleClickPixels)
        {
            doubleClickPending = true;
            lastClickTime = float.NegativeInfinity;
        }
        else
        {
            lastClickTime = now;
            lastClickPosition = at;
        }
    }

    private bool BeginInspect(CarStructureNode node)
    {
        ICarExplorerModel model = CarExplorerModels.Current;
        if (node == null || model == null || model.IsXr || !IsDesktopActive || body == null || viewCamera == null) return false;
        if (!model.TryGetNodeBounds(node, out Bounds bounds)) return false;
        BindModel(model);

        bool entering = inspectPhase != InspectPhase.Orbit;
        if (inspectPhase == InspectPhase.Off)
        {
            walkLocalPosition = transform.localPosition;
            walkNearClip = viewCamera.nearClipPlane;
        }
        if (entering)
        {
            walkCursorLocked = Cursor.lockState == CursorLockMode.Locked;
            // The cursor stays free while inspecting, so clicks keep selecting and drilling at the pointer.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            dragButton = 0;
        }
        if (inspectNode != node) inspectStateDirty = true;
        if (entering) inspectStateDirty = true;
        inspectNode = node;
        inspectPhase = InspectPhase.Orbit;
        modelChanged = false;
        modelWasMoving = model.IsMoving;
        Frame(bounds, entering);
        StartFlight();
        return true;
    }

    // F, Esc, Tab, a walking key or CarExplorerInspect.RequestExit: the camera flies back to the body's eye.
    private void EndInspect(bool lockCursor)
    {
        if (inspectPhase != InspectPhase.Orbit) return;
        inspectPhase = InspectPhase.Return;
        inspectNode = null;
        inspectStateDirty = true;
        dragButton = 0;
        if (lockCursor || walkCursorLocked) LockGameplayCursor();
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        StartFlight();
    }

    // Without a flight back (XR took over, the walkthrough was switched off): the camera is put back at once.
    private void StopInspectNow()
    {
        if (inspectPhase == InspectPhase.Off) return;
        FinishReturn();
        inspectNode = null;
        inspectStateDirty = false;
        doubleClickPending = false;
        CarExplorerInspect.SetState(false, null);
    }

    private void FinishReturn()
    {
        inspectPhase = InspectPhase.Off;
        inspectBlend = 1f;
        if (body != null)
        {
            transform.localPosition = walkLocalPosition;
            ApplyLook();
        }
        if (viewCamera != null) viewCamera.nearClipPlane = walkNearClip;
    }

    private void StartFlight()
    {
        flightFromPosition = transform.position;
        flightFromRotation = transform.rotation;
        inspectBlend = 0f;
    }

    private void UpdateInspectInput(Keyboard keyboard, Mouse mouse)
    {
        if (keyboard != null)
        {
            if (keyboard.tabKey.wasPressedThisFrame) { EndInspect(true); return; }
            if (keyboard.fKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame ||
                keyboard.wKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame ||
                keyboard.sKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame ||
                keyboard.upArrowKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame ||
                keyboard.leftArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
            {
                EndInspect(false);
                return;
            }
        }

        // The selection, or where its parts will be once they settle, changed: frame it again.
        if (boundModel != null)
        {
            bool moving = boundModel.IsMoving;
            bool settled = modelWasMoving && !moving;
            modelWasMoving = moving;
            if (modelChanged || settled)
            {
                modelChanged = false;
                CheckInspectTarget();
            }
        }

        if (mouse == null) return;
        bool overHud = CarExplorerModels.PointerOverHud;
        if (mouse.leftButton.wasPressedThisFrame) StartDrag(1, overHud);
        else if (mouse.rightButton.wasPressedThisFrame) StartDrag(2, overHud);
        if ((dragButton == 1 && !mouse.leftButton.isPressed) || (dragButton == 2 && !mouse.rightButton.isPressed))
            dragButton = 0;
        Vector2 delta = mouse.delta.ReadValue();
        if (dragButton != 0 && !dragOverHud)
        {
            // A short click stays a click (the explorer selects with it); only a drag moves the view, from the
            // frame it passes the threshold.
            if (dragTravel < DragThresholdPixels) dragTravel += delta.magnitude;
            if (dragTravel >= DragThresholdPixels)
            {
                if (dragButton == 1) Orbit(delta);
                else Pan(delta);
            }
        }
        float scroll = mouse.scroll.ReadValue().y;
        if (!overHud && Mathf.Abs(scroll) > 0.001f) Zoom(Mathf.Clamp(scroll, -3f, 3f));
    }

    private void StartDrag(int button, bool overHud)
    {
        dragButton = button;
        dragOverHud = overHud;
        dragTravel = 0f;
    }

    // A new selection is framed from the current angle; for the same node, only a change of its planned bounds
    // (a level opening or closing, also checked when a move ends) frames it again. Zoom and pan are kept otherwise.
    private void CheckInspectTarget()
    {
        ICarExplorerModel model = boundModel;
        CarStructureNode selected = model.Selected;
        if (selected != null && selected != inspectNode)
        {
            if (model.TryGetNodeBounds(selected, out Bounds next))
            {
                inspectNode = selected;
                inspectStateDirty = true;
                Frame(next, false);
                StartFlight();
            }
            return;
        }
        if (inspectNode == null) return;
        // The node is no longer drawn (put back into the closed car, the view back to normal): back to walking.
        if (!model.TryGetNodeBounds(inspectNode, out Bounds bounds)) { EndInspect(false); return; }
        if ((bounds.center - inspectBounds.center).sqrMagnitude <= 0.0004f &&
            (bounds.size - inspectBounds.size).sqrMagnitude <= 0.0009f) return;
        Frame(bounds, false);
        StartFlight();
    }

    // The node fills inspectFill of the view's height (and stays within the part of the view the HUD leaves free),
    // seen from the visitor's side (entering) or from the current orbit angle, out of walls, furniture, Remy and
    // other parts, and above the floor.
    private void Frame(Bounds bounds, bool fromVisitorSide)
    {
        inspectBounds = bounds;
        orbitPivot = bounds.center;
        orbitPan = Vector3.zero;
        if (fromVisitorSide)
        {
            Vector3 look = bounds.center - transform.position;
            if (look.sqrMagnitude < 0.0001f) look = transform.forward;
            orbitYaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            float elevation = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
            // A three-quarter view from above, as a CAD view would open.
            orbitPitch = Mathf.Clamp(elevation, 12f, 55f);
        }

        float wanted = FramingDistance(bounds, Quaternion.Euler(orbitPitch, orbitYaw, 0f), out float minimum);
        float maximum = Mathf.Min(Mathf.Max(wanted * 3f, wanted + 1f), Mathf.Max(wanted, 10f));
        orbitMinDistance = minimum;
        orbitMaxDistance = maximum;
        orbitPanLimit = Mathf.Max(0.3f, bounds.extents.magnitude * 1.5f);

        // Of a few angles around the visitor's (or the current) view, the first whose line to the node is clear
        // for the whole distance, else the clearest.
        float baseYaw = orbitYaw, basePitch = orbitPitch;
        float bestFree = -1f, bestYaw = baseYaw, bestPitch = basePitch;
        for (int i = 0; i < CandidateAngles.Length; i++)
        {
            float candidateYaw = baseYaw + CandidateAngles[i].x;
            float candidatePitch = ClampPitchAboveFloor(Mathf.Clamp(basePitch + CandidateAngles[i].y, -25f, 85f), wanted);
            Vector3 direction = -(Quaternion.Euler(candidatePitch, candidateYaw, 0f) * Vector3.forward);
            float free = FreeDistance(bounds, direction, wanted);
            if (free > bestFree + 0.05f)
            {
                bestFree = free;
                bestYaw = candidateYaw;
                bestPitch = candidatePitch;
            }
            if (free >= wanted - 0.001f) break;
        }
        orbitYaw = bestYaw;
        orbitPitch = bestPitch;
        orbitDistance = Mathf.Clamp(Mathf.Min(wanted, bestFree), minimum, maximum);
    }

    private static readonly Vector2[] CandidateAngles =
    {
        new Vector2(0f, 0f), new Vector2(25f, 0f), new Vector2(-25f, 0f), new Vector2(0f, 20f),
        new Vector2(50f, 10f), new Vector2(-50f, 10f), new Vector2(80f, 15f), new Vector2(-80f, 15f),
        new Vector2(0f, 40f), new Vector2(120f, 20f), new Vector2(-120f, 20f), new Vector2(180f, 25f)
    };

    // Distance from the node's centre for the framing: its box, seen along the view, fills inspectFill of the view's
    // height (and stays within the part of the view the HUD leaves free); the closest zoom lets it fill 125 %.
    private float FramingDistance(Bounds bounds, Quaternion view, out float minimum)
    {
        Rect free = SafeViewport();
        float byHeight = DistanceForFill(bounds, view, inspectFill, true);
        float byFreeHeight = DistanceForFill(bounds, view, 0.85f * free.height, true);
        float byFreeWidth = DistanceForFill(bounds, view, 0.75f * free.width, false);
        minimum = Mathf.Max(DistanceForFill(bounds, view, 1.25f, true), DistanceForFill(bounds, view, 1.25f, false));
        return Mathf.Max(byHeight, byFreeHeight, byFreeWidth, minimum + 0.02f);
    }

    // The distance at which the box's eight corners, projected, span `fill` of the view's height (or width). The
    // span only shrinks with distance, so a bisection finds it.
    private float DistanceForFill(Bounds bounds, Quaternion view, float fill, bool vertical)
    {
        Vector3 e = bounds.extents;
        Vector3 f = view * Vector3.forward;
        float halfDepth = Mathf.Abs(f.x) * e.x + Mathf.Abs(f.y) * e.y + Mathf.Abs(f.z) * e.z;
        float tanHalf = Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        if (!vertical) tanHalf *= Mathf.Max(0.1f, viewCamera.aspect);
        Vector3 axis = view * (vertical ? Vector3.up : Vector3.right);
        float low = halfDepth + 0.01f, high = Mathf.Max(low * 2f, 60f);
        for (int iteration = 0; iteration < 24; iteration++)
        {
            float distance = 0.5f * (low + high);
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                float depth = distance + Vector3.Dot(corner, f);
                float across = Vector3.Dot(corner, axis) / Mathf.Max(0.001f, depth);
                min = Mathf.Min(min, across);
                max = Mathf.Max(max, across);
            }
            if ((max - min) / (2f * tanHalf) > fill) low = distance;
            else high = distance;
        }
        return high;
    }

    // How far the camera can be from the node's centre along a direction before it meets something that is not
    // the node itself (its parts' boxes lie within its bounds) or the visitor's own body.
    private float FreeDistance(Bounds bounds, Vector3 direction, float wanted)
    {
        Bounds own = bounds;
        own.Expand(0.1f);
        int count = Physics.RaycastNonAlloc(bounds.center, direction, rayHits, wanted + CameraClearance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float free = wanted;
        for (int i = 0; i < count; i++)
        {
            Collider hit = rayHits[i].collider;
            if (hit == null || hit == character) continue;
            Bounds box = hit.bounds;
            if (own.Contains(box.min) && own.Contains(box.max)) continue;
            free = Mathf.Min(free, rayHits[i].distance - CameraClearance);
        }
        return free;
    }

    private float ClampPitchAboveFloor(float candidatePitch, float distance)
    {
        float lowest = body.position.y + 0.35f;
        float needed = (lowest - orbitPivot.y) / Mathf.Max(0.05f, distance);
        if (needed <= -1f) return candidatePitch;
        float minimumPitch = needed >= 1f ? 85f : Mathf.Asin(needed) * Mathf.Rad2Deg;
        return Mathf.Max(candidatePitch, Mathf.Min(85f, minimumPitch));
    }

    private static Rect SafeViewport()
    {
        Rect free = CarExplorerModels.HudSafeViewport;
        if (free.width < 0.2f || free.height < 0.2f) return new Rect(0f, 0f, 1f, 1f);
        return free;
    }

    private void Orbit(Vector2 delta)
    {
        // Dragging turns the node as if held: right turns it to the right, up tilts its top away.
        orbitYaw += delta.x * OrbitDegreesPerPixel;
        orbitPitch = ClampPitchAboveFloor(Mathf.Clamp(orbitPitch - delta.y * OrbitDegreesPerPixel, -25f, 85f), orbitDistance);
    }

    private void Pan(Vector2 delta)
    {
        float tanV = Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float metresPerPixel = 2f * orbitDistance * tanV / Mathf.Max(1f, Screen.height);
        orbitPan -= (transform.right * delta.x + transform.up * delta.y) * metresPerPixel;
        orbitPan = Vector3.ClampMagnitude(orbitPan, orbitPanLimit);
    }

    private void Zoom(float scroll)
    {
        orbitDistance = Mathf.Clamp(orbitDistance * Mathf.Exp(-scroll * 0.15f), orbitMinDistance, orbitMaxDistance);
        orbitPitch = ClampPitchAboveFloor(orbitPitch, orbitDistance);
    }

    private void UpdateInspectCamera()
    {
        if (inspectPhase == InspectPhase.Off) return;
        Vector3 position;
        Quaternion rotation;
        if (inspectPhase == InspectPhase.Orbit)
        {
            Vector3 pivot = orbitPivot + orbitPan;
            position = pivot - Quaternion.Euler(orbitPitch, orbitYaw, 0f) * Vector3.forward * orbitDistance;
            rotation = AimAt(position, pivot, SafeViewport().center);
        }
        else
        {
            position = body.TransformPoint(walkLocalPosition);
            rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        if (inspectBlend < 1f)
        {
            inspectBlend = Mathf.Min(1f, inspectBlend + Time.deltaTime / Mathf.Max(0.05f, inspectFlightSeconds));
            float eased = Mathf.SmoothStep(0f, 1f, inspectBlend);
            position = Vector3.Lerp(flightFromPosition, position, eased);
            rotation = Quaternion.Slerp(flightFromRotation, rotation, eased);
        }
        transform.SetPositionAndRotation(position, rotation);

        if (inspectPhase == InspectPhase.Return && inspectBlend >= 1f)
        {
            FinishReturn();
            return;
        }
        // Close up, the near plane comes in so the node is not cut; far away it is the walking camera's own.
        float surface = Vector3.Distance(position, orbitPivot + orbitPan) - inspectBounds.extents.magnitude;
        float near = Mathf.Clamp(surface * 0.3f, MinInspectNearClip, walkNearClip);
        if (Mathf.Abs(viewCamera.nearClipPlane - near) > near * 0.02f) viewCamera.nearClipPlane = near;
    }

    // The camera's rotation, without roll, that shows the target at a viewport point (the middle of the part of
    // the view the HUD leaves free) rather than at the centre of the screen.
    private Quaternion AimAt(Vector3 eye, Vector3 target, Vector2 viewportPoint)
    {
        Vector3 w = target - eye;
        float length = w.magnitude;
        if (length < 0.0001f) return transform.rotation;
        w /= length;
        float tanV = Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        Vector3 d = new Vector3((2f * viewportPoint.x - 1f) * tanV * Mathf.Max(0.1f, viewCamera.aspect),
            (2f * viewportPoint.y - 1f) * tanV, 1f).normalized;
        // Pitch first: the direction d, pitched down by theta, has the elevation of the target.
        float radius = Mathf.Sqrt(d.y * d.y + d.z * d.z);
        float phi = Mathf.Atan2(d.z, d.y);
        float theta = Mathf.Acos(Mathf.Clamp(w.y / radius, -1f, 1f)) - phi;
        float pitched = d.y * Mathf.Sin(theta) + d.z * Mathf.Cos(theta);
        // Then yaw: the pitched direction turned onto the target's azimuth.
        float yawRadians = Mathf.Atan2(w.x, w.z) - Mathf.Atan2(d.x, pitched);
        return Quaternion.Euler(theta * Mathf.Rad2Deg, yawRadians * Mathf.Rad2Deg, 0f);
    }

    private void OnGUI()
    {
        if (!IsDesktopActive) return;
        // While the cursor is locked, clicks select whatever is under the screen centre.
        if (Cursor.lockState == CursorLockMode.Locked && !CarExplorerHud.HelpShown) DrawCrosshair();
        // The Car Explorer HUD draws the key hint (and a help overlay) while it exists.
        if (CarExplorerHud.DrawsDesktopHint) return;
        GUI.color = Color.white;
        float width = Mathf.Max(0f, Screen.width - 36f);
        GUI.Label(new Rect(18f, Screen.height - 72f, width, 24f), "WASD / arrow keys: walk   Mouse: look   LMB: take a part apart   Esc: free cursor, put the part back   Tab: resume look   E: door");
        GUI.Label(new Rect(18f, Screen.height - 48f, width, 24f), "T: X-ray on / off   O or ] / PageUp: explode   R or [ / PageDown: back   Q: English   U: Uzbek");
    }

    private static void DrawCrosshair()
    {
        float x = Mathf.Round(Screen.width * 0.5f);
        float y = Mathf.Round(Screen.height * 0.5f);
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(x - 9f, y - 2f, 18f, 4f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - 2f, y - 9f, 4f, 18f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(x - 8f, y - 1f, 16f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - 1f, y - 8f, 2f, 16f), Texture2D.whiteTexture);
    }
}
