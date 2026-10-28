using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Collision-aware showroom greeter for the existing Remy presenter.
/// Reuses CarExplorerController's distance-based greeting panel. After the greeting he keeps a station beside the
/// car the visitor is looking at, out of their line of sight to it.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShowroomPresenterAgent : MonoBehaviour
{
    [Header("Showroom interior")]
    // Optional; when empty, 'Showroom Floor' and 'Showroom Architecture' are found by name.
    [SerializeField] private Transform showroomFloor;
    [SerializeField] private Transform showroomArchitecture;
    [SerializeField, Min(0f)] private float interiorWallInset = 1f;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 1.15f;
    [SerializeField] private float turnSpeed = 240f;
    [SerializeField] private float stopDistance = 1.9f;
    [SerializeField, Min(0.5f)] private float stuckTimeout = 1.5f;
    [SerializeField, Min(1f)] private float approachTimeout = 15f;
    [Header("Station beside the car")]
    // Optional; when empty, 'CarConcept', its podium 'Circular Display' and the displays under
    // 'Showroom Premium Collection' are found by name. Without the car Remy keeps following the visitor.
    [SerializeField] private Transform heroCar;
    [SerializeField] private Transform heroPodium;
    [SerializeField] private Transform displayCollection;
    // Gap between Remy's body and the car's parts, wherever they have been taken, and the podium edge.
    [SerializeField, Min(0.1f)] private float stationClearance = 0.45f;
    // Preferred bearing of the station around the car, in degrees from the visitor's line to the car's centre.
    [SerializeField, Range(30f, 90f)] private float stationBearingMin = 60f;
    [SerializeField, Range(90f, 160f)] private float stationBearingMax = 110f;
    // How long the station may stay well outside that band (the visitor walked around the car) before Remy moves.
    [SerializeField, Min(0.5f)] private float stationRetargetDwell = 2f;
    // How long the visitor looks at another display before Remy moves to its side.
    [SerializeField, Min(0.5f)] private float displayVisitDwell = 3f;
    [Header("Company during the greeting")]
    // Fresh serialized field names prevent older scene values from restoring
    // a previous follow distance or angle after recompilation.
    [SerializeField, Range(1.5f, 4f)] private float followDistance = 2.5f;
    [SerializeField, Range(15f, 60f)] private float followBearingDegrees = 35f;
    [SerializeField] private bool followOnVisitorLeft = true;
    [SerializeField, Min(0.25f)] private float followRetargetDistance = 1f;
    [SerializeField, Range(10f, 90f)] private float followRetargetAngle = 30f;
    [SerializeField, Min(0.05f)] private float followArrivalRadius = 0.16f;
    [SerializeField, Min(30f)] private float followTurnSpeed = 135f;
    [SerializeField, Min(0.2f)] private float obstacleProbeDistance = 0.75f;
    [SerializeField] private Vector3[] aisleWaypoints =
    {
        new Vector3(-5.4f, 0f, -6.0f),
        new Vector3(-5.4f, 0f, -2.2f),
        new Vector3(-5.0f, 0f, 1.8f),
        new Vector3(-4.2f, 0f, 5.0f)
    };

    // Probe capsules start above low steps such as the display disc.
    private const float StepClearance = 0.25f;
    private const float ApproachBearingDegrees = 35f;
    private const float ApproachArrivalRadius = 0.3f;
    // Planned spots keep Remy fully on screen; an existing spot is kept while he is mostly on screen.
    private const float PlanEdgeMarginDegrees = 7f;
    private const float ComfortEdgeMarginDegrees = 3f;
    private const float CentreBearingDegrees = 12f;
    private const float ViewPenalty = 0.6f;
    // Hiding a registered panel from the visitor, or standing hidden behind one, costs up to this much.
    // A spot that covers more than ComfortKeepOutCover of a panel is not kept once the greeting is over.
    private const float KeepOutPenalty = 0.6f;
    private const float ComfortKeepOutCover = 0.25f;
    // Half-width margin added to Remy's capsule radius for his silhouette (arms, shoulders).
    private const float SilhouetteMargin = 0.05f;
    // After arriving Remy keeps the greeting distance this long, which covers the explorer's 7 s WELCOME, and
    // longer while the explorer still shows it. He leaves for his station after GreetingCompanyLimit in any case.
    private const float GreetingDistanceHold = 8f;
    private const float GreetingCompanyLimit = 20f;
    private const float InteriorExitMargin = 0.5f;
    private const float PersonalSpace = 0.9f;
    private const float TurnAwayDwell = 0.6f;
    private const float DriftOutOfViewDwell = 2f;
    private const float ProgressStep = 0.1f;
    private const int MaxDetourAttempts = 3;
    private static readonly float[] DetourOffsets = { 1.5f, 3f, 4.5f, 6f };
    // Station: the parts' footprint is read at a low rate. A station is kept while its bearing stays within
    // StationBandTolerance of the preferred band; standing in the visitor's line of sight for SightBlockDwell, or
    // inside the footprint, moves Remy, and no soft reason re-plans within StationReplanInterval of the last plan.
    private const float StationRefreshInterval = 0.5f;
    private const float RendererRefreshInterval = 5f;
    private const float StationBandTolerance = 25f;
    private const float SightBlockDwell = 0.35f;
    private const float StationReplanInterval = 1f;
    private const float StationRingStep = 0.6f;
    private const float RouteCornerStep = 25f;
    // Stations whose straight way is blocked that get a way round before only straight ways count (the stations of
    // a premium display seen from the far side of the hero car need several).
    private const int MaxStationRouteAttempts = 8;
    // Extra width of the planned sight-line clearance, so small steps of the visitor do not move Remy again.
    private const float SightPlanMargin = 0.35f;
    private const float SightBlockPenalty = 4f;
    // Parts the visitor studies (taken out, the exploded car, the inspected node): a station whose bearing, seen from the
    // eye, lies within their angular span widened by BehindPartsMargin degrees (at least Remy's silhouette plus
    // BehindPartsGap) and farther away than they are would show him among or behind them. A station is left after
    // SightBlockDwell; planned stations keep BehindPartsPlanMargin more, so small steps of the visitor do not move him.
    private const float BehindPartsMargin = 6f;
    private const float BehindPartsGap = 2f;
    private const float BehindPartsPlanMargin = 4f;
    private const float BehindPartsPenalty = 3f;
    // While the eye moves (the visitor walks, the inspect camera orbits) the region behind the parts moves with it, so
    // Remy waits up to BehindPartsMovingDwell instead of chasing it; the eye is steady once it has stayed within
    // EyeSteadyRadius, its view turned by at most EyeSteadyAngle degrees, for EyeSteadyTime. While the desktop inspect
    // camera frames a node he never re-plans before the camera is steady.
    private const float BehindPartsMovingDwell = 1.5f;
    private const float EyeSteadyRadius = 0.2f;
    private const float EyeSteadyAngle = 4f;
    private const float EyeSteadyTime = 0.5f;
    private const float SideSwitchPenalty = 0.6f;
    private const float HiddenPenalty = 0.8f;
    private const float OutOfViewPenalty = 0.3f;
    // A visitor walking round the car at this many degrees per second or more is heading for the stations ahead of
    // them, which cost up to AheadPenalty extra, so Remy gives way to the other side instead of walking on ahead.
    private const float CirclingRate = 6f;
    private const float AheadPenalty = 1.4f;
    // The visitor attends a display within this distance of it and this many degrees from their view direction.
    private const float DisplayAttendDistance = 5.5f;
    private const float DisplayAttendAngle = 50f;
    private const int HeroSubject = -1;
    private static readonly Collider[] OverlapHits = new Collider[32];
    private static readonly RaycastHit[] CastHits = new RaycastHit[32];
    // 24 ring bearings 15 degrees apart plus up to 16 that just clear the edges of registered panels.
    private static readonly float[] RingBearings = new float[40];
    private static readonly Vector3[] CandidateSpots = new Vector3[120];
    private static readonly float[] CandidateCosts = new float[120];
    private static readonly Vector2[] FootprintCorners = new Vector2[4];

    private struct ViewKeepOut
    {
        public Transform Panel;
        public float HalfWidth;
    }

    private Camera visitorCamera;
    private Transform visitorBody;
    private Camera visitorBodyCamera;
    private Transform visitorBodyCameraParent;
    private CharacterController visitorBodyController;
    private CharacterController motor;
    private Animator animator;
    private Vector3 lastVisitorForward = Vector3.forward;
    private bool visitorInside;
    private bool hasInterior;
    private float interiorFloorTop = float.NegativeInfinity;
    private Vector2 interiorMin;
    private Vector2 interiorMax;
    private Transform explanationTarget;
    private Vector3 explanationLocalFocus;
    private Transform gestureTarget;
    private float explanationFocusUntil;
    private int waypointIndex;
    private bool greetingTripStarted;
    private bool directApproach;
    private bool approachOnHold;
    private bool arrived;
    private bool metVisitor;
    private float arrivedTime = float.NegativeInfinity;
    private bool stepBackPending;
    private float approachDeadline;
    private float gaitTime;
    private float gaitSpeed;
    private Vector3 navTarget;
    private Vector3 navDetour;
    private bool hasNavTarget;
    private bool hasNavDetour;
    // Corners of a way round a car that follow navDetour, walked in order (see TrySetRouteAround).
    private readonly Vector3[] routeCorners = new Vector3[16];
    private readonly Vector3[] routeCornerScratch = new Vector3[16];
    private int routeCornerCount;
    private int routeCornerIndex;
    private float navBestDistance;
    private float navProgressTime;
    private float nextShortcutCheck;
    private Vector3 planVisitorPosition;
    private Vector3 planVisitorForward;
    private bool planVisitorInside;
    private bool followSpotInView;
    private float planKeepOutCover;
    private Vector3 waitingCentre;
    private float outOfViewSince = -1f;
    private float nextPersonalSpacePlan;
    private readonly List<Vector3> rejectedTargets = new List<Vector3>();
    private readonly List<ViewKeepOut> viewKeepOuts = new List<ViewKeepOut>();
    private float componentAttentionWeight;
    private float componentAttentionVelocity;

    // Station state. Subjects are the hero car (HeroSubject) or a premium display (its index in displayFootprints).
    private bool greetingOver;
    private bool stationPlanned;
    private int plannedSubject = HeroSubject;
    private int attendedSubject = HeroSubject;
    private int pendingSubject = HeroSubject;
    private float pendingSubjectSince;
    private float nextStationRefresh;
    private float nextRendererRefresh;
    private float nextStationPlan;
    private float sightBlockedSince = -1f;
    private float bandLeftSince = -1f;
    // The visitor's smoothed angular speed round the hero car (degrees per second, sign as SignedBearing).
    private float visitorCircling;
    private Vector3 lastVisitorOffset;
    private bool hasLastVisitorOffset;
    private readonly List<Renderer> heroRenderers = new List<Renderer>();
    private bool hasHeroFootprint;
    private bool heroFootprintMoving;
    private Vector2 heroMin;
    private Vector2 heroMax;
    private Vector3 heroCentre;
    private bool hasPodium;
    private Vector2 podiumCentre;
    private float podiumRadius;
    private readonly List<Bounds> displayFootprints = new List<Bounds>();
    // The parts the visitor studies, where the explorer plans them (see RefreshExhibit). The version changes with the
    // node, the view and the open levels; a station the last plan had to accept behind them is kept until it changes.
    private bool hasExhibit;
    private Bounds exhibitBounds;
    private CarStructureNode exhibitNode;
    private CarExplorerView exhibitView;
    private int exhibitOpenLevels;
    private int exhibitVersion;
    private float nextExhibitRefresh;
    private float behindPartsSince = -1f;
    private bool behindPartsAccepted;
    private int behindPartsAcceptedVersion;
    private Vector3 eyeAnchor;
    private Vector3 eyeAnchorForward;
    private float eyeSteadySince;

    private Transform hips;
    private Transform leftUpperLeg;
    private Transform rightUpperLeg;
    private Transform leftUpperArm;
    private Transform rightUpperArm;
    private Transform leftLowerArm;
    private Transform rightLowerArm;
    private Transform leftHand;
    private Transform rightHand;
    private Transform leftIndexProximal;
    private Transform rightIndexProximal;
    private Transform spine;
    private Transform chest;
    private Transform neck;
    private Transform head;
    private Quaternion hipsRest;
    private Vector3 hipsRestPosition;
    private Quaternion leftLegRest;
    private Quaternion rightLegRest;
    private Quaternion leftArmRest;
    private Quaternion rightArmRest;
    private Quaternion leftLowerArmRest;
    private Quaternion rightLowerArmRest;
    private Quaternion leftHandRest;
    private Quaternion rightHandRest;
    private Quaternion spineRest;
    private Quaternion chestRest;
    private Quaternion neckRest;
    private Quaternion headRest;
    private Vector3 leftArmSwingAxis = Vector3.right;
    private Vector3 rightArmSwingAxis = Vector3.right;
    private Quaternion leftUpperArmFramePose;
    private Quaternion rightUpperArmFramePose;
    private Quaternion leftLowerArmFramePose;
    private Quaternion rightLowerArmFramePose;
    private Quaternion leftHandFramePose;
    private Quaternion rightHandFramePose;
    private float leftGestureWeight;
    private float rightGestureWeight;
    private float leftGestureWeightVelocity;
    private float rightGestureWeightVelocity;
    private Vector3 gestureAimPosition;
    private Vector3 gestureAimVelocity;
    private bool hasGestureAimPosition;
    private bool pointWithLeftArm;
    private bool hasLeftArmFramePose;
    private bool hasRightArmFramePose;

    /// <summary>
    /// True once Remy has come to the visitor and stood close by in view; a timed-out approach does not count
    /// until he has caught up.
    /// </summary>
    public bool HasArrived => greetingTripStarted && arrived && metVisitor;

    private float BodyRadius
    {
        get
        {
            Vector3 scale = transform.lossyScale;
            return motor.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }
    }

    private float BodyHeight => motor.height * Mathf.Abs(transform.lossyScale.y);

    private void Awake()
    {
        animator = GetComponent<Animator>();
        motor = GetComponent<CharacterController>();
        if (motor == null)
            motor = gameObject.AddComponent<CharacterController>();

        Vector3 worldScale = transform.lossyScale;
        float scaleY = Mathf.Max(0.001f, Mathf.Abs(worldScale.y));
        float scaleXZ = Mathf.Max(0.001f, Mathf.Max(Mathf.Abs(worldScale.x), Mathf.Abs(worldScale.z)));
        motor.height = 1.72f / scaleY;
        motor.radius = 0.27f / scaleXZ;
        motor.center = new Vector3(0f, 0.86f / scaleY, 0f);
        motor.stepOffset = 0.28f / scaleY;
        motor.slopeLimit = 45f;
        // Slow turning steps can be shorter than the default minimum move distance.
        motor.minMoveDistance = 0f;

        ResolveVisitorCamera();
        CacheRigPose();
    }

    private void Start()
    {
        ResolveShowroomInterior();
        ResolveStationSubjects();
    }

    private void Update()
    {
        ResolveVisitorCamera();
        ResetProceduralArmBasePose();

        if (visitorCamera == null)
            return;

        Vector3 visitorPosition = visitorCamera.transform.position;
        Vector3 visitorForward = GetVisitorForward();
        // The exit margin keeps a visitor standing on the threshold from toggling in and out.
        visitorInside = IsInsideShowroom(visitorPosition, visitorInside ? -InteriorExitMargin : 0f);
        if (!greetingTripStarted && visitorInside)
            BeginGreetingTrip();

        float attentionTarget = explanationTarget != null && Time.time < explanationFocusUntil ? 1f : 0f;
        componentAttentionWeight = Mathf.SmoothDamp(componentAttentionWeight, attentionTarget,
            ref componentAttentionVelocity, 0.22f, Mathf.Infinity, Time.deltaTime);
        // Approach and greet the visitor, then keep a station beside the car they look at (or follow them when the
        // scene has no hero car).
        if (greetingTripStarted && !arrived)
            UpdateApproach(visitorPosition, visitorForward);
        else if (greetingTripStarted && (!IsGreetingOver() || !hasHeroFootprint))
            UpdateFollow(visitorPosition, visitorForward);
        else if (greetingTripStarted)
            UpdateStation(visitorPosition, visitorForward);
        else
            AnimateIdle();

        KeepAboveShowroomFloor();
        AnimateUpperBodyAttention();
        CaptureArmFramePose();
    }

    // Safety net for the thin showroom floor: if anything ever pushes Remy below it, put him back on top
    // instead of letting him wade through the floor.
    private void KeepAboveShowroomFloor()
    {
        if (motor == null || float.IsNegativeInfinity(interiorFloorTop) || !IsInsideShowroom(transform.position, 0f))
            return;
        Vector3 position = transform.position;
        if (position.y >= interiorFloorTop - 0.05f)
            return;
        bool wasEnabled = motor.enabled;
        motor.enabled = false;
        transform.position = new Vector3(position.x, interiorFloorTop, position.z);
        motor.enabled = wasEnabled;
    }

    // Animator evaluation happens after Update. Apply the procedural pose in
    // LateUpdate so it remains visible instead of being overwritten next frame.
    private void LateUpdate()
    {
        if (animator == null || !animator.isHuman)
            return;

        // If a controller is assigned later, capture its evaluated pose as the
        // base before applying the gesture. Remy currently has no controller.
        if (animator.runtimeAnimatorController != null)
            CaptureArmFramePose();

        Transform target = gestureTarget;
        if (target != null)
        {
            Vector3 focus = GetFocusPoint(target);
            if (!hasGestureAimPosition)
            {
                gestureAimPosition = focus;
                hasGestureAimPosition = true;
            }
            gestureAimPosition = Vector3.SmoothDamp(gestureAimPosition, focus,
                ref gestureAimVelocity, 0.16f, Mathf.Infinity, Time.deltaTime);

            // Switch arms once the focus is clearly on Remy's other side after he turned or walked.
            Vector3 localFocus = transform.InverseTransformDirection(focus - transform.position);
            float side = localFocus.x / Mathf.Max(0.001f, new Vector2(localFocus.x, localFocus.z).magnitude);
            if (pointWithLeftArm ? side > 0.3f : side < -0.3f)
                pointWithLeftArm = !pointWithLeftArm;
        }

        bool leftAvailable = leftUpperArm != null && leftLowerArm != null && leftHand != null;
        bool rightAvailable = rightUpperArm != null && rightLowerArm != null && rightHand != null;
        bool preferLeft = pointWithLeftArm;
        bool pointLeft = leftAvailable && (!rightAvailable || preferLeft);
        bool pointRight = rightAvailable && (!leftAvailable || !preferLeft);
        float leftGoal = target != null && pointLeft ? 1f : 0f;
        float rightGoal = target != null && pointRight ? 1f : 0f;
        leftGestureWeight = Mathf.SmoothDamp(leftGestureWeight, leftGoal,
            ref leftGestureWeightVelocity, 0.20f, Mathf.Infinity, Time.deltaTime);
        rightGestureWeight = Mathf.SmoothDamp(rightGestureWeight, rightGoal,
            ref rightGestureWeightVelocity, 0.20f, Mathf.Infinity, Time.deltaTime);

        if (leftAvailable && (leftGestureWeight > 0.001f || leftGoal > 0f))
            ApplyPointingPose(leftUpperArm, leftLowerArm, leftHand, leftIndexProximal,
                leftUpperArmFramePose, leftLowerArmFramePose, leftHandFramePose, leftGestureWeight,
                gestureAimPosition, true);
        if (rightAvailable && (rightGestureWeight > 0.001f || rightGoal > 0f))
            ApplyPointingPose(rightUpperArm, rightLowerArm, rightHand, rightIndexProximal,
                rightUpperArmFramePose, rightLowerArmFramePose, rightHandFramePose, rightGestureWeight,
                gestureAimPosition, false);
    }

    private void ResolveVisitorCamera()
    {
        // The visitor's eye is the explorer's view camera (the desktop camera, the XR head or the desktop inspect
        // camera orbiting a node), read every frame, so every sight-line test follows the view the visitor sees.
        // Without an explorer: in XR the desktop camera is deactivated before any Awake and the rig's head camera is
        // Camera.main. The view can also switch at runtime (XR falling back to desktop), so an inactive camera is
        // replaced.
        ICarExplorerModel model = CarExplorerModels.Current;
        Camera modelCamera = model != null ? model.ViewCamera : null;
        if (modelCamera != null && modelCamera.isActiveAndEnabled)
        {
            if (modelCamera != visitorCamera)
            {
                visitorCamera = modelCamera;
                ClearNavigation();
            }
        }
        else if (visitorCamera == null || !visitorCamera.isActiveAndEnabled)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != visitorCamera)
            {
                visitorCamera = mainCamera;
                ClearNavigation();
            }
        }

        // The body changes with the camera, and the desktop body is created (re-parenting the camera)
        // during another component's Start, after this Awake. Without a body (or once it is destroyed)
        // the lookup is repeated.
        Transform cameraParent = visitorCamera != null ? visitorCamera.transform.parent : null;
        if (visitorCamera == visitorBodyCamera && cameraParent == visitorBodyCameraParent &&
            (visitorCamera == null || visitorBodyController != null))
            return;
        visitorBodyCamera = visitorCamera;
        visitorBodyCameraParent = cameraParent;
        visitorBodyController = visitorCamera != null
            ? visitorCamera.GetComponentInParent<CharacterController>() : null;
        visitorBody = visitorBodyController != null ? visitorBodyController.transform
            : visitorCamera != null ? visitorCamera.transform : null;
    }

    private Vector3 GetVisitorForward()
    {
        Vector3 forward = Vector3.ProjectOnPlane(visitorCamera.transform.forward, Vector3.up);
        if (forward.sqrMagnitude > 0.0004f)
            lastVisitorForward = forward.normalized;
        return lastVisitorForward;
    }

    // Horizontal half field of view minus a margin for Remy's body width.
    private float GetVisibleHalfAngle(float edgeMargin)
    {
        float verticalHalf = visitorCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float horizontalHalf = Mathf.Atan(Mathf.Tan(verticalHalf) * Mathf.Max(0.1f, visitorCamera.aspect)) *
                               Mathf.Rad2Deg;
        return Mathf.Max(15f, horizontalHalf - edgeMargin);
    }

    private void ResolveShowroomInterior()
    {
        Transform floor = showroomFloor != null ? showroomFloor : FindShowroomObject("Showroom Floor");
        Transform architecture = showroomArchitecture != null
            ? showroomArchitecture : FindShowroomObject("Showroom Architecture");
        Renderer floorRenderer = floor != null ? floor.GetComponentInChildren<Renderer>() : null;
        float floorTop = floorRenderer != null ? floorRenderer.bounds.max.y : transform.position.y;
        if (floorRenderer != null)
            interiorFloorTop = floorTop;

        bool hasWalls = false;
        Bounds walls = default;
        List<Bounds> sideWalls = new List<Bounds>();
        if (architecture != null)
        {
            // Floor-standing pieces (glazing, mullions, wall panels) outline the interior;
            // the canopy and beams overhang the open facade and are skipped.
            foreach (Renderer part in architecture.GetComponentsInChildren<Renderer>())
            {
                Bounds bounds = part.bounds;
                if (!part.enabled || bounds.min.y > floorTop + 0.6f)
                    continue;
                if (!hasWalls)
                {
                    walls = bounds;
                    hasWalls = true;
                }
                else
                    walls.Encapsulate(bounds);
                // Wall-height glazing running front to back (not mullions, sills or the rear wall).
                if (bounds.size.y >= 1.15f && bounds.size.z >= 1.4f && bounds.size.x <= 0.5f)
                    sideWalls.Add(bounds);
            }
        }

        hasInterior = hasWalls || floorRenderer != null;
        if (!hasInterior)
        {
            Debug.LogWarning("[Showroom Presenter] 'Showroom Floor' and 'Showroom Architecture' were not found; Remy treats the whole scene as the showroom.", this);
            return;
        }

        Bounds area = hasWalls ? walls : floorRenderer.bounds;
        interiorMin = new Vector2(area.min.x, area.min.z);
        interiorMax = new Vector2(area.max.x, area.max.z);
        if (hasWalls)
        {
            // The rear wall sticks out past the side glass, so the sides come from the glazing's inner faces;
            // otherwise a visitor walking outside along the glass would count as inside.
            float innerLeft = float.NegativeInfinity;
            float innerRight = float.PositiveInfinity;
            foreach (Bounds side in sideWalls)
            {
                if (side.center.x < walls.center.x)
                    innerLeft = Mathf.Max(innerLeft, side.max.x);
                else
                    innerRight = Mathf.Min(innerRight, side.min.x);
            }
            if (innerLeft > interiorMin.x && innerLeft < walls.center.x)
                interiorMin.x = innerLeft;
            if (innerRight < interiorMax.x && innerRight > walls.center.x)
                interiorMax.x = innerRight;
        }
        if (hasWalls && floorRenderer != null)
        {
            Bounds floorBounds = floorRenderer.bounds;
            Vector2 min = Vector2.Max(interiorMin, new Vector2(floorBounds.min.x, floorBounds.min.z));
            Vector2 max = Vector2.Min(interiorMax, new Vector2(floorBounds.max.x, floorBounds.max.z));
            if (min.x < max.x && min.y < max.y)
            {
                interiorMin = min;
                interiorMax = max;
            }
        }
    }

    private Transform FindShowroomObject(string objectName)
    {
        Transform found = transform.root.Find(objectName);
        if (found == null)
        {
            GameObject named = GameObject.Find(objectName);
            if (named != null)
                found = named.transform;
        }
        return found;
    }

    private bool IsInsideShowroom(Vector3 position, float inset)
    {
        return !hasInterior ||
               (position.x >= interiorMin.x + inset && position.x <= interiorMax.x - inset &&
                position.z >= interiorMin.y + inset && position.z <= interiorMax.y - inset);
    }

    private Vector3 ClampToInterior(Vector3 position)
    {
        if (!hasInterior)
            return position;
        position.x = ClampInside(position.x, interiorMin.x, interiorMax.x, interiorWallInset);
        position.z = ClampInside(position.z, interiorMin.y, interiorMax.y, interiorWallInset);
        return position;
    }

    private static float ClampInside(float value, float min, float max, float inset)
    {
        min += inset;
        max -= inset;
        return min <= max ? Mathf.Clamp(value, min, max) : (min + max) * 0.5f;
    }

    private void BeginGreetingTrip()
    {
        greetingTripStarted = true;
        arrived = false;
        metVisitor = false;
        greetingOver = false;
        directApproach = false;
        approachOnHold = false;
        waypointIndex = 0;
        approachDeadline = Time.time + approachTimeout;
        rejectedTargets.Clear();
        ClearNavigation();
    }

    private void Arrive()
    {
        arrived = true;
        arrivedTime = Time.time;
        stepBackPending = true;
        ClearNavigation();
        AnimateIdle();
    }

    // Remy has met the visitor once he stands at a conversational distance (one FOLLOW does not step away
    // from right after arriving), inside their view and not hidden.
    private bool IsMeetingVisitor(Vector3 visitorPosition, Vector3 visitorForward)
    {
        float distance = HorizontalDistance(transform.position, visitorPosition);
        return distance >= Mathf.Min(stopDistance, followDistance) - 0.35f && distance <= followDistance + 0.6f &&
               Mathf.Abs(SignedBearing(visitorPosition, visitorForward, transform.position)) <=
               GetVisibleHalfAngle(ComfortEdgeMarginDegrees) &&
               IsVisibleToVisitor(transform.position);
    }

    private bool IsHoldingGreetingDistance()
    {
        return Time.time - arrivedTime < GreetingDistanceHold;
    }

    // The greeting is over once Remy has met the visitor, held the greeting distance and the explorer no longer
    // shows the welcome, once he has met a visitor who has started exploring the car (they want the car in view, not
    // company), once the visitor has left the showroom, or GreetingCompanyLimit after he arrived. It stays over: a
    // welcome shown later (beside him at his station) does not call him back.
    private bool IsGreetingOver()
    {
        if (greetingOver)
            return true;
        float since = Time.time - arrivedTime;
        if (!visitorInside || since >= GreetingCompanyLimit ||
            (metVisitor && (IsVisitorExploring() || (since >= GreetingDistanceHold && !IsWelcomeShown()))))
        {
            greetingOver = true;
            stationPlanned = false;
            ClearNavigation();
        }
        return greetingOver;
    }

    private static bool IsWelcomeShown()
    {
        ICarExplorerModel model = CarExplorerModels.Current;
        return model != null && model.GreetingShown;
    }

    // Something selected or out of the car, or a view other than the normal one.
    private static bool IsVisitorExploring()
    {
        ICarExplorerModel model = CarExplorerModels.Current;
        return model != null &&
               (model.Selected != null || model.TakenOut != null || model.View != CarExplorerView.Normal);
    }

    private void UpdateApproach(Vector3 visitorPosition, Vector3 visitorForward)
    {
        if (!directApproach)
        {
            if (Time.time < approachDeadline && SelectAisleWaypoint(visitorPosition, out Vector3 waypoint))
            {
                // A blocked aisle waypoint is skipped instead of walking in place.
                if (!StepToward(waypoint, turnSpeed))
                {
                    waypointIndex++;
                    ResetProgress();
                }
                return;
            }

            // The approach budget starts once Remy leaves the aisle.
            directApproach = true;
            approachDeadline = Time.time + approachTimeout;
            ClearNavigation();
        }

        if (Time.time >= approachDeadline)
        {
            Arrive();
            return;
        }

        if (!hasNavTarget || VisitorMovedSincePlan(visitorPosition, visitorForward))
        {
            rejectedTargets.Clear();
            PlanApproach(visitorPosition, visitorForward);
        }

        // No reachable spot: wait facing the visitor until they move or the approach times out. A visitor
        // who walks up to the waiting Remy has met him.
        if (approachOnHold)
        {
            FaceConversationTarget();
            AnimateIdle();
            if (IsMeetingVisitor(visitorPosition, visitorForward))
            {
                metVisitor = true;
                Arrive();
            }
            return;
        }

        float targetDistance = HorizontalDistance(transform.position, navTarget);
        if (!hasNavDetour && targetDistance <= ApproachArrivalRadius)
        {
            // Close enough once in view; coming from outside the view he steps onto the spot itself. If the
            // visitor has turned away meanwhile, he is met once FOLLOW has brought him back into view.
            bool meeting = IsMeetingVisitor(visitorPosition, visitorForward);
            if (meeting || targetDistance <= followArrivalRadius)
            {
                metVisitor = meeting;
                Arrive();
                return;
            }
        }

        if (!StepAlongRoute(turnSpeed))
        {
            // No progress for a while: try the next free point around the visitor.
            rejectedTargets.Add(navTarget);
            PlanApproach(visitorPosition, visitorForward);
        }
    }

    // Aisle waypoints that would not bring Remy closer to the visitor are skipped.
    private bool SelectAisleWaypoint(Vector3 goal, out Vector3 waypoint)
    {
        waypoint = Vector3.zero;
        while (aisleWaypoints != null && waypointIndex < aisleWaypoints.Length)
        {
            waypoint = aisleWaypoints[waypointIndex];
            waypoint.y = transform.position.y;
            if (HorizontalDistance(transform.position, waypoint) >= 0.55f &&
                HorizontalDistance(waypoint, goal) < HorizontalDistance(transform.position, goal))
                return true;
            waypointIndex++;
            ResetProgress();
        }
        return false;
    }

    private void PlanApproach(Vector3 visitorPosition, Vector3 visitorForward)
    {
        planVisitorPosition = visitorPosition;
        planVisitorForward = visitorForward;
        // 1.6-1.9 m away at the front-left or front-right, whichever is closer and reachable. The mirrored
        // spot on Remy's side of the visitor's view line is always the closer one, so he does not walk across
        // in front of the visitor; straight ahead or behind them, the side he follows on breaks the tie.
        float bearing = Mathf.Min(ApproachBearingDegrees, GetVisibleHalfAngle(PlanEdgeMarginDegrees));
        float side = followOnVisitorLeft ? -1f : 1f;
        float remyBearing = SignedBearing(visitorPosition, visitorForward, transform.position);
        if (Mathf.Abs(remyBearing) > 5f && Mathf.Abs(remyBearing) < 175f)
            side = Mathf.Sign(remyBearing);
        int count = BuildVisitorRing(visitorPosition, visitorForward, side * bearing,
            stopDistance - 0.15f, 0.15f, 0.15f, false);
        approachOnHold = !TryChooseRoute(count, true);
        if (approachOnHold)
            SetRoute(transform.position, false, transform.position);
    }

    private bool VisitorMovedSincePlan(Vector3 visitorPosition, Vector3 visitorForward)
    {
        return HorizontalDistance(visitorPosition, planVisitorPosition) > followRetargetDistance ||
               Vector3.Angle(planVisitorForward, visitorForward) > followRetargetAngle;
    }

    private void UpdateFollow(Vector3 visitorPosition, Vector3 visitorForward)
    {
        if (NeedsFollowPlan(visitorPosition, visitorForward))
        {
            rejectedTargets.Clear();
            PlanFollow(visitorPosition, visitorForward);
        }

        if (!hasNavDetour && HorizontalDistance(transform.position, navTarget) <= followArrivalRadius)
        {
            if (!metVisitor && IsMeetingVisitor(visitorPosition, visitorForward))
                metVisitor = true;
            FaceConversationTarget();
            AnimateIdle();
            return;
        }

        if (!StepAlongRoute(followTurnSpeed))
        {
            // Blocked for a while: try another spot, or hold position when none is reachable.
            rejectedTargets.Add(navTarget);
            PlanFollow(visitorPosition, visitorForward);
        }
    }

    private bool NeedsFollowPlan(Vector3 visitorPosition, Vector3 visitorForward)
    {
        if (!hasNavTarget || visitorInside != planVisitorInside)
            return true;
        // While the visitor is outside, Remy keeps waiting where they left.
        if (!visitorInside)
            return false;
        if (HorizontalDistance(visitorPosition, planVisitorPosition) > followRetargetDistance)
            return true;
        // Step aside when the visitor walks up to Remy, but not while he is passing by.
        bool waiting = !hasNavDetour && HorizontalDistance(transform.position, navTarget) <= followArrivalRadius;
        if (waiting && Time.time >= nextPersonalSpacePlan &&
            HorizontalDistance(visitorPosition, transform.position) < PersonalSpace)
            return true;

        if (!IsHoldingGreetingDistance())
        {
            // Once the greeting is over, step back out to the follow distance.
            if (stepBackPending)
            {
                stepBackPending = false;
                if (HorizontalDistance(navTarget, visitorPosition) < followDistance - 0.35f)
                    return true;
            }
            // A panel shown or moved behind Remy (e.g. the overview once a part card closes): find a clearer
            // spot. Only a clear increase over the cover where he settled counts, so an overlap that the
            // last plan could not avoid is not re-planned over and over.
            if (waiting)
            {
                float cover = GetKeepOutCover(transform.position, visitorPosition);
                if (planKeepOutCover < 0f)
                    planKeepOutCover = cover;
                else if (cover > Mathf.Max(ComfortKeepOutCover, planKeepOutCover + 0.15f))
                    return true;
            }
        }

        float turned = Vector3.Angle(planVisitorForward, visitorForward);
        // The last plan found no free spot in view (e.g. the visitor faces a wall): retry only after a real turn.
        if (!followSpotInView)
            return turned > followRetargetAngle;

        // Turning re-targets only while Remy is out of view, so turning toward him never pushes him
        // further around the visitor. A turn past followRetargetAngle re-targets quickly; drifting out
        // of view after smaller turns takes longer, so glancing around does not make him shuffle.
        bool outOfView = Mathf.Abs(SignedBearing(visitorPosition, visitorForward, transform.position)) >
                         GetVisibleHalfAngle(ComfortEdgeMarginDegrees);
        if (!outOfView)
        {
            outOfViewSince = -1f;
            return false;
        }
        if (outOfViewSince < 0f)
            outOfViewSince = Time.time;
        float dwell = turned > followRetargetAngle ? TurnAwayDwell : DriftOutOfViewDwell;
        return Time.time - outOfViewSince >= dwell;
    }

    private void PlanFollow(Vector3 visitorPosition, Vector3 visitorForward)
    {
        bool visitorJustLeft = planVisitorInside || !hasNavTarget;
        planVisitorPosition = visitorPosition;
        planVisitorForward = visitorForward;
        planVisitorInside = visitorInside;
        outOfViewSince = -1f;
        nextPersonalSpacePlan = Time.time + 1f;
        // Measured once Remy stands at the new spot.
        planKeepOutCover = -1f;

        bool found;
        followSpotInView = false;
        if (visitorInside)
        {
            float comfortHalfAngle = GetVisibleHalfAngle(ComfortEdgeMarginDegrees);
            if (IsComfortableFollowSpot(transform.position, visitorPosition, visitorForward, comfortHalfAngle))
            {
                SetRoute(transform.position, false, transform.position);
                followSpotInView = true;
                return;
            }
            // Front-left (by default) inside the field of view. When Remy already stands on the
            // other side he stays there rather than crossing in front of the visitor.
            float bearing = Mathf.Min(followBearingDegrees, GetVisibleHalfAngle(PlanEdgeMarginDegrees));
            float side = followOnVisitorLeft ? -1f : 1f;
            float remyBearing = SignedBearing(visitorPosition, visitorForward, transform.position);
            if (Mathf.Abs(remyBearing) > 5f && Mathf.Abs(remyBearing) < 175f &&
                HorizontalDistance(transform.position, visitorPosition) < followDistance + 3f)
                side = Mathf.Sign(remyBearing);
            int count = BuildVisitorRing(visitorPosition, visitorForward, side * bearing,
                followDistance, 0.3f, 0.8f, true);
            found = TryChooseRoute(count, true);
            followSpotInView = found &&
                               Mathf.Abs(SignedBearing(visitorPosition, visitorForward, navTarget)) <= comfortHalfAngle;
        }
        else
        {
            // Never follow onto the street: wait just inside, near where the visitor left.
            if (visitorJustLeft)
                waitingCentre = ClampToInterior(visitorPosition);
            found = TryChooseRoute(BuildWaitingSpots(waitingCentre), false);
        }

        if (!found)
            SetRoute(transform.position, false, transform.position);
    }

    private bool IsComfortableFollowSpot(Vector3 spot, Vector3 visitorPosition, Vector3 visitorForward,
        float comfortHalfAngle)
    {
        float distance = HorizontalDistance(spot, visitorPosition);
        float bearing = Mathf.Abs(SignedBearing(visitorPosition, visitorForward, spot));
        // Right after arriving the greeting distance counts too, and a partly covered panel is tolerated,
        // so Remy does not walk off during the greeting. Afterwards the follow band applies.
        bool greeting = IsHoldingGreetingDistance();
        float minDistance = greeting ? Mathf.Min(stopDistance, followDistance) - 0.35f : followDistance - 0.35f;
        return distance >= minDistance && distance <= followDistance + 0.6f &&
               bearing >= CentreBearingDegrees && bearing <= comfortHalfAngle &&
               (greeting || GetKeepOutCover(spot, visitorPosition) <= ComfortKeepOutCover) &&
               IsInsideShowroom(spot, interiorWallInset) && IsVisibleToVisitor(spot);
    }

    // ---- Station beside the car

    private void ResolveStationSubjects()
    {
        if (heroCar == null)
            heroCar = FindShowroomObject("CarConcept");
        if (heroPodium == null)
            heroPodium = FindShowroomObject("Circular Display");
        if (displayCollection == null)
            displayCollection = FindShowroomObject("Showroom Premium Collection");

        // At Start the parts are at rest, so the ring is centred on the assembled car.
        RefreshHeroFootprint();
        heroCentre = hasHeroFootprint
            ? new Vector3((heroMin.x + heroMax.x) * 0.5f, transform.position.y, (heroMin.y + heroMax.y) * 0.5f)
            : transform.position;

        // Only a podium under the car counts.
        hasPodium = false;
        if (hasHeroFootprint && heroPodium != null && TryGetRendererBounds(heroPodium, out Bounds podium))
        {
            podiumCentre = new Vector2(podium.center.x, podium.center.z);
            podiumRadius = Mathf.Max(podium.extents.x, podium.extents.z);
            hasPodium = podiumRadius < 10f &&
                        Vector2.Distance(podiumCentre, new Vector2(heroCentre.x, heroCentre.z)) < podiumRadius;
        }

        displayFootprints.Clear();
        if (displayCollection != null)
            foreach (Transform display in displayCollection)
                if (display.gameObject.activeInHierarchy && TryGetRendererBounds(display, out Bounds bounds))
                    displayFootprints.Add(bounds);
    }

    private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Renderer part in root.GetComponentsInChildren<Renderer>())
        {
            if (!part.enabled)
                continue;
            if (!found)
            {
                bounds = part.bounds;
                found = true;
            }
            else
                bounds.Encapsulate(part.bounds);
        }
        return found;
    }

    // Footprint (seen from above) of every part of the car that is shown, wherever the explorer has moved it: taken
    // out, spread or exploded. Read at a low rate; the last footprint is kept while nothing is shown.
    private void RefreshHeroFootprint()
    {
        if (heroCar == null)
            return;
        if (Time.time >= nextRendererRefresh)
        {
            nextRendererRefresh = Time.time + RendererRefreshInterval;
            heroCar.GetComponentsInChildren(true, heroRenderers);
        }
        bool found = false;
        Vector2 min = Vector2.zero;
        Vector2 max = Vector2.zero;
        for (int i = 0; i < heroRenderers.Count; i++)
        {
            Renderer part = heroRenderers[i];
            if (part == null || !part.enabled || !part.gameObject.activeInHierarchy ||
                !(part is MeshRenderer || part is SkinnedMeshRenderer))
                continue;
            Bounds bounds = part.bounds;
            Vector2 low = new Vector2(bounds.min.x, bounds.min.z);
            Vector2 high = new Vector2(bounds.max.x, bounds.max.z);
            if (!found)
            {
                min = low;
                max = high;
                found = true;
            }
            else
            {
                min = Vector2.Min(min, low);
                max = Vector2.Max(max, high);
            }
        }
        if (!found)
            return;
        // Where the studied parts are heading counts at once, so a station is planned clear of where they will be.
        if (hasExhibit)
        {
            min = Vector2.Min(min, new Vector2(exhibitBounds.min.x, exhibitBounds.min.z));
            max = Vector2.Max(max, new Vector2(exhibitBounds.max.x, exhibitBounds.max.z));
        }
        // Parts still on their way: a station they would cover is left only once they settle (or reach Remy).
        heroFootprintMoving = hasHeroFootprint && (Vector2.Distance(min, heroMin) > 0.02f ||
                                                   Vector2.Distance(max, heroMax) > 0.02f);
        heroMin = min;
        heroMax = max;
        hasHeroFootprint = true;
    }

    // The car the visitor attends: a premium display they look at from nearby for displayVisitDwell, otherwise the
    // hero car (after stationRetargetDwell, or at once when they leave the showroom).
    private void UpdateAttendedSubject(Vector3 visitorPosition, Vector3 visitorForward)
    {
        int subject = HeroSubject;
        if (visitorInside)
        {
            float best = DisplayAttendDistance;
            for (int i = 0; i < displayFootprints.Count; i++)
            {
                GetSubjectFootprint(i, out Vector2 min, out Vector2 max);
                float distance = DistanceToFootprint(visitorPosition, min, max);
                if (distance <= best &&
                    Mathf.Abs(SignedBearing(visitorPosition, visitorForward, displayFootprints[i].center)) <=
                    DisplayAttendAngle)
                {
                    best = distance;
                    subject = i;
                }
            }
            // The hero car wins while it is nearer and in view.
            if (subject != HeroSubject && DistanceToFootprint(visitorPosition, heroMin, heroMax) < best &&
                Mathf.Abs(SignedBearing(visitorPosition, visitorForward, heroCentre)) <= DisplayAttendAngle)
                subject = HeroSubject;
        }
        if (subject != pendingSubject)
        {
            pendingSubject = subject;
            pendingSubjectSince = Time.time;
        }
        float dwell = subject == HeroSubject ? stationRetargetDwell : displayVisitDwell;
        if (subject != attendedSubject && (!visitorInside || Time.time - pendingSubjectSince >= dwell))
            attendedSubject = subject;
    }

    // The parts the visitor studies, where the explorer plans them to be once the current move ends: the node the
    // desktop inspect camera frames, the whole car in the exploded view, else the assembly that is out of the car.
    // Re-read when the node, the view or the open levels change, and every StationRefreshInterval. True when they
    // changed (another node or view, or bounds that moved).
    private bool RefreshExhibit()
    {
        ICarExplorerModel model = CarExplorerModels.Current;
        CarStructureNode node = null;
        CarExplorerView view = CarExplorerView.Normal;
        int openLevels = 0;
        if (model != null)
        {
            view = model.View;
            openLevels = model.OpenPath.Count;
            node = CarExplorerInspect.Active && CarExplorerInspect.Target != null ? CarExplorerInspect.Target
                : view == CarExplorerView.Exploded ? model.Root : model.TakenOut;
        }
        bool changed = node != exhibitNode || view != exhibitView || openLevels != exhibitOpenLevels;
        if (!changed && Time.time < nextExhibitRefresh)
            return false;
        nextExhibitRefresh = Time.time + StationRefreshInterval;
        exhibitNode = node;
        exhibitView = view;
        exhibitOpenLevels = openLevels;
        bool had = hasExhibit;
        Bounds previous = exhibitBounds;
        hasExhibit = node != null && model.TryGetNodeBounds(node, out exhibitBounds);
        if (!changed && had == hasExhibit &&
            (!hasExhibit || ((exhibitBounds.min - previous.min).sqrMagnitude < 0.0025f &&
                             (exhibitBounds.max - previous.max).sqrMagnitude < 0.0025f)))
            return false;
        exhibitVersion++;
        return true;
    }

    private void UpdateStation(Vector3 visitorPosition, Vector3 visitorForward)
    {
        TrackVisitorCircling(visitorPosition);
        if (Vector3.Distance(visitorPosition, eyeAnchor) > EyeSteadyRadius ||
            Vector3.Angle(visitorForward, eyeAnchorForward) > EyeSteadyAngle)
        {
            eyeAnchor = visitorPosition;
            eyeAnchorForward = visitorForward;
            eyeSteadySince = Time.time;
        }
        // New parts to study re-read the footprint at once, so the stations clear where they are heading.
        if (RefreshExhibit() || Time.time >= nextStationRefresh)
        {
            nextStationRefresh = Time.time + StationRefreshInterval;
            RefreshHeroFootprint();
            UpdateAttendedSubject(visitorPosition, visitorForward);
        }

        if (NeedsStationPlan(visitorPosition))
        {
            rejectedTargets.Clear();
            PlanStation(visitorPosition);
        }

        if (!hasNavDetour && HorizontalDistance(transform.position, navTarget) <= followArrivalRadius)
        {
            // A visitor who walks up to Remy at his station has met him too.
            if (!metVisitor && IsMeetingVisitor(visitorPosition, visitorForward))
                metVisitor = true;
            FaceConversationTarget();
            AnimateIdle();
            return;
        }

        if (!StepAlongRoute(followTurnSpeed))
        {
            // Blocked for a while: try another station, or hold position when none is reachable.
            rejectedTargets.Add(navTarget);
            PlanStation(visitorPosition);
        }
    }

    // Smoothed rate at which the visitor walks round the planned subject; jumps (teleports, a camera switch) reset it.
    private void TrackVisitorCircling(Vector3 visitorPosition)
    {
        Vector3 offset = visitorPosition - GetSubjectCentre(plannedSubject);
        offset.y = 0f;
        if (hasLastVisitorOffset && Time.deltaTime > 0f && offset.sqrMagnitude > 0.25f &&
            (offset - lastVisitorOffset).sqrMagnitude < 0.25f)
        {
            float rate = Vector3.SignedAngle(lastVisitorOffset, offset, Vector3.up) / Time.deltaTime;
            visitorCircling = Mathf.Lerp(visitorCircling, rate, 1f - Mathf.Exp(-3f * Time.deltaTime));
        }
        else
            visitorCircling = 0f;
        lastVisitorOffset = offset;
        hasLastVisitorOffset = true;
    }

    // Remy moves only when he has to: the visitor turned to another car, parts now cover his station, he stands in
    // the visitor's line of sight to the car or its parts, the visitor has walked well around the car, or the
    // visitor walks up to him.
    private bool NeedsStationPlan(Vector3 visitorPosition)
    {
        if (!stationPlanned || !hasNavTarget || plannedSubject != attendedSubject || planVisitorInside != visitorInside)
            return true;
        bool waiting = !hasNavDetour && HorizontalDistance(transform.position, navTarget) <= followArrivalRadius;
        Vector3 station = waiting ? transform.position : navTarget;
        // Parts taken out or spread over the station: step clear once they have settled, or at once when they come
        // up to his body.
        bool moving = plannedSubject == HeroSubject && heroFootprintMoving;
        if (IsInsideSubjectFootprint(station, plannedSubject, moving ? BodyRadius : BodyRadius + stationClearance * 0.5f))
            return true;
        // While the desktop inspect camera frames a node, that camera is the eye and the node the studied parts, and
        // only that view counts: he leaves a station that shows him among, behind or in front of the node, but only
        // once the camera has stood still, so he never shuffles round while the visitor orbits, zooms or pans. The
        // band, personal-space and sight-line reasons wait until the camera is back on the body.
        if (CarExplorerInspect.Active)
            return NeedsInspectStationPlan(station, visitorPosition);
        // While the visitor is outside, Remy waits at the hero car's station.
        if (!visitorInside)
            return false;
        // Step aside when the visitor walks up to Remy, but not while he is passing by.
        if (waiting && Time.time >= nextPersonalSpacePlan &&
            HorizontalDistance(visitorPosition, transform.position) < PersonalSpace)
            return true;
        if (Time.time < nextStationPlan)
            return false;

        // On the way to a station only the station counts, so a route may cross the view for a moment.
        if (BlocksSightLine(station, visitorPosition, plannedSubject, BodyRadius + SilhouetteMargin))
        {
            if (sightBlockedSince < 0f)
                sightBlockedSince = Time.time;
            if (Time.time - sightBlockedSince >= SightBlockDwell)
                return true;
        }
        else
            sightBlockedSince = -1f;

        // Parts out of the car in front of him, as the visitor sees it: he would stand among them. Kept only when the
        // last plan found no clear station for the same parts and the visitor has not moved since; while the eye
        // moves he waits a little longer, so he does not chase a view that is still changing.
        if (IsBehindExhibit(station, visitorPosition, plannedSubject, 0f) &&
            !(behindPartsAccepted && behindPartsAcceptedVersion == exhibitVersion &&
              HorizontalDistance(visitorPosition, planVisitorPosition) <= followRetargetDistance))
        {
            if (behindPartsSince < 0f)
                behindPartsSince = Time.time;
            float dwell = Time.time - eyeSteadySince >= EyeSteadyTime ? SightBlockDwell : BehindPartsMovingDwell;
            if (Time.time - behindPartsSince >= dwell)
                return true;
        }
        else
            behindPartsSince = -1f;

        // The station has drifted well out of the band (to the visitor's side of the car, or behind it), or is far
        // out since the parts that pushed Remy there went back.
        float bearing = Mathf.Abs(GetStationBearing(station, visitorPosition, plannedSubject));
        if (bearing < stationBearingMin - StationBandTolerance || bearing > stationBearingMax + StationBandTolerance ||
            (!moving && IsFarFromSubject(station, plannedSubject)))
        {
            if (bandLeftSince < 0f)
                bandLeftSince = Time.time;
            if (Time.time - bandLeftSince >= stationRetargetDwell)
                return true;
        }
        else
            bandLeftSince = -1f;
        return false;
    }

    // While inspecting (see NeedsStationPlan): true once the station has stood in the inspect camera's view of the
    // node for SightBlockDwell with the camera steady, unless the last plan found no station clear of that view for
    // the same node and camera position.
    private bool NeedsInspectStationPlan(Vector3 station, Vector3 eyePosition)
    {
        if (Time.time - eyeSteadySince < EyeSteadyTime || Time.time < nextStationPlan ||
            !IsInExhibitSpan(station, eyePosition, plannedSubject, 0f, true) ||
            (behindPartsAccepted && behindPartsAcceptedVersion == exhibitVersion &&
             HorizontalDistance(eyePosition, planVisitorPosition) <= EyeSteadyRadius))
        {
            behindPartsSince = -1f;
            return false;
        }
        if (behindPartsSince < 0f)
            behindPartsSince = Time.time;
        return Time.time - behindPartsSince >= SightBlockDwell;
    }

    private void PlanStation(Vector3 visitorPosition)
    {
        stationPlanned = true;
        plannedSubject = attendedSubject;
        planVisitorInside = visitorInside;
        planVisitorPosition = visitorPosition;
        nextStationPlan = Time.time + StationReplanInterval;
        nextPersonalSpacePlan = Time.time + 1f;
        sightBlockedSince = -1f;
        bandLeftSince = -1f;
        behindPartsSince = -1f;
        // A spot that already works is kept (e.g. after the view camera changed or the visitor came back in).
        if (IsGoodStation(transform.position, visitorPosition))
            SetRoute(transform.position, false, transform.position);
        else if (!TryChooseStationRoute(BuildStationRing(visitorPosition), visitorPosition))
            SetRoute(transform.position, false, transform.position);
        // No clear station for these parts: the one chosen is kept until they change or the visitor moves (while
        // inspecting: until the node changes or the inspect camera settles elsewhere).
        behindPartsAccepted = CarExplorerInspect.Active
            ? IsInExhibitSpan(navTarget, visitorPosition, plannedSubject, 0f, true)
            : IsBehindExhibit(navTarget, visitorPosition, plannedSubject, 0f);
        behindPartsAcceptedVersion = exhibitVersion;
    }

    // Takes the cheapest free station that Remy can walk to straight or, for the first MaxStationRouteAttempts blocked
    // ones, round the car along the station ring (else via one detour point).
    private bool TryChooseStationRoute(int count, Vector3 visitorPosition)
    {
        Array.Sort(CandidateCosts, CandidateSpots, 0, count);
        int attempts = 0;
        for (int i = 0; i < count; i++)
        {
            Vector3 spot = CandidateSpots[i];
            if (!IsInsideShowroom(spot, interiorWallInset) || IsRejected(spot) || !IsStandingSpotClear(spot))
                continue;
            if (IsPathClear(transform.position, spot))
            {
                // A straight walk across the visitor's view of the car goes round the far side instead, if it can.
                if (!CrossesSightLines(transform.position, spot, visitorPosition) ||
                    !TrySetRouteAround(transform.position, spot, visitorPosition, true, plannedSubject))
                    SetRoute(spot, false, spot);
                return true;
            }
            if (attempts++ >= MaxStationRouteAttempts)
                continue;
            // Round the car the station belongs to; to a premium display, also round the hero car in between.
            if (TrySetRouteAround(transform.position, spot, visitorPosition, false, plannedSubject) ||
                (plannedSubject != HeroSubject &&
                 TrySetRouteAround(transform.position, spot, visitorPosition, false, HeroSubject)))
                return true;
            if (TryFindDetour(transform.position, spot, out Vector3 via))
            {
                SetRoute(spot, true, via);
                return true;
            }
        }
        return false;
    }

    // A way round a car (`around`, usually the planned subject) just outside its station ring, with a corner every
    // RouteCornerStep degrees. Of the two ways round, the one through fewer of the visitor's sight lines to the planned
    // subject (then the shorter) is taken; every leg must be walkable, and with clearOfSight none may cross the sight
    // lines at all.
    private bool TrySetRouteAround(Vector3 from, Vector3 to, Vector3 visitorPosition, bool clearOfSight, int around)
    {
        Vector3 centre = GetSubjectCentre(around);
        GetSubjectFootprint(around, out Vector2 min, out Vector2 max);
        float start = Mathf.Atan2(from.z - centre.z, from.x - centre.x) * Mathf.Rad2Deg;
        float end = Mathf.Atan2(to.z - centre.z, to.x - centre.x) * Mathf.Rad2Deg;
        float bestCost = float.PositiveInfinity;
        int bestCorners = 0;
        for (int sense = -1; sense <= 1; sense += 2)
        {
            float sweep = Mathf.Repeat(sense * (end - start), 360f);
            int legs = Mathf.Max(1, Mathf.CeilToInt(sweep / RouteCornerStep));
            int corners = legs - 1;
            if (corners > routeCornerScratch.Length)
                continue;
            float cost = 0f;
            Vector3 previous = from;
            bool clear = true;
            for (int i = 1; i <= corners && clear; i++)
            {
                float angle = (start + sense * sweep * i / legs) * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 corner = centre + direction * (GetStationRadius(around, centre, direction, min, max) + 0.1f);
                corner.y = from.y;
                clear = IsInsideShowroom(corner, interiorWallInset * 0.5f) && IsStandingSpotClear(corner) &&
                        IsPathClear(previous, corner) &&
                        !(clearOfSight && CrossesSightLines(previous, corner, visitorPosition));
                cost += HorizontalDistance(previous, corner);
                if (BlocksSightLine(corner, visitorPosition, plannedSubject, BodyRadius + SilhouetteMargin))
                {
                    clear &= !clearOfSight;
                    cost += 4f;
                }
                routeCornerScratch[i - 1] = corner;
                previous = corner;
            }
            if (!clear || !IsPathClear(previous, to) ||
                (clearOfSight && CrossesSightLines(previous, to, visitorPosition)))
                continue;
            cost += HorizontalDistance(previous, to);
            if (cost >= bestCost)
                continue;
            bestCost = cost;
            bestCorners = corners;
            Array.Copy(routeCornerScratch, routeCorners, corners);
        }
        if (float.IsPositiveInfinity(bestCost) || bestCorners == 0)
            return false;
        Vector3 first = routeCorners[0];
        SetRoute(to, true, first);
        routeCornerCount = bestCorners;
        routeCornerIndex = 1;
        return true;
    }

    // Near the subject, clear of its parts and the podium, within the tolerated band, out of the visitor's sight
    // lines and personal space, and inside the showroom.
    private bool IsGoodStation(Vector3 spot, Vector3 visitorPosition)
    {
        if (!IsInsideShowroom(spot, interiorWallInset) || IsOnPodium(spot) ||
            HorizontalDistance(spot, visitorPosition) < PersonalSpace + 0.3f ||
            IsInsideSubjectFootprint(spot, plannedSubject, BodyRadius + stationClearance * 0.5f) ||
            IsFarFromSubject(spot, plannedSubject))
            return false;
        float bearing = Mathf.Abs(GetStationBearing(spot, visitorPosition, plannedSubject));
        return bearing >= stationBearingMin - StationBandTolerance &&
               bearing <= stationBearingMax + StationBandTolerance &&
               !BlocksSightLine(spot, visitorPosition, plannedSubject, BodyRadius + SilhouetteMargin) &&
               !IsBehindExhibit(spot, visitorPosition, plannedSubject, 0f);
    }

    // Candidate stations on two rings just outside the subject's parts (and podium), 10 degrees apart. Bearings are
    // measured at the subject's centre from the visitor's line to it; the preferred band, staying on Remy's side, a
    // short walk and a spot in view of a visitor looking at the car (or at the parts they study) cost least, standing
    // in their sight lines most and behind the studied parts nearly as much.
    private int BuildStationRing(Vector3 visitorPosition)
    {
        Vector3 centre = GetSubjectCentre(plannedSubject);
        GetSubjectFootprint(plannedSubject, out Vector2 min, out Vector2 max);
        Vector3 axis = visitorPosition - centre;
        axis.y = 0f;
        if (axis.sqrMagnitude < 0.01f)
            axis = -lastVisitorForward;
        axis.Normalize();
        Vector3 viewAxis = -axis;
        if (hasExhibit && plannedSubject == HeroSubject)
        {
            Vector3 towardParts = exhibitBounds.center - visitorPosition;
            towardParts.y = 0f;
            if (towardParts.sqrMagnitude > 0.25f)
                viewAxis = towardParts;
        }
        float remyBearing = GetStationBearing(transform.position, visitorPosition, plannedSubject);
        float remySide = Mathf.Abs(remyBearing) > 15f && Mathf.Abs(remyBearing) < 165f ? Mathf.Sign(remyBearing) : 0f;
        float middle = (stationBearingMin + stationBearingMax) * 0.5f;
        float viewHalfAngle = GetVisibleHalfAngle(ComfortEdgeMarginDegrees);
        float sightRadius = BodyRadius + SilhouetteMargin + SightPlanMargin;
        float circling = Mathf.Clamp01(Mathf.Abs(visitorCircling) / CirclingRate);
        int count = 0;
        for (int step = -17; step <= 18; step++)
        {
            float bearing = step * 10f;
            float absolute = Mathf.Abs(bearing);
            Vector3 direction = Quaternion.AngleAxis(bearing, Vector3.up) * axis;
            float radius = GetStationRadius(plannedSubject, centre, direction, min, max);
            float angleCost = absolute < stationBearingMin ? (stationBearingMin - absolute) / 20f
                : absolute > stationBearingMax ? (absolute - stationBearingMax) / 25f : 0f;
            angleCost += Mathf.Abs(absolute - middle) / 120f;
            if (remySide != 0f && absolute > 5f && absolute < 175f && Mathf.Sign(bearing) != remySide)
                angleCost += SideSwitchPenalty;
            if (absolute > 5f && absolute < 175f && Mathf.Sign(bearing) == Mathf.Sign(visitorCircling))
                angleCost += AheadPenalty * circling;
            for (int ring = 0; ring < 2; ring++)
            {
                Vector3 spot = centre + direction * (radius + ring * StationRingStep);
                spot.y = transform.position.y;
                float cost = angleCost + ring * 0.2f + HorizontalDistance(transform.position, spot) * 0.05f +
                             GetKeepOutCover(spot, visitorPosition) * KeepOutPenalty;
                if (BlocksSightLine(spot, visitorPosition, plannedSubject, sightRadius))
                    cost += SightBlockPenalty;
                else if (IsBehindExhibit(spot, visitorPosition, plannedSubject, BehindPartsPlanMargin))
                    cost += BehindPartsPenalty;
                if (HorizontalDistance(spot, visitorPosition) < PersonalSpace + 0.6f)
                    cost += 2f;
                if (Mathf.Abs(SignedBearing(visitorPosition, viewAxis, spot)) > viewHalfAngle)
                    cost += OutOfViewPenalty;
                if (!IsVisibleToVisitor(spot))
                    cost += HiddenPenalty;
                CandidateSpots[count] = spot;
                CandidateCosts[count] = cost;
                count++;
            }
        }
        return count;
    }

    // Distance from the subject's centre along a horizontal direction to just outside its parts' footprint (and the
    // hero car's podium), grown by Remy's body and the station clearance.
    private float GetStationRadius(int subject, Vector3 centre, Vector3 direction, Vector2 min, Vector2 max)
    {
        float pad = BodyRadius + stationClearance;
        Vector2 origin = new Vector2(centre.x, centre.z);
        Vector2 heading = new Vector2(direction.x, direction.z);
        float radius = RayExitRect(origin, heading, min - new Vector2(pad, pad), max + new Vector2(pad, pad));
        if (subject == HeroSubject && hasPodium)
            radius = Mathf.Max(radius, RayExitCircle(origin, heading, podiumCentre, podiumRadius + pad));
        return radius;
    }

    // Signed degrees, at the subject's centre, from the visitor's line to the spot.
    private float GetStationBearing(Vector3 spot, Vector3 visitorPosition, int subject)
    {
        Vector3 centre = GetSubjectCentre(subject);
        Vector3 towardVisitor = visitorPosition - centre;
        towardVisitor.y = 0f;
        return SignedBearing(centre, towardVisitor, spot);
    }

    private Vector3 GetSubjectCentre(int subject)
    {
        return subject == HeroSubject ? heroCentre : displayFootprints[subject].center;
    }

    private void GetSubjectFootprint(int subject, out Vector2 min, out Vector2 max)
    {
        if (subject == HeroSubject)
        {
            min = heroMin;
            max = heroMax;
            return;
        }
        Bounds bounds = displayFootprints[subject];
        min = new Vector2(bounds.min.x, bounds.min.z);
        max = new Vector2(bounds.max.x, bounds.max.z);
    }

    // Further out than the outer station ring (plus a step) in this direction, e.g. at the reception desk.
    private bool IsFarFromSubject(Vector3 point, int subject)
    {
        Vector3 centre = GetSubjectCentre(subject);
        Vector3 outward = point - centre;
        outward.y = 0f;
        float distance = outward.magnitude;
        if (distance < 0.01f)
            return false;
        GetSubjectFootprint(subject, out Vector2 min, out Vector2 max);
        return distance > GetStationRadius(subject, centre, outward / distance, min, max) + 2f * StationRingStep + 0.4f;
    }

    private bool IsInsideSubjectFootprint(Vector3 point, int subject, float pad)
    {
        GetSubjectFootprint(subject, out Vector2 min, out Vector2 max);
        return point.x > min.x - pad && point.x < max.x + pad && point.z > min.y - pad && point.z < max.y + pad;
    }

    private bool IsOnPodium(Vector3 point)
    {
        return hasPodium &&
               Vector2.Distance(new Vector2(point.x, point.z), podiumCentre) < podiumRadius + BodyRadius * 0.5f;
    }

    // True when a body of this radius at the spot stands in the visitor's line of sight to the subject's parts, seen
    // from above: inside the region swept by the lines from the visitor to every point of the parts' footprint.
    private bool BlocksSightLine(Vector3 spot, Vector3 visitorPosition, int subject, float radius)
    {
        GetSubjectFootprint(subject, out Vector2 min, out Vector2 max);
        FootprintCorners[0] = min;
        FootprintCorners[1] = new Vector2(max.x, min.y);
        FootprintCorners[2] = max;
        FootprintCorners[3] = new Vector2(min.x, max.y);
        Vector2 eye = new Vector2(visitorPosition.x, visitorPosition.z);
        Vector2 point = new Vector2(spot.x, spot.z);
        for (int i = 0; i < 4; i++)
            if (DiscTouchesTriangle(point, radius, eye, FootprintCorners[i], FootprintCorners[(i + 1) % 4]))
                return true;
        return false;
    }

    // True when the visitor would see Remy at the spot among or behind the parts they study (the hero car's exhibit):
    // seen from above, his bearing from the eye lies within the parts' angular span widened by BehindPartsMargin plus
    // extraMargin degrees (at least his silhouette plus BehindPartsGap), and he stands farther from the eye than the
    // nearest point of the parts. Standing in front of them is the sight-line test's job. Never true while the eye is
    // over the parts' footprint, where their span has no edges.
    private bool IsBehindExhibit(Vector3 spot, Vector3 eyePosition, int subject, float extraMargin)
    {
        return IsInExhibitSpan(spot, eyePosition, subject, extraMargin, false);
    }

    // IsBehindExhibit, and with includeFront also in front of the parts within their span (the inspect camera's view
    // of the node it frames, where the car's sight-line region is not the view).
    private bool IsInExhibitSpan(Vector3 spot, Vector3 eyePosition, int subject, float extraMargin, bool includeFront)
    {
        if (!hasExhibit || subject != HeroSubject)
            return false;
        Vector2 min = new Vector2(exhibitBounds.min.x, exhibitBounds.min.z);
        Vector2 max = new Vector2(exhibitBounds.max.x, exhibitBounds.max.z);
        Vector2 eye = new Vector2(eyePosition.x, eyePosition.z);
        float dx = Mathf.Max(min.x - eye.x, 0f, eye.x - max.x);
        float dz = Mathf.Max(min.y - eye.y, 0f, eye.y - max.y);
        if (dx <= 0f && dz <= 0f)
            return false;
        Vector2 toSpot = new Vector2(spot.x, spot.z) - eye;
        float spotDistance = toSpot.magnitude;
        if (!includeFront && spotDistance * spotDistance <= dx * dx + dz * dz)
            return false;
        // Corner bearings from the eye, measured from its line to the parts' centre; the eye is outside the footprint,
        // so they span less than 180 degrees.
        Vector2 reference = (min + max) * 0.5f - eye;
        float low = float.PositiveInfinity;
        float high = float.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            Vector2 corner = new Vector2((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y);
            float angle = Vector2.SignedAngle(reference, corner - eye);
            low = Mathf.Min(low, angle);
            high = Mathf.Max(high, angle);
        }
        float silhouette = GetSilhouetteHalfAngle(spotDistance);
        float margin = Mathf.Max(BehindPartsMargin, silhouette + BehindPartsGap) + extraMargin;
        float bearing = Vector2.SignedAngle(reference, toSpot);
        // Out of the view of a visitor looking at the parts (an exploded car can span more than the view), he is not
        // seen among them.
        if (Mathf.Abs(bearing) > GetVisibleHalfAngle(0f) + silhouette)
            return false;
        return Mathf.Abs(Mathf.DeltaAngle((low + high) * 0.5f, bearing)) <= (high - low) * 0.5f + margin;
    }

    // True when walking from one point to the other (end points excluded) passes through the visitor's sight lines.
    private bool CrossesSightLines(Vector3 from, Vector3 to, Vector3 visitorPosition)
    {
        int samples = Mathf.Clamp(Mathf.CeilToInt(HorizontalDistance(from, to) / 0.5f), 1, 32);
        for (int i = 1; i < samples; i++)
            if (BlocksSightLine(Vector3.Lerp(from, to, (float)i / samples), visitorPosition, plannedSubject,
                    BodyRadius + SilhouetteMargin))
                return true;
        return false;
    }

    private static bool DiscTouchesTriangle(Vector2 centre, float radius, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(b - a, centre - a);
        float d2 = Cross(c - b, centre - b);
        float d3 = Cross(a - c, centre - c);
        bool hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;
        if (!(hasNegative && hasPositive))
            return true;
        float squared = radius * radius;
        return SquaredDistanceToSegment(centre, a, b) < squared || SquaredDistanceToSegment(centre, b, c) < squared ||
               SquaredDistanceToSegment(centre, c, a) < squared;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static float SquaredDistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float t = segment.sqrMagnitude > 0.0001f
            ? Mathf.Clamp01(Vector2.Dot(point - start, segment) / segment.sqrMagnitude) : 0f;
        return (point - (start + segment * t)).sqrMagnitude;
    }

    private static float DistanceToFootprint(Vector3 point, Vector2 min, Vector2 max)
    {
        float dx = Mathf.Max(min.x - point.x, 0f, point.x - max.x);
        float dz = Mathf.Max(min.y - point.z, 0f, point.z - max.y);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // Distance along a ray (unit direction) to where it leaves the rectangle; 0 when it never enters it.
    private static float RayExitRect(Vector2 origin, Vector2 direction, Vector2 min, Vector2 max)
    {
        float near = float.NegativeInfinity;
        float far = float.PositiveInfinity;
        for (int axis = 0; axis < 2; axis++)
        {
            if (Mathf.Abs(direction[axis]) < 1e-5f)
            {
                if (origin[axis] < min[axis] || origin[axis] > max[axis])
                    return 0f;
                continue;
            }
            float first = (min[axis] - origin[axis]) / direction[axis];
            float second = (max[axis] - origin[axis]) / direction[axis];
            near = Mathf.Max(near, Mathf.Min(first, second));
            far = Mathf.Min(far, Mathf.Max(first, second));
        }
        return near <= far && far > 0f ? far : 0f;
    }

    // Distance along a ray (unit direction) to where it leaves the circle; 0 when it never enters it.
    private static float RayExitCircle(Vector2 origin, Vector2 direction, Vector2 centre, float radius)
    {
        Vector2 offset = origin - centre;
        float b = Vector2.Dot(offset, direction);
        float discriminant = b * b - (offset.sqrMagnitude - radius * radius);
        return discriminant < 0f ? 0f : Mathf.Max(0f, -b + Mathf.Sqrt(discriminant));
    }

    // Candidate spots on rings around the visitor. Bearings are degrees from the visitor's view
    // direction, positive to the right; the mirrored bearing costs otherSidePenalty extra, spots
    // outside the view (or, when following, right in front of the visitor) cost ViewPenalty extra,
    // and spots in front of (or behind) a registered panel cost up to KeepOutPenalty extra.
    private int BuildVisitorRing(Vector3 visitorPosition, Vector3 visitorForward, float preferredBearing,
        float distance, float spread, float otherSidePenalty, bool keepViewClear)
    {
        float comfortHalfAngle = GetVisibleHalfAngle(ComfortEdgeMarginDegrees);
        int bearings = 0;
        for (int step = -11; step <= 12; step++)
            RingBearings[bearings++] = Mathf.DeltaAngle(0f, preferredBearing + step * 15f);
        // Like the preferred bearing, these keep the plan margin, so a spot reached within the arrival
        // radius still passes the comfort test.
        const float arrivalSlack = PlanEdgeMarginDegrees - ComfortEdgeMarginDegrees;
        bearings = AddKeepOutBearings(bearings, visitorPosition, visitorForward, distance,
            keepViewClear ? CentreBearingDegrees + arrivalSlack : 0f, GetVisibleHalfAngle(PlanEdgeMarginDegrees));

        int count = 0;
        for (int i = 0; i < bearings; i++)
        {
            float bearing = RingBearings[i];
            float angleCost = Mathf.Min(Mathf.Abs(Mathf.DeltaAngle(bearing, preferredBearing)),
                Mathf.Abs(Mathf.DeltaAngle(bearing, -preferredBearing)) + otherSidePenalty * 45f) / 45f;
            if (Mathf.Abs(bearing) > comfortHalfAngle || (keepViewClear && Mathf.Abs(bearing) < CentreBearingDegrees))
                angleCost += ViewPenalty;
            Vector3 direction = Quaternion.AngleAxis(bearing, Vector3.up) * visitorForward;
            for (int ring = -1; ring <= 1; ring++)
            {
                Vector3 spot = visitorPosition + direction * (distance + ring * spread);
                spot.y = transform.position.y;
                CandidateSpots[count] = spot;
                CandidateCosts[count] = angleCost + Mathf.Abs(ring) * 0.25f +
                                        HorizontalDistance(transform.position, spot) * 0.08f +
                                        GetKeepOutCover(spot, visitorPosition) * KeepOutPenalty;
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Registers or updates a world-space panel (e.g. the explorer's overview) that Remy should not stand in
    /// front of: spots that hide part of it from the visitor cost extra, but are not forbidden. halfWidth is
    /// half the panel's width in metres; zero or less removes the entry, as does destroying the panel. Its
    /// colliders do not count as obstacles for Remy's planning, and while it is inactive it is ignored.
    /// </summary>
    public void SetViewKeepOut(Transform panel, float halfWidth)
    {
        for (int i = viewKeepOuts.Count - 1; i >= 0; i--)
            if (viewKeepOuts[i].Panel == null || viewKeepOuts[i].Panel == panel)
                viewKeepOuts.RemoveAt(i);
        if (panel != null && halfWidth > 0f)
            viewKeepOuts.Add(new ViewKeepOut { Panel = panel, HalfWidth = halfWidth });
    }

    // Largest share (0-1) of a shown panel that Remy would hide from the visitor at this spot, or of Remy
    // that a panel standing in front of him would hide.
    private float GetKeepOutCover(Vector3 spot, Vector3 visitorPosition)
    {
        Vector3 toSpot = spot - visitorPosition;
        toSpot.y = 0f;
        float spotDistance = toSpot.magnitude;
        if (viewKeepOuts.Count == 0 || spotDistance < 0.05f)
            return 0f;
        float halfAngle = GetSilhouetteHalfAngle(spotDistance);
        float cover = 0f;
        for (int i = 0; i < viewKeepOuts.Count; i++)
        {
            if (!TryGetKeepOutSpan(viewKeepOuts[i], visitorPosition, toSpot, out float low, out float high,
                    out float panelDistance))
                continue;
            float overlap = Mathf.Min(high, halfAngle) - Mathf.Max(low, -halfAngle);
            if (overlap <= 0f)
                continue;
            float share = panelDistance > spotDistance
                ? overlap / Mathf.Max(0.01f, high - low)
                : overlap / (2f * halfAngle);
            cover = Mathf.Max(cover, Mathf.Min(1f, share));
        }
        return cover;
    }

    // Bearings (degrees from reference, as seen by the visitor) of a shown panel's two edges.
    private static bool TryGetKeepOutSpan(ViewKeepOut keepOut, Vector3 visitorPosition, Vector3 reference,
        out float low, out float high, out float distance)
    {
        low = high = distance = 0f;
        if (keepOut.Panel == null || !keepOut.Panel.gameObject.activeInHierarchy)
            return false;
        Vector3 centre = keepOut.Panel.position;
        Vector3 halfSpan = Vector3.ProjectOnPlane(keepOut.Panel.right, Vector3.up).normalized * keepOut.HalfWidth;
        float a = SignedBearing(visitorPosition, reference, centre - halfSpan);
        float b = SignedBearing(visitorPosition, reference, centre + halfSpan);
        low = Mathf.Min(a, b);
        high = Mathf.Max(a, b);
        distance = HorizontalDistance(visitorPosition, centre);
        // A span wider than 180 degrees wraps around behind the reference direction.
        return high - low < 180f;
    }

    // Adds ring bearings at which Remy's silhouette just clears a shown panel's edges, clamped into the
    // in-view band [minBearing, maxBearing] on that side, so the least-covering spot in view is a candidate.
    private int AddKeepOutBearings(int count, Vector3 visitorPosition, Vector3 visitorForward, float distance,
        float minBearing, float maxBearing)
    {
        float clearance = GetSilhouetteHalfAngle(Mathf.Max(0.5f, distance)) + 1f;
        foreach (ViewKeepOut keepOut in viewKeepOuts)
        {
            if (count + 2 > RingBearings.Length)
                break;
            if (!TryGetKeepOutSpan(keepOut, visitorPosition, visitorForward, out float low, out float high, out _) ||
                high < -maxBearing - clearance || low > maxBearing + clearance)
                continue;
            RingBearings[count++] = ClampIntoBand(low - clearance, minBearing, maxBearing);
            RingBearings[count++] = ClampIntoBand(high + clearance, minBearing, maxBearing);
        }
        return count;
    }

    private static float ClampIntoBand(float bearing, float minBearing, float maxBearing)
    {
        return (bearing < 0f ? -1f : 1f) * Mathf.Clamp(Mathf.Abs(bearing), minBearing, maxBearing);
    }

    // Half the angle (degrees) that Remy's silhouette spans for the visitor at this horizontal distance.
    private float GetSilhouetteHalfAngle(float distance)
    {
        return Mathf.Atan((BodyRadius + SilhouetteMargin) / distance) * Mathf.Rad2Deg;
    }

    private bool IsViewKeepOutCollider(Collider candidate)
    {
        foreach (ViewKeepOut keepOut in viewKeepOuts)
            if (keepOut.Panel != null && candidate.transform.IsChildOf(keepOut.Panel))
                return true;
        return false;
    }

    private int BuildWaitingSpots(Vector3 center)
    {
        center.y = transform.position.y;
        int count = 0;
        for (int ring = 0; ring < 3; ring++)
        {
            int directions = ring == 0 ? 1 : 12;
            for (int i = 0; i < directions; i++)
            {
                Vector3 spot = center + Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward * (ring * 0.75f);
                CandidateSpots[count] = spot;
                CandidateCosts[count] = ring + HorizontalDistance(transform.position, spot) * 0.08f;
                count++;
            }
        }
        return count;
    }

    // Takes the cheapest free (and visible) candidate that Remy can walk to in a straight line or,
    // for the first few blocked ones, around the obstacle via a single detour point.
    private bool TryChooseRoute(int count, bool requireVisibility)
    {
        Array.Sort(CandidateCosts, CandidateSpots, 0, count);
        int detourAttempts = 0;
        for (int i = 0; i < count; i++)
        {
            Vector3 spot = CandidateSpots[i];
            if (!IsInsideShowroom(spot, interiorWallInset) || IsRejected(spot) || !IsStandingSpotClear(spot) ||
                (requireVisibility && !IsVisibleToVisitor(spot)))
                continue;
            if (IsPathClear(transform.position, spot))
            {
                SetRoute(spot, false, spot);
                return true;
            }
            if (detourAttempts++ < MaxDetourAttempts && TryFindDetour(transform.position, spot, out Vector3 via))
            {
                SetRoute(spot, true, via);
                return true;
            }
        }
        return false;
    }

    // Looks for one intermediate point beside the straight line that clears the blocking obstacle,
    // e.g. to walk around the display car instead of into it.
    private bool TryFindDetour(Vector3 from, Vector3 to, out Vector3 via)
    {
        via = to;
        Vector3 axis = to - from;
        axis.y = 0f;
        float length = axis.magnitude;
        if (length < 0.5f)
            return false;
        axis /= length;
        Vector3 side = Vector3.Cross(Vector3.up, axis);
        float bestCost = float.PositiveInfinity;
        // Between stations, a way round behind the car is worth a longer walk than one through the visitor's view.
        bool keepOutOfSight = greetingOver && hasHeroFootprint && stationPlanned;
        Vector3 visitor = visitorCamera.transform.position;
        foreach (float offset in DetourOffsets)
        {
            for (int sign = -1; sign <= 1; sign += 2)
            {
                for (int along = 1; along <= 3; along++)
                {
                    Vector3 candidate = from + axis * (length * along * 0.25f) + side * (sign * offset);
                    candidate.y = from.y;
                    float cost = HorizontalDistance(from, candidate) + HorizontalDistance(candidate, to);
                    if (keepOutOfSight &&
                        BlocksSightLine(candidate, visitor, plannedSubject, BodyRadius + SilhouetteMargin))
                        cost += 4f;
                    if (cost >= bestCost || !IsInsideShowroom(candidate, interiorWallInset) ||
                        !IsStandingSpotClear(candidate) || !IsPathClear(from, candidate) ||
                        !IsPathClear(candidate, to))
                        continue;
                    bestCost = cost;
                    via = candidate;
                }
            }
        }
        return bestCost < float.PositiveInfinity;
    }

    private bool IsRejected(Vector3 spot)
    {
        foreach (Vector3 rejected in rejectedTargets)
            if (HorizontalDistance(spot, rejected) < 0.5f)
                return true;
        return false;
    }

    private void SetRoute(Vector3 target, bool viaDetour, Vector3 detour)
    {
        navTarget = target;
        navDetour = detour;
        hasNavTarget = true;
        hasNavDetour = viaDetour;
        routeCornerCount = 0;
        routeCornerIndex = 0;
        nextShortcutCheck = Time.time + 0.5f;
        ResetProgress();
    }

    private void ClearNavigation()
    {
        hasNavTarget = false;
        hasNavDetour = false;
        routeCornerCount = 0;
        routeCornerIndex = 0;
        outOfViewSince = -1f;
        gaitSpeed = 0f;
        ResetProgress();
    }

    private void ResetProgress()
    {
        navBestDistance = float.PositiveInfinity;
        navProgressTime = Time.time;
    }

    private bool StepAlongRoute(float degreesPerSecond)
    {
        if (hasNavDetour)
        {
            bool shortcut = false;
            if (Time.time >= nextShortcutCheck)
            {
                nextShortcutCheck = Time.time + 0.5f;
                // Between stations the short cut must not cross the visitor's view of the car either.
                shortcut = IsPathClear(transform.position, navTarget) &&
                           !(greetingOver && stationPlanned &&
                             CrossesSightLines(transform.position, navTarget, visitorCamera.transform.position));
            }
            if (shortcut)
            {
                hasNavDetour = false;
                routeCornerIndex = routeCornerCount;
                ResetProgress();
            }
            else if (HorizontalDistance(transform.position, navDetour) < 0.5f)
            {
                // On to the next corner of a way round the car, or to the target.
                if (routeCornerIndex < routeCornerCount)
                    navDetour = routeCorners[routeCornerIndex++];
                else
                    hasNavDetour = false;
                ResetProgress();
            }
        }
        return StepToward(hasNavDetour ? navDetour : navTarget, degreesPerSecond);
    }

    // Walks one frame toward goal. Returns false once stuckTimeout passes without getting closer.
    private bool StepToward(Vector3 goal, float degreesPerSecond)
    {
        Vector3 toGoal = goal - transform.position;
        toGoal.y = 0f;
        float distance = toGoal.magnitude;
        if (distance < navBestDistance - ProgressStep)
        {
            navBestDistance = distance;
            navProgressTime = Time.time;
        }
        bool progressing = Time.time - navProgressTime < stuckTimeout;
        if (distance < 0.001f)
            return progressing;

        Vector3 direction = toGoal / distance;
        Vector3 start = transform.position + Vector3.up * 0.35f;
        float castRadius = Mathf.Max(0.12f, BodyRadius * 0.82f);
        direction = SteerAroundObstacles(start, direction, castRadius, Mathf.Min(obstacleProbeDistance, distance));
        if (direction.sqrMagnitude < 0.001f)
        {
            AnimateIdle();
            return progressing;
        }

        TurnUprightToward(direction, degreesPerSecond);
        // Slow down while the body is still turning so Remy does not slide sideways.
        float alignment = Mathf.Clamp01(Vector3.Dot(transform.forward, direction));
        Vector3 previousPosition = transform.position;
        CollisionFlags flags = motor.Move(direction * (walkSpeed * Mathf.Lerp(0.35f, 1f, alignment) * Time.deltaTime));
        if ((flags & CollisionFlags.Below) == 0)
            motor.Move(Physics.gravity * Time.deltaTime);
        AnimateGait(previousPosition);
        return progressing;
    }

    private void AnimateGait(Vector3 previousPosition)
    {
        float moved = HorizontalDistance(previousPosition, transform.position);
        float speed = Time.deltaTime > 0f ? moved / Time.deltaTime : 0f;
        gaitSpeed = Mathf.Lerp(gaitSpeed, speed, 1f - Mathf.Exp(-10f * Time.deltaTime));
        // The stride follows the distance actually covered, so a blocked Remy does not walk in place.
        gaitTime += moved * 8.5f / Mathf.Max(0.1f, walkSpeed);
        if (gaitSpeed < walkSpeed * 0.2f)
            AnimateIdle();
        else
            AnimateWalk(gaitTime);
    }

    private void GetProbeCapsule(Vector3 foot, out Vector3 bottom, out Vector3 top, out float radius)
    {
        radius = BodyRadius * 0.9f;
        bottom = foot + Vector3.up * (StepClearance + radius);
        top = foot + Vector3.up * Mathf.Max(StepClearance + radius, BodyHeight - radius);
    }

    // Props with colliders (cars, furniture, walls) block spots, routes and sight lines. Registered panels do
    // not: they only add the keep-out cost.
    private bool IsStandingSpotClear(Vector3 foot)
    {
        GetProbeCapsule(foot, out Vector3 bottom, out Vector3 top, out float radius);
        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, OverlapHits,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (!IsOwnCollider(OverlapHits[i]) && !IsViewKeepOutCollider(OverlapHits[i]))
                return false;
        return true;
    }

    private bool IsPathClear(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        delta.y = 0f;
        float distance = delta.magnitude;
        if (distance < 0.01f)
            return true;
        // Routes keep out of the visitor's personal space unless Remy already stands inside it.
        Vector3 visitor = visitorCamera.transform.position;
        float clearance = Mathf.Min(PersonalSpace, HorizontalDistance(from, visitor)) - 0.05f;
        if (HorizontalDistanceToSegment(visitor, from, to) < clearance)
            return false;
        // After the greeting Remy walks around the car's podium, not across it (only off it, if he stands on it). It
        // is too low for the probe capsules.
        if (greetingOver && hasPodium)
        {
            Vector3 podium = new Vector3(podiumCentre.x, from.y, podiumCentre.y);
            float podiumClearance = Mathf.Min(podiumRadius + BodyRadius, HorizontalDistance(from, podium)) - 0.05f;
            if (HorizontalDistanceToSegment(podium, from, to) < podiumClearance)
                return false;
        }
        GetProbeCapsule(from, out Vector3 bottom, out Vector3 top, out float radius);
        int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, delta / distance, CastHits, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            // A distance of 0 means the object already overlaps the start (e.g. a part or the visitor moved
            // into Remy); it must not pin him in place, the target spot itself has been checked.
            RaycastHit hit = CastHits[i];
            if (hit.distance > 0f && !IsOwnCollider(hit.collider) && !IsViewKeepOutCollider(hit.collider))
                return false;
        }
        return true;
    }

    private bool IsVisibleToVisitor(Vector3 foot)
    {
        Vector3 eye = visitorCamera.transform.position;
        Vector3 delta = foot + Vector3.up * (BodyHeight * 0.8f) - eye;
        float distance = delta.magnitude;
        if (distance < 0.01f)
            return true;
        int count = Physics.RaycastNonAlloc(eye, delta / distance, CastHits, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = CastHits[i].collider;
            if (!IsOwnCollider(hit) && !IsVisitorCollider(hit) && !IsViewKeepOutCollider(hit))
                return false;
        }
        return true;
    }

    private bool IsOwnCollider(Collider candidate)
    {
        return candidate == null || candidate == motor || candidate.transform == transform ||
               candidate.transform.IsChildOf(transform);
    }

    private bool IsVisitorCollider(Collider candidate)
    {
        return visitorBody != null && candidate.transform.IsChildOf(visitorBody);
    }

    // Local avoidance of the nearest obstacle ahead. Panels count here, since Remy's body does collide
    // with them. Something that already overlaps him is left to the CharacterController instead of
    // freezing him in place.
    private Vector3 SteerAroundObstacles(Vector3 start, Vector3 direction, float radius, float distance)
    {
        int count = Physics.SphereCastNonAlloc(start, radius, direction, CastHits, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Vector3 blockingNormal = Vector3.zero;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = CastHits[i];
            if (hit.distance <= 0f || hit.distance >= nearest || IsOwnCollider(hit.collider))
                continue;
            nearest = hit.distance;
            blockingNormal = hit.normal;
        }
        if (float.IsPositiveInfinity(nearest))
            return direction;

        Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
        Vector3 first = (direction + side * 0.9f).normalized;
        Vector3 second = (direction - side * 0.9f).normalized;
        bool firstClear = IsSteerDirectionClear(start, first, radius, distance);
        bool secondClear = IsSteerDirectionClear(start, second, radius, distance);
        if (firstClear && secondClear)
            return Vector3.Dot(first, direction) >= Vector3.Dot(second, direction) ? first : second;
        if (firstClear) return first;
        if (secondClear) return second;

        Vector3 tangent = Vector3.ProjectOnPlane(direction, blockingNormal).normalized;
        return tangent.sqrMagnitude > 0.001f ? tangent : Vector3.zero;
    }

    private bool IsSteerDirectionClear(Vector3 start, Vector3 direction, float radius, float distance)
    {
        int count = Physics.SphereCastNonAlloc(start, radius, direction, CastHits, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (CastHits[i].distance > 0f && !IsOwnCollider(CastHits[i].collider))
                return false;
        return true;
    }

    private void TurnUprightToward(Vector3 horizontalDirection, float degreesPerSecond = -1f)
    {
        horizontalDirection.y = 0f;
        if (horizontalDirection.sqrMagnitude < 0.001f)
            return;
        Quaternion target = Quaternion.LookRotation(horizontalDirection.normalized, Vector3.up);
        float speed = degreesPerSecond > 0f ? degreesPerSecond : turnSpeed;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, speed * Time.deltaTime);
    }

    private void FaceConversationTarget()
    {
        Vector3 focus = visitorCamera.transform.position;
        if (explanationTarget != null && componentAttentionWeight > 0.001f)
            focus = Vector3.Lerp(focus, GetFocusPoint(explanationTarget), componentAttentionWeight);
        Vector3 towardFocus = focus - transform.position;
        towardFocus.y = 0f;
        if (towardFocus.sqrMagnitude < 0.001f)
            return;
        Quaternion target = Quaternion.LookRotation(towardFocus.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
    }

    private static float SignedBearing(Vector3 origin, Vector3 forward, Vector3 point)
    {
        Vector3 toPoint = point - origin;
        toPoint.y = 0f;
        return toPoint.sqrMagnitude < 0.0001f ? 0f : Vector3.SignedAngle(forward, toPoint, Vector3.up);
    }

    public void SetExplanationTarget(Transform target)
    {
        SetExplanationTarget(target, target != null ? EstimateGeometryCenter(target) : Vector3.zero);
    }

    /// <summary>
    /// Looks and points at <paramref name="worldFocusPoint"/>, e.g. a part's geometry centre. The point is
    /// kept in the target's local space, so Remy keeps tracking it while the target moves. Null clears.
    /// </summary>
    public void SetExplanationTarget(Transform target, Vector3 worldFocusPoint)
    {
        if (target == null)
        {
            explanationTarget = null;
            explanationFocusUntil = 0f;
            gestureTarget = null;
            return;
        }

        Vector3 localFocus = target.InverseTransformPoint(worldFocusPoint);
        explanationLocalFocus = IsFinite(localFocus) ? localFocus : Vector3.zero;
        if (explanationTarget == target)
            return;

        explanationTarget = target;
        explanationFocusUntil = Time.time + 0.9f;
        gestureTarget = target;
        Vector3 focus = GetFocusPoint(target);
        pointWithLeftArm = transform.InverseTransformPoint(focus).x < 0f;
        if (!hasGestureAimPosition)
        {
            gestureAimPosition = focus;
            hasGestureAimPosition = true;
        }
    }

    private Vector3 GetFocusPoint(Transform target)
    {
        if (target != explanationTarget)
            return target.position;
        Vector3 focus = target.TransformPoint(explanationLocalFocus);
        return IsFinite(focus) ? focus : target.position;
    }

    // Most CarConcept nodes have their pivot at the car origin, so prefer the rendered geometry.
    private static Vector3 EstimateGeometryCenter(Transform target)
    {
        Renderer own = target.GetComponent<Renderer>();
        if (own != null && own.enabled)
            return own.bounds.center;
        bool found = false;
        Bounds bounds = default;
        foreach (Renderer part in target.GetComponentsInChildren<Renderer>())
        {
            if (!part.enabled)
                continue;
            if (!found)
            {
                bounds = part.bounds;
                found = true;
            }
            else
                bounds.Encapsulate(part.bounds);
        }
        return found ? bounds.center : target.position;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
               !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
    }

    private void CacheRigPose()
    {
        if (animator == null)
            return;
        if (!animator.isHuman)
        {
            Debug.LogWarning("[Showroom Presenter] Remy's Animator has no valid Humanoid rig; head and arm gestures are unavailable.", this);
            return;
        }

        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        head = animator.GetBoneTransform(HumanBodyBones.Head);
        leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        leftIndexProximal = animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
        rightIndexProximal = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);

        if (hips != null)
        {
            hipsRest = hips.localRotation;
            hipsRestPosition = hips.localPosition;
        }
        if (leftUpperLeg != null) leftLegRest = leftUpperLeg.localRotation;
        if (rightUpperLeg != null) rightLegRest = rightUpperLeg.localRotation;
        if (leftUpperArm != null) leftArmRest = leftUpperArm.localRotation;
        if (rightUpperArm != null) rightArmRest = rightUpperArm.localRotation;
        if (leftLowerArm != null) leftLowerArmRest = leftLowerArm.localRotation;
        if (rightLowerArm != null) rightLowerArmRest = rightLowerArm.localRotation;
        if (leftHand != null) leftHandRest = leftHand.localRotation;
        if (rightHand != null) rightHandRest = rightHand.localRotation;
        if (spine != null) spineRest = spine.localRotation;
        if (chest != null) chestRest = chest.localRotation;
        if (neck != null) neckRest = neck.localRotation;
        if (head != null) headRest = head.localRotation;

        // The upper arms' local X runs front-to-back in the arms-down rest pose, so swing them about
        // the body's lateral axis instead. Like the legs, a positive angle moves the limb forward.
        if (leftUpperArm != null) leftArmSwingAxis = Quaternion.Inverse(leftUpperArm.rotation) * -transform.right;
        if (rightUpperArm != null) rightArmSwingAxis = Quaternion.Inverse(rightUpperArm.rotation) * -transform.right;

        bool leftArmReady = leftUpperArm != null && leftLowerArm != null && leftHand != null;
        bool rightArmReady = rightUpperArm != null && rightLowerArm != null && rightHand != null;
        if (!leftArmReady && !rightArmReady)
            Debug.LogWarning("[Showroom Presenter] No complete upper-arm/lower-arm/hand bone chain was found; component pointing will be unavailable.", this);
        else if (!leftArmReady || !rightArmReady)
            Debug.LogWarning("[Showroom Presenter] Only one complete arm chain was found; component pointing will use that arm.", this);

        CaptureArmFramePose();
    }

    private void CaptureArmFramePose()
    {
        if (leftUpperArm != null)
        {
            leftUpperArmFramePose = leftUpperArm.localRotation;
            hasLeftArmFramePose = true;
        }
        if (rightUpperArm != null)
        {
            rightUpperArmFramePose = rightUpperArm.localRotation;
            hasRightArmFramePose = true;
        }
        if (leftLowerArm != null) leftLowerArmFramePose = leftLowerArm.localRotation;
        if (rightLowerArm != null) rightLowerArmFramePose = rightLowerArm.localRotation;
        if (leftHand != null) leftHandFramePose = leftHand.localRotation;
        if (rightHand != null) rightHandFramePose = rightHand.localRotation;
    }

    private void ResetProceduralArmBasePose()
    {
        if (animator == null || animator.runtimeAnimatorController != null)
            return;

        // With no Animator Controller, reset the last LateUpdate pose before
        // applying this frame's walk/idle animation and capturing its base.
        if (leftUpperArm != null) leftUpperArm.localRotation = leftArmRest;
        if (rightUpperArm != null) rightUpperArm.localRotation = rightArmRest;
        if (leftLowerArm != null) leftLowerArm.localRotation = leftLowerArmRest;
        if (rightLowerArm != null) rightLowerArm.localRotation = rightLowerArmRest;
        if (leftHand != null) leftHand.localRotation = leftHandRest;
        if (rightHand != null) rightHand.localRotation = rightHandRest;
    }

    private void AnimateUpperBodyAttention()
    {
        if (visitorCamera == null || animator == null || !animator.isHuman)
            return;

        Vector3 focus = visitorCamera.transform.position;
        if (explanationTarget != null && componentAttentionWeight > 0.001f)
            focus = Vector3.Lerp(focus, GetFocusPoint(explanationTarget), componentAttentionWeight);
        Vector3 worldDirection = focus - (head != null ? head.position : transform.position);
        if (worldDirection.sqrMagnitude < 0.001f)
            return;

        Vector3 localDirection = transform.InverseTransformDirection(worldDirection.normalized);
        float yaw = Mathf.Clamp(Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg, -50f, 50f);
        // Positive pitch looks up. These bones' local X points to Remy's right, where a positive
        // Euler X angle tips the face down, hence the negated angles below.
        float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(localDirection.y, -1f, 1f)) * Mathf.Rad2Deg, -18f, 18f);
        float blend = 1f - Mathf.Exp(-5f * Time.deltaTime);

        if (spine != null)
            spine.localRotation = Quaternion.Slerp(spine.localRotation,
                spineRest * Quaternion.Euler(-pitch * 0.10f, yaw * 0.16f, 0f), blend);
        if (chest != null)
            chest.localRotation = Quaternion.Slerp(chest.localRotation,
                chestRest * Quaternion.Euler(-pitch * 0.18f, yaw * 0.24f, 0f), blend);
        if (neck != null)
            neck.localRotation = Quaternion.Slerp(neck.localRotation,
                neckRest * Quaternion.Euler(-pitch * 0.28f, yaw * 0.30f, 0f), blend);
        if (head != null)
            head.localRotation = Quaternion.Slerp(head.localRotation,
                headRest * Quaternion.Euler(-pitch * 0.42f, yaw * 0.42f, 0f), blend);
    }

    private void ApplyPointingPose(Transform upperArm, Transform lowerArm, Transform hand,
        Transform indexProximal, Quaternion upperArmBase, Quaternion lowerArmBase,
        Quaternion handBase, float weight, Vector3 targetPosition, bool isLeftArm)
    {
        if ((!hasLeftArmFramePose && isLeftArm) || (!hasRightArmFramePose && !isLeftArm))
            return;

        upperArm.localRotation = upperArmBase;
        lowerArm.localRotation = lowerArmBase;
        hand.localRotation = handBase;
        if (weight <= 0.001f)
            return;

        Vector3 upperSegment = lowerArm.position - upperArm.position;
        Vector3 toTarget = targetPosition - upperArm.position;
        if (upperSegment.sqrMagnitude < 0.0001f || toTarget.sqrMagnitude < 0.0001f)
            return;

        Vector3 upperAim = (toTarget.normalized + Vector3.up * 0.22f +
                            transform.right * (isLeftArm ? -0.06f : 0.06f)).normalized;
        Quaternion upperBaseWorld = upperArm.rotation;
        Quaternion upperTargetWorld = Quaternion.FromToRotation(upperSegment.normalized, upperAim) * upperBaseWorld;
        upperArm.rotation = Quaternion.Slerp(upperBaseWorld, upperTargetWorld, weight * 0.88f);

        Vector3 forearmSegment = hand.position - lowerArm.position;
        Vector3 forearmAimVector = targetPosition - lowerArm.position;
        if (forearmSegment.sqrMagnitude > 0.0001f && forearmAimVector.sqrMagnitude > 0.0001f)
        {
            Vector3 forearmAim = (forearmAimVector.normalized + Vector3.up * 0.10f).normalized;
            Quaternion lowerBaseWorld = lowerArm.rotation;
            Quaternion lowerTargetWorld = Quaternion.FromToRotation(forearmSegment.normalized,
                forearmAim) * lowerBaseWorld;
            lowerArm.rotation = Quaternion.Slerp(lowerBaseWorld, lowerTargetWorld, weight * 0.88f);
        }

        if (indexProximal != null)
        {
            Vector3 fingerDirection = indexProximal.position - hand.position;
            Vector3 handAim = targetPosition - hand.position;
            if (fingerDirection.sqrMagnitude > 0.0001f && handAim.sqrMagnitude > 0.0001f)
            {
                Quaternion handBaseWorld = hand.rotation;
                Quaternion handTargetWorld = Quaternion.FromToRotation(fingerDirection.normalized,
                    handAim.normalized) * handBaseWorld;
                hand.rotation = Quaternion.Slerp(handBaseWorld, handTargetWorld, weight * 0.72f);
            }
        }
    }

    private void AnimateWalk(float phase)
    {
        float swing = Mathf.Sin(phase);
        float opposite = Mathf.Sin(phase + Mathf.PI);
        if (hips != null)
        {
            hips.localRotation = hipsRest * Quaternion.Euler(0f, swing * 2.5f, 0f);
            Vector3 p = hipsRestPosition;
            p.y += Mathf.Abs(swing) * 0.018f;
            hips.localPosition = p;
        }
        if (leftUpperLeg != null)
            leftUpperLeg.localRotation = leftLegRest * Quaternion.Euler(swing * 22f, 0f, 0f);
        if (rightUpperLeg != null)
            rightUpperLeg.localRotation = rightLegRest * Quaternion.Euler(opposite * 22f, 0f, 0f);
        if (leftUpperArm != null)
            leftUpperArm.localRotation = leftArmRest * Quaternion.AngleAxis(opposite * 16f, leftArmSwingAxis);
        if (rightUpperArm != null)
            rightUpperArm.localRotation = rightArmRest * Quaternion.AngleAxis(swing * 16f, rightArmSwingAxis);
    }

    private void AnimateIdle()
    {
        if (hips != null)
        {
            hips.localRotation = Quaternion.Slerp(hips.localRotation, hipsRest, Time.deltaTime * 5f);
            Vector3 p = Vector3.Lerp(hips.localPosition, hipsRestPosition, Time.deltaTime * 5f);
            hips.localPosition = p;
        }
        if (leftUpperLeg != null)
            leftUpperLeg.localRotation = Quaternion.Slerp(leftUpperLeg.localRotation, leftLegRest, Time.deltaTime * 5f);
        if (rightUpperLeg != null)
            rightUpperLeg.localRotation = Quaternion.Slerp(rightUpperLeg.localRotation, rightLegRest, Time.deltaTime * 5f);
        if (leftUpperArm != null)
            leftUpperArm.localRotation = Quaternion.Slerp(leftUpperArm.localRotation, leftArmRest, Time.deltaTime * 5f);
        if (rightUpperArm != null)
            rightUpperArm.localRotation = Quaternion.Slerp(rightUpperArm.localRotation, rightArmRest, Time.deltaTime * 5f);
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static float HorizontalDistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector2 p = new Vector2(point.x, point.z);
        Vector2 a = new Vector2(start.x, start.z);
        Vector2 segment = new Vector2(end.x, end.z) - a;
        float t = segment.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, segment) / segment.sqrMagnitude) : 0f;
        return Vector2.Distance(p, a + segment * t);
    }
}
