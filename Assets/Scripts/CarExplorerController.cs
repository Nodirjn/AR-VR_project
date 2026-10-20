using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using System.Linq;

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
        public Collider SelectionCollider;
        public Renderer[] Renderers;
        public MaterialPropertyBlock[] OriginalBlocks;
        public bool OwnsCollider;
        public Bounds AtlasBounds;
        public Vector3 OriginalWorldPosition;
        public Vector3 OriginalWorldGeometryCenter;
        public bool IsV6Part;
        public XRGrabInteractable GrabInteractable;
        public Transform AtlasProxy;
        public Renderer[] AtlasOriginalRenderers;
        public bool[] AtlasOriginalRendererEnabledStates;
        public Collider AtlasOriginalCollider;
        public bool AtlasOriginalColliderEnabled;
        public XRGrabInteractable AtlasOriginalGrab;
        public bool AtlasOriginalGrabEnabled;
        public Vector3 AtlasFromWorldPosition;
        public Quaternion AtlasFromWorldRotation;
        public Vector3 AtlasFromLocalScale;
        public Quaternion AtlasWorldRotation;
        public Vector3 AtlasWorldScale;
    }

    private sealed class MotionTrack
    {
        public Transform Target;
        public Transform OriginalParent;
        public Quaternion OriginalWorldRotation;
        public Vector3 OriginalWorldScale;
        public Quaternion AtlasLocalRotation;
        public Vector3 AtlasLocalScale;
        public Vector3 OriginalPosition;
        public Quaternion OriginalRotation;
        public Vector3 OriginalScale;
        public Vector3 FromPosition;
        public Quaternion FromRotation;
        public Vector3 FromScale;
        public Vector3 FromWorldPosition;
        public Vector3 AtlasWorldPosition;
        public bool UsesAtlasPosition;
        public bool IsMeshPart;
        public bool IsHood;
        public Quaternion HoodLocalRotation;
        public readonly Vector3[] StepOffsets = new Vector3[8];
    }

    private sealed class AtlasRegion
    {
        public Transform Root;
        public string Category;
        public List<Part> Parts = new List<Part>();
        public Dictionary<Part, Vector2> Slots = new Dictionary<Part, Vector2>();
        public Vector3 SourceCenter;
        public Vector2 Size;
        public Vector2 BoardOrigin;
    }

    [Serializable]
    private sealed class PartVoiceCue
    {
        public string componentName;
        public AudioClip clip;
    }

    private enum Command { Explode, Restore, Reset, English, Uzbek }

    [SerializeField] private Transform presenterRoot;
    [SerializeField] private AudioSource presenterAudioSource;
    [SerializeField] private AudioClip greetingClip;
    [SerializeField] private List<PartVoiceCue> partVoiceCues = new List<PartVoiceCue>();
    [SerializeField, Min(0.5f)] private float greetingDistance = 2.8f;
    [SerializeField, Min(1f)] private float greetingDisplayDuration = 7f;
    private ShowroomPresenterAgent presenterAgent;
    private Animator presenterAnimator;
    private Transform presenterHeadAnchor;

    private readonly List<Part> parts = new List<Part>();
    private readonly List<MotionTrack> motionTracks = new List<MotionTrack>();
    private readonly Dictionary<Transform, MotionTrack> trackByTransform = new Dictionary<Transform, MotionTrack>();
    private readonly HashSet<Transform> registeredParts = new HashSet<Transform>();
    private readonly Dictionary<Collider, Part> partByCollider = new Dictionary<Collider, Part>();
    private readonly Dictionary<Collider, Command> commandByCollider = new Dictionary<Collider, Command>();
    private readonly List<InputAction> actions = new List<InputAction>();

    private InputAction selectAction;
    private InputAction leftSelectAction;
    private InputAction explodeAction;
    private InputAction restoreAction;
    private InputAction resetAction;
    private Transform engineRoot;
    private Transform hoodRoot;
    private Renderer[] carRootRenderers;
    private bool[] originalCarRootRendererStates;
    private XRInteractionManager ownedInteractionManager;
    private XRInteractionManager interactionManager;
    private readonly List<XRGrabInteractable> reenableGrabsAfterTransition = new List<XRGrabInteractable>();
    private Transform controlsRoot;
    private Transform infoRoot;
    private Transform componentInfoRoot;
    private Transform greetingRoot;
    private Transform nameplateRoot;
    private TextMesh levelText;
    private TextMesh infoHeading;
    private TextMesh infoBody;
    private TextMesh componentInfoHeading;
    private TextMesh componentInfoBody;
    private TextMesh greetingHeading;
    private TextMesh greetingBody;
    private TextMesh nameplateText;
    private TextMesh englishLanguageLabel;
    private TextMesh uzbekLanguageLabel;
    private Bounds heroDisplayBounds;
    private Material panelMaterial;
    private Material goldMaterial;
    private Part selectedPart;
    private int currentLevel;
    private int transitionTargetLevel;
    private float transitionT = 1f;
    private float nextGreetingCheck;
    private float greetingHideTime;
    private bool greetingShown;
    private bool greetingCompleted;
    private bool componentPanelFacingInitialized;
    private bool infoDismissed;
    private bool showUzbek;
    private bool ownsAudioSource;
    private Vector3 cameraPivot;
    private Vector3 cameraUp = Vector3.up;
    private float cameraOrbitDistance = 8f;
    private Vector3 preAtlasCameraPosition;
    private Quaternion preAtlasCameraRotation;
    private bool atlasCameraFramed;
    private Transform atlasRoot;
    private float atlasLayoutScale = 1f;
    private float atlasGridHeight;
    private Vector3 atlasAnchorPosition;
    private Vector3 atlasFacingForward = Vector3.forward;
    private float atlasViewportDistance;
    // Level 0 is the assembled overview; levels 1-6 are the six reveal steps,
    // for seven user-facing stages in total.
    private const int MaxExplodeLevel = 6;
    private const float AtlasPartGap = 0.18f;
    private const float AtlasMinimumCell = 0.06f;
    private const float AtlasRegionWidth = 4.2f;
    private const float AtlasEngineRegionWidth = 3.4f;
    private const float AtlasCabinRegionWidth = 3.2f;
    private const float AtlasRunningGearRegionWidth = 3.2f;
    private const float AtlasBoardWidth = 8f;
    private const float AtlasCellGap = 0.025f;
    private const float AtlasMaximumExtent = 4.2f;
    private const float AtlasFloorClearance = 0.18f;
    private static readonly string[] StageTitles = {
        "Complete Vehicle", "Exterior Overview + Separation", "Hood Open — V6 Revealed",
        "V6 Engine Extracted", "Major Engine Assemblies", "Internal Mechanisms",
        "Final Atlas — All Components"
    };
    private static readonly string[] StageExplanations = {
        "The car is fully assembled.\nAvtomobil to‘liq yig‘ilgan.",
        "Exterior sections begin to separate.\nTashqi qismlar ajrala boshlaydi.",
        "The hood opens; the V6 stays installed.\nKaput ochiladi; V6 joyida qoladi.",
        "The assembled V6 lifts out for inspection.\nYig‘ilgan V6 ko‘rik uchun ko‘tariladi.",
        "Major modeled engine assemblies separate.\nDvigatelning asosiy qismlari ajraladi.",
        "Smaller engine mechanisms separate further.\nMayda dvigatel mexanizmlari ham ajraladi.",
        "All modeled parts are arranged for viewing.\nModeldagi barcha qismlar ko‘rish uchun joylandi."
    };
    private static readonly string[] StageTitlesUzbek = {
        "To‘liq avtomobil", "Tashqi ko‘rinish va ajralish", "Kaput ochiq — V6 ko‘rinadi",
        "V6 dvigateli chiqarildi", "Asosiy qismlar", "Ichki mexanizmlar",
        "Barcha qismlar"
    };
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

        viewCamera = ResolveActiveViewCamera();

        if (presenterRoot == null)
        {
            GameObject presenter = GameObject.Find("Remy Presenter");
            if (presenter != null)
                presenterRoot = presenter.transform;
        }

        if (presenterRoot != null)
        {
            presenterAgent = presenterRoot.GetComponent<ShowroomPresenterAgent>();
            presenterAnimator = presenterRoot.GetComponentInChildren<Animator>(true);
            if (presenterAnimator != null && presenterAnimator.isHuman)
                presenterHeadAnchor = presenterAnimator.GetBoneTransform(HumanBodyBones.Head);
        }

        if (presenterAudioSource == null && presenterRoot != null)
            presenterAudioSource = presenterRoot.GetComponent<AudioSource>();

        if (carRoot != null)
        {
            hoodRoot = carRoot.Find("BodyHood");
            engineRoot = carRoot.Find("V6_Engine_Prototype(Clone)");
            if (engineRoot == null)
            {
                foreach (Transform candidate in carRoot.GetComponentsInChildren<Transform>(true))
                    if (candidate.name.IndexOf("V6", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        candidate.GetComponentInChildren<Renderer>(true) != null)
                    { engineRoot = candidate; break; }
            }
        }

        if (carRoot == null)
        {
            Debug.LogError("[Car Explorer] Could not find the existing CarConcept root.");
            enabled = false;
            return;
        }

        carRootRenderers = carRoot.GetComponents<Renderer>();
        originalCarRootRendererStates = new bool[carRootRenderers.Length];
        for (int i = 0; i < carRootRenderers.Length; i++)
            originalCarRootRendererStates[i] = carRootRenderers[i] != null && carRootRenderers[i].enabled;

        BuildComponentRegistry();
        heroDisplayBounds = GetOriginalCarWorldBounds();
        SetCarRootMeshVisibility(0);
        EnsureXRGrabbables();
        InitializeCameraNavigation();
        if (parts.Count == 0)
        {
            Debug.LogError("[Car Explorer] No enabled mesh components were found.");
            enabled = false;
            return;
        }

        BuildPanel();
        BuildInputActions();
        UpdatePanel();
    }

    private void Update()
    {
        Camera activeViewCamera = ResolveActiveViewCamera();
        if (activeViewCamera != null)
            viewCamera = activeViewCamera;

        AnimateParts();
        UpdateCameraNavigation();
        FollowCamera();
        UpdatePresenterGreeting();

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.oKey.wasPressedThisFrame) Explode();
            if (keyboard.rKey.wasPressedThisFrame) Restore();
            if (keyboard.tKey.wasPressedThisFrame) ResetExplorer();
            if (keyboard.qKey.wasPressedThisFrame) SetLanguage(false);
            if (keyboard.uKey.wasPressedThisFrame) SetLanguage(true);
            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.rightBracketKey.wasPressedThisFrame ||
                keyboard.pageUpKey.wasPressedThisFrame) Explode();
            if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.leftBracketKey.wasPressedThisFrame ||
                keyboard.pageDownKey.wasPressedThisFrame) Restore();
            if (keyboard.escapeKey.wasPressedThisFrame) DismissGreeting();
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && viewCamera != null)
            ProcessRay(viewCamera.ScreenPointToRay(mouse.position.ReadValue()));

        if (!XRSettings.isDeviceActive && selectAction != null && selectAction.WasPressedThisFrame())
            ProcessRay(viewCamera != null
                ? viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : default);

        if (!XRSettings.isDeviceActive && leftSelectAction != null && leftSelectAction.WasPressedThisFrame())
            ProcessRay(viewCamera != null ? viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) : default);

        if (explodeAction != null && explodeAction.WasPressedThisFrame()) Explode();
        if (restoreAction != null && restoreAction.WasPressedThisFrame()) Restore();
        if (resetAction != null && resetAction.WasPressedThisFrame()) ResetExplorer();
    }

    private void LateUpdate()
    {
        UpdateNameplate();
    }

    private Camera ResolveActiveViewCamera()
    {
        Camera[] cameras = Camera.allCameras;
        if (XRSettings.isDeviceActive)
        {
            foreach (Camera candidate in cameras)
            {
                if (candidate == null || !candidate.isActiveAndEnabled)
                    continue;
                for (Transform node = candidate.transform; node != null; node = node.parent)
                    if (node.name.IndexOf("XR Origin", StringComparison.OrdinalIgnoreCase) >= 0)
                        return candidate;
            }
        }

        if (viewCamera != null && viewCamera.isActiveAndEnabled)
            return viewCamera;
        Camera mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.isActiveAndEnabled)
            return mainCamera;
        return cameras.Length > 0 ? cameras[0] : null;
    }

    // Inventory every enabled, independently-rendered mesh object under CarConcept.
    // Mesh submeshes/material sections are intentionally not reported as separate parts.
    private void BuildComponentRegistry()
    {
        Renderer[] renderers = carRoot.GetComponentsInChildren<Renderer>(true);
        Dictionary<Transform, List<Renderer>> renderersByNode = new Dictionary<Transform, List<Renderer>>();
        List<Renderer> included = new List<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if ((filter == null || filter.sharedMesh == null) && (skinned == null || skinned.sharedMesh == null))
                continue;
            if (!renderersByNode.TryGetValue(renderer.transform, out List<Renderer> list))
            {
                list = new List<Renderer>();
                renderersByNode.Add(renderer.transform, list);
            }
            list.Add(renderer);
            included.Add(renderer);
        }

        foreach (KeyValuePair<Transform, List<Renderer>> entry in renderersByNode)
        {
            Transform node = entry.Key;
            Renderer[] nodeRenderers = entry.Value.ToArray();
            Part part = new Part
            {
                Target = node,
                DisplayName = GetUserFacingPartName(node),
                AtlasBounds = GetRendererBoundsInSpace(nodeRenderers, carRoot),
                OriginalWorldPosition = node.position,
                OriginalWorldGeometryCenter = GetWorldRendererBounds(nodeRenderers).center,
                IsV6Part = engineRoot != null && (node == engineRoot || node.IsChildOf(engineRoot)),
                Renderers = nodeRenderers,
                OriginalBlocks = new MaterialPropertyBlock[nodeRenderers.Length]
            };
            for (int i = 0; i < nodeRenderers.Length; i++)
            {
                part.OriginalBlocks[i] = new MaterialPropertyBlock();
                nodeRenderers[i].GetPropertyBlock(part.OriginalBlocks[i]);
            }

            Collider existing = node.GetComponent<Collider>();
            if (existing != null)
                part.SelectionCollider = existing;
            else
            {
                part.SelectionCollider = CreateBoundsCollider(node, nodeRenderers);
                part.OwnsCollider = part.SelectionCollider != null;
            }
            if (part.SelectionCollider != null)
                partByCollider[part.SelectionCollider] = part;
            parts.Add(part);
            registeredParts.Add(node);
        }

        parts.Sort((a, b) => string.CompareOrdinal(GetHierarchyPath(a.Target), GetHierarchyPath(b.Target)));
        BuildProgressiveMotion(included);
        Debug.Log("[Car Explorer] Registry: " + parts.Count + " selectable mesh objects, " +
                  motionTracks.Count + " animated hierarchy transforms.");
    }

    private static string GetUserFacingPartName(Transform node)
    {
        if (node == null)
            return "Vehicle Component";

        if (!node.name.StartsWith("Node-", StringComparison.OrdinalIgnoreCase))
            return node.name.Replace('_', ' ');

        string parentName = node.parent != null ? node.parent.name : string.Empty;
        if (parentName.StartsWith("WheelFront", StringComparison.OrdinalIgnoreCase))
            return parentName.EndsWith("L", StringComparison.OrdinalIgnoreCase)
                ? "Front Left Wheel" : "Front Right Wheel";
        if (parentName.StartsWith("WheelRear", StringComparison.OrdinalIgnoreCase))
            return parentName.EndsWith("L", StringComparison.OrdinalIgnoreCase)
                ? "Rear Left Wheel" : "Rear Right Wheel";

        return string.IsNullOrEmpty(parentName) ? "Vehicle Component" : parentName.Replace('_', ' ');
    }

    private static string GetHierarchyPath(Transform node)
    {
        if (node == null) return "";
        string path = node.name;
        while (node.parent != null)
        {
            node = node.parent;
            path = node.name + "/" + path;
        }
        return path;
    }

    private void BuildProgressiveMotion(List<Renderer> included)
    {
        foreach (Part part in parts) AddStep(part.Target, 0, Vector3.zero);

        // Stage 2 combines a modest assembly separation with clearance between
        // independently modeled exterior/cabin meshes. The V6 stays installed.
        for (int i = 0; i < carRoot.childCount; i++)
        {
            Transform assembly = carRoot.GetChild(i);
            if (assembly == engineRoot) continue;
            Bounds bounds;
            if (!TryGetBounds(assembly, included, out bounds)) continue;
            AddStep(assembly, 1, RadialOffset(assembly, bounds, carRoot, included, 0.28f, 0.7f));
        }

        // Stage 2 gives each independently modeled non-engine mesh its own
        // deterministic clearance around its source assembly.
        Bounds carBounds;
        Vector3 carCenter = TryGetBounds(carRoot, included, out carBounds) ? carBounds.center : carRoot.position;
        foreach (Part part in parts)
        {
            if (part.IsV6Part || part.Target == carRoot) continue;
            Vector3 direction = part.Target.position - carCenter;
            if (direction.sqrMagnitude < 0.0001f)
            {
                int index = Mathf.Max(0, part.Target.GetSiblingIndex());
                float angle = index * 2.39996323f;
                direction = carRoot.right * Mathf.Cos(angle) + carRoot.forward * Mathf.Sin(angle);
            }
            direction.Normalize();
            float distance = Mathf.Clamp(part.AtlasBounds.extents.magnitude * 0.22f, 0.24f, 0.72f);
            Vector3 worldOffset = direction * distance;
            AddStep(part.Target, 1, part.Target.parent.InverseTransformVector(worldOffset * 0.55f));
        }

        BuildEngineDisassemblyMotion(included);
        motionTracks.Sort((a, b) => GetTransformDepth(a.Target).CompareTo(GetTransformDepth(b.Target)));
    }

    private void BuildEngineDisassemblyMotion(List<Renderer> included)
    {
        if (engineRoot == null) return;
        AddStep(engineRoot, 0, Vector3.zero);

        if (hoodRoot != null && trackByTransform.TryGetValue(hoodRoot, out MotionTrack hoodTrack))
        {
            Part hoodPart = parts.FirstOrDefault(p => p.Target == hoodRoot);
            Bounds hoodBounds = hoodPart != null ? hoodPart.AtlasBounds : new Bounds(carRoot.InverseTransformPoint(hoodRoot.position), Vector3.one);
            Vector3 hinge = new Vector3(hoodBounds.center.x,
                hoodBounds.center.y + hoodBounds.extents.y * 0.86f, hoodBounds.center.z);
            Quaternion opening = Quaternion.AngleAxis(-62f, Vector3.right);
            Vector3 priorOffset = hoodTrack.StepOffsets[1];
            Vector3 rotatedPosition = hinge + priorOffset + opening * (hoodTrack.OriginalPosition - hinge);
            hoodTrack.StepOffsets[2] = rotatedPosition - hoodTrack.OriginalPosition - priorOffset;
            hoodTrack.IsHood = true;
            hoodTrack.HoodLocalRotation = opening * hoodTrack.OriginalRotation;
        }

        Vector3 engineCenter = engineRoot.position;
        Bounds engineBounds;
        if (TryGetBounds(engineRoot, included, out engineBounds)) engineCenter = engineBounds.center;
        foreach (Part part in parts)
        {
            if (!part.IsV6Part || part.Target == engineRoot) continue;
            string n = part.DisplayName.ToLowerInvariant();
            if (IsMajorEngineComponent(n))
                AddEngineRadialStep(part, engineCenter, 4, 0.42f, 0.95f);
            else if (IsInternalEngineComponent(n) || IsInternalAssembly(part.Target.parent, engineRoot))
                AddEngineRadialStep(part, engineCenter, 5, 0.24f, 0.60f);
        }
    }

    private static bool IsMajorEngineComponent(string name)
    {
        return ContainsAny(name, "block", "cylinder head", "oil pan", "valve cover", "side cover",
            "manifold", "housing", "sump", "case");
    }

    private static bool IsInternalEngineComponent(string name)
    {
        return ContainsAny(name, "piston", "crank", "cam", "valve", "spring", "rocker", "rod",
            "shaft", "retainer", "gear", "belt", "lifter", "pin", "screw", "bolt", "nut", "washer");
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        foreach (string term in terms)
            if (value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static bool IsInternalAssembly(Transform node, Transform engine)
    {
        return node != null && engine != null && node != engine && node.IsChildOf(engine) &&
               node.GetComponent<Renderer>() == null;
    }

    private void AddEngineRadialStep(Part part, Vector3 center, int stage, float min, float max)
    {
        Vector3 direction = part.OriginalWorldGeometryCenter - center;
        if (direction.sqrMagnitude < 0.0001f)
        {
            int index = part.Target.GetSiblingIndex();
            float angle = index * 2.39996323f;
            direction = carRoot.right * Mathf.Cos(angle) + carRoot.up * Mathf.Sin(angle);
        }
        direction.Normalize();
        float distance = Mathf.Clamp(part.AtlasBounds.extents.magnitude * 0.45f, min, max);
        Vector3 worldOffset = direction * distance;
        AddStep(part.Target, stage, part.Target.parent.InverseTransformVector(worldOffset));
    }

    private void BuildFinalAtlasLayout()
    {
        if (atlasRoot != null) Destroy(atlasRoot.gameObject);
        atlasRoot = new GameObject("Car Explorer Stage 7 Atlas Root").transform;
        atlasRoot.localScale = Vector3.one;
        Camera camera = viewCamera != null ? viewCamera : Camera.main;
        Bounds heroBounds = GetOriginalCarWorldBounds();
        atlasAnchorPosition = heroBounds.center;
        Vector3 towardCamera = camera != null
            ? Vector3.ProjectOnPlane(camera.transform.position - heroBounds.center, Vector3.up).normalized
            : Vector3.ProjectOnPlane(carRoot.forward, Vector3.up).normalized;
        Vector3 forward = towardCamera;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 up = Vector3.up;
        atlasRoot.rotation = Quaternion.LookRotation(forward, up);

        Dictionary<Part, Vector2> slots = new Dictionary<Part, Vector2>();
        Vector2 gridSize = PackAtlasParts(parts, AtlasBoardWidth, AtlasCellGap, right, up, slots);
        float largestGridExtent = Mathf.Max(gridSize.x, gridSize.y);
        atlasGridHeight = gridSize.y;
        float heroDisplayExtent = Mathf.Max(heroBounds.size.x, heroBounds.size.z);
        atlasLayoutScale = largestGridExtent > 0.001f
            ? Mathf.Min(1f, Mathf.Min(AtlasMaximumExtent, heroDisplayExtent * 1.1f) / largestGridExtent) : 1f;
        atlasLayoutScale = Mathf.Max(atlasLayoutScale, largestGridExtent > 0.001f ? 2f / largestGridExtent : 1f);
        atlasRoot.localScale = Vector3.one * atlasLayoutScale;
        Vector2 centering = new Vector2(-gridSize.x * 0.5f, gridSize.y * 0.5f);
        SetAtlasRootAboveFloor();

        foreach (Part part in parts)
        {
            Vector2 slot = slots[part] + centering;
            Vector3 localCenter = new Vector3(slot.x, slot.y, 0f);
            Vector3 geometryOffset = part.OriginalWorldGeometryCenter - part.OriginalWorldPosition;
            MotionTrack track = trackByTransform[part.Target];
            track.UsesAtlasPosition = part.Target != carRoot;
            track.AtlasWorldPosition = atlasRoot.TransformPoint(localCenter) - geometryOffset * atlasLayoutScale;
            track.AtlasLocalRotation = Quaternion.Inverse(atlasRoot.rotation) * track.OriginalWorldRotation;
            track.AtlasLocalScale = track.OriginalWorldScale;
            if (part.Target == carRoot)
            {
                part.AtlasWorldRotation = track.OriginalWorldRotation;
                part.AtlasWorldScale = track.OriginalWorldScale;
                part.AtlasFromWorldPosition = carRoot.position;
                part.AtlasFromWorldRotation = carRoot.rotation;
                part.AtlasFromLocalScale = carRoot.lossyScale;
            }
        }
    }

    private Bounds GetOriginalCarWorldBounds()
    {
        bool hasBounds = false;
        Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);
        foreach (Part part in parts)
        {
            if (part == null) continue;
            if (!hasBounds) { localBounds = part.AtlasBounds; hasBounds = true; }
            else localBounds.Encapsulate(part.AtlasBounds);
        }
        if (!hasBounds) return new Bounds(carRoot.position, Vector3.one);

        Bounds worldBounds = new Bounds(carRoot.TransformPoint(localBounds.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = localBounds.center + new Vector3(
                (i & 1) == 0 ? -localBounds.extents.x : localBounds.extents.x,
                (i & 2) == 0 ? -localBounds.extents.y : localBounds.extents.y,
                (i & 4) == 0 ? -localBounds.extents.z : localBounds.extents.z);
            worldBounds.Encapsulate(carRoot.TransformPoint(corner));
        }
        return worldBounds;
    }

    private void SetAtlasRootAboveFloor()
    {
        if (atlasRoot == null) return;
        Vector3 position = atlasAnchorPosition;
        position.y = GetShowroomFloorHeight() + atlasGridHeight * atlasLayoutScale * 0.5f + AtlasFloorClearance;
        atlasRoot.position = position;
    }

    private float GetShowroomFloorHeight()
    {
        GameObject floor = GameObject.Find("Showroom Floor");
        if (floor != null)
        {
            Collider collider = floor.GetComponent<Collider>();
            if (collider != null) return collider.bounds.max.y;
            Renderer renderer = floor.GetComponent<Renderer>();
            if (renderer != null) return renderer.bounds.max.y;
            return floor.transform.position.y;
        }
        return carRoot != null ? carRoot.position.y : 0f;
    }

    private void PrepareAtlasParts()
    {
        if (atlasRoot == null) return;
        foreach (Part part in parts)
        {
            MotionTrack track = trackByTransform[part.Target];
            if (part.Target == carRoot)
            {
                CreateCarRootAtlasProxy(part, track);
                continue;
            }
            if (!track.UsesAtlasPosition) continue;
            part.Target.SetParent(atlasRoot, true);
            // The atlas root applies atlasLayoutScale to both slot positions
            // and mesh geometry. Keep the reparented local scale as-is here so
            // geometry is compacted once, not twice.
            track.AtlasLocalScale = part.Target.localScale;
        }
    }

    private void CreateCarRootAtlasProxy(Part part, MotionTrack track)
    {
        MeshFilter sourceFilter = carRoot.GetComponent<MeshFilter>();
        MeshRenderer sourceRenderer = carRoot.GetComponent<MeshRenderer>();
        if (sourceFilter == null || sourceRenderer == null || sourceFilter.sharedMesh == null)
        {
            Debug.LogWarning("[Car Explorer] CarConcept root mesh has no MeshFilter/MeshRenderer; it remains in place in Stage 7.");
            return;
        }
        part.AtlasOriginalRenderers = part.Renderers;
        part.AtlasOriginalRendererEnabledStates = new bool[part.Renderers.Length];
        for (int i = 0; i < part.Renderers.Length; i++)
            part.AtlasOriginalRendererEnabledStates[i] = part.Renderers[i] != null && part.Renderers[i].enabled;
        part.AtlasOriginalCollider = part.SelectionCollider;
        part.AtlasOriginalColliderEnabled = part.SelectionCollider != null && part.SelectionCollider.enabled;
        part.AtlasOriginalGrab = part.GrabInteractable;
        part.AtlasOriginalGrabEnabled = part.GrabInteractable != null && part.GrabInteractable.enabled;

        GameObject proxy = new GameObject(part.Target.name + " Atlas Display");
        proxy.transform.SetParent(atlasRoot, false);
        proxy.transform.position = part.AtlasFromWorldPosition;
        proxy.transform.rotation = part.AtlasFromWorldRotation;
        proxy.transform.localScale = part.AtlasFromLocalScale;
        MeshFilter filter = proxy.AddComponent<MeshFilter>();
        filter.sharedMesh = sourceFilter.sharedMesh;
        MeshRenderer renderer = proxy.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = sourceRenderer.sharedMaterials;
        renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        renderer.receiveShadows = sourceRenderer.receiveShadows;
        renderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
        renderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;

        part.AtlasProxy = proxy.transform;
        part.AtlasFromWorldPosition = proxy.transform.position;
        part.AtlasFromWorldRotation = proxy.transform.rotation;
        part.AtlasFromLocalScale = proxy.transform.localScale;
        part.Renderers = new[] { renderer };
        part.SelectionCollider = CreateBoundsCollider(proxy.transform, part.Renderers);
        partByCollider[part.SelectionCollider] = part;
        if (part.AtlasOriginalCollider != null) part.AtlasOriginalCollider.enabled = false;
        if (part.AtlasOriginalGrab != null) part.AtlasOriginalGrab.enabled = false;
        Rigidbody body = proxy.AddComponent<Rigidbody>();
        body.isKinematic = true; body.useGravity = false;
        XRGrabInteractable grab = proxy.AddComponent<XRGrabInteractable>();
        grab.colliders.Clear();
        if (part.SelectionCollider != null) grab.colliders.Add(part.SelectionCollider);
        grab.selectEntered.AddListener(_ => SelectPart(part));
        part.GrabInteractable = grab;
    }

    private void RestoreAtlasHierarchy()
    {
        if (atlasRoot == null) return;
        foreach (MotionTrack track in motionTracks.Where(t => t.UsesAtlasPosition && t.Target != carRoot))
            track.Target.SetParent(track.OriginalParent, true);
        foreach (Part part in parts)
        {
            if (part.AtlasProxy == null) continue;
            partByCollider.Remove(part.SelectionCollider);
            part.Renderers = part.AtlasOriginalRenderers;
            for (int i = 0; i < part.AtlasOriginalRenderers.Length; i++)
                if (part.AtlasOriginalRenderers[i] != null && part.AtlasOriginalRendererEnabledStates != null &&
                    i < part.AtlasOriginalRendererEnabledStates.Length)
                    part.AtlasOriginalRenderers[i].enabled = part.AtlasOriginalRendererEnabledStates[i];
            part.SelectionCollider = part.AtlasOriginalCollider;
            part.GrabInteractable = part.AtlasOriginalGrab;
            if (part.AtlasOriginalCollider != null)
                part.AtlasOriginalCollider.enabled = part.AtlasOriginalColliderEnabled;
            if (part.AtlasOriginalGrab != null)
                part.AtlasOriginalGrab.enabled = part.AtlasOriginalGrabEnabled;
            Destroy(part.AtlasProxy.gameObject);
            part.AtlasProxy = null; part.AtlasOriginalRenderers = null;
            part.AtlasOriginalRendererEnabledStates = null;
        }
        Destroy(atlasRoot.gameObject);
        atlasRoot = null;
    }

    private void FitAtlasRootToCamera()
    {
        if (viewCamera == null || atlasRoot == null) return;

        const float margin = 0.05f;
        float minimumScale = atlasGridHeight > 0.001f ? 2f / atlasGridHeight : 0.1f;
        for (int attempt = 0; attempt < 20 && atlasLayoutScale > minimumScale; attempt++)
        {
            bool hasBounds = false;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (Part part in parts)
            foreach (Renderer renderer in part.Renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!hasBounds) break;

            bool fits = true;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + new Vector3(
                    (i & 1) == 0 ? -bounds.extents.x : bounds.extents.x,
                    (i & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                    (i & 4) == 0 ? -bounds.extents.z : bounds.extents.z);
                Vector3 vp = viewCamera.WorldToViewportPoint(corner);
                bool inside = vp.z > viewCamera.nearClipPlane &&
                    vp.x >= margin && vp.x <= 1f - margin &&
                    vp.y >= margin && vp.y <= 1f - margin;
                if (!inside) fits = false;
            }
            if (fits)
            {
                return;
            }
            atlasLayoutScale = Mathf.Max(minimumScale, atlasLayoutScale * 0.88f);
            atlasRoot.localScale = Vector3.one * atlasLayoutScale;
            SetAtlasRootAboveFloor();
        }
    }

    private void RestoreDesktopCameraAfterAtlas()
    {
        if (!atlasCameraFramed || viewCamera == null) return;
        viewCamera.transform.position = preAtlasCameraPosition;
        viewCamera.transform.rotation = preAtlasCameraRotation;
        atlasCameraFramed = false;
        InitializeCameraNavigation();
    }

    private static float RegionDepthOffset(string category)
    {
        if (category == "Engine") return 0.12f;
        if (category == "Cabin") return 0f;
        if (category == "Wheels and Running Gear") return -0.12f;
        return 0.06f;
    }

    private static int AtlasCategoryOrder(string category)
    {
        if (category == "Exterior and Body") return 0;
        if (category == "Engine") return 1;
        if (category == "Cabin") return 2;
        return 3;
    }

    private static string GetAtlasCategory(Part part)
    {
        if (part.IsV6Part) return "Engine";
        string path = GetHierarchyPath(part.Target);
        if (ContainsAny(path, "Interior", "Seat", "Dashboard", "Dash", "Steering")) return "Cabin";
        if (ContainsAny(path, "Wheel", "Tire", "Tyre", "Axle", "Suspension", "Brake"))
            return "Wheels and Running Gear";
        return "Exterior and Body";
    }

    private void SetCarRootMeshVisibility(int level)
    {
        if (carRootRenderers == null || originalCarRootRendererStates == null) return;
        bool visible = level < 3 || level == MaxExplodeLevel;
        for (int i = 0; i < carRootRenderers.Length; i++)
            if (carRootRenderers[i] != null)
                carRootRenderers[i].enabled = visible && originalCarRootRendererStates[i];
    }

    private void EnsureXRGrabbables()
    {
        interactionManager = FindFirstObjectByType<XRInteractionManager>(FindObjectsInactive.Include);
        if (interactionManager == null)
        {
            GameObject managerObject = new GameObject("Car Explorer XR Interaction Manager");
            ownedInteractionManager = managerObject.AddComponent<XRInteractionManager>();
            interactionManager = ownedInteractionManager;
        }

        foreach (Part part in parts)
        {
            Transform target = part.Target;
            // Prevent XRI's default descendant-collider scan from claiming the
            // colliders of child components. Detach children for the synchronous
            // AddComponent registration, then restore their exact local transforms.
            List<Transform> children = new List<Transform>();
            for (int i = 0; i < target.childCount; i++) children.Add(target.GetChild(i));
            foreach (Transform child in children) child.SetParent(null, false);

            XRGrabInteractable grab = target.GetComponent<XRGrabInteractable>();
            if (grab == null) grab = target.gameObject.AddComponent<XRGrabInteractable>();
            if (grab.enabled) grab.enabled = false;
            grab.colliders.Clear();
            if (part.SelectionCollider != null) grab.colliders.Add(part.SelectionCollider);

            foreach (Transform child in children) child.SetParent(target, false);
            grab.enabled = true;

            Rigidbody body = target.GetComponent<Rigidbody>();
            if (body == null) body = target.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            part.GrabInteractable = grab;
            grab.selectEntered.AddListener(_ => SelectPart(part));
        }
    }

    private static Vector2 PackAtlasParts(List<Part> source, float maxWidth, float gap,
        Vector3 right, Vector3 up, Dictionary<Part, Vector2> slots)
    {
        Dictionary<Part, Vector2> sizes = new Dictionary<Part, Vector2>();
        foreach (Part part in source)
        {
            bool hasBounds = false;
            float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f;
            foreach (Renderer renderer in part.Renderers)
            {
                if (renderer == null) continue;
                Bounds b = renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + new Vector3(
                        (i & 1) == 0 ? -b.extents.x : b.extents.x,
                        (i & 2) == 0 ? -b.extents.y : b.extents.y,
                        (i & 4) == 0 ? -b.extents.z : b.extents.z);
                    float x = Vector3.Dot(corner, right), y = Vector3.Dot(corner, up);
                    if (!hasBounds) { minX = maxX = x; minY = maxY = y; hasBounds = true; }
                    else { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
                }
            }
            sizes[part] = new Vector2(Mathf.Max(AtlasMinimumCell, maxX - minX),
                                      Mathf.Max(AtlasMinimumCell, maxY - minY));
        }

        List<Part> ordered = source
            .OrderByDescending(p => sizes[p].x * sizes[p].y)
            .ThenBy(p => GetHierarchyPath(p.Target), StringComparer.Ordinal)
            .ToList();
        float cursorX = 0f, cursorTop = 0f, rowHeight = 0f, usedWidth = 0f;
        foreach (Part part in ordered)
        {
            Vector2 size = sizes[part];
            if (cursorX > 0f && cursorX + size.x > maxWidth)
            {
                cursorX = 0f;
                cursorTop += rowHeight + gap;
                rowHeight = 0f;
            }
            slots[part] = new Vector2(cursorX + size.x * 0.5f,
                -(cursorTop + size.y * 0.5f));
            cursorX += size.x + gap;
            usedWidth = Mathf.Max(usedWidth, cursorX - gap);
            rowHeight = Mathf.Max(rowHeight, size.y);
        }
        float usedHeight = source.Count == 0 ? 0f : cursorTop + rowHeight;
        return new Vector2(usedWidth, Mathf.Max(0f, usedHeight));
    }

    private static Bounds GetWorldRendererBounds(Renderer[] renderers)
    {
        Bounds result = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) result.Encapsulate(renderers[i].bounds);
        return result;
    }

    private static Bounds GetRendererBoundsInSpace(Renderer[] renderers, Transform space)
    {
        bool hasBounds = false;
        Bounds result = new Bounds(Vector3.zero, Vector3.zero);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            Bounds bounds = renderer.bounds;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 world = new Vector3(x == 0 ? bounds.min.x : bounds.max.x,
                    y == 0 ? bounds.min.y : bounds.max.y,
                    z == 0 ? bounds.min.z : bounds.max.z);
                Vector3 local = space.InverseTransformPoint(world);
                if (!hasBounds) { result = new Bounds(local, Vector3.zero); hasBounds = true; }
                else result.Encapsulate(local);
            }
        }
        return result;
    }

    private static int GetTransformDepth(Transform target)
    {
        int depth = 0;
        while (target != null) { depth++; target = target.parent; }
        return depth;
    }

    private static Vector3 RadialOffset(Transform node, Bounds nodeBounds, Transform reference,
        List<Renderer> renderers, float minDistance, float maxDistance)
    {
        Bounds referenceBounds;
        Vector3 center = TryGetBounds(reference, renderers, out referenceBounds)
            ? referenceBounds.center : reference.position;
        Vector3 direction = nodeBounds.center - center;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = node.position - reference.position;
            if (direction.sqrMagnitude < 0.0001f)
            {
                int index = Mathf.Max(0, node.GetSiblingIndex());
                float angle = index * 2.39996323f;
                direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0.35f);
            }
        }
        direction.Normalize();
        float distance = Mathf.Clamp(nodeBounds.extents.magnitude * 0.42f, minDistance, maxDistance);
        Vector3 worldOffset = direction * distance;
        return node.parent != null ? node.parent.InverseTransformVector(worldOffset) : worldOffset;
    }

    private static bool TryGetBounds(Transform root, List<Renderer> renderers, out Bounds bounds)
    {
        bool found = false;
        bounds = new Bounds(root != null ? root.position : Vector3.zero, Vector3.zero);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || (renderer.transform != root && !renderer.transform.IsChildOf(root)))
                continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    private void AddStep(Transform target, int level, Vector3 offset)
    {
        if (!trackByTransform.TryGetValue(target, out MotionTrack track))
        {
            track = new MotionTrack
            {
                Target = target,
                OriginalParent = target.parent,
                OriginalWorldRotation = target.rotation,
                OriginalWorldScale = target.lossyScale,
                IsMeshPart = registeredParts.Contains(target),
                OriginalPosition = target.localPosition,
                OriginalRotation = target.localRotation,
                OriginalScale = target.localScale
            };
            trackByTransform.Add(target, track);
            motionTracks.Add(track);
        }
        track.StepOffsets[level] += offset;
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
                Vector3 worldCorner = new Vector3(x == 0 ? bounds.min.x : bounds.max.x,
                    y == 0 ? bounds.min.y : bounds.max.y,
                    z == 0 ? bounds.min.z : bounds.max.z);
                Vector3 localCorner = target.InverseTransformPoint(worldCorner);
                if (!hasBounds) { min = max = localCorner; hasBounds = true; }
                else { min = Vector3.Min(min, localCorner); max = Vector3.Max(max, localCorner); }
            }
        }
        if (!hasBounds) return null;
        BoxCollider collider = target.gameObject.AddComponent<BoxCollider>();
        collider.center = (min + max) * 0.5f;
        collider.size = Vector3.Max(max - min, Vector3.one * 0.025f);
        return collider;
    }

    private void BuildInputActions()
    {
        selectAction = CreateAction("Car Explorer Select", InputActionType.Button,
            "<XRController>{RightHand}/trigger");
        leftSelectAction = CreateAction("Car Explorer Left Select", InputActionType.Button,
            "<XRController>{LeftHand}/trigger");
        explodeAction = CreateAction("Car Explorer Explode", InputActionType.Button,
            "<XRController>{RightHand}/primaryButton");
        restoreAction = CreateAction("Car Explorer Restore", InputActionType.Button,
            "<XRController>{RightHand}/secondaryButton");
        resetAction = CreateAction("Car Explorer Reset", InputActionType.Button,
            "<XRController>{LeftHand}/menuButton");

        selectAction.Enable();
        leftSelectAction.Enable();
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
        // Runtime UI can survive a script/domain reload when Enter Play Mode
        // skips scene reload. Rebuild cleanly so the new controller owns its
        // labels, colliders, and raycast command map instead of retaining a
        // stale, unbound panel.
        DestroyStaleRuntimeObject("Car Explorer Controls");
        DestroyStaleRuntimeObject("Car Explorer Part Information");
        DestroyStaleRuntimeObject("Car Explorer Component Information");
        DestroyStaleRuntimeObject("Showroom Presenter Greeting");
        DestroyStaleRuntimeObject("Remy Nameplate");
        controlsRoot = null;
        infoRoot = null;
        componentInfoRoot = null;
        greetingRoot = null;
        nameplateRoot = null;
        componentPanelFacingInitialized = false;

        controlsRoot = new GameObject("Car Explorer Controls").transform;
        infoRoot = new GameObject("Car Explorer Part Information").transform;
        componentInfoRoot = new GameObject("Car Explorer Component Information").transform;
        greetingRoot = new GameObject("Showroom Presenter Greeting").transform;
        nameplateRoot = new GameObject("Remy Nameplate").transform;
        componentInfoRoot.gameObject.SetActive(false);
        greetingRoot.gameObject.SetActive(false);

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");

        panelMaterial = CreateUiMaterial(shader, new Color(0.025f, 0.035f, 0.045f, 1f));
        goldMaterial = CreateUiMaterial(shader, new Color(0.88f, 0.58f, 0.22f, 1f));

        CreateFrame(controlsRoot, "Controls frame", 1.10f, 1.58f);
        CreateText(controlsRoot, "Title", "CAR EXPLORER", new Vector3(0f, 0.63f, 0f),
            0.028f, new Color(0.94f, 0.72f, 0.39f, 1f), TextAlignment.Center);
        levelText = CreateText(controlsRoot, "Exploded level", "ASSEMBLED  |  LEVEL 0 / 3",
            new Vector3(0f, 0.49f, 0f), 0.016f, Color.white, TextAlignment.Center);

        CreateCommandCard("EXPLORE", "Advance one stage", Command.Explode, new Vector3(0f, 0.28f, 0f));
        CreateCommandCard("RESTORE", "Previous stage", Command.Restore, new Vector3(0f, 0.08f, 0f));
        CreateCommandCard("RESET", "Complete vehicle", Command.Reset, new Vector3(0f, -0.12f, 0f));

        CreateText(controlsRoot, "Keyboard Help",
            "Right-drag orbit  |  Middle pan  |  Wheel zoom\nO: explore  R: restore  T: reset  Q: English  U: Uzbek",
            new Vector3(0f, -0.36f, 0f), 0.014f, new Color(0.94f, 0.95f, 0.97f, 1f),
            TextAlignment.Center);
        CreateText(controlsRoot, "Controller Help", "Point / pinch: select\nA: explore  B: previous  Menu: reset",
            new Vector3(0f, -0.56f, 0f), 0.014f, new Color(0.90f, 0.92f, 0.95f, 1f),
            TextAlignment.Center);
        englishLanguageLabel = CreateLanguageButton("EN", Command.English, -0.18f);
        uzbekLanguageLabel = CreateLanguageButton("UZ", Command.Uzbek, 0.18f);

        CreateFrame(infoRoot, "Information frame", 1.70f, 0.98f);
        infoHeading = CreateText(infoRoot, "Information heading", "VEHICLE OVERVIEW",
            new Vector3(0f, 0.29f, 0f), 0.032f, new Color(1f, 0.77f, 0.42f, 1f),
            TextAlignment.Center);
        infoBody = CreateText(infoRoot, "Information text",
            "Aurelle CarConcept showcase model.\nAurelle CarConcept namoyish modeli.",
            new Vector3(0f, -0.10f, 0f), 0.021f, new Color(0.98f, 0.985f, 1f, 1f), TextAlignment.Center);

        CreateFrame(componentInfoRoot, "Component information frame", 1.80f, 0.92f);
        componentInfoHeading = CreateText(componentInfoRoot, "Component information heading",
            "COMPONENT DETAILS", new Vector3(0f, 0.30f, 0f), 0.038f,
            new Color(1f, 0.77f, 0.42f, 1f), TextAlignment.Center);
        componentInfoBody = CreateText(componentInfoRoot, "Component explanation",
            "Select a component to view Remy's explanation.\nRemy izohini ko‘rish uchun qismni tanlang.",
            new Vector3(0f, -0.12f, 0f), 0.027f, new Color(0.98f, 0.985f, 1f, 1f),
            TextAlignment.Center);

        CreateFrame(greetingRoot, "Greeting frame", 0.98f, 0.44f);
        greetingHeading = CreateText(greetingRoot, "Greeting heading", "WELCOME",
            new Vector3(0f, 0.12f, 0f), 0.030f, new Color(1f, 0.77f, 0.42f, 1f),
            TextAlignment.Center);
        greetingBody = CreateText(greetingRoot, "Greeting message",
            "I can explain the CarConcept.",
            new Vector3(0f, -0.09f, 0f), 0.021f, new Color(0.98f, 0.985f, 1f, 1f),
            TextAlignment.Center);

        CreateFrame(nameplateRoot, "Nameplate frame", 0.74f, 0.24f);
        nameplateText = CreateText(nameplateRoot, "Presenter name", "REMY",
            Vector3.zero, 0.034f, new Color(1f, 0.77f, 0.42f, 1f), TextAlignment.Center);
    }

    private static void DestroyStaleRuntimeObject(string objectName)
    {
        // Include inactive runtime roots left by Enter Play Mode with domain or
        // scene reload disabled; never touch prefab assets or imported models.
        foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (candidate == null || !candidate.scene.IsValid() || candidate.name != objectName)
                continue;
            candidate.SetActive(false);
            Destroy(candidate);
        }
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
        const float width = 0.84f;
        const float height = 0.15f;
        GameObject card = new GameObject(label + " Card");
        card.transform.SetParent(controlsRoot, false);
        card.transform.localPosition = localPosition;

        CreateBox(card.transform, label + " Card Background", new Vector3(0f, 0f, 0.025f),
            new Vector3(width, height, 0.025f), panelMaterial);
        CreateCardBorder(card.transform, width, height);

        BoxCollider collider = card.AddComponent<BoxCollider>();
        collider.size = new Vector3(width, height, 0.10f);
        RegisterCommandCollider(card, collider, command);

        CreateText(card.transform, label + " Label", label, new Vector3(0f, 0.028f, 0f),
            0.018f, Color.white, TextAlignment.Center);
        CreateText(card.transform, label + " Hint", hint, new Vector3(0f, -0.036f, 0f),
            0.012f, new Color(0.90f, 0.91f, 0.93f, 1f), TextAlignment.Center);
    }

    private TextMesh CreateLanguageButton(string label, Command command, float x)
    {
        const float width = 0.32f;
        const float height = 0.075f;
        GameObject button = new GameObject(label + " Language Button");
        button.transform.SetParent(controlsRoot, false);
        button.transform.localPosition = new Vector3(x, -0.72f, 0f);
        CreateBox(button.transform, "Button Background", new Vector3(0f, 0f, 0.025f),
            new Vector3(width, height, 0.025f), panelMaterial);
        CreateCardBorder(button.transform, width, height);
        BoxCollider collider = button.AddComponent<BoxCollider>();
        collider.size = new Vector3(width, height, 0.10f);
        RegisterCommandCollider(button, collider, command);
        return CreateText(button.transform, "Button Label", label,
            new Vector3(0f, 0f, 0f), 0.019f, Color.white, TextAlignment.Center);
    }

    private void RegisterCommandCollider(GameObject button, BoxCollider collider, Command command)
    {
        commandByCollider[collider] = command;
        XRSimpleInteractable xrButton = button.AddComponent<XRSimpleInteractable>();
        xrButton.interactionManager = interactionManager;
        xrButton.colliders.Clear();
        xrButton.colliders.Add(collider);
        xrButton.selectEntered.AddListener(_ => RunCommand(command));
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
        // Put glyphs just in front of their dark panel/card surfaces to avoid
        // depth overlap while keeping every label parented to the same facing root.
        label.transform.localPosition = localPosition + Vector3.forward * 0.055f;
        // Unity TextMesh glyphs face their local -Z side. The panel roots are
        // aimed toward the camera, so turn only the glyph plane toward it.
        label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        TextMesh mesh = label.AddComponent<TextMesh>();
        mesh.text = WrapText(text, 29);
        mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        mesh.fontSize = 36;
        mesh.characterSize = characterSize;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = alignment;
        mesh.lineSpacing = 1.10f;
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

    private void InitializeCameraNavigation()
    {
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool found = false;
        foreach (Part part in parts)
        foreach (Renderer renderer in part.Renderers)
        {
            if (renderer == null) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        cameraPivot = found ? bounds.center : carRoot.position;
        cameraUp = carRoot.TransformDirection(Vector3.forward).normalized;
        if (cameraUp.sqrMagnitude < 0.5f) cameraUp = Vector3.up;
        if (viewCamera != null)
            cameraOrbitDistance = Mathf.Max(2f, Vector3.Distance(viewCamera.transform.position, cameraPivot));
    }

    private void UpdateCameraNavigation()
    {
        // Existing XR locomotion, snap turn, head tracking and controller
        // bindings remain in charge in headset mode; mouse navigation is desktop-only.
        if (XRSettings.isDeviceActive || DesktopWalkthroughController.IsDesktopActive || viewCamera == null) return;
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 delta = mouse.delta.ReadValue();
        if (mouse.rightButton.isPressed && delta.sqrMagnitude > 0.001f) OrbitCamera(delta);
        if (mouse.middleButton.isPressed && delta.sqrMagnitude > 0.001f) PanCamera(delta);
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f) ZoomCamera(scroll);
    }

    private void OrbitCamera(Vector2 delta)
    {
        Transform cameraTransform = viewCamera.transform;
        Vector3 offset = cameraTransform.position - cameraPivot;
        float distance = Mathf.Clamp(offset.magnitude, 2f, 100f);
        offset = Quaternion.AngleAxis(delta.x * 0.24f, cameraUp) *
                 Quaternion.AngleAxis(-delta.y * 0.24f, cameraTransform.right) * offset;
        float elevation = Mathf.Asin(Mathf.Clamp(Vector3.Dot(offset.normalized, cameraUp), -1f, 1f))
                          * Mathf.Rad2Deg;
        elevation = Mathf.Clamp(elevation, -25f, 78f);
        Vector3 horizontal = Vector3.ProjectOnPlane(offset, cameraUp);
        if (horizontal.sqrMagnitude < 0.0001f)
            horizontal = Vector3.ProjectOnPlane(cameraTransform.forward, cameraUp);
        horizontal.Normalize();
        float radians = elevation * Mathf.Deg2Rad;
        offset = (horizontal * Mathf.Cos(radians) + cameraUp * Mathf.Sin(radians)) * distance;
        cameraTransform.position = cameraPivot + offset;
        cameraTransform.rotation = Quaternion.LookRotation(-offset.normalized, cameraUp);
        cameraOrbitDistance = distance;
    }

    private void PanCamera(Vector2 delta)
    {
        Transform cameraTransform = viewCamera.transform;
        float scale = Mathf.Clamp(cameraOrbitDistance, 2f, 100f) * 0.0016f;
        Vector3 movement = (-cameraTransform.right * delta.x + cameraTransform.up * delta.y) * scale;
        cameraTransform.position += movement;
        cameraPivot += movement;
    }

    private void ZoomCamera(float scroll)
    {
        Transform cameraTransform = viewCamera.transform;
        Vector3 offset = cameraTransform.position - cameraPivot;
        cameraOrbitDistance = Mathf.Clamp(offset.magnitude * Mathf.Exp(-scroll * 0.0012f), 2f, 100f);
        cameraTransform.position = cameraPivot + offset.normalized * cameraOrbitDistance;
        cameraTransform.rotation = Quaternion.LookRotation(-offset.normalized, cameraUp);
    }

    private void FollowCamera()
    {
        if (viewCamera == null)
            return;

        Transform cameraTransform = viewCamera.transform;
        Vector3 cameraRight = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        if (cameraRight.sqrMagnitude < 0.001f)
            cameraRight = Vector3.ProjectOnPlane(carRoot.right, Vector3.up).normalized;
        Vector3 towardCamera = Vector3.ProjectOnPlane(cameraTransform.position - heroDisplayBounds.center,
            Vector3.up).normalized;
        if (towardCamera.sqrMagnitude < 0.001f)
            towardCamera = -cameraTransform.forward;
        if (controlsRoot != null)
        {
            float carHalfWidth = Mathf.Abs(cameraRight.x) * heroDisplayBounds.extents.x +
                                 Mathf.Abs(cameraRight.z) * heroDisplayBounds.extents.z;
            controlsRoot.position = heroDisplayBounds.center + cameraRight * (carHalfWidth + 0.62f) +
                                    Vector3.up * 0.12f;
            controlsRoot.rotation = Quaternion.LookRotation(towardCamera, Vector3.up);
        }

        if (infoRoot != null)
        {
            float carHalfWidth = Mathf.Abs(cameraRight.x) * heroDisplayBounds.extents.x +
                                 Mathf.Abs(cameraRight.z) * heroDisplayBounds.extents.z;
            Vector3 infoPosition = heroDisplayBounds.center - cameraRight * (carHalfWidth + 0.68f) +
                                   Vector3.up * 0.12f;
            infoRoot.position = infoPosition;
            infoRoot.rotation = Quaternion.LookRotation(towardCamera, Vector3.up);
        }

        if (componentInfoRoot != null && presenterRoot != null)
        {
            Vector3 presenterSide = Vector3.ProjectOnPlane(presenterRoot.right, Vector3.up).normalized;
            if (presenterSide.sqrMagnitude < 0.001f)
                presenterSide = cameraRight;
            componentInfoRoot.position = presenterRoot.position + presenterSide * 1.05f +
                                         Vector3.up * 1.00f;
            Vector3 componentPanelFacing = Vector3.ProjectOnPlane(
                cameraTransform.position - componentInfoRoot.position, Vector3.up).normalized;
            if (componentPanelFacing.sqrMagnitude < 0.001f)
                componentPanelFacing = towardCamera;
            Quaternion panelTargetRotation = Quaternion.LookRotation(componentPanelFacing, Vector3.up);
            if (!componentPanelFacingInitialized)
            {
                componentInfoRoot.rotation = panelTargetRotation;
                componentPanelFacingInitialized = true;
            }
            else
                componentInfoRoot.rotation = Quaternion.RotateTowards(componentInfoRoot.rotation,
                    panelTargetRotation, 540f * Time.deltaTime);
        }

        if (greetingRoot != null && presenterRoot != null)
        {
            Vector3 greetingSide = Vector3.ProjectOnPlane(cameraRight, Vector3.up).normalized;
            greetingRoot.position = presenterRoot.position + Vector3.up * 1.10f + greetingSide * 0.72f;
            Vector3 greetingFacing = Vector3.ProjectOnPlane(cameraTransform.position - greetingRoot.position,
                Vector3.up).normalized;
            if (greetingFacing.sqrMagnitude < 0.001f)
                greetingFacing = towardCamera;
            greetingRoot.rotation = Quaternion.LookRotation(greetingFacing, Vector3.up);
        }
    }

    private void UpdateNameplate()
    {
        if (nameplateRoot == null || presenterRoot == null)
            return;

        nameplateRoot.gameObject.SetActive(presenterRoot.gameObject.activeInHierarchy);
        if (!presenterRoot.gameObject.activeInHierarchy)
            return;

        if (viewCamera == null || !viewCamera.isActiveAndEnabled)
            viewCamera = ResolveActiveViewCamera();
        if (viewCamera == null)
            return;

        if (presenterAnimator == null)
        {
            presenterAnimator = presenterRoot.GetComponentInChildren<Animator>(true);
            if (presenterAnimator != null && presenterAnimator.isHuman)
                presenterHeadAnchor = presenterAnimator.GetBoneTransform(HumanBodyBones.Head);
        }

        Vector3 headPosition;
        if (presenterHeadAnchor != null)
            headPosition = presenterHeadAnchor.position;
        else
        {
            Bounds bounds = GetPresenterWorldBounds();
            headPosition = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }

        nameplateRoot.position = headPosition + Vector3.up * 0.25f;
        Vector3 toCamera = Vector3.ProjectOnPlane(viewCamera.transform.position - nameplateRoot.position,
            Vector3.up).normalized;
        if (toCamera.sqrMagnitude < 0.001f)
            toCamera = Vector3.ProjectOnPlane(-viewCamera.transform.forward, Vector3.up).normalized;
        if (toCamera.sqrMagnitude > 0.001f)
            nameplateRoot.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
    }

    private Bounds GetPresenterWorldBounds()
    {
        if (presenterRoot == null)
            return new Bounds(heroDisplayBounds.center + Vector3.up * 1.5f, Vector3.one * 0.5f);
        Renderer[] renderers = presenterRoot.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        Bounds bounds = new Bounds(presenterRoot.position + Vector3.up, Vector3.one * 0.5f);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (!found) bounds.center = presenterRoot.position + Vector3.up;
        return bounds;
    }

    private void UpdatePresenterGreeting()
    {
        if (greetingShown && Time.unscaledTime >= greetingHideTime &&
            (presenterAudioSource == null || !presenterAudioSource.isPlaying))
        {
            greetingShown = false;
            if (greetingRoot != null)
                greetingRoot.gameObject.SetActive(false);
        }

        if (greetingCompleted || greetingShown || presenterRoot == null || viewCamera == null ||
            Time.unscaledTime < nextGreetingCheck)
            return;

        nextGreetingCheck = Time.unscaledTime + 0.25f;
        if (Vector3.Distance(viewCamera.transform.position, presenterRoot.position) > greetingDistance)
            return;

        greetingShown = true;
        greetingCompleted = true;
        greetingHideTime = Time.unscaledTime + Mathf.Max(greetingDisplayDuration,
            greetingClip != null ? greetingClip.length : 0f);
        infoDismissed = false;
        UpdatePanel();
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
        if (presenterAgent != null)
            presenterAgent.SetExplanationTarget(null);
        HideGreeting(true);
        StopVoice();
        infoDismissed = true;
        UpdatePanel();
    }

    private void DismissGreeting()
    {
        HideGreeting(greetingShown);
    }

    private void HideGreeting(bool completeGreeting)
    {
        if (completeGreeting || greetingShown)
            greetingCompleted = true;
        greetingShown = false;
        greetingHideTime = 0f;
        if (greetingRoot != null)
            greetingRoot.gameObject.SetActive(false);
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

        Physics.SyncTransforms();

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
            case Command.English: SetLanguage(false); break;
            case Command.Uzbek: SetLanguage(true); break;
        }
    }

    private void SetLanguage(bool uzbek)
    {
        showUzbek = uzbek;
        UpdatePanel();
    }

    public void Explode()
    {
        if (transitionT < 1f || currentLevel >= MaxExplodeLevel) return;
        BeginTransition(currentLevel + 1);
    }

    public void Restore()
    {
        if (transitionT < 1f) return;
        ClearHighlight(selectedPart);
        selectedPart = null;
        if (presenterAgent != null)
            presenterAgent.SetExplanationTarget(null);
        HideGreeting(greetingShown);
        StopVoice();
        infoDismissed = false;
        UpdatePanel();
        if (currentLevel > 0)
            BeginTransition(currentLevel - 1);
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
        if (transitionTargetLevel == 3 && engineRoot != null && trackByTransform.TryGetValue(engineRoot, out MotionTrack engineTrack))
        {
            Vector3 forward = viewCamera != null
                ? Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized
                : Vector3.ProjectOnPlane(carRoot.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = carRoot.forward;
            Vector3 extraction = Vector3.up * 1.65f - forward * 1.25f;
            engineTrack.StepOffsets[3] = engineRoot.parent.InverseTransformVector(extraction);
        }
        if (transitionTargetLevel == MaxExplodeLevel)
        {
            SetCarRootMeshVisibility(MaxExplodeLevel);
            BuildFinalAtlasLayout();
            PrepareAtlasParts();
        }
        else if (atlasRoot != null)
        {
            RestoreAtlasHierarchy();
        }

        reenableGrabsAfterTransition.Clear();
        foreach (Part part in parts)
        {
            if (part.GrabInteractable != null && part.GrabInteractable.isSelected)
            {
                part.GrabInteractable.enabled = false;
                reenableGrabsAfterTransition.Add(part.GrabInteractable);
            }
        }

        transitionT = 0f;
        foreach (MotionTrack track in motionTracks)
        {
            track.FromPosition = track.Target.localPosition;
            track.FromWorldPosition = track.Target.position;
            track.FromRotation = track.Target.localRotation;
            track.FromScale = track.Target.localScale;
        }
    }

    private void AnimateParts()
    {
        if (transitionT >= 1f) return;
        transitionT = Mathf.Min(1f, transitionT + Time.deltaTime / Mathf.Max(0.1f, transitionDuration));
        float eased = transitionT * transitionT * (3f - 2f * transitionT);
        foreach (MotionTrack track in motionTracks)
        {
            if (transitionTargetLevel == MaxExplodeLevel && track.UsesAtlasPosition)
            {
                track.Target.position = Vector3.LerpUnclamped(
                    track.FromWorldPosition, track.AtlasWorldPosition, eased);
                track.Target.localRotation = Quaternion.SlerpUnclamped(
                    track.FromRotation, track.AtlasLocalRotation, eased);
            }
            else
            {
                Vector3 offset = Vector3.zero;
                if (transitionTargetLevel < MaxExplodeLevel)
                    for (int level = 1; level <= transitionTargetLevel; level++)
                        offset += track.StepOffsets[level];
                track.Target.localPosition = Vector3.LerpUnclamped(
                    track.FromPosition, track.OriginalPosition + offset, eased);

                Quaternion targetRotation = track.IsHood && transitionTargetLevel >= 2 &&
                                            transitionTargetLevel < MaxExplodeLevel
                    ? track.HoodLocalRotation : track.OriginalRotation;
                track.Target.localRotation = Quaternion.SlerpUnclamped(
                    track.FromRotation, targetRotation, eased);
            }
            Vector3 targetScale = transitionTargetLevel == MaxExplodeLevel && track.UsesAtlasPosition
                ? track.AtlasLocalScale : track.OriginalScale;
            track.Target.localScale = Vector3.LerpUnclamped(
                track.FromScale, targetScale, eased);
        }

        if (transitionTargetLevel == MaxExplodeLevel)
        {
            foreach (Part part in parts)
            {
                if (part.AtlasProxy == null) continue;
                part.AtlasProxy.position = Vector3.Lerp(part.AtlasFromWorldPosition,
                    trackByTransform[part.Target].AtlasWorldPosition, eased);
                part.AtlasProxy.rotation = Quaternion.Slerp(part.AtlasFromWorldRotation,
                    part.AtlasWorldRotation, eased);
                part.AtlasProxy.localScale = Vector3.Lerp(part.AtlasFromLocalScale,
                    part.AtlasWorldScale, eased);
            }
        }

        if (transitionT >= 1f)
        {
            currentLevel = transitionTargetLevel;
            SetCarRootMeshVisibility(currentLevel);
            if (currentLevel == MaxExplodeLevel)
            {
                foreach (Part part in parts)
                    if (part.AtlasProxy != null)
                        foreach (Renderer source in part.AtlasOriginalRenderers)
                            if (source != null) source.enabled = false;
                FitAtlasRootToCamera();
            }
            if (currentLevel == 0) RestoreExactTransforms();
            foreach (XRGrabInteractable grab in reenableGrabsAfterTransition)
                if (grab != null) grab.enabled = true;
            reenableGrabsAfterTransition.Clear();
            UpdatePanel();
        }
    }

    private void RestoreExactTransforms()
    {
        if (atlasRoot != null) RestoreAtlasHierarchy();
        foreach (MotionTrack track in motionTracks)
        {
            if (track.Target == null) continue;
            track.Target.localPosition = track.OriginalPosition;
            track.Target.localRotation = track.OriginalRotation;
            track.Target.localScale = track.OriginalScale;
        }
    }

        private void SelectPart(Part part)
    {
        if (selectedPart == part)
            return;

        ClearHighlight(selectedPart);
        selectedPart = part;
        if (selectedPart != null)
            HideGreeting(true);
        if (presenterAgent != null)
            presenterAgent.SetExplanationTarget(selectedPart != null ? selectedPart.Target : null);
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
            levelText.text = WrapText((showUzbek ? "BOSQICH " : "STAGE ") + (currentLevel + 1) + " / 7 — " +
                (showUzbek ? StageTitlesUzbek[currentLevel] : StageTitles[currentLevel]), 28);
        if (englishLanguageLabel != null)
            englishLanguageLabel.color = showUzbek ? Color.white : new Color(0.94f, 0.72f, 0.39f, 1f);
        if (uzbekLanguageLabel != null)
            uzbekLanguageLabel.color = showUzbek ? new Color(0.94f, 0.72f, 0.39f, 1f) : Color.white;

        UpdateGreetingPanel();

        bool showOverview = !infoDismissed && selectedPart == null;
        bool showComponentInfo = !infoDismissed && selectedPart != null;
        if (infoRoot != null)
            infoRoot.gameObject.SetActive(showOverview);
        if (componentInfoRoot != null)
            componentInfoRoot.gameObject.SetActive(showComponentInfo);

        if (showOverview && infoHeading != null && infoBody != null)
        {
            infoHeading.text = showUzbek ? "AVTOMOBIL HAQIDA" : "VEHICLE OVERVIEW";
            infoBody.text = WrapText(GetLocalizedBilingualText(
                "Aurelle CarConcept showcase model.\nAurelle CarConcept namoyish modeli."), 24);
        }

        if (showComponentInfo && componentInfoHeading != null && componentInfoBody != null)
        {
            componentInfoHeading.text = WrapText(selectedPart.DisplayName, 24);
            componentInfoBody.text = WrapText(GetLocalizedBilingualText(
                GetBilingualPartExplanation(selectedPart)), 28);
        }
    }

    private void UpdateGreetingPanel()
    {
        if (greetingRoot == null)
            return;

        greetingRoot.gameObject.SetActive(greetingShown);
        if (greetingHeading == null || greetingBody == null)
            return;

        greetingHeading.text = showUzbek ? "XUSH KELIBSIZ" : "WELCOME";
        greetingBody.text = showUzbek
            ? "CarConcept haqida tushuntiraman."
            : "I can explain the CarConcept.";
    }

    private string GetLocalizedBilingualText(string text)
    {
        string[] languages = text.Split('\n');
        return showUzbek && languages.Length > 1 ? languages[1] : languages[0];
    }

    private static string LimitText(string text, int limit)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= limit) return text;
        return text.Substring(0, limit - 1) + "…";
    }

    private static string GetBilingualPartExplanation(Part part)
    {
        if (part.IsV6Part)
            return part.Target.name.IndexOf("V6_Engine", StringComparison.OrdinalIgnoreCase) >= 0
                ? "The V6 engine assembly is shown separately for inspection.\nV6 dvigateli ko‘rik uchun alohida ko‘rsatilgan."
                : "This part belongs to the V6 engine assembly.\nBu qism V6 dvigatel yig‘masiga tegishli.";
        string category = GetAtlasCategory(part);
        if (category == "Cabin")
            return "This part is part of the cabin and driver controls.\nBu qism salon va haydovchi boshqaruviga tegishli.";
        if (category == "Wheels and Running Gear")
            return "Wheel and tire assembly for the vehicle.\nAvtomobilning g‘ildirak va shina yig‘masi.";
        return "This part is part of the vehicle's exterior body.\nBu qism avtomobil tashqi kuzoviga tegishli.";
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
        RestoreExactTransforms();
        foreach (Part part in parts)
            if (part.OwnsCollider && part.SelectionCollider != null)
                Destroy(part.SelectionCollider);
        if (ownsAudioSource && presenterAudioSource != null)
            Destroy(presenterAudioSource);
        if (ownedInteractionManager != null)
            Destroy(ownedInteractionManager.gameObject);
        if (controlsRoot != null)
            Destroy(controlsRoot.gameObject);
        if (infoRoot != null)
            Destroy(infoRoot.gameObject);
        if (panelMaterial != null)
            Destroy(panelMaterial);
        if (goldMaterial != null)
            Destroy(goldMaterial);
    }
}
