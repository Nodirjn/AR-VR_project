using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-pass VR/desktop explorer for the existing CarConcept GLB hierarchy.
/// Moves only named, meaningful assemblies; all transforms are restored exactly.
/// </summary>
public sealed class CarExplorerController : MonoBehaviour
{
    [SerializeField] private Transform carRoot;
    [SerializeField] private Camera viewCamera;
    [SerializeField, Min(0.1f)] private float transitionDuration = 0.8f;
    [SerializeField, Min(1f)] private float rayDistance = 20f;

    private sealed class Part
    {
        public Transform Target;
        public string DisplayName;
        public string Description;
        public int Level;
        public Vector3 OriginalPosition;
        public Quaternion OriginalRotation;
        public Vector3 OriginalScale;
        public Vector3 ExplodedPosition;
        public Vector3 FromPosition;
        public Quaternion FromRotation;
        public Vector3 FromScale;
        public Collider SelectionCollider;
        public Renderer[] Renderers;
        public MaterialPropertyBlock[] OriginalBlocks;
    }

    [Serializable]
    private sealed class PartVoiceCue
    {
        public string componentName;
        public AudioClip clip;
    }

    private enum Command { Explode, Restore, Reset }

    [SerializeField] private Transform presenterRoot;
    [SerializeField] private AudioSource presenterAudioSource;
    [SerializeField] private AudioClip greetingClip;
    [SerializeField] private List<PartVoiceCue> partVoiceCues = new List<PartVoiceCue>();
    [SerializeField, Min(0.5f)] private float greetingDistance = 2.8f;

    private readonly List<Part> parts = new List<Part>();
    private readonly HashSet<Transform> registeredParts = new HashSet<Transform>();
    private readonly Dictionary<Collider, Part> partByCollider = new Dictionary<Collider, Part>();
    private readonly Dictionary<Collider, Command> commandByCollider = new Dictionary<Collider, Command>();
    private readonly List<InputAction> actions = new List<InputAction>();

    private InputAction selectAction;
    private InputAction explodeAction;
    private InputAction restoreAction;
    private InputAction resetAction;
    private Transform controlsRoot;
    private Transform infoRoot;
    private TextMesh levelText;
    private TextMesh infoHeading;
    private TextMesh infoBody;
    private Material panelMaterial;
    private Material goldMaterial;
    private Part selectedPart;
    private int currentLevel;
    private int transitionTargetLevel;
    private float transitionT = 1f;
    private float nextGreetingCheck;
    private bool greetingShown;
    private bool infoDismissed;
    private bool ownsAudioSource;
    private const int MaxExplodeLevel = 3;
    private const float HighlightAmount = 0.34f;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        if (carRoot == null)
        {
            GameObject car = GameObject.Find("CarConcept");
            if (car != null)
                carRoot = car.transform;
        }

        if (viewCamera == null)
            viewCamera = Camera.main;

        if (presenterRoot == null)
        {
            GameObject presenter = GameObject.Find("Remy Presenter");
            if (presenter != null)
                presenterRoot = presenter.transform;
        }

        if (presenterAudioSource == null && presenterRoot != null)
            presenterAudioSource = presenterRoot.GetComponent<AudioSource>();

        if (carRoot == null)
        {
            Debug.LogError("[Car Explorer] Could not find the existing CarConcept root.");
            enabled = false;
            return;
        }

        BuildNamedParts();
        if (parts.Count == 0)
        {
            Debug.LogError("[Car Explorer] No supported named car parts were found.");
            enabled = false;
            return;
        }

