using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>Desktop first-person controls for the existing scene camera. The XR rig remains untouched.</summary>
[DisallowMultipleComponent]
public sealed class DesktopWalkthroughController : MonoBehaviour
{
    [SerializeField] private float walkingSpeed = 4.2f;
    [SerializeField] private float mouseSensitivity = 0.12f;
    [SerializeField] private float eyeHeight = 1.7f;
    [SerializeField] private float gravity = -22f;
    [SerializeField] private Transform entrance;

    private CharacterController character;
    private float verticalSpeed;
    private float yaw;
    private float pitch;
    private Transform leftDoor;
    private Transform rightDoor;
    private bool doorOpen;
    private static bool active;

    public static bool IsDesktopActive => active && !XRSettings.isDeviceActive;

    private void Start()
    {
        active = !XRSettings.isDeviceActive;
        if (!active) return;

        Camera camera = GetComponent<Camera>();
        if (camera == null) camera = Camera.main;
        if (camera == null) { enabled = false; return; }
        if (camera.transform != transform) { enabled = false; return; }

        character = GetComponent<CharacterController>();
        if (character == null) character = gameObject.AddComponent<CharacterController>();
        character.height = 1.8f;
        character.radius = 0.32f;
        character.center = new Vector3(0f, -eyeHeight + character.height * 0.5f, 0f);
        character.skinWidth = 0.06f;
        character.stepOffset = 0.32f;
        character.slopeLimit = 48f;
        character.minMoveDistance = 0f;

        yaw = transform.eulerAngles.y;
        pitch = transform.eulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        pitch = Mathf.Clamp(pitch, -80f, 80f);
        LockGameplayCursor();
        SetSpawnHeight();
        AddArchitectureCollision();
        AddExteriorGround();
        BuildEntranceDoor();
    }

    private void OnDisable()
    {
        if (active)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        active = false;
    }

    private void Update()
    {
        if (!IsDesktopActive || character == null) return;
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

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

        if (Cursor.lockState == CursorLockMode.Locked && mouse != null)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, -80f, 80f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        Vector3 movement = Vector3.zero;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) movement += forward;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) movement -= forward;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) movement += right;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) movement -= right;
        }
        if (movement.sqrMagnitude > 1f) movement.Normalize();

        if (character.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
        verticalSpeed += gravity * Time.deltaTime;
        character.Move((movement * walkingSpeed + Vector3.up * verticalSpeed) * Time.deltaTime);

        if (keyboard != null && keyboard.eKey.wasPressedThisFrame) ToggleEntranceIfNear();
    }

    private static void LockGameplayCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void SetSpawnHeight()
    {
        Vector3 origin = transform.position + Vector3.up * 0.25f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 8f, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        float floorY = float.NegativeInfinity;
        foreach (RaycastHit hit in hits)
            if (hit.collider != null && hit.collider.GetComponent<CharacterController>() == null)
                floorY = Mathf.Max(floorY, hit.point.y);
        Vector3 p = transform.position;
        p.y = float.IsNegativeInfinity(floorY) ? 1.65f : floorY + eyeHeight;
        transform.position = p;
    }

    private static void AddArchitectureCollision()
    {
        GameObject architecture = GameObject.Find("Showroom Architecture");
        if (architecture == null) return;
        MeshFilter[] meshes = architecture.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter filter in meshes)
        {
            if (filter == null || filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
            Renderer renderer = filter.GetComponent<Renderer>();
            if (renderer == null || !renderer.enabled) continue;
            Bounds b = renderer.bounds;
            if (b.size.y < 1.15f || (b.size.x < 1.4f && b.size.z < 1.4f)) continue;
            MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = false;
        }
    }

    private static void AddExteriorGround()
    {
        if (GameObject.Find("Desktop Walkable Exterior Ground") != null) return;
        GameObject ground = new GameObject("Desktop Walkable Exterior Ground");
        ground.transform.position = new Vector3(1f, -0.16f, 0f);
        BoxCollider collider = ground.AddComponent<BoxCollider>();
        collider.size = new Vector3(100f, 0.22f, 100f);
    }

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
        pivot.localPosition = new Vector3(pivotX * 1.65f, 0f, 0f);
        if (side == "Left") leftDoor = pivot; else rightDoor = pivot;
        float center = direction * 0.825f;
        CreateCube(side + " Door Glass", pivot, new Vector3(center, 1.58f, 0f), new Vector3(1.55f, 3.12f, 0.09f), glass, true);
        CreateCube(side + " Door Stile", pivot, new Vector3(center - direction * 0.75f, 1.58f, -0.07f), new Vector3(0.09f, 3.2f, 0.16f), frame, false);
        CreateCube(side + " Door Stile", pivot, new Vector3(center + direction * 0.75f, 1.58f, -0.07f), new Vector3(0.09f, 3.2f, 0.16f), frame, false);
        CreateCube(side + " Door Rail", pivot, new Vector3(center, 0.04f, -0.07f), new Vector3(1.58f, 0.09f, 0.16f), frame, false);
        CreateCube(side + " Door Rail", pivot, new Vector3(center, 3.12f, -0.07f), new Vector3(1.58f, 0.09f, 0.16f), frame, false);
        CreateCube(side + " Door Handle", pivot, new Vector3(center - direction * 0.17f, 1.05f, -0.14f), new Vector3(0.045f, 0.28f, 0.1f), brass, false);
    }

    private static GameObject CreateCube(string name, Transform parent, Vector3 localPosition, Vector3 size, Material material, bool collide)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = localPosition;
        cube.transform.localScale = size;
        cube.GetComponent<Renderer>().sharedMaterial = material;
        if (!collide) Object.Destroy(cube.GetComponent<Collider>());
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
        if (entrance == null || Vector3.Distance(transform.position, entrance.position) > 4.5f) return;
        doorOpen = !doorOpen;
        if (leftDoor != null) leftDoor.localRotation = Quaternion.Euler(0f, doorOpen ? 92f : 0f, 0f);
        if (rightDoor != null) rightDoor.localRotation = Quaternion.Euler(0f, doorOpen ? -92f : 0f, 0f);
    }

    private void OnGUI()
    {
        if (!IsDesktopActive) return;
        GUI.color = Color.white;
        GUI.Label(new Rect(18f, Screen.height - 72f, 680f, 24f), "WASD: walk   Mouse: look   Esc: cursor   Tab: resume look   E: door   O / R / T: explore / restore / reset");
        GUI.Label(new Rect(18f, Screen.height - 48f, 480f, 24f), "[ / ] or arrow keys: previous / next stage   Q: English   U: Uzbek");
    }
}
