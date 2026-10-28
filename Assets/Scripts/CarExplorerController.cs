using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// VR/desktop explorer of the CarConcept and its V6 as a structure tree (vehicle, systems, assemblies,
/// sub-assemblies, parts), in three views:
/// <list type="bullet">
/// <item>Normal - the complete car.</item>
/// <item>X-ray (T) - the body shell fades into tinted glass (Resources/AurelleXRayGhost.shader) and the engine,
/// cabin, wheels, floor and internals show through it in their own materials.</item>
/// <item>Exploded (O) - every assembly out of the car as one unit, in 3D around it.</item>
/// </list>
/// Drill-down: a click on a part takes its assembly out of the car in one piece; the next click on it makes the
/// assembly come apart one level, and every further click opens the level that holds the clicked part, down to the
/// single part. Every transform and material is restored exactly. Implements <see cref="ICarExplorerModel"/> for the
/// HUD, which owns every on-screen panel.
/// </summary>
public sealed partial class CarExplorerController : MonoBehaviour, ICarExplorerModel
{
    [SerializeField] private Transform carRoot;
    [SerializeField] private Camera viewCamera;
    [SerializeField, Min(0.1f)] private float transitionDuration = 0.9f;
    [SerializeField, Min(0.1f)] private float explodeDuration = 1.5f;
    [SerializeField, Min(0.1f)] private float ghostFadeDuration = 0.6f;
    [SerializeField, Min(1f)] private float rayDistance = 20f;
    // A soft top light over the car in the X-ray view, so the dark cabin and the engine read through the glass.
    [SerializeField, Min(0f)] private float xrayLightIntensity = 2.4f;

    // A mesh object of the car: the renderers on one transform.
    private sealed class Part
    {
        public Transform Target;
        public string DisplayName;
        public string Path;
        public Collider SelectionCollider;
        public Renderer[] Renderers;
        public bool OwnsCollider;
        // Mesh bounds in the target's own space, so the box turns with the part.
        public Bounds LocalBounds;
        public bool IsV6Part;
        // The V6 and the generated internals: inside the body, drawn only where they can be seen.
        public bool IsInner;
        // Body skin: turns to glass in the X-ray view.
        public bool IsShell;
        public Node Leaf;
        public Node Assembly;
        // Exact mesh collider in the explorer's own physics scene (ProcessRay); null for a part without a readable
        // mesh (its box picks it).
        public MeshCollider PickCollider;
        public XRGrabInteractable GrabInteractable;
        // Highlight: 0 none, 1 hover (HUD), 2 selected, 3 dimmed (outside the level that is apart).
        public int Tint;
        // Equal to placementStamp while the part is an obstacle for the node being placed (GatherPartBoxes).
        public int PlacementStamp;
        // Pose a grabbed part glides back to after release (local to its parent).
        public Vector3 HomeLocalPosition;
        public Quaternion HomeLocalRotation;
        public Vector3 HomeLocalScale;
        public Vector3 ReturnFromPosition;
        public Quaternion ReturnFromRotation;
        public Vector3 ReturnFromScale;
        public float ReturnT = 1f;
    }

    private enum AssemblyKind
    {
        Generic, Wheel, Door, Front, Rear, Roof, Windscreen, BodyFrame, Seats, Cockpit, Floor, Chassis, Engine
    }

    // A node of the structure tree with its motion: its offset from its parent's place (world space), which is the
    // assembly's move out of the car for an assembly and the spread of its level for the nodes below.
    private sealed class Node
    {
        public readonly CarStructureNode Info = new CarStructureNode();
        public Node Parent;
        public readonly List<Node> Children = new List<Node>();
        // Parts that are this node's own (leaves), and every part under it.
        public readonly List<Part> Parts = new List<Part>();
        public readonly List<Part> AllParts = new List<Part>();
        public Node Assembly;
        public AssemblyKind Kind;
        public string Key;
        public CarPartInfo PartInfo;
        public Transform Source;
        public bool Generated;
        public SpreadHint Hint;
        public bool HintIsWorld;
        public float Distance = 1f;
        public Bounds RestBounds;
        public int Index;
        // Sibling order: rule groups by their table position, everything else by creation.
        public int Order;
        public int Created;
        public Vector3 SpreadOffset;
        // While this node is apart it stands this much further out of its parent's cluster (ComputePull).
        public Vector3 PullOffset;
        public Vector3 Offset, From, Target, World;
        public bool Moving, Spreading;
        public float Delay;
        // Assemblies only.
        public float Yaw, FromYaw, TargetYaw;
        public Vector3 YawPivot;
        public Vector3 FocusOffset;
        public float FocusYaw;
        public Vector3 FocusPivot;
        public Vector3 ExplodeOffset;
        public float ExplodeDelay;
        // -1 / +1: the car's left / right side; 0: on the centre line.
        public float Side;
        // Inside the body: taking it out runs through the shell, so the normal view turns to X-ray for it.
        public bool Enclosed;
        // False: it stays in place and only comes apart (the floor tub, the underbody that carries the car).
        public bool Relocates = true;
        public bool Inner;
        public bool InBay;
        public Vector3 Axle;
        // Inner content: its renderers as found, and whether they are drawn now.
        public Renderer[] InnerRenderers;
        public bool[] InnerEnabled;
        public ShadowCastingMode[] InnerShadows;
        public Collider[] InnerColliders;
        public bool InnerShown = true;
        public bool InnerNeededDuringMove;
    }

    private sealed class ControllerPose
    {
        public InputAction AimPosition;
        public InputAction AimRotation;
        public InputAction DevicePosition;
        public InputAction DeviceRotation;
    }

    // One animated transform (a part). Its pose is its rest pose plus its owner's world offset; a part under another
    // moving part keeps its offset relative to that one (and turns with it).
    private sealed class MotionTrack
    {
        public Transform Node;
        public MotionTrack ParentTrack;
        public Node Owner;
        public Node Assembly;
        public Vector3 RestLocalPosition;
        public Quaternion RestLocalRotation;
        public Vector3 RestLocalScale;
        public Vector3 RestWorldPosition;
        // The parent's world -> local matrix and world rotation at rest (a static or only moved parent).
        public Matrix4x4 ParentWorldToLocal;
        public Quaternion ParentRestRotation;
        // What a grab or an interrupted return left on top of the computed pose, faded out by the move.
        public Vector3 FromResidual;
        public Quaternion FromRotationResidual;
        public Vector3 FromScale;
        public bool Moving;
    }

    // One renderer of the body shell and its glass state (X-ray view).
    private sealed class GhostRenderer
    {
        public Renderer Renderer;
        public Part Part;
        // The renderer's own materials, read when it turns to glass and put back exactly when it turns back.
        public Material[] Original;
        public Material[] SolidPhase;
        public Material[] GlassPhase;
        public MaterialPropertyBlock[] Blocks;
        public ShadowCastingMode OriginalShadows;
        public bool Swapped;
        public bool GlassPhaseActive;
        // Inner layers (trims, inner hood and hatch panels, seals) are fainter glass than the outer skin.
        public float GlassOpacity = 1f;
        public float Amount;
        public float Target;
    }

    private struct RayHitEntry
    {
        public float Distance;
        public Collider Collider;
        public Vector3 Point;
        public bool Precise;
    }

    [Serializable]
    private sealed class PartVoiceCue
    {
        public string componentName;
        public AudioClip clip;
    }

    [SerializeField] private Transform presenterRoot;
    [SerializeField] private AudioSource presenterAudioSource;
    [SerializeField] private AudioClip greetingClip;
    [SerializeField] private List<PartVoiceCue> partVoiceCues = new List<PartVoiceCue>();
    [SerializeField, Min(0.5f)] private float greetingDistance = 2.8f;
    [SerializeField, Min(1f)] private float greetingDisplayDuration = 7f;
    private ShowroomPresenterAgent presenterAgent;

    // Declared first: tools look the parts up by their Target field.
    private readonly List<Part> parts = new List<Part>();
    private readonly List<Node> assemblies = new List<Node>();
    private readonly List<Node> allNodes = new List<Node>();
    private readonly List<MotionTrack> motionTracks = new List<MotionTrack>();
    private readonly List<Node> movingNodes = new List<Node>();
    private readonly List<MotionTrack> movingTracks = new List<MotionTrack>();
    private readonly Dictionary<Transform, MotionTrack> trackByTransform = new Dictionary<Transform, MotionTrack>();
    private readonly Dictionary<Transform, Part> partByTransform = new Dictionary<Transform, Part>();
    private readonly Dictionary<Collider, Part> partByCollider = new Dictionary<Collider, Part>();
    private readonly Dictionary<Collider, Part> partByPickCollider = new Dictionary<Collider, Part>();
    private readonly Dictionary<CarStructureNode, Node> nodeByInfo = new Dictionary<CarStructureNode, Node>();
    private readonly Dictionary<Transform, CarPartInfo> taggedByTransform = new Dictionary<Transform, CarPartInfo>();
    private readonly Dictionary<string, Node> nodesByKey = new Dictionary<string, Node>();
    private readonly Dictionary<string, Node> systemsByName = new Dictionary<string, Node>();
    private readonly HashSet<string> usedIds = new HashSet<string>();
    private int createdNodes;
    private readonly List<Transform> hierarchyScratch = new List<Transform>();
    private readonly List<Renderer> hiddenFineDetail = new List<Renderer>();
    private readonly List<InputAction> actions = new List<InputAction>();
    private readonly List<GhostRenderer> ghostRenderers = new List<GhostRenderer>();
    private readonly Dictionary<Renderer, GhostRenderer> ghostByRenderer = new Dictionary<Renderer, GhostRenderer>();
    private readonly List<Bounds> spreadBoxes = new List<Bounds>();
    private readonly List<Node> spreadOrder = new List<Node>();
    private readonly List<Bounds> explodeBoxes = new List<Bounds>();
    private readonly List<Node> explodeOrder = new List<Node>();
    private readonly List<Node> chainScratch = new List<Node>();
    private readonly List<Node> openPath = new List<Node>();
    private readonly List<CarStructureNode> openPathInfo = new List<CarStructureNode>();