        BuildPanel();
        BuildInputActions();
        UpdatePanel();
    }

    private void Update()
    {
        if (viewCamera == null)
            viewCamera = Camera.main;

        AnimateParts();
        FollowCamera();
        UpdatePresenterGreeting();

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.oKey.wasPressedThisFrame) Explode();
            if (keyboard.rKey.wasPressedThisFrame) Restore();
            if (keyboard.tKey.wasPressedThisFrame) ResetExplorer();
            if (keyboard.escapeKey.wasPressedThisFrame) DismissInfo();
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && viewCamera != null)
            ProcessRay(viewCamera.ScreenPointToRay(mouse.position.ReadValue()));

        if (selectAction != null && selectAction.WasPressedThisFrame())
            ProcessRay(viewCamera != null
                ? viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : default);

        if (explodeAction != null && explodeAction.WasPressedThisFrame()) Explode();
        if (restoreAction != null && restoreAction.WasPressedThisFrame()) Restore();
        if (resetAction != null && resetAction.WasPressedThisFrame()) ResetExplorer();
    }

    private void BuildNamedParts()
    {
        // Level 1: named exterior assemblies. The car is longitudinal on local Y,
        // lateral on X, and vertical on Z.
        AddPart("Bonnet / hood", "BodyHood",
            "Front hood assembly with its attached lights and trim.", 1, new Vector3(0f, -0.48f, 0.18f));
        AddPart("Left door", "BodyDoorLColor1",
            "Left door assembly with glass, mirror and interior trim.", 1, new Vector3(-0.48f, 0f, 0f));
        AddPart("Right door", "BodyDoorRColor1",
            "Right door assembly with glass, mirror and interior trim.", 1, new Vector3(0.48f, 0f, 0f));
        AddPart("Rear body and hatch assembly", "BodyRearPanelsColor1",
            "Rear body panel group; the model has a rear hatch mesh within this assembly.", 1,
            new Vector3(0f, 0.48f, 0.12f));
        AddPart("Roof panel", "BodyRoofPanel",
            "Separate roof panel above the cabin.", 1, new Vector3(0f, 0f, 0.38f));
        AddPart("Exterior body panels", "BodyPanelsColor2",
            "Separate exterior body panel mesh.", 1, new Vector3(0f, 0f, -0.24f));
        AddPart("Body pillars", "BodyPillars",
            "Structural pillar mesh around the cabin glazing.", 1, new Vector3(0f, 0f, 0.26f));
        AddPart("Windshield", "BodyWindshield",
            "Front windshield glass mesh.", 1, new Vector3(0f, -0.12f, 0.24f));
        AddPart("Front left wheel", "WheelFrontL",
            "Front left wheel assembly, including its rim and brake meshes.", 1,
            new Vector3(-0.46f, -0.08f, 0f));
        AddPart("Front right wheel", "WheelFrontR",
            "Front right wheel assembly, including its rim and brake meshes.", 1,
            new Vector3(0.46f, -0.08f, 0f));
        AddPart("Rear left wheel", "WheelRearL",
            "Rear left wheel assembly, including its rim and brake meshes.", 1,
            new Vector3(-0.46f, 0.08f, 0f));
        AddPart("Rear right wheel", "WheelRearR",
            "Rear right wheel assembly, including its rim and brake meshes.", 1,
            new Vector3(0.46f, 0.08f, 0f));

        // Level 2: available powertrain, axle, braking and cabin components.
        AddPart("Engine", "Engine", "Separate engine mesh in the front engine bay.",
            2, new Vector3(0f, 0f, 0.42f));
        AddPart("Axles", "Axles", "Separate axle assembly; no individual suspension meshes are named.",
            2, new Vector3(0f, 0f, -0.34f));

        AddPart("Front left brake disc", "WheelFrontLBrakeDisc", "Front left brake disc mesh.",
            2, new Vector3(-0.12f, 0f, 0f));
        AddPart("Front left brake pad", "WheelFrontLBrakePad", "Front left brake pad mesh.",
            2, new Vector3(-0.16f, 0f, 0.04f));
        AddPart("Front right brake disc", "WheelFrontRBrakeDisc", "Front right brake disc mesh.",
            2, new Vector3(0.12f, 0f, 0f));
        AddPart("Front right brake pad", "WheelFrontRBrakePad", "Front right brake pad mesh.",
            2, new Vector3(0.16f, 0f, 0.04f));
        AddPart("Rear left brake disc", "WheelRearLBrakeDisc", "Rear left brake disc mesh.",
            2, new Vector3(-0.12f, 0f, 0f));
        AddPart("Rear left brake pad", "WheelRearLBrakePad", "Rear left brake pad mesh.",
            2, new Vector3(-0.16f, 0f, 0.04f));
        AddPart("Rear right brake disc", "WheelRearRBrakeDisc", "Rear right brake disc mesh.",
            2, new Vector3(0.12f, 0f, 0f));
        AddPart("Rear right brake pad", "WheelRearRBrakePad", "Rear right brake pad mesh.",
            2, new Vector3(0.16f, 0f, 0.04f));

        AddPart("Driver seat", "InteriorSeatsColor1", "Separate driver seat mesh.",
            2, new Vector3(-0.12f, 0f, 0.28f));
        AddPart("Passenger seat", "InteriorSeatsColor2", "Separate passenger seat mesh.",
            2, new Vector3(0.12f, 0f, 0.28f));
        AddPart("Seat frame 1", "InteriorSeatsFrame1", "Separate seat frame mesh.",
            2, new Vector3(-0.16f, 0f, -0.12f));
        AddPart("Seat frame 2", "InteriorSeatsFrame2", "Separate seat frame mesh.",
            2, new Vector3(0.16f, 0f, -0.12f));
        AddPart("Dashboard center", "InteriorDashMid", "Central dashboard mesh.",
            2, new Vector3(0f, -0.28f, 0.12f));
        AddPart("Dashboard sides", "InteriorDashSides", "Separate dashboard side meshes.",
            2, new Vector3(0f, -0.30f, 0.08f));
        AddPart("Steering wheel assembly", "InteriorSteeringBase",
            "Steering wheel assembly with separate wheel and emblem meshes.", 2,
            new Vector3(0f, -0.20f, 0.16f));
        AddPart("Steering dash", "InteriorSteeringDash", "Steering instrument panel mesh.",
            2, new Vector3(0f, -0.26f, 0.12f));
        AddPart("Steering column", "InteriorSteeringDashColumn", "Separate steering column mesh.",
            2, new Vector3(0f, -0.18f, -0.12f));
        AddPart("Accelerator pedal", "InteriorPedalAccel", "Accelerator pedal mesh.",
            2, new Vector3(0f, -0.10f, -0.08f));
        AddPart("Accelerator pedal arm", "InteriorPedalAccelArm", "Accelerator pedal arm mesh.",
            2, new Vector3(0f, -0.10f, -0.12f));
        AddPart("Brake pedal", "InteriorPedalBrake", "Brake pedal mesh.",
            2, new Vector3(0f, -0.10f, -0.08f));
        AddPart("Brake pedal arm", "InteriorPedalBrakeArm", "Brake pedal arm mesh.",
            2, new Vector3(0f, -0.10f, -0.12f));
        AddPart("Cabin floor", "InteriorFloor", "Interior floor mesh.",
            2, new Vector3(0f, 0f, -0.22f));
        AddPart("Floor mats", "InteriorFloormats", "Separate interior floor mat meshes.",
            2, new Vector3(0f, 0f, -0.18f));
        AddPart("Interior cage", "InteriorCage", "Cabin structural cage mesh.",
            2, new Vector3(0f, 0f, 0.30f));
        AddPart("Interior center section", "InteriorMid", "Interior center section mesh.",
            2, new Vector3(0f, 0.20f, 0.12f));
        AddPart("Interior pillar", "InteriorPillar", "Interior pillar trim mesh.",
            2, new Vector3(0f, 0f, 0.22f));

        // Level 3: individually named small trim and mechanism meshes. No bolt/nut
        // objects are named in the source hierarchy, so none are invented here.
        AddSmall("Front left wheel rim", "WheelFrontLRim", new Vector3(-0.10f, 0f, 0f));
        AddSmall("Front right wheel rim", "WheelFrontRRim", new Vector3(0.10f, 0f, 0f));
        AddSmall("Rear left wheel rim", "WheelRearLRim", new Vector3(-0.10f, 0f, 0f));
        AddSmall("Rear right wheel rim", "WheelRearRRim", new Vector3(0.10f, 0f, 0f));
        AddSmall("Hood grille", "BodyHoodTopgrill", new Vector3(0f, -0.08f, 0.08f));
        AddSmall("Hood inner panel 1", "BodyHoodInterior01", new Vector3(0f, 0f, 0.08f));
        AddSmall("Hood inner panel 2", "BodyHoodInterior02", new Vector3(0f, 0f, -0.08f));
        AddSmall("Hood underside", "BodyHoodUnder", new Vector3(0f, 0f, -0.10f));
        AddSmall("Left door outer trim", "BodyDoorLColor2", new Vector3(-0.10f, 0f, 0f));
        AddSmall("Left door handle 1", "BodyDoorLHandle01", new Vector3(-0.08f, 0f, 0f));
        AddSmall("Left door handle 2", "BodyDoorLHandle02", new Vector3(-0.08f, 0f, 0.02f));
        AddSmall("Left mirror", "BodyDoorLMirror", new Vector3(-0.12f, 0f, 0.02f));
        AddSmall("Left mirror trim", "BodyDoorLMirrorColor1", new Vector3(-0.12f, 0f, 0f));
        AddSmall("Left mirror accent", "BodyDoorLMirrorColor2", new Vector3(-0.12f, 0f, 0.02f));
        AddSmall("Left door window", "BodyDoorLWindow", new Vector3(0f, 0f, 0.08f));
        AddSmall("Left door window gasket", "BodyDoorLWindowGasket", new Vector3(0f, 0f, 0.08f));
        AddSmall("Right door outer trim", "BodyDoorRColor2", new Vector3(0.10f, 0f, 0f));
        AddSmall("Right door handle 1", "BodyDoorRHandle01", new Vector3(0.08f, 0f, 0f));
        AddSmall("Right door handle 2", "BodyDoorRHandle02", new Vector3(0.08f, 0f, 0.02f));
        AddSmall("Right mirror", "BodyDoorRMirror", new Vector3(0.12f, 0f, 0.02f));
        AddSmall("Right mirror trim", "BodyDoorRMirrorColor1", new Vector3(0.12f, 0f, 0f));
        AddSmall("Right mirror accent", "BodyDoorRMirrorColor2", new Vector3(0.12f, 0f, 0.02f));
        AddSmall("Right door window", "BodyDoorRWindow", new Vector3(0f, 0f, 0.08f));
        AddSmall("Right door window gasket", "BodyDoorRWindowGasket", new Vector3(0f, 0f, 0.08f));
        AddSmall("Windshield wipers", "BodyWindshieldWipers", new Vector3(0f, -0.10f, 0.06f));
        AddSmall("Wiper base", "BodyWindshieldWipersBase", new Vector3(0f, -0.10f, 0.05f));
        AddSmall("Windshield gasket", "BodyWindshieldGasket", new Vector3(0f, -0.08f, 0.06f));
        AddSmall("Rear windshield", "BodyRearwindow", new Vector3(0f, 0.08f, 0.06f));
        AddSmall("Rear side windows", "BodyWindowsRearSides", new Vector3(0f, 0.08f, 0.06f));
        AddSmall("Rear lights", "BodyTaillights", new Vector3(0f, 0.08f, 0.06f));
        AddSmall("Rear light panels", "BodyTaillightsPanels", new Vector3(0f, 0.08f, 0.04f));
        AddSmall("Rear turn signals", "BodyTurnsignalsRear", new Vector3(0f, 0.08f, 0.04f));
        AddSmall("License plate", "License Plate", new Vector3(0f, 0.08f, 0f));
        AddSmall("Steering wheel emblem", "InteriorSteeringEmblem", new Vector3(0f, 0f, 0.04f));
        AddSmall("Steering wheel section 1", "InteriorSteeringWheel01", new Vector3(-0.06f, 0f, 0f));
        AddSmall("Steering wheel section 2", "InteriorSteeringWheel02", new Vector3(0.06f, 0f, 0f));
        AddSmall("Steering wheel section 3", "InteriorSteeringWheel03", new Vector3(0f, 0f, 0.06f));
        AddSmall("Steering wheel section 4", "InteriorSteeringWheel04", new Vector3(0f, 0f, -0.06f));
    }

    private void AddSmall(string displayName, string nodeName, Vector3 offset)
    {
        AddPart(displayName, nodeName, "Separate named trim or detail mesh.", 3, offset);
    }

    private void AddPart(string displayName, string nodeName, string description,
        int level, Vector3 offset)
    {
        Transform target = FindPartTransform(carRoot, nodeName);
        if (target == null || !registeredParts.Add(target))
            return;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        Part part = new Part
        {
            Target = target,
            DisplayName = displayName,
            Description = description,
            Level = level,
            OriginalPosition = target.localPosition,
            OriginalRotation = target.localRotation,
            OriginalScale = target.localScale,
            ExplodedPosition = target.localPosition + offset,
            Renderers = renderers,
            OriginalBlocks = new MaterialPropertyBlock[renderers.Length]
        };

        for (int i = 0; i < renderers.Length; i++)
        {
            part.OriginalBlocks[i] = new MaterialPropertyBlock();
            renderers[i].GetPropertyBlock(part.OriginalBlocks[i]);
        }

        part.SelectionCollider = CreateBoundsCollider(target, renderers);
        if (part.SelectionCollider != null)
            partByCollider[part.SelectionCollider] = part;

        parts.Add(part);
    }

    private static Transform FindPartTransform(Transform parent, string nodeName)
    {
        if (parent == null)
            return null;
        if (parent.name == nodeName)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindPartTransform(parent.GetChild(i), nodeName);
            if (found != null)
                return found;
        }
        return null;
    }

    private static Collider CreateBoundsCollider(Transform target, Renderer[] renderers)
    {
        bool hasBounds = false;
        Vector3 min = Vector3.zero;
        Vector3 max = Vector3.zero;

        foreach (Renderer renderer in renderers)
        {
            Bounds bounds = renderer.bounds;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 worldCorner = new Vector3(
                    x == 0 ? bounds.min.x : bounds.max.x,
                    y == 0 ? bounds.min.y : bounds.max.y,
                    z == 0 ? bounds.min.z : bounds.max.z);
                Vector3 localCorner = target.InverseTransformPoint(worldCorner);
                if (!hasBounds)
                {
                    min = max = localCorner;
                    hasBounds = true;
                }
                else
                {
                    min = Vector3.Min(min, localCorner);
                    max = Vector3.Max(max, localCorner);
                }
            }
        }

        if (!hasBounds)
            return null;

        BoxCollider collider = target.GetComponent<BoxCollider>();
        if (collider == null)
            collider = target.gameObject.AddComponent<BoxCollider>();
        collider.center = (min + max) * 0.5f;
        collider.size = Vector3.Max(max - min, Vector3.one * 0.025f);
        collider.isTrigger = false;
        return collider;
    }

    private void BuildInputActions()
    {
        selectAction = CreateAction("Car Explorer Select", InputActionType.Button,
            "<XRController>{RightHand}/trigger");
        explodeAction = CreateAction("Car Explorer Explode", InputActionType.Button,
            "<XRController>{RightHand}/primaryButton");
        restoreAction = CreateAction("Car Explorer Restore", InputActionType.Button,
            "<XRController>{RightHand}/secondaryButton");
        resetAction = CreateAction("Car Explorer Reset", InputActionType.Button,
            "<XRController>{LeftHand}/menuButton");

        selectAction.Enable();
        explodeAction.Enable();
        restoreAction.Enable();
        resetAction.Enable();
    }

    private InputAction CreateAction(string actionName, InputActionType type, string binding)
    {
        InputAction action = new InputAction(actionName, type, binding);
        actions.Add(action);
        return action;
    }

    private void BuildPanel()
    {
        if (controlsRoot != null || transform.Find("Car Explorer Controls") != null)
            return;

        controlsRoot = new GameObject("Car Explorer Controls").transform;
        controlsRoot.SetParent(transform, false);
        infoRoot = new GameObject("Car Explorer Part Information").transform;
        infoRoot.SetParent(transform, false);

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");

        panelMaterial = CreateUiMaterial(shader, new Color(0.025f, 0.035f, 0.045f, 1f));
        goldMaterial = CreateUiMaterial(shader, new Color(0.88f, 0.58f, 0.22f, 1f));

        CreateFrame(controlsRoot, "Controls frame", 0.52f, 0.94f);
        CreateText(controlsRoot, "Title", "CAR EXPLORER", new Vector3(0f, 0.32f, 0f),
            0.0085f, new Color(0.94f, 0.72f, 0.39f, 1f), TextAlignment.Center);
        levelText = CreateText(controlsRoot, "Exploded level", "ASSEMBLED  |  LEVEL 0 / 3",
            new Vector3(0f, 0.245f, 0f), 0.0042f, Color.white, TextAlignment.Center);

        CreateCommandCard("EXPLODE", "Advance one layer", Command.Explode, new Vector3(0f, 0.135f, 0f));
        CreateCommandCard("RESTORE", "Return to assembled", Command.Restore, new Vector3(0f, -0.035f, 0f));
        CreateCommandCard("RESET", "Clear selection", Command.Reset, new Vector3(0f, -0.205f, 0f));

        CreateText(controlsRoot, "Keyboard Help", "O  EXPLODE     R  RESTORE     T  RESET",
            new Vector3(0f, -0.345f, 0f), 0.0038f, new Color(0.84f, 0.87f, 0.90f, 1f),
            TextAlignment.Center);
        CreateText(controlsRoot, "Controller Help", "Right trigger: select\nA: next level   B: restore\nLeft menu: reset",
            new Vector3(0f, -0.40f, 0f), 0.0032f, new Color(0.72f, 0.77f, 0.82f, 1f),
            TextAlignment.Center);

        CreateFrame(infoRoot, "Information frame", 0.50f, 0.37f);
        infoHeading = CreateText(infoRoot, "Information heading", "PART INFORMATION",
            new Vector3(0f, 0.115f, 0f), 0.0055f, new Color(0.94f, 0.72f, 0.39f, 1f),
            TextAlignment.Center);
        infoBody = CreateText(infoRoot, "Information text",
            "Choose a named car component to see its purpose.",
            new Vector3(0f, -0.035f, 0f), 0.0060f, Color.white, TextAlignment.Center);
    }

    private Material CreateUiMaterial(Shader shader, Color color)
    {
        Material material = new Material(shader);
        if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, color);
        if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);
        material.color = color;
        return material;
    }

    private void CreateCommandCard(string label, string hint, Command command, Vector3 localPosition)
    {
        const float width = 0.42f;
        const float height = 0.14f;
        GameObject card = new GameObject(label + " Card");
        card.transform.SetParent(controlsRoot, false);
        card.transform.localPosition = localPosition;

        CreateBox(card.transform, label + " Card Background", new Vector3(0f, 0f, 0.025f),
            new Vector3(width, height, 0.025f), panelMaterial);
        CreateCardBorder(card.transform, width, height);

        BoxCollider collider = card.AddComponent<BoxCollider>();
        collider.size = new Vector3(width, height, 0.10f);
        commandByCollider[collider] = command;

        CreateText(card.transform, label + " Label", label, new Vector3(0f, 0.023f, 0f),
            0.0060f, Color.white, TextAlignment.Center);
        CreateText(card.transform, label + " Hint", hint, new Vector3(0f, -0.030f, 0f),
            0.0042f, new Color(0.82f, 0.84f, 0.87f, 1f), TextAlignment.Center);
    }

    private void CreateFrame(Transform parent, string name, float width, float height)
    {
        CreateBox(parent, name + " Background", new Vector3(0f, 0f, 0.025f),
            new Vector3(width, height, 0.025f), panelMaterial);
        CreateCardBorder(parent, width, height);
    }

    private void CreateCardBorder(Transform parent, float width, float height)
    {
        const float thickness = 0.005f;
        CreateBox(parent, "Gold Border Top", new Vector3(0f, height * 0.5f, 0.008f),
            new Vector3(width, thickness, 0.012f), goldMaterial);
        CreateBox(parent, "Gold Border Bottom", new Vector3(0f, -height * 0.5f, 0.008f),
            new Vector3(width, thickness, 0.012f), goldMaterial);
        CreateBox(parent, "Gold Border Left", new Vector3(-width * 0.5f, 0f, 0.008f),
            new Vector3(thickness, height, 0.012f), goldMaterial);
        CreateBox(parent, "Gold Border Right", new Vector3(width * 0.5f, 0f, 0.008f),
            new Vector3(thickness, height, 0.012f), goldMaterial);
    }

    private static void CreateBox(Transform parent, string objectName, Vector3 localPosition,
        Vector3 localScale, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = objectName;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPosition;
        box.transform.localScale = localScale;
        Collider collider = box.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }

        MeshRenderer renderer = box.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    private TextMesh CreateText(Transform parent, string objectName, string text,
        Vector3 localPosition, float characterSize, Color color, TextAlignment alignment)
    {
        GameObject label = new GameObject(objectName);
        label.transform.SetParent(parent, false);
        label.transform.localPosition = localPosition;

        TextMesh mesh = label.AddComponent<TextMesh>();
        mesh.text = WrapText(text, 29);
        mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        mesh.fontSize = 48;
        mesh.characterSize = characterSize;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = alignment;
        mesh.color = color;
        mesh.richText = false;
        return mesh;
    }

    private static string WrapText(string value, int maxCharacters)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        string[] paragraphs = value.Split('\n');
        List<string> output = new List<string>();
        foreach (string paragraph in paragraphs)
        {
            string[] words = paragraph.Split(' ');
            string line = "";
            foreach (string word in words)
            {
                if (line.Length > 0 && line.Length + word.Length + 1 > maxCharacters)
                {
                    output.Add(line);
                    line = word;
                }
                else
                {
                    line = line.Length == 0 ? word : line + " " + word;
                }
            }
            if (line.Length > 0)
                output.Add(line);
        }
        return string.Join("\n", output);
    }

    private void FollowCamera()
    {
        if (viewCamera == null)
            return;

        Transform cameraTransform = viewCamera.transform;
        if (controlsRoot != null)
        {
            controlsRoot.position = cameraTransform.position +
                                    cameraTransform.forward * 1.65f -
                                    cameraTransform.right * 0.75f +
                                    cameraTransform.up * 0.16f;
            controlsRoot.rotation = cameraTransform.rotation;
        }

        if (infoRoot != null)
        {
            infoRoot.position = cameraTransform.position +
                                cameraTransform.forward * 1.72f +
                                cameraTransform.right * 0.75f +
                                cameraTransform.up * 0.42f;
            infoRoot.rotation = cameraTransform.rotation;
        }
    }

    private void UpdatePresenterGreeting()
    {
        if (greetingShown || presenterRoot == null || viewCamera == null ||
            Time.unscaledTime < nextGreetingCheck)
            return;

        nextGreetingCheck = Time.unscaledTime + 0.25f;
        if (Vector3.Distance(viewCamera.transform.position, presenterRoot.position) > greetingDistance)
            return;

        greetingShown = true;
        infoDismissed = false;
        infoHeading.text = "WELCOME";
        infoBody.text = WrapText("Welcome to the showroom. Select a car component to learn more.", 22);
        if (greetingClip != null)
            PlayVoice(greetingClip);
    }

    private void PlayVoice(AudioClip clip)
    {
        if (clip == null || presenterRoot == null)
            return;
        if (presenterAudioSource == null)
            presenterAudioSource = presenterRoot.GetComponent<AudioSource>();
        if (presenterAudioSource == null)
        {
            presenterAudioSource = presenterRoot.gameObject.AddComponent<AudioSource>();
            presenterAudioSource.playOnAwake = false;
            presenterAudioSource.spatialBlend = 1f;
            presenterAudioSource.minDistance = 1f;
            presenterAudioSource.maxDistance = 8f;
            ownsAudioSource = true;
        }

        presenterAudioSource.Stop();
        presenterAudioSource.clip = clip;
        presenterAudioSource.Play();
    }

    private void StopVoice()
    {
        if (presenterAudioSource != null)
            presenterAudioSource.Stop();
    }

    private void DismissInfo()
    {
        ClearHighlight(selectedPart);
        selectedPart = null;
        StopVoice();
        infoDismissed = true;
        if (infoRoot != null)
            infoRoot.gameObject.SetActive(false);
    }

    private string GetPartVoiceName(Part part)
    {
        if (part == null || partVoiceCues == null)
            return null;
        foreach (PartVoiceCue cue in partVoiceCues)
        {
            if (cue != null && cue.componentName == part.DisplayName)
                return cue.clip != null ? cue.clip.name : null;
        }
        return null;
    }

    private void ProcessRay(Ray ray)
    {
        if (ray.direction.sqrMagnitude < 0.5f)
            return;

        // Showroom props and in-scene screens may have their own colliders.
        // Skip those and select the nearest hit that belongs to an explorer part
        // or one of this panel's command buttons.
        RaycastHit[] hits = Physics.RaycastAll(ray, rayDistance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (commandByCollider.TryGetValue(hit.collider, out Command command))
            {
                RunCommand(command);
                return;
            }

            if (partByCollider.TryGetValue(hit.collider, out Part part))
            {
                SelectPart(part);
                return;
            }
        }

        SelectPart(null);
    }

    private void RunCommand(Command command)
    {
        switch (command)
        {
            case Command.Explode: Explode(); break;
            case Command.Restore: Restore(); break;
            case Command.Reset: ResetExplorer(); break;
        }
    }

    public void Explode()
    {
        if (currentLevel >= MaxExplodeLevel)
            return;
        BeginTransition(currentLevel + 1);
    }

    public void Restore()
    {
        BeginTransition(0);
    }

    public void ResetExplorer()
    {
        BeginTransition(0);
        SelectPart(null);
        StopVoice();
        infoDismissed = false;
        UpdatePanel();
    }

    private void BeginTransition(int targetLevel)
    {
        transitionTargetLevel = Mathf.Clamp(targetLevel, 0, MaxExplodeLevel);
        transitionT = 0f;
        foreach (Part part in parts)
        {
            part.FromPosition = part.Target.localPosition;
            part.FromRotation = part.Target.localRotation;
            part.FromScale = part.Target.localScale;
        }
    }

    private void AnimateParts()
    {
        if (transitionT >= 1f)
            return;

        transitionT = Mathf.Min(1f, transitionT + Time.deltaTime / Mathf.Max(0.1f, transitionDuration));
        float eased = transitionT * transitionT * (3f - 2f * transitionT);

        foreach (Part part in parts)
        {
            Vector3 targetPosition = transitionTargetLevel >= part.Level
                ? part.ExplodedPosition
                : part.OriginalPosition;
            part.Target.localPosition = Vector3.LerpUnclamped(part.FromPosition, targetPosition, eased);
            part.Target.localRotation = Quaternion.SlerpUnclamped(part.FromRotation, part.OriginalRotation, eased);
            part.Target.localScale = Vector3.LerpUnclamped(part.FromScale, part.OriginalScale, eased);
        }

        if (transitionT >= 1f)
        {
            currentLevel = transitionTargetLevel;
            if (currentLevel == 0)
                RestoreExactTransforms();
            UpdatePanel();
        }
    }

    private void RestoreExactTransforms()
    {
        foreach (Part part in parts)
        {
            part.Target.localPosition = part.OriginalPosition;
            part.Target.localRotation = part.OriginalRotation;
            part.Target.localScale = part.OriginalScale;
        }
    }

    private void SelectPart(Part part)
    {
        if (selectedPart == part)
            return;

        ClearHighlight(selectedPart);
        selectedPart = part;
        StopVoice();
        infoDismissed = false;
        if (infoRoot != null)
            infoRoot.gameObject.SetActive(true);
        ApplyHighlight(selectedPart);
        UpdatePanel();

        if (selectedPart != null && partVoiceCues != null)
        {
            foreach (PartVoiceCue cue in partVoiceCues)
            {
                if (cue != null && cue.componentName == selectedPart.DisplayName && cue.clip != null)
                {
                    PlayVoice(cue.clip);
                    break;
                }
            }
        }
    }

    private void ApplyHighlight(Part part)
    {
        if (part == null)
            return;

        Color highlight = Color.Lerp(Color.white, new Color(0.95f, 0.61f, 0.2f, 1f), HighlightAmount);
        foreach (Renderer renderer in part.Renderers)
        {
            if (renderer == null)
                continue;

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, highlight);
            block.SetColor(ColorId, highlight);
            renderer.SetPropertyBlock(block);
        }
    }

    private void ClearHighlight(Part part)
    {
        if (part == null)
            return;

        for (int i = 0; i < part.Renderers.Length; i++)
        {
            Renderer renderer = part.Renderers[i];
            if (renderer != null)
                renderer.SetPropertyBlock(part.OriginalBlocks[i]);
        }
    }

    private void UpdatePanel()
    {
        if (levelText != null)
            levelText.text = currentLevel == 0
                ? "ASSEMBLED  |  LEVEL 0 / 3"
                : "EXPLODED  |  LEVEL " + currentLevel + " / 3";

        if (infoHeading == null || infoBody == null || infoDismissed)
            return;

        if (selectedPart != null)
        {
            infoHeading.text = selectedPart.DisplayName.ToUpperInvariant();
            infoBody.text = WrapText(selectedPart.Description, 22);
        }
        else if (greetingShown)
        {
            infoHeading.text = "WELCOME";
            infoBody.text = WrapText("Welcome to the showroom. Select a car component to learn more.", 22);
        }
        else
        {
            infoHeading.text = "PART INFORMATION";
            infoBody.text = WrapText("Choose a named car component to see its purpose.", 22);
        }
    }

    private void OnDestroy()
    {
        foreach (InputAction action in actions)
        {
            action.Disable();
            action.Dispose();
        }
        actions.Clear();

        StopVoice();
        if (ownsAudioSource && presenterAudioSource != null)
            Destroy(presenterAudioSource);
        if (panelMaterial != null)
            Destroy(panelMaterial);
        if (goldMaterial != null)
            Destroy(goldMaterial);
    }
}