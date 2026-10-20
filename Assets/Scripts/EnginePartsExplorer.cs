using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using Unity.XR.CoreUtils;

/// <summary>
/// Runtime anatomy explorer for the imported V6 assembly. It operates on the
/// existing GameObjects and meshes; it never creates or combines component geometry.
/// </summary>
public sealed class EnginePartsExplorer : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float transitionDuration = 0.7f;
    [SerializeField, Min(1f)] private float rayDistance = 20f;

    private enum Command { Explode, Restore, Reset, ToggleHood }

    private sealed class SelectablePart
    {
        public Transform Target;
        public Renderer Renderer;
        public Collider Collider;
        public MaterialPropertyBlock OriginalBlock;
        public bool OwnsCollider;
    }

    private sealed class MoveTrack
    {
        public Transform Target;
        public Vector3 OriginalPosition;
        public Quaternion OriginalRotation;
        public Vector3 OriginalScale;
        public readonly Vector3[] StepOffsets = new Vector3[4];
        public readonly bool[] HasStep = new bool[4];
        public Vector3 FromPosition;
        public Quaternion FromRotation;
        public Vector3 FromScale;
    }

    private readonly List<SelectablePart> selectableParts = new List<SelectablePart>();
    private readonly Dictionary<Collider, SelectablePart> partByCollider =
        new Dictionary<Collider, SelectablePart>();
    private readonly Dictionary<Collider, Command> commandByCollider =
        new Dictionary<Collider, Command>();
    private readonly Dictionary<Transform, MoveTrack> moveByTransform =
        new Dictionary<Transform, MoveTrack>();
    private readonly List<MoveTrack> moveTracks = new List<MoveTrack>();
    private readonly List<InputAction> inputActions = new List<InputAction>();

    private Camera viewCamera;
    private InputAction selectAction;
    private Transform panelRoot;
    private TextMesh levelText;
    private TextMesh selectedNameText;
    private TextMesh hoodText;
    private SelectablePart selectedPart;
    private Material panelMaterial;
    private Material goldMaterial;
    private Transform hood;
    private Renderer[] hoodRenderers = Array.Empty<Renderer>();
    private bool[] hoodRendererOriginalStates = Array.Empty<bool>();
    private Collider[] hoodColliders = Array.Empty<Collider>();
    private bool[] hoodColliderOriginalStates = Array.Empty<bool>();
    private bool hoodOpen;
    private bool hasHoodSnapshot;
    private Bounds assemblyBounds;
    private int currentLevel;
    private int transitionTargetLevel;
    private float transitionT = 1f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private const int MaxLevel = 3;
    private const float HighlightAmount = 0.38f;

    public int CurrentLevel => transitionT >= 1f ? currentLevel : transitionTargetLevel;
    public string SelectedComponentName => selectedPart != null ? selectedPart.Target.name : string.Empty;

    private void Awake()
    {
        viewCamera = Camera.main;
        BuildSelectableParts();
        BuildMovementPlan();
        BuildPanel();
        BuildInput();
        UpdatePanel();
    }

    private void Start()
    {
        Transform car = transform.parent;
        while (car != null && car.name != "CarConcept")
            car = car.parent;

        if (car != null)
        {
            hood = FindExact(car, "BodyHood");
            if (hood != null)
            {
                hoodRenderers = hood.GetComponentsInChildren<Renderer>(true);
                hoodRendererOriginalStates = new bool[hoodRenderers.Length];
                for (int i = 0; i < hoodRenderers.Length; i++)
                    hoodRendererOriginalStates[i] = hoodRenderers[i] != null && hoodRenderers[i].enabled;

                hoodColliders = hood.GetComponentsInChildren<Collider>(true);
                hoodColliderOriginalStates = new bool[hoodColliders.Length];
                for (int i = 0; i < hoodColliders.Length; i++)
                    hoodColliderOriginalStates[i] = hoodColliders[i] != null && hoodColliders[i].enabled;

                hasHoodSnapshot = true;
            }
        }
    }

    private void Update()
    {
        if (viewCamera == null)
            viewCamera = Camera.main;

        AnimateParts();
        FollowCamera();

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.pageUpKey.wasPressedThisFrame) Explode();
            if (keyboard.pageDownKey.wasPressedThisFrame) Restore();
            if (keyboard.homeKey.wasPressedThisFrame) ResetExplorer();
            if (keyboard.hKey.wasPressedThisFrame) ToggleHood();
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && viewCamera != null)
            ProcessRay(viewCamera.ScreenPointToRay(mouse.position.ReadValue()));

        if (selectAction != null && selectAction.WasPressedThisFrame() && viewCamera != null)
            ProcessRay(GetXRSelectionRay());
    }

    private Ray GetXRSelectionRay()
    {
        UnityEngine.XR.InputDevice leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        Vector3 trackingPosition = Vector3.zero;
        Quaternion trackingRotation = Quaternion.identity;
        if (leftController.isValid &&
            leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out trackingPosition) &&
            leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out trackingRotation))
        {
            XROrigin xrOrigin = viewCamera != null ? viewCamera.GetComponentInParent<XROrigin>() : null;
            if (xrOrigin != null)
            {
                Vector3 worldPosition = xrOrigin.transform.TransformPoint(trackingPosition);
                Quaternion worldRotation = xrOrigin.transform.rotation * trackingRotation;
                return new Ray(worldPosition, worldRotation * Vector3.forward);
            }

            return new Ray(trackingPosition, trackingRotation * Vector3.forward);
        }

        return viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
    }

    private void BuildSelectableParts()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        bool hasBounds = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer.transform == transform)
                continue;
            if (!hasBounds)
            {
                assemblyBounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                assemblyBounds.Encapsulate(renderer.bounds);
            }
        }

        HashSet<Transform> registered = new HashSet<Transform>();
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer.transform == transform || !registered.Add(renderer.transform))
                continue;

            Collider collider = renderer.GetComponent<Collider>();
            bool ownsCollider = false;
            if (collider == null)
            {
                collider = CreateSelectionCollider(renderer);
                ownsCollider = collider != null;
            }
            if (collider == null)
                continue;

            SelectablePart part = new SelectablePart
            {
                Target = renderer.transform,
                Renderer = renderer,
                Collider = collider,
                OwnsCollider = ownsCollider,
                OriginalBlock = new MaterialPropertyBlock()
            };
            renderer.GetPropertyBlock(part.OriginalBlock);
            selectableParts.Add(part);
            partByCollider[collider] = part;
        }
    }

    private static Collider CreateSelectionCollider(Renderer renderer)
    {
        Transform target = renderer.transform;
        Bounds bounds = renderer.bounds;
        Vector3 min = Vector3.zero;
        Vector3 max = Vector3.zero;
        bool initialized = false;

        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
        {
            Vector3 corner = new Vector3(
                x == 0 ? bounds.min.x : bounds.max.x,
                y == 0 ? bounds.min.y : bounds.max.y,
                z == 0 ? bounds.min.z : bounds.max.z);
            Vector3 local = target.InverseTransformPoint(corner);
            if (!initialized)
            {
                min = max = local;
                initialized = true;
            }
            else
            {
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
        }

        if (!initialized)
            return null;

        BoxCollider box = target.gameObject.AddComponent<BoxCollider>();
        box.center = (min + max) * 0.5f;
        box.size = Vector3.Max(max - min, Vector3.one * 0.015f);
        box.isTrigger = false;
        return box;
    }

    private void BuildMovementPlan()
    {
        List<Transform> nodes = new List<Transform>();
        CollectTransforms(transform, nodes);

        foreach (Transform node in nodes)
        {
            string n = node.name;
            if (IsMajorGroup(n, node))
                AddStep(node, 1, GetRadialDirection(node, MajorFallback(n)), 0.20f);

            if (IsInternalGroup(n))
                AddStep(node, 2, GetRadialDirection(node, InternalFallback(n)), 0.23f);

            if (IsValveOrSpring(n))
            {
                Vector3 bank = GetRadialDirection(node,
                    new Vector3(0f, Mathf.Sign(node.localPosition.y == 0f ? 1f : node.localPosition.y), -0.25f));
                AddStep(node, 2, bank, 0.18f);
                AddStep(node, 3, GetRadialDirection(node, bank), 0.11f);
            }

            if (IsCamLobe(n) || n.StartsWith("Horse ", StringComparison.Ordinal))
                AddStep(node, 3, GetRadialDirection(node, Vector3.right), 0.12f);
        }

        foreach (Transform node in nodes)
        {
            if (IsAssemblyGroup(node.name))
            {
                Vector3 groupCenter = GetBounds(node).center;
                for (int i = 0; i < node.childCount; i++)
                {
                    Transform child = node.GetChild(i);
                    if (HasRenderer(child))
                        AddStep(child, 3, GetRadialDirection(child, groupCenter, Vector3.right), 0.11f);
                }
            }
        }

        NormalizeOffsets();
    }

    private static void CollectTransforms(Transform root, List<Transform> output)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            output.Add(child);
            CollectTransforms(child, output);
        }
    }

    private static bool HasRenderer(Transform target)
    {
        return target.GetComponent<Renderer>() != null;
    }

    private static bool IsMajorGroup(string name, Transform node)
    {
        return name.StartsWith("Engine cylinder head", StringComparison.Ordinal) ||
               name == "Oil pan" ||
               name == "Engine side cover" ||
               name.StartsWith("Valve cover", StringComparison.Ordinal) ||
               name.StartsWith("Exhaust manifold", StringComparison.Ordinal) ||
               name.StartsWith("Engine belt wheels", StringComparison.Ordinal) ||
               (name == "Intake manifold mirored005" && node.childCount > 0) ||
               (name.StartsWith("Engine air filter", StringComparison.Ordinal) && node.childCount > 0) ||
               (name.StartsWith("Air turbo (Supercharger)", StringComparison.Ordinal) && node.childCount > 0);
    }

    private static bool IsInternalGroup(string name)
    {
        return name.StartsWith("Piston assembly", StringComparison.Ordinal) ||
               name.StartsWith("Rocker arm assembly", StringComparison.Ordinal) ||
               name.StartsWith("Cam shaft retainer", StringComparison.Ordinal) ||
               name == "Crankshaft" ||
               name == "Cam shaft" ||
               name == "Cam shaft001";
    }

    private static bool IsValveOrSpring(string name)
    {
        return name.StartsWith("Engine valve", StringComparison.Ordinal) ||
               name.StartsWith("Rocker arm backing spring", StringComparison.Ordinal);
    }

    private static bool IsCamLobe(string name)
    {
        return (name.StartsWith("Cam", StringComparison.Ordinal) ||
                name.StartsWith("cam", StringComparison.Ordinal)) &&
               !name.StartsWith("Cam shaft", StringComparison.Ordinal);
    }

    private static bool IsAssemblyGroup(string name)
    {
        return name.StartsWith("Piston assembly", StringComparison.Ordinal) ||
               name.StartsWith("Rocker arm assembly", StringComparison.Ordinal) ||
               name == "Intake manifold mirored005" ||
               name.StartsWith("Engine air filter", StringComparison.Ordinal) ||
               name.StartsWith("Air turbo (Supercharger)", StringComparison.Ordinal);
    }

    private static Vector3 MajorFallback(string name)
    {
        if (name == "Oil pan") return Vector3.back;
        if (name.StartsWith("Engine cylinder head", StringComparison.Ordinal)) return Vector3.up;
        if (name.StartsWith("Exhaust manifold", StringComparison.Ordinal)) return Vector3.left;
        if (name.StartsWith("Valve cover", StringComparison.Ordinal)) return Vector3.up;
        return Vector3.right;
    }

    private static Vector3 InternalFallback(string name)
    {
        if (name == "Crankshaft") return Vector3.back;
        if (name.StartsWith("Cam shaft", StringComparison.Ordinal)) return Vector3.up;
        return Vector3.right;
    }

    private Vector3 GetRadialDirection(Transform target, Vector3 fallback)
    {
        return GetRadialDirection(target, assemblyBounds.center, fallback);
    }

    private Vector3 GetRadialDirection(Transform target, Vector3 referenceWorld, Vector3 fallback)
    {
        Vector3 delta = GetBounds(target).center - referenceWorld;
        if (delta.sqrMagnitude < 0.0001f)
            delta = transform.TransformDirection(fallback);
        Vector3 rootLocal = transform.InverseTransformDirection(delta.normalized);
        return rootLocal.sqrMagnitude > 0.0001f ? rootLocal.normalized : Vector3.up;
    }

    private Bounds GetBounds(Transform target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return new Bounds(target.position, Vector3.one * 0.05f);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            if (renderers[i] != null) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private void AddStep(Transform target, int level, Vector3 rootLocalDirection, float distance)
    {
        if (target == null || target == transform || level < 1 || level > MaxLevel)
            return;

        if (!moveByTransform.TryGetValue(target, out MoveTrack track))
        {
            track = new MoveTrack
            {
                Target = target,
                OriginalPosition = target.localPosition,
                OriginalRotation = target.localRotation,
                OriginalScale = target.localScale
            };
            moveByTransform.Add(target, track);
            moveTracks.Add(track);
        }

        Vector3 worldDirection = transform.TransformDirection(rootLocalDirection.normalized);
        Vector3 parentLocalDirection = target.parent != null
            ? target.parent.InverseTransformDirection(worldDirection)
            : worldDirection;
        track.StepOffsets[level] = parentLocalDirection.normalized * distance;
        track.HasStep[level] = true;
    }

    private void NormalizeOffsets()
    {
        foreach (MoveTrack track in moveTracks)
        {
            Vector3 cumulative = Vector3.zero;
            for (int level = 1; level <= MaxLevel; level++)
            {
                if (track.HasStep[level])
                    cumulative += track.StepOffsets[level];
                track.StepOffsets[level] = cumulative;
            }
        }
    }

    private Vector3 DesiredPosition(MoveTrack track, int level)
    {
        return track.OriginalPosition + track.StepOffsets[Mathf.Clamp(level, 0, MaxLevel)];
    }

    private void BuildInput()
    {
        selectAction = new InputAction("Engine Explorer Select", InputActionType.Button,
            "<XRController>{LeftHand}/trigger");
        selectAction.Enable();
        inputActions.Add(selectAction);
    }

    private void BuildPanel()
    {
        panelRoot = new GameObject("Engine Explorer Controls").transform;
        panelRoot.SetParent(transform, false);

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");
        panelMaterial = CreateMaterial(shader, new Color(0.025f, 0.035f, 0.045f, 1f));
        goldMaterial = CreateMaterial(shader, new Color(0.88f, 0.58f, 0.22f, 1f));

        CreateFrame(panelRoot, 0.52f, 0.74f);
        CreateText(panelRoot, "Title", "ENGINE EXPLORER", new Vector3(0f, 0.33f, 0f),
            0.0075f, new Color(0.94f, 0.72f, 0.39f, 1f));
        levelText = CreateText(panelRoot, "Level", "ASSEMBLED  |  LEVEL 0 / 3",
            new Vector3(0f, 0.25f, 0f), 0.0040f, Color.white);
        selectedNameText = CreateText(panelRoot, "Selected component name", "SELECT A COMPONENT",
            new Vector3(0f, 0.18f, 0f), 0.0043f, Color.white);

        CreateCommandCard("EXPLODE", Command.Explode, new Vector3(0f, 0.06f, 0f));
        CreateCommandCard("RESTORE", Command.Restore, new Vector3(0f, -0.045f, 0f));
        CreateCommandCard("RESET", Command.Reset, new Vector3(0f, -0.135f, 0f));
        CreateCommandCard("OPEN HOOD", Command.ToggleHood, new Vector3(0f, -0.22f, 0f));
        hoodText = CreateText(panelRoot, "Hood status", "HOOD CLOSED",
            new Vector3(0f, -0.29f, 0f), 0.0035f, new Color(0.82f, 0.84f, 0.87f, 1f));
        CreateText(panelRoot, "Keyboard help", "MOUSE / LEFT TRIGGER SELECT",
            new Vector3(0f, -0.335f, 0f), 0.0030f, new Color(0.72f, 0.77f, 0.82f, 1f));
    }

    private Material CreateMaterial(Shader shader, Color color)
    {
        Material material = new Material(shader);
        if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, color);
        if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);
        material.color = color;
        return material;
    }

    private void CreateFrame(Transform parent, float width, float height)
    {
        CreateBox(parent, "Frame background", new Vector3(0f, 0f, 0.025f),
            new Vector3(width, height, 0.025f), panelMaterial);
        CreateBorder(parent, width, height);
    }

    private void CreateBorder(Transform parent, float width, float height)
    {
        const float thickness = 0.005f;
        CreateBox(parent, "Border top", new Vector3(0f, height * 0.5f, 0.008f),
            new Vector3(width, thickness, 0.012f), goldMaterial);
        CreateBox(parent, "Border bottom", new Vector3(0f, -height * 0.5f, 0.008f),
            new Vector3(width, thickness, 0.012f), goldMaterial);
        CreateBox(parent, "Border left", new Vector3(-width * 0.5f, 0f, 0.008f),
            new Vector3(thickness, height, 0.012f), goldMaterial);
        CreateBox(parent, "Border right", new Vector3(width * 0.5f, 0f, 0.008f),
            new Vector3(thickness, height, 0.012f), goldMaterial);
    }

    private void CreateCommandCard(string label, Command command, Vector3 localPosition)
    {
        const float width = 0.42f;
        const float height = 0.085f;
        GameObject card = new GameObject(label + " Card");
        card.transform.SetParent(panelRoot, false);
        card.transform.localPosition = localPosition;

        CreateBox(card.transform, "Background", new Vector3(0f, 0f, 0.025f),
            new Vector3(width, height, 0.025f), panelMaterial);
        CreateBorder(card.transform, width, height);

        BoxCollider collider = card.AddComponent<BoxCollider>();
        collider.size = new Vector3(width, height, 0.10f);
        commandByCollider[collider] = command;
        CreateText(card.transform, "Label", label, new Vector3(0f, 0f, 0f),
            0.0055f, Color.white);
    }

    private static void CreateBox(Transform parent, string name, Vector3 position,
        Vector3 scale, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = position;
        box.transform.localScale = scale;

        Collider collider = box.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            UnityEngine.Object.Destroy(collider);
        }

        MeshRenderer renderer = box.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    private static TextMesh CreateText(Transform parent, string name, string text,
        Vector3 position, float characterSize, Color color)
    {
        GameObject label = new GameObject(name);
        label.transform.SetParent(parent, false);
        label.transform.localPosition = position;
        TextMesh mesh = label.AddComponent<TextMesh>();
        mesh.text = text;
        mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        mesh.fontSize = 48;
        mesh.characterSize = characterSize;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.color = color;
        mesh.richText = false;
        return mesh;
    }

    private void FollowCamera()
    {
        if (viewCamera == null || panelRoot == null)
            return;

        Transform cameraTransform = viewCamera.transform;
        panelRoot.position = cameraTransform.position +
                             cameraTransform.forward * 1.62f +
                             cameraTransform.right * 0.76f -
                             cameraTransform.up * 0.28f;
        panelRoot.rotation = cameraTransform.rotation;
    }

    private void ProcessRay(Ray ray)
    {
        if (ray.direction.sqrMagnitude < 0.5f)
            return;

        RaycastHit[] hits = Physics.RaycastAll(ray, rayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (commandByCollider.TryGetValue(hit.collider, out Command command))
            {
                RunCommand(command);
                return;
            }
            if (partByCollider.TryGetValue(hit.collider, out SelectablePart part))
            {
                SelectPart(part);
                return;
            }
        }
    }

    private void RunCommand(Command command)
    {
        switch (command)
        {
            case Command.Explode: Explode(); break;
            case Command.Restore: Restore(); break;
            case Command.Reset: ResetExplorer(); break;
            case Command.ToggleHood: ToggleHood(); break;
        }
    }

    public void Explode()
    {
        if (CurrentLevel >= MaxLevel)
            return;
        if (CurrentLevel == 0)
            SetHoodOpen(true);
        BeginTransition(CurrentLevel + 1);
    }

    public void Restore()
    {
        BeginTransition(0);
    }

    public void ResetExplorer()
    {
        BeginTransition(0);
        SelectPart(null);
        SetHoodOpen(false);
        UpdatePanel();
    }

    public void ToggleHood()
    {
        SetHoodOpen(!hoodOpen);
    }

    private void SetHoodOpen(bool open)
    {
        hoodOpen = open;
        if (hasHoodSnapshot)
        {
            for (int i = 0; i < hoodRenderers.Length; i++)
                if (hoodRenderers[i] != null)
                    hoodRenderers[i].enabled = open ? false : hoodRendererOriginalStates[i];
            for (int i = 0; i < hoodColliders.Length; i++)
                if (hoodColliders[i] != null)
                    hoodColliders[i].enabled = open ? false : hoodColliderOriginalStates[i];
        }
        UpdatePanel();
    }

    private void BeginTransition(int targetLevel)
    {
        transitionTargetLevel = Mathf.Clamp(targetLevel, 0, MaxLevel);
        transitionT = 0f;
        foreach (MoveTrack track in moveTracks)
        {
            track.FromPosition = track.Target.localPosition;
            track.FromRotation = track.Target.localRotation;
            track.FromScale = track.Target.localScale;
        }
    }

    private void AnimateParts()
    {
        if (transitionT >= 1f)
            return;

        transitionT = Mathf.Min(1f, transitionT + Time.deltaTime /
            Mathf.Max(0.1f, transitionDuration));
        float eased = transitionT * transitionT * (3f - 2f * transitionT);

        foreach (MoveTrack track in moveTracks)
        {
            Vector3 targetPosition = DesiredPosition(track, transitionTargetLevel);
            track.Target.localPosition = Vector3.LerpUnclamped(track.FromPosition, targetPosition, eased);
            track.Target.localRotation = Quaternion.SlerpUnclamped(
                track.FromRotation, track.OriginalRotation, eased);
            track.Target.localScale = Vector3.LerpUnclamped(
                track.FromScale, track.OriginalScale, eased);
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
        foreach (MoveTrack track in moveTracks)
        {
            if (track.Target == null) continue;
            track.Target.localPosition = track.OriginalPosition;
            track.Target.localRotation = track.OriginalRotation;
            track.Target.localScale = track.OriginalScale;
        }
    }

    private void SelectPart(SelectablePart part)
    {
        if (selectedPart == part)
            return;

        ClearHighlight(selectedPart);
        selectedPart = part;
        if (selectedPart != null)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            selectedPart.Renderer.GetPropertyBlock(block);
            Color color = Color.Lerp(Color.white, new Color(0.95f, 0.61f, 0.2f, 1f), HighlightAmount);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            selectedPart.Renderer.SetPropertyBlock(block);
        }
        UpdatePanel();
    }

    private void ClearHighlight(SelectablePart part)
    {
        if (part != null && part.Renderer != null)
            part.Renderer.SetPropertyBlock(part.OriginalBlock);
    }

    private void UpdatePanel()
    {
        if (levelText != null)
        {
            int level = CurrentLevel;
            levelText.text = level == 0
                ? "ASSEMBLED  |  LEVEL 0 / 3"
                : "EXPLODED  |  LEVEL " + level + " / 3";
        }

        if (selectedNameText != null)
            selectedNameText.text = selectedPart != null
                ? WrapName(selectedPart.Target.name, 24)
                : "SELECT A COMPONENT";

        if (hoodText != null)
            hoodText.text = hoodOpen ? "HOOD OPEN" : "HOOD CLOSED";
    }

    private static string WrapName(string value, int limit)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= limit)
            return value;

        List<string> lines = new List<string>();
        int start = 0;
        while (start < value.Length)
        {
            int length = Mathf.Min(limit, value.Length - start);
            if (start + length < value.Length)
            {
                int split = value.LastIndexOf(' ', start + length - 1, length);
                if (split > start) length = split - start;
            }
            lines.Add(value.Substring(start, length).Trim());
            start += length;
            while (start < value.Length && value[start] == ' ') start++;
        }
        return string.Join("\n", lines);
    }

    private static Transform FindExact(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindExact(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    private void OnDestroy()
    {
        ClearHighlight(selectedPart);
        RestoreExactTransforms();
        if (hasHoodSnapshot)
            SetHoodOpen(false);

        foreach (InputAction action in inputActions)
        {
            action.Disable();
            action.Dispose();
        }
        inputActions.Clear();

        foreach (SelectablePart part in selectableParts)
        {
            if (part.OwnsCollider && part.Collider != null)
                Destroy(part.Collider);
        }

        if (panelRoot != null)
            Destroy(panelRoot.gameObject);
        if (panelMaterial != null)
            Destroy(panelMaterial);
        if (goldMaterial != null)
            Destroy(goldMaterial);
    }
}