    private InputAction selectAction;
    private InputAction leftSelectAction;
    private InputAction explodeAction;
    private InputAction restoreAction;
    private InputAction resetAction;
    private ControllerPose rightControllerPose;
    private ControllerPose leftControllerPose;
    private Transform engineRoot;
    private Transform internalsRoot;
    private Part carRootPart;
    private Node root;
    // The car's rest geometry centre and bounds (world), and its horizontal axes: toward the front section / V6,
    // and to the car's right.
    private Vector3 carRestCenter;
    private Bounds carRestBounds;
    private Vector3 carForward = Vector3.forward;
    private Vector3 carRight = Vector3.right;
    private XRInteractionManager ownedInteractionManager;
    private XRInteractionManager interactionManager;
    private XRSelectFilterDelegate partGrabFilter;
    private readonly List<Part> returningParts = new List<Part>();
    private readonly List<Material> materialScratch = new List<Material>();
    private MaterialPropertyBlock highlightBlock;
    private Camera[] cameraBuffer = new Camera[4];
    private Camera originCamera;
    private Transform originCameraParent;
    private Transform originCache;
    private Transform rayInteractorRig;
    private readonly List<XRRayInteractor> rayInteractors = new List<XRRayInteractor>();
    private float showroomFloorHeight;
    // The view, the assembly out of the car (as one unit or apart; in the exploded view the one taken apart in
    // place), the levels that are apart (openPath, outermost first), the selection and the HUD's hover.
    private CarExplorerView view = CarExplorerView.Normal;
    private Node takenOut;
    private Node selected;
    private Node hovered;
    // The deepest level that is apart; everything outside it is dimmed (null: nothing dimmed).
    private Node dimFocus;
    // The part last clicked or grabbed inside the selection (for voice cues and tools), or null.
    private Part selectedPart;
    // 0 normal, 1 X-ray, 2 exploded: kept as an int for tools that read the explorer's view.
    private int currentLevel;
    // Progress of the running move (1 = at rest in the current configuration).
    private float transitionT = 1f;
    private float transitionLength = 1f;
    // The part of the move each node takes (the move's length minus the largest start delay).
    private float transitionSpan = 1f;
    private float transitionClock;
    private bool phasedMotion;
    private bool ghostAnimating;
    private bool wasMoving;
    private Light xrayLight;
    private float xrayLightLevel;
    private float nextGreetingCheck;
    private float greetingHideTime;
    private float greetingShownTime;
    private bool greetingShown;
    private bool greetingCompleted;
    private bool showUzbek;
    private bool ownsAudioSource;
    private bool initialized;
    private bool registered;
    private Vector3 cameraPivot;
    private Vector3 cameraUp = Vector3.up;
    private float cameraOrbitDistance = 8f;
    // X-ray glass: the shared shader and its two render-state set-ups (see AurelleXRayGhost.shader).
    private Shader ghostShader;
    private bool ghostShaderUnavailable;
    private Material ghostSolidMaterial;
    private Material ghostGlassMaterial;
    // Exact picking: the car's meshes as colliders in a physics scene of their own, so nothing else (walkers, Remy,
    // XR casts) collides with them. Cooked on worker threads after start; until then the part boxes are used.
    private static int pickSceneCount;
    private Scene pickScene;
    private PhysicsScene pickPhysics;
    private JobHandle pickBakeHandle;
    private NativeArray<EntityId> pickBakeMeshIds;
    private bool pickBakeScheduled;
    private int pickBakeFrames;
    private bool pickReady;
    private bool applicationQuitting;
    private readonly List<Part> pickParts = new List<Part>();
    private readonly RaycastHit[] mainHits = new RaycastHit[96];
    private readonly RaycastHit[] pickHits = new RaycastHit[64];
    private RayHitEntry[] rayEntries = new RayHitEntry[160];
    private readonly Collider[] keepOutBuffer = new Collider[96];
    private GameObject premiumCollection;
    private readonly List<Bounds> keepOuts = new List<Bounds>();
    // Planned boxes of the drawn parts around a node being placed, and their parts (GatherPartBoxes).
    private readonly List<Bounds> partBoxes = new List<Bounds>();
    private readonly List<Part> partBoxOwners = new List<Part>();
    private int placementStamp;
    private readonly Collider[] overlapBuffer = new Collider[128];
    // The podium under the car (Circular Display), seen from above: the exploded view keeps the tagged assemblies on it.
    private bool hasPodium;
    private Vector2 podiumCenter;
    private float podiumRadius;
    // A desktop press while the inspect camera orbits: it picks on release unless the pointer was dragged.
    private bool pointerArmed;
    // The second press of a double click belongs to the walkthrough's inspect camera (it frames what the first
    // press selected); the same window as DesktopWalkthroughController's.
    private float lastPressTime = float.NegativeInfinity;
    private Vector2 lastPressPosition;
    private Vector2 pointerTravel;
    private Vector2 pointerPressPosition;
    private float pointerMaxTravel;
    // Levels opened (or an assembly placed) while CarExplorerInspect.Active were placed for the inspect camera; once
    // it is back on the walking body they are placed again for the walking eye (at replaceForWalkingAt).
    private bool placedWhileInspecting;
    private bool wasInspecting;
    private float replaceForWalkingAt = -1f;

