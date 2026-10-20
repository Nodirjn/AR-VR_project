using System;
using UnityEngine;

/// <summary>
/// Collision-aware showroom greeter for the existing Remy presenter.
/// Reuses CarExplorerController's distance-based greeting panel.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShowroomPresenterAgent : MonoBehaviour
{
    [Header("Entrance trigger")]
    [SerializeField] private float entranceZ = 13.2f;
    [SerializeField] private float entranceHalfWidth = 5.5f;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 1.15f;
    [SerializeField] private float turnSpeed = 240f;
    [SerializeField] private float stopDistance = 1.9f;
    [Header("Personal follow position")]
    // Fresh serialized field names prevent older scene values from restoring
    // a previous follow distance or angle after recompilation.
    [SerializeField, Range(2.0f, 6.0f)] private float followRootDistanceMeters = 4.0f;
    [SerializeField, Range(0f, 15f)] private float followRearwardAngleDegrees = 0f;
    [SerializeField, Min(0.05f)] private float followArrivalRadius = 0.16f;
    [SerializeField, Min(0.03f)] private float followReplanDistance = 0.12f;
    [SerializeField, Min(0.05f)] private float followReplanInterval = 0.2f;
    [SerializeField, Min(0.05f)] private float followTargetSmoothTime = 0.22f;
    [SerializeField, Min(30f)] private float followTurnSpeed = 135f;
    [SerializeField, Min(0.2f)] private float obstacleProbeDistance = 0.75f;
    [SerializeField] private Vector3[] aisleWaypoints =
    {
        new Vector3(-5.4f, 0f, -6.0f),
        new Vector3(-5.4f, 0f, -2.2f),
        new Vector3(-5.0f, 0f, 1.8f),
        new Vector3(-4.2f, 0f, 5.0f)
    };

    private Transform player;
    private Transform playerBody;
    private CharacterController motor;
    private Animator animator;
    private Vector3 previousPlayerPosition;
    private Vector3 lastPlayerBodyForward = Vector3.forward;
    private Transform explanationTarget;
    private Transform gestureTarget;
    private float explanationFocusUntil;
    private int waypointIndex;
    private bool greetingTripStarted;
    private bool arrived;
    private float gaitTime;
    private Vector3 plannedFollowPosition;
    private Vector3 smoothedFollowPosition;
    private Vector3 followPositionVelocity;
    private Vector3 followAnchorPosition;
    private float nextFollowReplanTime;
    private bool hasFollowPosition;
    private float componentAttentionWeight;
    private float componentAttentionVelocity;

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

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            player = mainCamera.transform;
            playerBody = ResolvePlayerBody(player);
        }

        CacheRigPose();
    }

    private void Start()
    {
        if (player != null)
        {
            previousPlayerPosition = GetPlayerBodyPosition();
            // The desktop and XR rigs can spawn inside the showroom, so there may
            // be no entrance crossing to observe during this Play session.
            if (IsInsideShowroom(player.position))
                BeginGreetingTrip();
        }
    }

    private void Update()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null && (player == null || player != mainCamera.transform))
        {
            player = mainCamera.transform;
            playerBody = ResolvePlayerBody(player);
            previousPlayerPosition = GetPlayerBodyPosition();
            hasFollowPosition = false;
            nextFollowReplanTime = 0f;
            Vector3 initialForward = Vector3.ProjectOnPlane(playerBody.forward, Vector3.up);
            if (initialForward.sqrMagnitude > 0.001f)
                lastPlayerBodyForward = initialForward.normalized;
            if (!greetingTripStarted && IsInsideShowroom(player.position))
                BeginGreetingTrip();
        }

        // Desktop controllers can be added during another component's Start,
        // after this agent's Awake has cached the camera hierarchy.
        if (player != null)
            playerBody = ResolvePlayerBody(player);

        ResetProceduralArmBasePose();

        if (player == null)
            return;

        Vector3 playerBodyPosition = GetPlayerBodyPosition();
        if (!greetingTripStarted && CrossedEntrance(previousPlayerPosition, playerBodyPosition))
            BeginGreetingTrip();

        previousPlayerPosition = playerBodyPosition;
        float attentionTarget = explanationTarget != null && Time.time < explanationFocusUntil ? 1f : 0f;
        componentAttentionWeight = Mathf.SmoothDamp(componentAttentionWeight, attentionTarget,
            ref componentAttentionVelocity, 0.22f, Mathf.Infinity, Time.deltaTime);
        if (greetingTripStarted && !arrived)
            WalkTowardPlayer();
        else if (greetingTripStarted)
            FollowPlayer();
        else
            AnimateIdle();

        AnimateUpperBodyAttention();
        CaptureArmFramePose();
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
            if (!hasGestureAimPosition)
            {
                gestureAimPosition = target.position;
                hasGestureAimPosition = true;
            }
            gestureAimPosition = Vector3.SmoothDamp(gestureAimPosition, target.position,
                ref gestureAimVelocity, 0.16f, Mathf.Infinity, Time.deltaTime);
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

    private bool CrossedEntrance(Vector3 previous, Vector3 current)
    {
        // The entrance is at the positive-Z end; players enter by moving toward -Z.
        bool crossedIntoShowroom = previous.z > entranceZ && current.z <= entranceZ;
        return crossedIntoShowroom && Mathf.Abs(current.x) <= entranceHalfWidth;
    }

    private bool IsInsideShowroom(Vector3 position)
    {
        return position.z < entranceZ;
    }

    private static Transform ResolvePlayerBody(Transform cameraTransform)
    {
        if (cameraTransform == null)
            return null;

        CharacterController bodyController = cameraTransform.GetComponent<CharacterController>();
        if (bodyController == null)
            bodyController = cameraTransform.GetComponentInParent<CharacterController>();
        if (bodyController != null)
            return bodyController.transform;

        // XR cameras are children of the XR Origin. The hierarchy root is the
        // player's body anchor when a rig CharacterController is unavailable.
        return cameraTransform.root != null ? cameraTransform.root : cameraTransform;
    }

    private Vector3 GetPlayerBodyPosition()
    {
        return playerBody != null ? playerBody.position : player.position;
    }

    private void BeginGreetingTrip()
    {
        greetingTripStarted = true;
        waypointIndex = 0;
    }

    private void WalkTowardPlayer()
    {
        Vector3 destination = player.position;
        Vector3 facing = player.forward;
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.001f)
            facing = Vector3.forward;
        destination += facing.normalized * stopDistance;
        destination.y = transform.position.y;

        Vector3 waypoint = destination;
        if (waypointIndex < aisleWaypoints.Length)
        {
            waypoint = aisleWaypoints[waypointIndex];
            waypoint.y = transform.position.y;

            if (HorizontalDistance(transform.position, waypoint) < 0.55f)
                waypointIndex++;
        }

        Vector3 toGoal = waypoint - transform.position;
        toGoal.y = 0f;

        if (waypointIndex >= aisleWaypoints.Length && HorizontalDistance(transform.position, destination) < 0.18f)
        {
            arrived = true;
            AnimateIdle();
            return;
        }

        if (toGoal.sqrMagnitude < 0.001f)
            return;

        Vector3 desired = toGoal.normalized;
        Vector3 start = transform.position + Vector3.up * 0.35f;
        Vector3 worldScale = transform.lossyScale;
        float worldRadius = motor.radius * Mathf.Max(Mathf.Abs(worldScale.x), Mathf.Abs(worldScale.z));
        float castRadius = Mathf.Max(0.12f, worldRadius * 0.82f);

        desired = SteerAroundObstacles(start, desired, castRadius, obstacleProbeDistance);

        TurnUprightToward(desired);

        CollisionFlags flags = motor.Move(desired * walkSpeed * Time.deltaTime);
        if ((flags & CollisionFlags.Below) == 0)
            motor.Move(Physics.gravity * Time.deltaTime);

        gaitTime += Time.deltaTime * 8.5f;
        AnimateWalk(gaitTime);
    }

    private void FollowPlayer()
    {
        Vector3 playerBodyPosition = GetPlayerBodyPosition();
        Vector3 horizontalMovement = playerBodyPosition - followAnchorPosition;
        horizontalMovement.y = 0f;
        bool playerMeaningfullyMoved = horizontalMovement.sqrMagnitude >=
                                       followReplanDistance * followReplanDistance;

        // Snapshot body position and body heading only on meaningful body
        // translation. Head/camera rotation alone cannot orbit Remy's target.
        if (!hasFollowPosition || (playerMeaningfullyMoved && Time.time >= nextFollowReplanTime))
        {
            Vector3 bodyForward = playerBody != null ? playerBody.forward : player.forward;
            Vector3 forward = Vector3.ProjectOnPlane(bodyForward, Vector3.up);
            if (forward.sqrMagnitude > 0.001f)
                lastPlayerBodyForward = forward.normalized;
            forward = lastPlayerBodyForward;

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 left = -right;
            plannedFollowPosition = FindSafeFollowPosition(left, forward, playerBodyPosition);
            followAnchorPosition = playerBodyPosition;
            nextFollowReplanTime = Time.time + followReplanInterval;
            if (!hasFollowPosition)
            {
                smoothedFollowPosition = transform.position;
                hasFollowPosition = true;
            }
        }

        smoothedFollowPosition = Vector3.SmoothDamp(smoothedFollowPosition, plannedFollowPosition,
            ref followPositionVelocity, followTargetSmoothTime, walkSpeed * 1.5f, Time.deltaTime);
        Vector3 destination = smoothedFollowPosition;
        Vector3 toDestination = destination - transform.position;
        toDestination.y = 0f;
        float distance = toDestination.magnitude;
        if (distance <= followArrivalRadius)
        {
            FaceConversationTarget();
            AnimateIdle();
            return;
        }

        Vector3 direction = toDestination / distance;
        Vector3 start = transform.position + Vector3.up * 0.35f;
        Vector3 worldScale = transform.lossyScale;
        float worldRadius = motor.radius * Mathf.Max(Mathf.Abs(worldScale.x), Mathf.Abs(worldScale.z));
        float castRadius = Mathf.Max(0.12f, worldRadius * 0.82f);
        direction = SteerAroundObstacles(start, direction, castRadius,
            Mathf.Min(obstacleProbeDistance, distance));
        if (direction.sqrMagnitude < 0.001f)
        {
            FaceConversationTarget();
            AnimateIdle();
            return;
        }

        TurnUprightToward(direction, followTurnSpeed);
        CollisionFlags flags = motor.Move(direction * walkSpeed * Time.deltaTime);
        if ((flags & CollisionFlags.Below) == 0)
            motor.Move(Physics.gravity * Time.deltaTime);
        gaitTime += Time.deltaTime * 8.5f;
        AnimateWalk(gaitTime);
    }

    private Vector3 FindSafeFollowPosition(Vector3 left, Vector3 forward, Vector3 playerBodyPosition)
    {
        float bestScore = float.PositiveInfinity;
        Vector3 bestPosition = transform.position;
        bool found = false;
        float[] angleOffsets = { 0f, -8f, 8f, -16f, 16f };
        float[] distanceOffsets = { 0f, -0.15f, 0.15f };
        float desiredAngle = followRearwardAngleDegrees * Mathf.Deg2Rad;
        foreach (float angleOffset in angleOffsets)
        {
            float candidateAngle = Mathf.Clamp(followRearwardAngleDegrees + angleOffset, 0f, 40f) * Mathf.Deg2Rad;
            // left*cos(angle) - forward*sin(angle): for forward +Z this is
            // -X (left); at zero degrees it has no rearward component.
            Vector3 direction = left * Mathf.Cos(candidateAngle) - forward * Mathf.Sin(candidateAngle);
            foreach (float distanceOffset in distanceOffsets)
            {
                Vector3 candidate = playerBodyPosition + direction * (followRootDistanceMeters + distanceOffset);
                candidate.y = transform.position.y;
                if (!IsStandingSpotClear(candidate))
                    continue;

                float angularCost = Mathf.Abs(candidateAngle - desiredAngle) * 0.35f;
                float rangeCost = Mathf.Abs(distanceOffset) * 0.8f;
                float continuityCost = hasFollowPosition
                    ? Vector3.Distance(candidate, plannedFollowPosition) * 0.05f : 0f;
                float score = angularCost + rangeCost + continuityCost;
                if (score >= bestScore)
                    continue;
                bestScore = score;
                bestPosition = candidate;
                found = true;
            }
        }

        // If every point on the trailing-left arc is blocked, hold position.
        return found ? bestPosition : transform.position;
    }

    private bool IsStandingSpotClear(Vector3 position)
    {
        Vector3 scale = transform.lossyScale;
        float radius = motor.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) * 0.9f;
        float height = motor.height * Mathf.Abs(scale.y);
        Vector3 bottom = position + Vector3.up * (radius + 0.04f);
        Vector3 top = position + Vector3.up * Mathf.Max(radius + 0.05f, height - radius);
        Collider[] overlaps = Physics.OverlapCapsule(bottom, top, radius,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach (Collider overlap in overlaps)
        {
            if (overlap == null || overlap == motor || overlap.transform == transform ||
                overlap.transform.IsChildOf(transform))
                continue;
            return false;
        }
        return true;
    }

    private Vector3 SteerAroundObstacles(Vector3 start, Vector3 direction, float radius, float distance)
    {
        RaycastHit[] hits = Physics.SphereCastAll(start, radius, direction, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        RaycastHit blockingHit = default;
        bool blocked = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider == motor || hit.collider.transform == transform ||
                hit.collider.transform.IsChildOf(transform))
                continue;
            blockingHit = hit;
            blocked = true;
            break;
        }
        if (!blocked)
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

        Vector3 tangent = Vector3.ProjectOnPlane(direction, blockingHit.normal).normalized;
        return tangent.sqrMagnitude > 0.001f ? tangent : Vector3.zero;
    }

    private bool IsSteerDirectionClear(Vector3 start, Vector3 direction, float radius, float distance)
    {
        RaycastHit[] hits = Physics.SphereCastAll(start, radius, direction, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
            if (hit.collider != null && hit.collider != motor && hit.collider.transform != transform &&
                !hit.collider.transform.IsChildOf(transform))
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
        Vector3 focus = player.position;
        if (explanationTarget != null && componentAttentionWeight > 0.001f)
            focus = Vector3.Lerp(player.position, explanationTarget.position, componentAttentionWeight);
        Vector3 towardPlayer = focus - transform.position;
        towardPlayer.y = 0f;
        if (towardPlayer.sqrMagnitude < 0.001f)
            return;
        Quaternion target = Quaternion.LookRotation(towardPlayer.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
    }

    public void SetExplanationTarget(Transform target)
    {
        if (target == null)
        {
            explanationTarget = null;
            explanationFocusUntil = 0f;
            gestureTarget = null;
            return;
        }

        if (explanationTarget == target)
            return;

        explanationTarget = target;
        explanationFocusUntil = Time.time + 0.9f;
        gestureTarget = target;
        pointWithLeftArm = transform.InverseTransformPoint(target.position).x < 0f;
        if (!hasGestureAimPosition)
        {
            gestureAimPosition = target.position;
            hasGestureAimPosition = true;
        }
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
        if (player == null || animator == null || !animator.isHuman)
            return;

        Vector3 focus = player.position;
        if (explanationTarget != null && componentAttentionWeight > 0.001f)
            focus = Vector3.Lerp(player.position, explanationTarget.position, componentAttentionWeight);
        Vector3 worldDirection = focus - (head != null ? head.position : transform.position);
        if (worldDirection.sqrMagnitude < 0.001f)
            return;

        Vector3 localDirection = transform.InverseTransformDirection(worldDirection.normalized);
        float yaw = Mathf.Clamp(Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg, -50f, 50f);
        float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(localDirection.y, -1f, 1f)) * Mathf.Rad2Deg, -18f, 18f);
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
            leftUpperArm.localRotation = leftArmRest * Quaternion.Euler(opposite * 16f, 0f, 0f);
        if (rightUpperArm != null)
            rightUpperArm.localRotation = rightArmRest * Quaternion.Euler(swing * 16f, 0f, 0f);
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
}