    private const float GhostFadeShadowsAt = 0.5f;
    private const float FocusClearance = 0.4f;
    // A taken-out assembly keeps this far (horizontally) from the viewer's eye: close enough to read, not in the face.
    private const float FocusViewerClearance = 1.3f;
    private const float FocusMaxHeight = 3.3f;
    // Places computed while the inspect camera was the eye are computed again for the walking eye this long after
    // inspecting ends: DesktopWalkthroughController's flight back (inspectFlightSeconds, 0.6 s) and a frame or two.
    private const float InspectReturnSeconds = 0.7f;
    // An opening level may move its taken-out assembly at most this much further from the eye (RefitTakenOut).
    private const float RefitFurther = 1f;
    private const float ExplodeMaxHeight = 3.95f;
    // Parts stay this far above the floor or display (the display's LED ring stands 0.07 m proud).
    private const float FloorClearance = 0.09f;
    private const float ViewerCapsuleRadius = 0.55f;
    // Children of a node that comes apart keep this gap between their boxes; assemblies in the exploded view this one.
    private const float SiblingGap = 0.03f;
    private const float ExplodeGap = 0.05f;
    // A level pulled out toward the viewer keeps this far (horizontally) from their eye.
    private const float PullViewerClearance = 0.6f;
    private const float PullMinHeight = 0.8f;
    // An open level below the assembly comes toward the eye, to about this far from it (horizontally), but in the
    // exploded view never more than PullTravelMax from where it was; it turns around the eye by these steps (either
    // way) to find room.
    private const float PullEyeDistance = 1.3f;
    private const float PullTravelMax = 2.5f;
    private static readonly float[] PullTurns = { 0f, 12f, 24f, 36f, 50f };
    // Nearer first, then further (a visitor looking up sees a level below the eye only further away).
    private static readonly float[] PullDistanceSteps = { 0f, -0.25f, 0.35f, 0.7f };
    // What is placed for the visitor stays below the eye by this much, so it is looked at, not up to.
    private const float BelowEye = 0.25f;
    // A placement counts as in view when this share of its screen box lies in the free part of the view: the part the
    // HUD leaves free on the desktop (CarExplorerModels.HudSafeViewport), the central 70 % of the field of view in XR.
    private const float ViewShareRequired = 0.85f;
    private static readonly Rect XrViewArea = new Rect(0.15f, 0.15f, 0.7f, 0.7f);
    // Taken-out assemblies that cannot go their own way try every bearing round the car, at these heights.
    private const int RingBearings = 16;
    private static readonly float[] RingHeights = { 1.1f, 0.8f };
    private static readonly float[] GenericTurns = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 120f, -120f, 150f, -150f, 180f };
    // Tagged assemblies in the exploded view stay this far inside the podium's edge, on tiers this far apart.
    private const float PodiumInset = 0.2f;
    private const float ExplodeTierStep = 0.6f;
    private const int ExplodeTiers = 6;
    // While the inspect camera orbits on a left drag, a press that moves this far (pixels) is no click; the
    // walkthrough starts the orbit at the same travel (DesktopWalkthroughController.DragThresholdPixels).
    private const float ClickSlop = 4f;
    // Assembly move, then spread (or spread collapses, then the move back): the share of the move each takes.
    private const float FirstPhaseShare = 0.62f;
    private const float SecondPhaseStart = 0.32f;
    private const float HighlightAmount = 0.34f;
    private const float HoverAmount = 0.16f;
    // Dark materials get more of the tint (up to these amounts), and metal loses most of its metallic for the
    // selection (half as much for the hover): metal shows its surroundings, not its base colour.
    private const float HighlightAmountDark = 0.62f;
    private const float HoverAmountDark = 0.3f;
    private const float HighlightMetallic = 0.2f;
    // While a level is apart, the rest of the car darkens this much, so the open level reads clearly.
    private const float DimAmount = 0.4f;
    private static readonly Color HighlightTint = new Color(0.95f, 0.61f, 0.2f, 1f);
    private static readonly int GltfBaseColorId = Shader.PropertyToID("baseColorFactor");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int GltfBaseMapId = Shader.PropertyToID("baseColorTexture");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int GltfMetallicId = Shader.PropertyToID("metallicFactor");
    private static readonly int GltfRoughnessId = Shader.PropertyToID("roughnessFactor");
    private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    private static readonly int GhostApproxColorId = Shader.PropertyToID("_ApproxColor");
    private static readonly int GhostApproxMapId = Shader.PropertyToID("_ApproxMap");
    private static readonly int GhostApproxMapStId = Shader.PropertyToID("_ApproxMap_ST");
    private static readonly int GhostApproxSmoothnessId = Shader.PropertyToID("_ApproxSmoothness");
    private static readonly int GhostApproxMetallicId = Shader.PropertyToID("_ApproxMetallic");
    private static readonly int GhostFadeId = Shader.PropertyToID("_Fade");
    private static readonly int GhostHighlightId = Shader.PropertyToID("_Highlight");
    private static readonly int GhostZWriteId = Shader.PropertyToID("_ZWrite");
    private static readonly int GhostGlassOpacityId = Shader.PropertyToID("_GlassOpacity");
    private static readonly int GhostCullId = Shader.PropertyToID("_Cull");
    private const string GhostShaderResource = "AurelleXRayGhost";
    private const string GhostShaderName = "Aurelle/X-Ray Ghost";
    private const float GrabReturnDuration = 0.35f;
    // CarConcept node names: words that run together or are abbreviated, and the few names the rules cannot word well.
    private static readonly Dictionary<string, string> PartNameWords = new Dictionary<string, string>
    {
        { "Topgrill", "Top Grille" }, { "Floormats", "Floor Mats" }, { "Taillights", "Tail Lights" },
        { "Turnsignals", "Turn Signals" }, { "Rearwindow", "Rear Window" }, { "Dash", "Dashboard" },
        { "Accel", "Accelerator" }, { "Gasket", "Seal" }, { "Mid", "Centre" }, { "Under", "Underside" }
    };
    private static readonly string[] CabinPartWords = { "Dash", "Seats", "Steering", "Pedal", "Floormats" };
    private static readonly Dictionary<string, string> PartNameOverrides = new Dictionary<string, string>
    {
        { "CarConcept", "Underbody" }, { "BodyPanelsColor2", "Lower Body Panels" },
        { "BodyWindowsRearSides", "Rear Side Windows" }, { "BodyTaillightsPanels", "Tail Light Panels" },
        { "BodyWindshieldWipersBase", "Wiper Base" }, { "InteriorSteeringDashColumn", "Steering Column" },
        { "InteriorSeatsColor2", "Seat Trim" }, { "InteriorSeatsFrame", "Seat Frame" }
    };

    // ---------------------------------------------------------------- ICarExplorerModel

    public event Action Changed;

    public CarStructureNode Root => root != null ? root.Info : null;
    public CarExplorerView View => view;
    public CarStructureNode TakenOut => takenOut != null ? takenOut.Info : null;
    public IReadOnlyList<CarStructureNode> OpenPath => openPathInfo;
    public CarStructureNode Selected => selected != null ? selected.Info : null;
    public bool Uzbek => showUzbek;
    public bool IsMoving => transitionT < 1f || ghostAnimating;
    public bool IsXr => IsXrView();
    public Camera ViewCamera => viewCamera;
    public bool GreetingShown => greetingShown;

    public void SetView(CarExplorerView next)
    {
        if (!initialized)
            return;
        ChangeView(next);
    }

    public void Select(CarStructureNode node)
    {
        if (!initialized)
            return;
        SelectNode(NodeOf(node), null);
    }

    public void Open(CarStructureNode node)
    {
        if (!initialized)
            return;
        Node target = NodeOf(node);
        if (target == null)
            return;
        if (target.Assembly == null)
        {
            // The vehicle and the systems never move as a unit: they are only explained.
            SelectNode(target, null);
            return;
        }
        OpenTo(target, null, true);
    }

    public void CloseTo(int levels)
    {
        if (!initialized || levels < 0 || levels >= openPath.Count)
            return;
        openPath.RemoveRange(levels, openPath.Count - levels);
        AfterLevelsChanged();
        SelectNode(levels > 0 ? openPath[levels - 1] : takenOut, null);
    }

    public void PutBack()
    {
        if (!initialized || takenOut == null)
            return;
        Node assembly = takenOut;
        takenOut = null;
        openPath.Clear();
        SyncOpenPathInfo();
        StartStateMove(false);
        if (selected != null && selected.Assembly == assembly)
            SelectNode(null, null);
        NotifyChanged();
    }

    public void Back()
    {
        if (!initialized)
            return;
        HideGreeting(greetingShown);
        StopVoice();
        if (openPath.Count > 0)
            CloseTo(openPath.Count - 1);
        else if (takenOut != null)
            PutBack();
        else if (view == CarExplorerView.Exploded)
            ChangeView(CarExplorerView.XRay);
        else if (view == CarExplorerView.XRay)
            ChangeView(CarExplorerView.Normal);
        NotifyChanged();
    }

    public void ResetAll()
    {
        if (!initialized)
            return;
        SelectNode(null, null);
        StopVoice();
        takenOut = null;
        openPath.Clear();
        SyncOpenPathInfo();
        ChangeView(CarExplorerView.Normal, true);
    }

    public void SetLanguage(bool uzbek)
    {
        if (showUzbek == uzbek)
            return;
        showUzbek = uzbek;
        NotifyChanged();
    }

    public void Highlight(CarStructureNode node)
    {
        if (!initialized)
            return;
        Node next = NodeOf(node);
        if (next == hovered)
            return;
        Node previous = hovered;
        hovered = next;
        RefreshTints(previous);
        RefreshTints(hovered);
    }

    public void DismissGreeting()
    {
        HideGreeting(greetingShown);
    }

    public bool TryGetNodeBounds(CarStructureNode info, out Bounds bounds)
    {
        Node node = NodeOf(info);
        bounds = default;
        if (node == null || node.AllParts.Count == 0)
            return false;
        // Inner content the current state does not draw (the V6 in the closed car) has nothing to frame.
        if (node.Assembly != null && node.Assembly.Inner && !IsInnerNeededAtRest(node.Assembly))
            return false;
        bounds = GetPlannedBounds(node, true);
        return true;
    }

    private Node NodeOf(CarStructureNode info)
    {
        return info != null && nodeByInfo.TryGetValue(info, out Node node) ? node : null;
    }

    private void NotifyChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private void SyncOpenPathInfo()
    {
        openPathInfo.Clear();
        foreach (Node node in openPath)
            openPathInfo.Add(node.Info);
    }

    // ---------------------------------------------------------------- lifecycle

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
            presenterAgent = presenterRoot.GetComponent<ShowroomPresenterAgent>();
        if (presenterAudioSource == null && presenterRoot != null)
            presenterAudioSource = presenterRoot.GetComponent<AudioSource>();

        if (carRoot != null)
        {
            engineRoot = carRoot.Find("V6_Engine_Prototype(Clone)");
            if (engineRoot == null)
            {
                foreach (Transform candidate in carRoot.GetComponentsInChildren<Transform>(true))
                    if (candidate.name.IndexOf("V6", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        candidate.GetComponentInChildren<Renderer>(true) != null)
                    { engineRoot = candidate; break; }
            }
            internalsRoot = carRoot.Find("Internals");
        }

        if (carRoot == null)
        {
            Debug.LogError("[Car Explorer] Could not find the existing CarConcept root.");
            enabled = false;
            return;
        }

        BuildComponentRegistry();
        if (parts.Count == 0)
        {
            Debug.LogError("[Car Explorer] No enabled mesh components were found.");
            enabled = false;
            return;
        }
        showroomFloorHeight = GetShowroomFloorHeight();
        BuildStructure();
        FindPodium();
        BuildGhostRenderers();
        UpdateInnerRendering(true);
        EnsureXRGrabbables();
        SchedulePickScene();
        InitializeCameraNavigation();
        DestroyStaleRuntimeObject("Car Explorer X-Ray Light");
        BuildInputActions();
        initialized = true;
    }

    private void OnEnable()
    {
        foreach (InputAction action in actions)
            action.Enable();
        if (!initialized)
            return;
        SetFineDetailHidden(true);
        UpdateInnerRendering(true);
        RegisterModel();
        NotifyChanged();
    }

    private void OnDisable()
    {
        foreach (InputAction action in actions)
            action.Disable();
        UnregisterModel();
        if (!initialized)
            return;

        // Leave no highlight, pointing gesture or voice behind while the explorer is off, and the car exactly as
        // authored: assembled, in its own materials, every renderer as found.
        hovered = null;
        dimFocus = null;
        SelectNode(null, null);
        foreach (Part part in parts)
            ClearHighlight(part);
        StopVoice();
        ReleaseGrabbedParts();
        SnapToRest();
        SyncOpenPathInfo();
        RestoreInnerRendering();
        SetFineDetailHidden(false);
    }

    private void RegisterModel()
    {
        if (registered)
            return;
        registered = true;
        CarExplorerModels.Register(this);
    }

    private void UnregisterModel()
    {
        if (!registered)
            return;
        registered = false;
        CarExplorerModels.Unregister(this);
    }

    private void Update()
    {
        if (!initialized)
            return;

        // The view can move to another camera at runtime (the player's XR -> desktop fallback, or back);
        // everything below reads only the resolved one.
        Camera activeViewCamera = ResolveActiveViewCamera();
        if (activeViewCamera != null && activeViewCamera != viewCamera)
        {
            viewCamera = activeViewCamera;
            cameraOrbitDistance = Mathf.Max(2f, Vector3.Distance(viewCamera.transform.position, cameraPivot));
            NotifyChanged();
        }

        CompletePickScene();
        UpdatePlacementAfterInspect();
        AnimateParts();
        UpdateGhosts();
        UpdateXRayLight();
        UpdateGrabReturns();
        UpdateCameraNavigation();
        UpdatePresenterGreeting();
        bool moving = IsMoving;
        if (moving != wasMoving)
        {
            wasMoving = moving;
            NotifyChanged();
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.tKey.wasPressedThisFrame) ToggleXRay();
            if (keyboard.oKey.wasPressedThisFrame) ExplodeAll();
            if (keyboard.rKey.wasPressedThisFrame) Back();
            if (keyboard.qKey.wasPressedThisFrame) SetLanguage(false);
            if (keyboard.uKey.wasPressedThisFrame) SetLanguage(true);
            // Arrow keys belong to the walkthrough.
            if (keyboard.rightBracketKey.wasPressedThisFrame || keyboard.pageUpKey.wasPressedThisFrame) ExplodeAll();
            if (keyboard.leftBracketKey.wasPressedThisFrame || keyboard.pageDownKey.wasPressedThisFrame) Back();
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                DismissGreeting();
                // While inspecting, Esc only takes the camera back to walking (DesktopWalkthroughController publishes
                // the change after this frame's Updates); the selection stays.
                if (!CarExplorerInspect.Active)
                    SelectNode(null, null);
            }
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && viewCamera != null)
            HandleDesktopPointer(mouse.leftButton.wasPressedThisFrame, mouse.leftButton.wasReleasedThisFrame,
                mouse.delta.ReadValue(), mouse.position.ReadValue());
        else
            pointerArmed = false;

        if (selectAction != null && selectAction.WasPressedThisFrame() && !CarExplorerModels.PointerOverHud &&
            !IsRayOverUi(InteractorHandedness.Right))
            ProcessRay(GetSelectionRay(rightControllerPose));
        if (leftSelectAction != null && leftSelectAction.WasPressedThisFrame() && !CarExplorerModels.PointerOverHud &&
            !IsRayOverUi(InteractorHandedness.Left))
            ProcessRay(GetSelectionRay(leftControllerPose));

        if (explodeAction != null && explodeAction.WasPressedThisFrame()) Explode();
        if (restoreAction != null && restoreAction.WasPressedThisFrame()) Back();
        if (resetAction != null && resetAction.WasPressedThisFrame()) ResetAll();
    }

    // A left click on the car. A locked cursor sits under the walkthrough crosshair; Mouse.position can be stale then
    // (macOS). A free pointer over the HUD clicks the HUD, not the car (CarExplorerModels.PointerOverHud is the HUD's
    // own word for it, EventSystem covers any other UI). While the inspect camera orbits on a left drag
    // (CarExplorerInspect.Active), the click picks on release, and only when the pointer moved less than ClickSlop
    // pixels since the press.
    private void HandleDesktopPointer(bool pressed, bool released, Vector2 delta, Vector2 position)
    {
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        if (pressed)
        {
            pointerArmed = false;
            if (!locked && IsPointerOverUi())
                return;
            Vector2 at = locked ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) : position;
            bool second = Time.unscaledTime - lastPressTime <= 0.4f && (at - lastPressPosition).sqrMagnitude <= 36f;
            lastPressTime = second ? float.NegativeInfinity : Time.unscaledTime;
            lastPressPosition = at;
            if (second && !CarExplorerInspect.Active)
                return;
            if (!CarExplorerInspect.Active)
            {
                ProcessRay(GetPointerRay(locked, position));
                return;
            }
            pointerArmed = true;
            pointerTravel = Vector2.zero;
            pointerPressPosition = position;
            pointerMaxTravel = 0f;
        }
        else if (pointerArmed)
            pointerTravel += delta;
        if (!pointerArmed)
            return;
        float travel = pointerTravel.magnitude;
        if (!locked)
            travel = Mathf.Max(travel, (position - pointerPressPosition).magnitude);
        pointerMaxTravel = Mathf.Max(pointerMaxTravel, travel);
        if (!released)
            return;
        pointerArmed = false;
        if (pointerMaxTravel < ClickSlop && (locked || !IsPointerOverUi()))
            ProcessRay(GetPointerRay(locked, position));
    }

    private static bool IsPointerOverUi()
    {
        return CarExplorerModels.PointerOverHud ||
               (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
    }

    private Ray GetPointerRay(bool locked, Vector2 position)
    {
        return locked ? viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) : viewCamera.ScreenPointToRay(position);
    }

    // An XR ray that points at the HUD presses the HUD (XRI's UI interaction), not the car behind it.
    private bool IsRayOverUi(InteractorHandedness hand)
    {
        Transform rig = GetXrOrigin(viewCamera);
        if (rig == null)
            return false;
        if (rig != rayInteractorRig)
        {
            rayInteractorRig = rig;
            rayInteractors.Clear();
            rig.GetComponentsInChildren(true, rayInteractors);
        }
        foreach (XRRayInteractor interactor in rayInteractors)
            if (interactor != null && interactor.isActiveAndEnabled && interactor.handedness == hand &&
                interactor.IsOverUIGameObject())
                return true;
        return false;
    }

    private Ray GetSelectionRay(ControllerPose pose)
    {
        if (XRSettings.isDeviceActive && TryGetControllerRay(pose, out Ray controllerRay))
            return controllerRay;
        return viewCamera != null ? viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) : default;
    }

    private bool TryGetControllerRay(ControllerPose pose, out Ray ray)
    {
        ray = default;
        if (pose == null)
            return false;
        InputAction position = pose.AimPosition.controls.Count > 0 ? pose.AimPosition : pose.DevicePosition;
        InputAction rotation = pose.AimRotation.controls.Count > 0 ? pose.AimRotation : pose.DeviceRotation;
        if (position.controls.Count == 0 || rotation.controls.Count == 0)
            return false;

        Quaternion trackedRotation = rotation.ReadValue<Quaternion>();
        float lengthSquared = trackedRotation.x * trackedRotation.x + trackedRotation.y * trackedRotation.y +
                              trackedRotation.z * trackedRotation.z + trackedRotation.w * trackedRotation.w;
        if (lengthSquared < 0.5f)
            return false;
        trackedRotation = Quaternion.Normalize(trackedRotation);

        // Controller and head poses share the XR Origin's tracking space (Camera Offset).
        Vector3 trackedPosition = position.ReadValue<Vector3>();
        Transform trackingSpace = viewCamera != null ? viewCamera.transform.parent : null;
        Vector3 origin = trackingSpace != null ? trackingSpace.TransformPoint(trackedPosition) : trackedPosition;
        Quaternion worldRotation = trackingSpace != null ? trackingSpace.rotation * trackedRotation : trackedRotation;
        ray = new Ray(origin, worldRotation * Vector3.forward);
        return true;
    }

    private bool IsXrView()
    {
        return XRSettings.isDeviceActive || GetXrOrigin(viewCamera) != null;
    }

    // The 'XR Origin' / 'XR Rig' above a camera, cached per camera and parent: Transform.name allocates a
    // new string on every read, and this is asked several times a frame.
    private Transform GetXrOrigin(Camera camera)
    {
        if (camera == null)
            return null;
        Transform parent = camera.transform.parent;
        if (camera != originCamera || parent != originCameraParent)
        {
            originCamera = camera;
            originCameraParent = parent;
            originCache = FindXrOrigin(camera.transform);
        }
        return originCache;
    }

    private static Transform FindXrOrigin(Transform node)
    {
        for (; node != null; node = node.parent)
        {
            string nodeName = node.name;
            if (nodeName.IndexOf("XR Origin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                nodeName.IndexOf("XR Rig", StringComparison.OrdinalIgnoreCase) >= 0)
                return node;
        }
        return null;
    }

    private Camera ResolveActiveViewCamera()
    {
        // Keep the current camera while it is still the right one; enumerate only when it is not, e.g. after the
        // XR rig or the desktop camera was switched off. In XR a head camera under the rig wins.
        bool xr = XRSettings.isDeviceActive;
        bool cachedActive = viewCamera != null && viewCamera.isActiveAndEnabled;
        if (cachedActive && (!xr || GetXrOrigin(viewCamera) != null))
            return viewCamera;

        if (cameraBuffer.Length < Camera.allCamerasCount)
            cameraBuffer = new Camera[Camera.allCamerasCount];
        int count = Camera.GetAllCameras(cameraBuffer);
        if (xr)
        {
            // The cached camera was checked above; skipping it keeps the rig-less XR fallback allocation-free.
            for (int i = 0; i < count; i++)
                if (cameraBuffer[i] != null && cameraBuffer[i] != viewCamera && cameraBuffer[i].isActiveAndEnabled &&
                    FindXrOrigin(cameraBuffer[i].transform) != null)
                    return cameraBuffer[i];
        }

        if (cachedActive)
            return viewCamera;
        Camera mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.isActiveAndEnabled)
            return mainCamera;
        return count > 0 ? cameraBuffer[0] : null;
    }

    // ---------------------------------------------------------------- views and drill-down

    /// <summary>
    /// The next view: normal -> X-ray -> exploded (XR A button). In the exploded view it does nothing.
    /// </summary>
    public void Explode()
    {
        if (!initialized)
            return;
        if (view == CarExplorerView.Normal)
            ChangeView(CarExplorerView.XRay);
        else if (view == CarExplorerView.XRay)
            ChangeView(CarExplorerView.Exploded);
    }

    /// <summary>Straight to the exploded view from any view (O, ] or PageUp).</summary>
    public void ExplodeAll()
    {
        if (!initialized || view == CarExplorerView.Exploded)
            return;
        ChangeView(CarExplorerView.Exploded);
    }

    /// <summary>
    /// X-ray view on or off (T). An assembly that is out stays out; from the exploded view the car comes back
    /// together as the normal car.
    /// </summary>
    public void ToggleXRay()
    {
        if (!initialized)
            return;
        ChangeView(view == CarExplorerView.Normal ? CarExplorerView.XRay : CarExplorerView.Normal);
    }

    /// <summary>One step back (R, [ or PageDown, XR B button): see <see cref="Back"/>.</summary>
    public void Restore()
    {
        Back();
    }

    /// <summary>The complete car in its own materials, nothing selected (XR left Menu button).</summary>
    public void ResetExplorer()
    {
        ResetAll();
    }

    /// <summary>Puts the assembly that is out back into the car and clears the selection.</summary>
    public void ReturnFocus()
    {
        if (!initialized)
            return;
        PutBack();
        SelectNode(null, null);
    }

    private void ChangeView(CarExplorerView next, bool force = false)
    {
        CarExplorerView previous = view;
        if (next == previous && !force)
            return;
        bool before = previous == CarExplorerView.Exploded;
        // The exploded view shows every assembly as one unit; leaving it, they all go home.
        if (next == CarExplorerView.Exploded || before)
        {
            takenOut = null;
            openPath.Clear();
            SyncOpenPathInfo();
        }
        view = next;
        currentLevel = (int)next;
        if (next == CarExplorerView.Exploded)
            FitExplodeLayout();
        bool staggered = next == CarExplorerView.Exploded || before;
        StartStateMove(staggered, before && next != CarExplorerView.Exploded);
        if (selected != null && takenOut == null && next == CarExplorerView.Normal && selected.Assembly != null &&
            selected.Assembly.Inner)
            SelectNode(null, null);
        NotifyChanged();
    }

    // Moves everything to the current state: the move, the glass and the inner content's drawing.
    private void StartStateMove(bool staggered, bool collapsing = false)
    {
        UpdateDimming();
        CaptureInnerNeeds();
        BeginMotion(staggered ? explodeDuration : transitionDuration, staggered, collapsing);
        SetGhostTargets();
        FinishInnerNeeds();
        UpdateInnerRendering();
    }

    // A click on a part: takes its assembly out in one piece, or, once it is out, opens the level that holds the
    // part; a part of another assembly takes that one out instead (the first goes back on the same move).
    private void OnPartClicked(Part part)
    {
        if (part == null || part.Leaf == null)
        {
            // Empty space only clears the selection; nothing moves.
            SelectNode(null, null);
            return;
        }
        OpenTo(part.Leaf, part, false);
    }

    // One step toward `target`: from the tree (fromTree) every level above the node opens and the node itself comes
    // apart; from a click on a part, only the next level along the part's chain opens.
    private void OpenTo(Node target, Part clicked, bool fromTree)
    {
        Node assembly = target.Assembly;
        if (assembly == null)
            return;
        bool changed = false;
        if (takenOut != assembly)
        {
            // Another assembly goes back while this one comes out (or, in the exploded view, comes apart in place).
            takenOut = assembly;
            openPath.Clear();
            changed = true;
            if (view != CarExplorerView.Exploded)
            {
                if (assembly.Enclosed && view == CarExplorerView.Normal)
                {
                    view = CarExplorerView.XRay;
                    currentLevel = (int)view;
                }
                assembly.FocusOffset = ComputeFocusOffset(assembly, assembly.RestBounds);
                if (!fromTree || target == assembly)
                {
                    SyncOpenPathInfo();
                    StartStateMove(false);
                    SelectNode(assembly, clicked);
                    return;
                }
            }
        }

        BuildChain(target, chainScratch);
        int common = 0;
        while (common < openPath.Count && common < chainScratch.Count && openPath[common] == chainScratch[common])
            common++;
        if (common < openPath.Count)
        {
            openPath.RemoveRange(common, openPath.Count - common);
            changed = true;
        }
        Node select = target;
        if (fromTree)
        {
            // Every level down to the node opens; the node itself comes apart when it can.
            for (int i = openPath.Count; i < chainScratch.Count; i++)
            {
                Node level = chainScratch[i];
                if (level.Children.Count < 2)
                    break;
                OpenLevel(level);
                changed = true;
            }
        }
        else
        {
            Node next = openPath.Count < chainScratch.Count ? chainScratch[openPath.Count] : target;
            if (next.Children.Count >= 2)
            {
                OpenLevel(next);
                changed = true;
                select = next;
            }
            else
                select = next;
        }
        if (changed)
            AfterLevelsChanged();
        SelectNode(select, clicked);
    }

    // See placedWhileInspecting: after the flight back, the taken-out assembly takes a new place when its place is
    // no longer valid for the walking eye, and its open levels come toward that eye again.
    private void UpdatePlacementAfterInspect()
    {
        bool inspecting = CarExplorerInspect.Active;
        if (inspecting != wasInspecting)
        {
            wasInspecting = inspecting;
            replaceForWalkingAt = !inspecting && placedWhileInspecting ? Time.time + InspectReturnSeconds : -1f;
            if (!inspecting)
                placedWhileInspecting = false;
        }
        if (replaceForWalkingAt < 0f || Time.time < replaceForWalkingAt)
            return;
        replaceForWalkingAt = -1f;
        if (takenOut == null || view == CarExplorerView.Exploded || viewCamera == null)
            return;
        Bounds spread = GetPlannedBounds(takenOut, false);
        Bounds placed = RotateBounds(spread, takenOut.FocusYaw, takenOut.FocusPivot);
        placed.center += takenOut.FocusOffset;
        GatherFocusKeepOuts();
        GatherPartBoxes(takenOut);
        ScoreView(placed, viewCamera.transform.position, FocusMaxHeight, FocusViewerClearance, out bool valid);
        if (!valid)
            takenOut.FocusOffset = ComputeFocusOffset(takenOut, spread);
        foreach (Node node in openPath)
            ComputePull(node);
        foreach (Node node in openPath)
            KeepSpreadInRoom(node);
        StartStateMove(false);
        NotifyChanged();
    }

    private void OpenLevel(Node node)
    {
        openPath.Add(node);
        ComputeSpread(node);
        ComputePull(node);
    }

    private void AfterLevelsChanged()
    {
        SyncOpenPathInfo();
        // When the taken-out assembly has to move to a new place, its open levels come out again from there.
        if (RefitTakenOut())
            foreach (Node node in openPath)
                ComputePull(node);
        foreach (Node node in openPath)
            KeepSpreadInRoom(node);
        StartStateMove(false);
        NotifyChanged();
    }

    // [assembly, ..., node]
    private static void BuildChain(Node node, List<Node> chain)
    {
        chain.Clear();
        for (Node at = node; at != null && at.Info.Kind != CarNodeKind.System && at.Info.Kind != CarNodeKind.Vehicle;
             at = at.Parent)
            chain.Add(at);
        chain.Reverse();
    }

    // ---------------------------------------------------------------- inner content (V6, internals)

    private void CaptureInnerRendering(Node assembly)
    {
        List<Renderer> renderers = new List<Renderer>();
        List<Collider> colliders = new List<Collider>();
        foreach (Part part in assembly.AllParts)
        {
            renderers.AddRange(part.Renderers);
            if (part.OwnsCollider && part.SelectionCollider != null)
                colliders.Add(part.SelectionCollider);
        }
        assembly.InnerRenderers = renderers.ToArray();
        assembly.InnerColliders = colliders.ToArray();
        assembly.InnerEnabled = new bool[assembly.InnerRenderers.Length];
        assembly.InnerShadows = new ShadowCastingMode[assembly.InnerRenderers.Length];
        for (int i = 0; i < assembly.InnerRenderers.Length; i++)
        {
            assembly.InnerEnabled[i] = assembly.InnerRenderers[i].enabled;
            assembly.InnerShadows[i] = assembly.InnerRenderers[i].shadowCastingMode;
        }
    }

    // Content inside the body (the V6 alone is 182 draws of 627k triangles) is drawn only where it can be seen: in the
    // X-ray and exploded views, while its assembly is out, while the front section over the bay is out (the bay's
    // content), and during a move that starts or ends in such a state. It never casts shadows.
    private bool IsInnerNeededAtRest(Node assembly)
    {
        if (view != CarExplorerView.Normal || takenOut == assembly)
            return true;
        return takenOut != null && takenOut.Kind == AssemblyKind.Front && assembly.InBay;
    }

    private void CaptureInnerNeeds()
    {
        foreach (Node assembly in assemblies)
            if (assembly.Inner)
                assembly.InnerNeededDuringMove = assembly.InnerShown;
    }

    private void FinishInnerNeeds()
    {
        foreach (Node assembly in assemblies)
            if (assembly.Inner)
                assembly.InnerNeededDuringMove |= IsInnerNeededAtRest(assembly);
    }

    private void UpdateInnerRendering(bool force = false)
    {
        bool moving = transitionT < 1f;
        foreach (Node assembly in assemblies)
        {
            if (!assembly.Inner || assembly.InnerRenderers == null)
                continue;
            bool visible = IsInnerNeededAtRest(assembly) || (moving && assembly.InnerNeededDuringMove);
            if (visible == assembly.InnerShown && !force)
                continue;
            assembly.InnerShown = visible;
            for (int i = 0; i < assembly.InnerRenderers.Length; i++)
            {
                Renderer renderer = assembly.InnerRenderers[i];
                if (renderer == null) continue;
                renderer.enabled = visible && assembly.InnerEnabled[i];
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            // Hidden parts must not catch rays or grabs either.
            foreach (Collider collider in assembly.InnerColliders)
                if (collider != null) collider.enabled = visible;
        }
    }

    private void RestoreInnerRendering()
    {
        foreach (Node assembly in assemblies)
        {
            if (!assembly.Inner || assembly.InnerRenderers == null)
                continue;
            for (int i = 0; i < assembly.InnerRenderers.Length; i++)
            {
                Renderer renderer = assembly.InnerRenderers[i];
                if (renderer == null) continue;
                renderer.enabled = assembly.InnerEnabled[i];
                renderer.shadowCastingMode = assembly.InnerShadows[i];
            }
            foreach (Collider collider in assembly.InnerColliders)
                if (collider != null) collider.enabled = true;
            assembly.InnerShown = true;
        }
    }

    private static bool IsNodeVisible(Node node)
    {
        foreach (Part part in node.AllParts)
            if (IsPartVisible(part))
                return true;
        return node.AllParts.Count == 0;
    }

    // ---------------------------------------------------------------- X-ray glass

    private void BuildGhostRenderers()
    {
        foreach (Part part in parts)
        {
            if (!part.IsShell)
                continue;
            foreach (Renderer renderer in part.Renderers)
            {
                if (renderer == null || ghostByRenderer.ContainsKey(renderer))
                    continue;
                GhostRenderer ghost = new GhostRenderer
                {
                    Renderer = renderer, Part = part,
                    GlassOpacity = IsInnerShell(part.Target.name) || part.Target == carRoot ? 0.3f : 1f
                };
                ghostRenderers.Add(ghost);
                ghostByRenderer.Add(renderer, ghost);
            }
        }
    }

    private bool EnsureGhostMaterials()
    {
        if (ghostSolidMaterial != null && ghostGlassMaterial != null)
            return true;
        if (ghostShaderUnavailable)
            return false;
        if (ghostShader == null)
        {
            ghostShader = Resources.Load<Shader>(GhostShaderResource);
            if (ghostShader == null)
                ghostShader = Shader.Find(GhostShaderName);
            if (ghostShader == null || !ghostShader.isSupported)
            {
                ghostShader = null;
                ghostShaderUnavailable = true;
                Debug.LogWarning("[Car Explorer] Shader '" + GhostShaderName +
                                 "' is unavailable; the X-ray view hides the body shell instead.");
                return false;
            }
        }
        // While fading the nearest panel hides what is behind it, as the solid body does; as glass the far side
        // shows through (see AurelleXRayGhost.shader).
        ghostSolidMaterial = new Material(ghostShader) { name = "Car Explorer X-Ray (fading)" };
        ghostSolidMaterial.SetFloat(GhostZWriteId, 1f);
        ghostSolidMaterial.SetFloat(GhostCullId, (float)CullMode.Back);
        ghostSolidMaterial.renderQueue = (int)RenderQueue.Transparent;
        ghostGlassMaterial = new Material(ghostShader) { name = "Car Explorer X-Ray (glass)" };
        ghostGlassMaterial.SetFloat(GhostZWriteId, 0f);
        ghostGlassMaterial.SetFloat(GhostCullId, (float)CullMode.Off);
        ghostGlassMaterial.renderQueue = (int)RenderQueue.Transparent;
        return true;
    }

    // Glass for the shell in the X-ray view, except the assembly that is out (shown solid); solid everywhere else.
    private void SetGhostTargets()
    {
        foreach (GhostRenderer ghost in ghostRenderers)
        {
            ghost.Target = view == CarExplorerView.XRay && ghost.Part.Assembly != takenOut ? 1f : 0f;
            if (ghost.Amount != ghost.Target)
                ghostAnimating = true;
        }
    }

    // Fades with the glass; a downward spot over the car, so it lights the cabin and the bay (and the display
    // just around the car, as a showroom spot would) without reaching the rest of the room.
    private void UpdateXRayLight()
    {
        float target = view == CarExplorerView.XRay && xrayLightIntensity > 0f ? 1f : 0f;
        if (xrayLightLevel == target)
            return;
        xrayLightLevel = Mathf.MoveTowards(xrayLightLevel, target, Time.deltaTime / Mathf.Max(0.05f, ghostFadeDuration));
        if (xrayLight == null)
        {
            GameObject lightObject = new GameObject("Car Explorer X-Ray Light");
            // Over the front of the cabin, so the engine bay gets its share.
            Vector3 over = carRestCenter + carForward * (Vector3.Dot(carRestBounds.extents, Abs(carForward)) * 0.3f);
            lightObject.transform.SetPositionAndRotation(
                new Vector3(over.x, carRestBounds.max.y + 1.1f, over.z),
                Quaternion.LookRotation(Vector3.down, carForward));
            xrayLight = lightObject.AddComponent<Light>();
            xrayLight.type = LightType.Spot;
            xrayLight.spotAngle = 105f;
            xrayLight.innerSpotAngle = 60f;
            xrayLight.range = carRestBounds.max.y + 2.4f;
            xrayLight.color = new Color(0.88f, 0.94f, 1f, 1f);
            xrayLight.shadows = LightShadows.None;
        }
        xrayLight.intensity = xrayLightIntensity * Smooth(xrayLightLevel);
        xrayLight.enabled = xrayLightLevel > 0f;
    }

    private static Vector3 Abs(Vector3 v)
    {
        return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }

    private void UpdateGhosts()
    {
        if (!ghostAnimating)
            return;
        float step = Time.deltaTime / Mathf.Max(0.05f, ghostFadeDuration);
        bool any = false;
        foreach (GhostRenderer ghost in ghostRenderers)
        {
            if (ghost.Amount == ghost.Target)
                continue;
            ghost.Amount = Mathf.MoveTowards(ghost.Amount, ghost.Target, step);
            ApplyGhost(ghost);
            if (ghost.Amount != ghost.Target)
                any = true;
        }
        ghostAnimating = any;
    }

    private void ApplyGhost(GhostRenderer ghost)
    {
        Renderer renderer = ghost.Renderer;
        if (renderer == null)
            return;
        if (ghost.Amount <= 0f)
        {
            if (ghost.Swapped)
                Unswap(ghost);
            renderer.forceRenderingOff = false;
            return;
        }
        if (!ghost.Swapped && !Swap(ghost))
        {
            // No glass shader on this platform: the shell is simply hidden past half way.
            renderer.forceRenderingOff = ghost.Amount >= 0.5f;
            return;
        }
        bool glass = ghost.Amount >= 1f;
        if (glass != ghost.GlassPhaseActive)
        {
            renderer.sharedMaterials = glass ? ghost.GlassPhase : ghost.SolidPhase;
            ghost.GlassPhaseActive = glass;
        }
        WriteGhostBlocks(ghost, GetGhostHighlight(ghost.Part));
    }

    private static float GetGhostHighlight(Part part)
    {
        return part.Tint == 2 ? 1f : part.Tint == 1 ? 0.45f : 0f;
    }

    // Puts the glass materials on, keeping the renderer's own materials (whatever they are now) to put back.
    private bool Swap(GhostRenderer ghost)
    {
        if (!EnsureGhostMaterials())
            return false;
        Renderer renderer = ghost.Renderer;
        renderer.GetSharedMaterials(materialScratch);
        int count = materialScratch.Count;
        if (ghost.Original == null || ghost.Original.Length != count)
        {
            ghost.Original = new Material[count];
            ghost.SolidPhase = new Material[count];
            ghost.GlassPhase = new Material[count];
            ghost.Blocks = new MaterialPropertyBlock[count];
        }
        for (int i = 0; i < count; i++)
        {
            ghost.Original[i] = materialScratch[i];
            ghost.SolidPhase[i] = ghostSolidMaterial;
            ghost.GlassPhase[i] = ghostGlassMaterial;
            if (ghost.Blocks[i] == null)
                ghost.Blocks[i] = new MaterialPropertyBlock();
            else
                ghost.Blocks[i].Clear();
            FillSolidLook(ghost.Blocks[i], materialScratch[i]);
            // The highlight tint of the own materials goes; the glass carries its own highlight.
            renderer.SetPropertyBlock(null, i);
        }
        // The fading material casts a shadow until half way; panels whose own materials cast none (the windows) keep
        // casting none.
        bool castsShadows = false;
        for (int i = 0; i < count; i++)
        {
            Material material = materialScratch[i];
            if (material != null && material.FindPass("ShadowCaster") >= 0 &&
                material.GetShaderPassEnabled("ShadowCaster"))
                castsShadows = true;
        }
        ghost.OriginalShadows = renderer.shadowCastingMode;
        if (!castsShadows)
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        materialScratch.Clear();
        renderer.sharedMaterials = ghost.SolidPhase;
        ghost.GlassPhaseActive = false;
        ghost.Swapped = true;
        return true;
    }

    private void Unswap(GhostRenderer ghost)
    {
        Renderer renderer = ghost.Renderer;
        ghost.Swapped = false;
        ghost.GlassPhaseActive = false;
        if (renderer == null)
            return;
        for (int i = 0; i < ghost.Original.Length; i++)
            renderer.SetPropertyBlock(null, i);
        renderer.sharedMaterials = ghost.Original;
        renderer.shadowCastingMode = ghost.OriginalShadows;
        if (ghost.Part.Tint > 0)
            TintRenderer(renderer, ghost.Part.Tint);
    }

    private static readonly int GltfClearcoatId = Shader.PropertyToID("clearcoatFactor");
    private static readonly int GltfClearcoatRoughnessId = Shader.PropertyToID("clearcoatRoughnessFactor");
    private static readonly int GhostApproxClearCoatId = Shader.PropertyToID("_ApproxClearCoat");
    private static readonly int GhostApproxClearCoatSmoothnessId = Shader.PropertyToID("_ApproxClearCoatSmoothness");

    // The glass shader's solid look stands in for the own material at the start of the fade: its base colour,
    // texture, metallic, smoothness and clear coat, whichever of the glTF, URP or legacy names it uses.
    private static void FillSolidLook(MaterialPropertyBlock block, Material material)
    {
        if (material == null)
        {
            block.SetColor(GhostApproxColorId, new Color(0.5f, 0.5f, 0.5f, 1f));
            return;
        }
        int colorId = GetBaseColorProperty(material);
        Color color = colorId != -1 ? material.GetColor(colorId) : new Color(0.5f, 0.5f, 0.5f, 1f);
        if (material.renderQueue < (int)RenderQueue.GeometryLast + 1)
            color.a = 1f;
        block.SetColor(GhostApproxColorId, color);
        int textureId = material.HasProperty(GltfBaseMapId) ? GltfBaseMapId
            : material.HasProperty(BaseMapId) ? BaseMapId : material.HasProperty(MainTexId) ? MainTexId : -1;
        Texture texture = textureId != -1 ? material.GetTexture(textureId) : null;
        if (texture != null)
        {
            Vector2 scale = material.GetTextureScale(textureId);
            Vector2 offset = material.GetTextureOffset(textureId);
            block.SetTexture(GhostApproxMapId, texture);
            block.SetVector(GhostApproxMapStId, new Vector4(scale.x, scale.y, offset.x, offset.y));
        }
        float metallic = material.HasProperty(GltfMetallicId) ? material.GetFloat(GltfMetallicId)
            : material.HasProperty(MetallicId) ? material.GetFloat(MetallicId) : 0f;
        float smoothness = material.HasProperty(GltfRoughnessId) ? 1f - material.GetFloat(GltfRoughnessId)
            : material.HasProperty(SmoothnessId) ? material.GetFloat(SmoothnessId) : 0.5f;
        block.SetFloat(GhostApproxMetallicId, Mathf.Clamp01(metallic));
        block.SetFloat(GhostApproxSmoothnessId, Mathf.Clamp01(smoothness));
        // glTF KHR_materials_clearcoat (the car paint): without it the stand-in loses the paint's mirror finish.
        float clearCoat = material.HasProperty(GltfClearcoatId) ? material.GetFloat(GltfClearcoatId) : 0f;
        float coatSmoothness = material.HasProperty(GltfClearcoatRoughnessId)
            ? 1f - material.GetFloat(GltfClearcoatRoughnessId) : 1f;
        block.SetFloat(GhostApproxClearCoatId, Mathf.Clamp01(clearCoat));
        block.SetFloat(GhostApproxClearCoatSmoothnessId, Mathf.Clamp01(coatSmoothness));
    }

    private void WriteGhostBlocks(GhostRenderer ghost, float highlight)
    {
        Renderer renderer = ghost.Renderer;
        if (renderer == null || ghost.Blocks == null)
            return;
        for (int i = 0; i < ghost.Blocks.Length; i++)
        {
            MaterialPropertyBlock block = ghost.Blocks[i];
            block.SetFloat(GhostFadeId, ghost.Amount);
            block.SetFloat(GhostHighlightId, highlight);
            block.SetFloat(GhostGlassOpacityId, Mathf.Max(ghost.GlassOpacity, highlight));
            renderer.SetPropertyBlock(block, i);
        }
    }

    // Every shell renderer back in its own materials at once (disable / destroy / snap to rest).
    private void RestoreAllGhosts()
    {
        xrayLightLevel = 0f;
        if (xrayLight != null)
            xrayLight.enabled = false;
        foreach (GhostRenderer ghost in ghostRenderers)
        {
            ghost.Target = 0f;
            ghost.Amount = 0f;
            if (ghost.Swapped)
                Unswap(ghost);
            if (ghost.Renderer != null)
                ghost.Renderer.forceRenderingOff = false;
        }
        ghostAnimating = false;
    }

    private bool IsGhosted(Part part)
    {
        if (part == null || !part.IsShell)
            return false;
        foreach (Renderer renderer in part.Renderers)
            if (renderer != null && ghostByRenderer.TryGetValue(renderer, out GhostRenderer ghost) &&
                ghost.Target >= 0.5f)
                return true;
        return false;
    }

    // ---------------------------------------------------------------- exact picking

    private struct PickBakeJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<EntityId> MeshIds;

        public void Execute(int index)
        {
            Physics.BakeMesh(MeshIds[index], false);
        }
    }

    // Every part's readable mesh (the car's, the V6's and the generated internals') is cooked for exact picking on
    // worker threads; the colliders are created once that is done (CompletePickScene). A part without a readable mesh
    // keeps its box.
    private void SchedulePickScene()
    {
        List<EntityId> ids = new List<EntityId>();
        HashSet<Mesh> seen = new HashSet<Mesh>();
        foreach (Part part in parts)
        {
            Mesh mesh = GetPickMesh(part);
            if (mesh != null && seen.Add(mesh))
                ids.Add(mesh.GetEntityId());
        }
        if (ids.Count == 0)
            return;
        pickBakeMeshIds = new NativeArray<EntityId>(ids.ToArray(), Allocator.Persistent);
        pickBakeHandle = new PickBakeJob { MeshIds = pickBakeMeshIds }.Schedule(ids.Count, 1);
        JobHandle.ScheduleBatchedJobs();
        pickBakeScheduled = true;
    }

    private static Mesh GetPickMesh(Part part)
    {
        if (part.Renderers.Length == 0 || !(part.Renderers[0] is MeshRenderer))
            return null;
        MeshFilter filter = part.Target.GetComponent<MeshFilter>();
        Mesh mesh = filter != null ? filter.sharedMesh : null;
        return mesh != null && mesh.isReadable && mesh.vertexCount > 0 ? mesh : null;
    }

    private void CompletePickScene()
    {
        if (!pickBakeScheduled)
            return;
        // The cooking's temporary job memory may live four frames: it is waited for on the third at the latest.
        if (!pickBakeHandle.IsCompleted && ++pickBakeFrames < 3)
            return;
        pickBakeHandle.Complete();
        pickBakeScheduled = false;
        if (pickBakeMeshIds.IsCreated)
            pickBakeMeshIds.Dispose();

        pickScene = SceneManager.CreateScene("Car Explorer Picking " + (++pickSceneCount),
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        pickPhysics = pickScene.GetPhysicsScene();
        foreach (Part part in parts)
        {
            Mesh mesh = GetPickMesh(part);
            if (mesh == null)
                continue;
            GameObject picker = new GameObject("Pick " + part.Target.name);
            SceneManager.MoveGameObjectToScene(picker, pickScene);
            MeshCollider collider = picker.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            part.PickCollider = collider;
            partByPickCollider[collider] = part;
            pickParts.Add(part);
        }
        pickReady = pickParts.Count > 0;
    }

    private void SyncPickColliders()
    {
        foreach (Part part in pickParts)
        {
            Transform picker = part.PickCollider != null ? part.PickCollider.transform : null;
            if (picker == null)
                continue;
            Transform source = part.Target;
            picker.SetPositionAndRotation(source.position, source.rotation);
            picker.localScale = source.lossyScale;
        }
    }

    private void DestroyPickScene()
    {
        if (pickBakeScheduled)
        {
            pickBakeHandle.Complete();
            pickBakeScheduled = false;
        }
        if (pickBakeMeshIds.IsCreated)
            pickBakeMeshIds.Dispose();
        foreach (Part part in pickParts)
            part.PickCollider = null;
        pickParts.Clear();
        partByPickCollider.Clear();
        pickReady = false;
        // On quit (and leaving Play Mode) every scene goes anyway.
        if (!applicationQuitting && pickScene.IsValid() && pickScene.isLoaded)
            SceneManager.UnloadSceneAsync(pickScene);
    }

    private void OnApplicationQuit()
    {
        applicationQuitting = true;
    }

    // ---------------------------------------------------------------- selection rays

    // The nearest visible part wins. Parts with a readable mesh are hit exactly (their meshes in the picking scene),
    // the others by their boxes. In the X-ray view the glass shell lets the ray through to the solid parts behind it
    // and is chosen only when nothing solid is behind it. Any other geometry in front blocks
    // the selection; a world-space UI in front (UI layer) takes the click.
    private void ProcessRay(Ray ray)
    {
        if (!initialized || ray.direction.sqrMagnitude < 0.5f)
            return;

        if (pickReady)
            SyncPickColliders();
        Physics.SyncTransforms();
        int count = CollectRayHits(ray);

        Part chosen = null;
        Part glass = null;
        for (int i = 0; i < count; i++)
        {
            Collider hitCollider = rayEntries[i].Collider;
            if (hitCollider == null)
                continue;
            Part part;
            if (rayEntries[i].Precise)
                part = partByPickCollider.TryGetValue(hitCollider, out Part exact) ? exact : null;
            else
            {
                if (hitCollider.gameObject.layer == UiLayer)
                {
                    if (glass != null)
                        break;
                    return;
                }
                if (hitCollider.isTrigger)
                    continue;
                if (!partByCollider.TryGetValue(hitCollider, out part))
                    part = null;
                // A part with an exact collider is picked by that one; its box only stands in until it exists.
                else if (part.PickCollider != null)
                    continue;
            }

            if (part != null)
            {
                if (!IsPartVisible(part))
                    continue;
                if (IsGhosted(part))
                {
                    if (glass == null)
                        glass = part;
                    continue;
                }
                chosen = part;
                break;
            }

            if (!IsIgnoredByRay(hitCollider))
                break;
        }

        OnPartClicked(chosen ?? glass);
    }

    private const int UiLayer = 5;

    // Main-scene hits (UI, part boxes, the room) and exact car hits, in one list nearest first.
    private int CollectRayHits(Ray ray)
    {
        int count = 0;
        int mainCount = Physics.RaycastNonAlloc(ray, mainHits, rayDistance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide);
        for (int i = 0; i < mainCount && count < rayEntries.Length; i++)
            rayEntries[count++] = new RayHitEntry
                { Distance = mainHits[i].distance, Collider = mainHits[i].collider, Point = mainHits[i].point };
        if (pickReady && pickPhysics.IsValid())
        {
            int pickCount = pickPhysics.Raycast(ray.origin, ray.direction, pickHits, rayDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < pickCount && count < rayEntries.Length; i++)
                rayEntries[count++] = new RayHitEntry
                {
                    Distance = pickHits[i].distance, Collider = pickHits[i].collider, Point = pickHits[i].point,
                    Precise = true
                };
        }
        // Insertion sort: a few dozen entries, no allocation.
        for (int i = 1; i < count; i++)
        {
            RayHitEntry entry = rayEntries[i];
            int j = i - 1;
            while (j >= 0 && rayEntries[j].Distance > entry.Distance)
            {
                rayEntries[j + 1] = rayEntries[j];
                j--;
            }
            rayEntries[j + 1] = entry;
        }
        return count;
    }

    private static bool IsPartVisible(Part part)
    {
        foreach (Renderer renderer in part.Renderers)
            if (renderer != null && renderer.enabled && !renderer.forceRenderingOff &&
                renderer.gameObject.activeInHierarchy)
                return true;
        return false;
    }

    private bool IsIgnoredByRay(Collider hitCollider)
    {
        // Player and presenter capsules, the XR rig and the explorer's own objects never block a selection.
        // Everything else in front does: the podium's MeshColliders, the walls, and the boxes that
        // ShowroomCollisionSetup gives the other display cars and the furniture.
        if (hitCollider is CharacterController)
            return true;
        Transform node = hitCollider.transform;
        if (node.IsChildOf(carRoot))
            return true;
        Transform rig = GetXrOrigin(viewCamera);
        return rig != null && node.IsChildOf(rig);
    }

    // ---------------------------------------------------------------- selection and highlight

    private void SelectNode(Node node, Part clicked)
    {
        selectedPart = node != null ? clicked : null;
        if (selected == node)
            return;
        Node previous = selected;
        selected = node;
        RefreshTints(previous);
        RefreshTints(selected);
        // The selection takes Remy's attention; a greeting seen only briefly returns once it clears.
        if (selected != null && greetingShown)
            HideGreeting(Time.unscaledTime - greetingShownTime >= 1.5f);
        UpdatePresenterTarget();
        StopVoice();
        if (selected != null)
            PlayVoiceCue(selected);
        NotifyChanged();
    }

    private void PlayVoiceCue(Node node)
    {
        if (partVoiceCues == null)
            return;
        foreach (PartVoiceCue cue in partVoiceCues)
        {
            // Cues may name the node as shown or by its object name.
            if (cue != null && cue.clip != null && (cue.componentName == node.Info.NameEnglish ||
                                                    (node.Source != null && cue.componentName == node.Source.name)))
            {
                PlayVoice(cue.clip);
                break;
            }
        }
    }

    // Each part of `node` gets the tint of the strongest of the selection, the hover and the dimming that hold it.
    private void RefreshTints(Node node)
    {
        if (node == null)
            return;
        foreach (Part part in node.AllParts)
            RefreshTint(part);
    }

    private void RefreshTint(Part part)
    {
        int tint = Contains(selected, part) ? 2 : Contains(hovered, part) ? 1
            : dimFocus != null && !Contains(dimFocus, part) ? 3 : 0;
        if (tint == part.Tint)
            return;
        ClearHighlight(part);
        part.Tint = tint;
        if (tint > 0)
            ApplyHighlight(part, tint);
    }

    // The dimming follows the deepest level that is apart (none while nothing is apart).
    private void UpdateDimming()
    {
        Node focus = openPath.Count > 0 ? openPath[openPath.Count - 1] : null;
        if (focus == dimFocus)
            return;
        dimFocus = focus;
        foreach (Part part in parts)
            RefreshTint(part);
    }

    private static bool Contains(Node node, Part part)
    {
        if (node == null)
            return false;
        for (Node at = part.Leaf; at != null; at = at.Parent)
            if (at == node)
                return true;
        return false;
    }

    // Remy looks and points at the selected node's geometry, not its pivot (often the car origin at floor level).
    // The point is kept in the node's own space, so he follows it while it moves.
    private void UpdatePresenterTarget()
    {
        if (presenterAgent == null)
            return;
        if (selected == null || selected.AllParts.Count == 0)
        {
            presenterAgent.SetExplanationTarget(null);
            return;
        }
        Transform anchor = selected.AllParts[0].Target;
        Bounds bounds = GetVisibleBounds(selected);
        presenterAgent.SetExplanationTarget(anchor, bounds.center);
    }

    // World bounds of the node's enabled renderers, or a point at its first part when none is enabled.
    private static Bounds GetVisibleBounds(Node node)
    {
        bool found = false;
        Bounds bounds = default;
        foreach (Part part in node.AllParts)
        foreach (Renderer renderer in part.Renderers)
        {
            if (renderer == null || !renderer.enabled) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (found) return bounds;
        return new Bounds(node.AllParts.Count > 0 ? node.AllParts[0].Target.position : Vector3.zero, Vector3.zero);
    }

    private void ApplyHighlight(Part part, int tint)
    {
        foreach (Renderer renderer in part.Renderers)
        {
            if (renderer == null)
                continue;
            if (ghostByRenderer.TryGetValue(renderer, out GhostRenderer ghost) && ghost.Swapped)
                WriteGhostBlocks(ghost, GetGhostHighlight(part));
            else
                TintRenderer(renderer, tint);
        }
    }

    // Tint the base colour each material actually has (glTFast, URP or legacy), slot by slot and starting from that
    // material's own colour, so textures and transparency are kept: 1 hover, 2 selected (toward the accent), 3 dimmed
    // (toward black). Dark colours take more of the accent, and the selection takes most of the metal's metallic
    // (the hover half as much), so dark metal and rubber show it too.
    private void TintRenderer(Renderer renderer, int tint)
    {
        if (highlightBlock == null)
            highlightBlock = new MaterialPropertyBlock();
        renderer.GetSharedMaterials(materialScratch);
        Material previous = null;
        bool tinted = false;
        for (int i = 0; i < materialScratch.Count; i++)
        {
            Material material = materialScratch[i];
            if (material == null)
                continue;
            if (material != previous)
            {
                previous = material;
                int colorId = GetBaseColorProperty(material);
                tinted = colorId != -1;
                if (!tinted)
                    continue;
                highlightBlock.Clear();
                Color original = material.GetColor(colorId);
                Color highlight;
                if (tint == 3)
                    highlight = Color.Lerp(original, Color.black, DimAmount);
                else
                {
                    float dark = Mathf.Clamp01(1f - original.grayscale / 0.45f);
                    float amount = tint == 2 ? Mathf.Lerp(HighlightAmount, HighlightAmountDark, dark)
                        : Mathf.Lerp(HoverAmount, HoverAmountDark, dark);
                    highlight = Color.Lerp(original, HighlightTint, amount);
                    int metallicId = material.HasProperty(GltfMetallicId) ? GltfMetallicId
                        : material.HasProperty(MetallicId) ? MetallicId : -1;
                    if (metallicId != -1)
                    {
                        float metallic = material.GetFloat(metallicId);
                        float target = Mathf.Min(metallic, HighlightMetallic);
                        if (tint == 1)
                            target = Mathf.Lerp(metallic, target, 0.5f);
                        if (target != metallic)
                            highlightBlock.SetFloat(metallicId, target);
                    }
                }
                highlight.a = original.a;
                highlightBlock.SetColor(colorId, highlight);
            }
            if (tinted)
                renderer.SetPropertyBlock(highlightBlock, i);
        }
        materialScratch.Clear();
    }

    private static int GetBaseColorProperty(Material material)
    {
        if (material.HasProperty(GltfBaseColorId)) return GltfBaseColorId;
        if (material.HasProperty(BaseColorId)) return BaseColorId;
        if (material.HasProperty(ColorId)) return ColorId;
        return -1;
    }

    private void ClearHighlight(Part part)
    {
        if (part == null || part.Tint == 0)
            return;
        // Only per-slot blocks were added; dropping them restores the original renderer state and its
        // SRP Batcher compatibility. Glass keeps its blocks, with the highlight off.
        foreach (Renderer renderer in part.Renderers)
        {
            if (renderer == null)
                continue;
            if (ghostByRenderer.TryGetValue(renderer, out GhostRenderer ghost) && ghost.Swapped)
            {
                WriteGhostBlocks(ghost, 0f);
                continue;
            }
            renderer.GetSharedMaterials(materialScratch);
            for (int i = 0; i < materialScratch.Count; i++)
                renderer.SetPropertyBlock(null, i);
        }
        materialScratch.Clear();
        part.Tint = 0;
    }

    // ---------------------------------------------------------------- greeting and voice

    private void UpdatePresenterGreeting()
    {
        if (greetingShown && Time.unscaledTime >= greetingHideTime &&
            (presenterAudioSource == null || !presenterAudioSource.isPlaying))
            HideGreeting(true);

        // Remy greets once he has arrived (or the viewer walks up to him) and is in view. It waits while something
        // is selected instead of being cancelled by an early click.
        if (greetingCompleted || greetingShown || selected != null || presenterRoot == null ||
            !presenterRoot.gameObject.activeInHierarchy || viewCamera == null ||
            Time.unscaledTime < nextGreetingCheck)
            return;

        nextGreetingCheck = Time.unscaledTime + 0.25f;
        float distance = HorizontalDistance(presenterRoot.position, viewCamera.transform.position);
        // HasArrived also turns true when his approach times out far away; it counts only near the follow ring.
        bool arrived = presenterAgent != null && presenterAgent.HasArrived && distance <= greetingDistance + 1.7f;
        if ((!arrived && distance > greetingDistance) || !IsInHorizontalView(presenterRoot.position))
            return;

        greetingShown = true;
        greetingShownTime = Time.unscaledTime;
        greetingHideTime = Time.unscaledTime + Mathf.Max(greetingDisplayDuration,
            greetingClip != null ? greetingClip.length : 0f);
        if (greetingClip != null)
            PlayVoice(greetingClip);
        NotifyChanged();
    }

    // Inside the camera's horizontal field of view, a few degrees in from the edge.
    private bool IsInHorizontalView(Vector3 point)
    {
        Vector3 look = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up);
        Vector3 toPoint = Vector3.ProjectOnPlane(point - viewCamera.transform.position, Vector3.up);
        if (look.sqrMagnitude < 0.0001f || toPoint.sqrMagnitude < 0.0001f)
            return true;
        float halfView = Mathf.Atan(Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) *
                                    Mathf.Max(0.1f, viewCamera.aspect)) * Mathf.Rad2Deg;
        return Mathf.Abs(Vector3.SignedAngle(look, toPoint, Vector3.up)) <= halfView - 3f;
    }

    private void HideGreeting(bool completeGreeting)
    {
        if (completeGreeting)
            greetingCompleted = true;
        if (!greetingShown)
            return;
        greetingShown = false;
        greetingHideTime = 0f;
        NotifyChanged();
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

    // ---------------------------------------------------------------- XR grabs

    private void EnsureXRGrabbables()
    {
        // In XR the rig is already active here. XRI defers creating its own default manager until the
        // scene has loaded, so a manager created here is also the one the rig's interactors adopt.
        interactionManager = FindAnyObjectByType<XRInteractionManager>();
        if (interactionManager == null || !interactionManager.isActiveAndEnabled)
        {
            GameObject managerObject = new GameObject("Car Explorer XR Interaction Manager");
            ownedInteractionManager = managerObject.AddComponent<XRInteractionManager>();
            interactionManager = ownedInteractionManager;
        }
        // Grabs wait for a move to end. The X-ray glass lets grabs through to the solid parts behind it, as it lets
        // the selection ray through (ProcessRay).
        partGrabFilter = new XRSelectFilterDelegate((interactor, interactable) => transitionT >= 1f &&
            !(partByTransform.TryGetValue(interactable.transform, out Part part) && IsGhosted(part)));

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
            ConfigurePartGrab(grab, part);
            if (target == carRoot)
            {
                // The root carries the whole car: gripping it selects the body but never lifts the car.
                grab.trackPosition = false;
                grab.trackRotation = false;
                grab.trackScale = false;
            }

            foreach (Transform child in children) child.SetParent(target, false);

            Rigidbody body = target.GetComponent<Rigidbody>();
            if (body == null) body = target.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            grab.enabled = true;
            part.GrabInteractable = grab;
        }
    }

    private void ConfigurePartGrab(XRGrabInteractable grab, Part part)
    {
        grab.interactionManager = interactionManager;
        grab.colliders.Clear();
        if (part.SelectionCollider != null) grab.colliders.Add(part.SelectionCollider);
        // Kinematic bodies: hold where grabbed, never throw, and keep the car's hierarchy while held.
        grab.useDynamicAttach = true;
        grab.throwOnDetach = false;
        grab.unparentTransformOnGrab = false;
        grab.retainTransformParent = false;
        grab.selectFilters.Add(partGrabFilter);
        grab.firstSelectEntered.AddListener(_ => OnPartGrabbed(part, grab.transform));
        grab.lastSelectExited.AddListener(_ => OnPartReleased(part, grab.transform));
    }

    private void OnPartGrabbed(Part part, Transform grabbed)
    {
        if (this == null || grabbed == null || !isActiveAndEnabled) return;
        // Grabs only start between moves, so the current pose is the pose of the current state,
        // unless the part is still gliding back from an earlier release.
        if (!returningParts.Remove(part))
        {
            part.HomeLocalPosition = grabbed.localPosition;
            part.HomeLocalRotation = grabbed.localRotation;
            part.HomeLocalScale = grabbed.localScale;
        }
        // A grab explains the part; nothing else moves.
        SelectNode(part.Leaf, part);
    }

    private void OnPartReleased(Part part, Transform released)
    {
        if (this == null || released == null) return;
        if (released.localPosition == part.HomeLocalPosition && released.localRotation == part.HomeLocalRotation &&
            released.localScale == part.HomeLocalScale)
            return;
        part.ReturnFromPosition = released.localPosition;
        part.ReturnFromRotation = released.localRotation;
        part.ReturnFromScale = released.localScale;
        part.ReturnT = 0f;
        if (!returningParts.Contains(part)) returningParts.Add(part);
    }

    private void UpdateGrabReturns()
    {
        for (int i = returningParts.Count - 1; i >= 0; i--)
        {
            Part part = returningParts[i];
            Transform target = part.GrabInteractable != null ? part.GrabInteractable.transform : null;
            if (target == null) { returningParts.RemoveAt(i); continue; }
            part.ReturnT = Mathf.Min(1f, part.ReturnT + Time.deltaTime / GrabReturnDuration);
            float eased = part.ReturnT * part.ReturnT * (3f - 2f * part.ReturnT);
            target.localPosition = Vector3.Lerp(part.ReturnFromPosition, part.HomeLocalPosition, eased);
            target.localRotation = Quaternion.Slerp(part.ReturnFromRotation, part.HomeLocalRotation, eased);
            target.localScale = Vector3.Lerp(part.ReturnFromScale, part.HomeLocalScale, eased);
            if (part.ReturnT >= 1f) returningParts.RemoveAt(i);
        }
    }

    private void ReleaseGrabbedParts()
    {
        foreach (Part part in parts)
        {
            XRGrabInteractable grab = part.GrabInteractable;
            if (grab == null || !grab.isSelected) continue;
            XRInteractionManager manager = grab.interactionManager != null ? grab.interactionManager : interactionManager;
            if (manager != null) manager.CancelInteractableSelection((IXRSelectInteractable)grab);
        }
        // The move animates every track on from wherever it is now; a part transform that is not a track (none
        // today) keeps gliding back to its place on its parent.
        for (int i = returningParts.Count - 1; i >= 0; i--)
        {
            Transform target = returningParts[i].Target;
            if (target == null || trackByTransform.ContainsKey(target))
                returningParts.RemoveAt(i);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static Bounds GetWorldRendererBounds(Renderer[] renderers)
    {
        Bounds result = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) result.Encapsulate(renderers[i].bounds);
        return result;
    }

    private static int GetTransformDepth(Transform target)
    {
        int depth = 0;
        while (target != null) { depth++; target = target.parent; }
        return depth;
    }

    private static Collider CreateBoundsCollider(Transform target, Bounds localBounds)
    {
        BoxCollider collider = target.gameObject.AddComponent<BoxCollider>();
        collider.center = localBounds.center;
        collider.size = Vector3.Max(localBounds.size, Vector3.one * 0.025f);
        return collider;
    }

    // Mesh bounds of the renderers in the space of `space`: an oriented box that turns with it, not the
    // world AABB of a rotated mesh. Renderers without a MeshFilter fall back to their world bounds.
    private static bool TryGetLocalGeometryBounds(Transform space, Renderer[] renderers, out Bounds bounds)
    {
        bool found = false;
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            MeshFilter filter = renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>() : null;
            bool hasMesh = filter != null && filter.sharedMesh != null;
            Bounds source = hasMesh ? filter.sharedMesh.bounds : renderer.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = GetBoxCorner(source, i);
                if (!hasMesh)
                    corner = space.InverseTransformPoint(corner);
                else if (renderer.transform != space)
                    corner = space.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                if (!found) { bounds = new Bounds(corner, Vector3.zero); found = true; }
                else bounds.Encapsulate(corner);
            }
        }
        return found;
    }

    private static void DestroyStaleRuntimeObject(string objectName)
    {
        // Runtime objects can survive Enter Play Mode without a scene reload; never touch assets.
        foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (candidate == null || !candidate.scene.IsValid() || candidate.name != objectName)
                continue;
            candidate.SetActive(false);
            Destroy(candidate);
        }
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
        rightControllerPose = CreateControllerPose("Right", "<XRController>{RightHand}/");
        leftControllerPose = CreateControllerPose("Left", "<XRController>{LeftHand}/");
        // OnEnable enables every action.
    }

    private ControllerPose CreateControllerPose(string label, string device)
    {
        string prefix = "Car Explorer " + label + " ";
        return new ControllerPose
        {
            AimPosition = CreateAction(prefix + "Aim Position", InputActionType.Value,
                device + "pointerPosition", "Vector3"),
            AimRotation = CreateAction(prefix + "Aim Rotation", InputActionType.Value,
                device + "pointerRotation", "Quaternion"),
            DevicePosition = CreateAction(prefix + "Device Position", InputActionType.Value,
                device + "devicePosition", "Vector3"),
            DeviceRotation = CreateAction(prefix + "Device Rotation", InputActionType.Value,
                device + "deviceRotation", "Quaternion")
        };
    }

    private InputAction CreateAction(string actionName, InputActionType type, string binding,
        string expectedControlType = null)
    {
        InputAction action = new InputAction(actionName, type, binding, expectedControlType: expectedControlType);
        actions.Add(action);
        return action;
    }

    // ---------------------------------------------------------------- camera navigation (no walkthrough)

    private void InitializeCameraNavigation()
    {
        cameraPivot = carRestCenter;
        cameraUp = carRoot.TransformDirection(Vector3.forward).normalized;
        if (cameraUp.sqrMagnitude < 0.5f) cameraUp = Vector3.up;
        if (viewCamera != null)
            cameraOrbitDistance = Mathf.Max(2f, Vector3.Distance(viewCamera.transform.position, cameraPivot));
    }

    private void UpdateCameraNavigation()
    {
        // Existing XR locomotion, snap turn, head tracking and controller
        // bindings remain in charge in headset mode; mouse navigation is desktop-only.
        if (viewCamera == null || DesktopWalkthroughController.IsDesktopActive || IsXrView()) return;
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

    private void OnDestroy()
    {
        foreach (InputAction action in actions)
        {
            action.Disable();
            action.Dispose();
        }
        actions.Clear();
        UnregisterModel();

        StopVoice();
        if (initialized)
        {
            SnapToRest();
            RestoreInnerRendering();
            SetFineDetailHidden(false);
        }
        DestroyPickScene();
        foreach (Part part in parts)
            if (part.OwnsCollider && part.SelectionCollider != null)
                Destroy(part.SelectionCollider);
        if (ownsAudioSource && presenterAudioSource != null)
            Destroy(presenterAudioSource);
        if (ownedInteractionManager != null)
        {
            // In XR the rig's interactors adopted this manager; it stays for them.
            List<IXRInteractor> interactors = new List<IXRInteractor>();
            ownedInteractionManager.GetRegisteredInteractors(interactors);
            if (interactors.Count == 0)
                Destroy(ownedInteractionManager.gameObject);
        }
        if (ghostSolidMaterial != null)
            Destroy(ghostSolidMaterial);
        if (ghostGlassMaterial != null)
            Destroy(ghostGlassMaterial);
        if (xrayLight != null)
            Destroy(xrayLight.gameObject);
    }
}
