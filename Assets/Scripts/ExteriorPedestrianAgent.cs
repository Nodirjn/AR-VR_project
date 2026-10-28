using System.Collections.Generic;
using UnityEngine;

/// <summary>Follows sidewalk waypoints with light obstacle yielding and procedural Humanoid gait.</summary>
[DisallowMultipleComponent]
public sealed class ExteriorPedestrianAgent : MonoBehaviour
{
    internal static readonly List<ExteriorPedestrianAgent> Active = new List<ExteriorPedestrianAgent>();
    private const float ArmDrop = 72f, ArmSwing = 18f, ElbowBend = 12f;
    [SerializeField] private Transform[] waypoints;
    [SerializeField] private ExteriorTrafficSignal crossingSignal;
    [SerializeField] private Vector3 crossingDirection = Vector3.right;
    [SerializeField] private int[] crossingWaitAtWaypointIndices;
    [SerializeField, Min(0f)] private float curbWaitDistance = 7.3f;
    [SerializeField, Min(0.2f)] private float walkSpeed = 1.05f;
    [SerializeField] private float scale = 0.42f;
    [SerializeField] private int appearanceSeed;
    private Transform leftUpperArm, rightUpperArm, leftLowerArm, rightLowerArm;
    private Transform leftUpperLeg, rightUpperLeg, leftLowerLeg, rightLowerLeg;
    private Quaternion leftArmBase, rightArmBase, leftForearmBase, rightForearmBase;
    private Quaternion leftLegBase, rightLegBase, leftShinBase, rightShinBase;
    private int waypointIndex;
    private float gaitClock, gaitWeight, idleUntil;
    private bool crossingStarted;
    private Vector3 previousPosition;
    private CharacterController controller;

    internal Vector3 Velocity { get; private set; }

    private void OnEnable()
    {
        if (!Active.Contains(this)) Active.Add(this);
    }

    private void OnDisable() => Active.Remove(this);

    public void Configure(Transform[] path, float speed, float actorScale, int seed,
        ExteriorTrafficSignal signal = null, Vector3 crossing = default, int[] crossingWaitIndices = null)
    {
        waypoints = path;
        walkSpeed = speed;
        scale = actorScale;
        appearanceSeed = seed;
        crossingSignal = signal;
        crossingDirection = crossing == default ? Vector3.right : crossing.normalized;
        crossingWaitAtWaypointIndices = crossingWaitIndices;
        transform.localScale = Vector3.one * scale;
    }

    private void Start()
    {
        previousPosition = transform.position;
        controller = GetComponent<CharacterController>();
        Animator animator = GetComponent<Animator>();
        if (animator != null && animator.isHuman)
        {
            leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            leftLowerLeg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            rightLowerLeg = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            CacheBases();
            animator.enabled = false;
            // Pose the arms down before the first frame renders: the first Update may only advance a waypoint and skip Animate.
            Animate(false);
        }
        ApplyAppearance();
    }

    private void Update()
    {
        Velocity = Vector3.zero;
        if (waypoints == null || waypoints.Length < 2) { Animate(false); return; }
        if (Time.time < idleUntil) { Animate(false); return; }
        Transform target = waypoints[waypointIndex % waypoints.Length];
        if (target == null) { waypointIndex = (waypointIndex + 1) % waypoints.Length; return; }

        Vector3 delta = target.position - transform.position;
        delta.y = 0f;
        if (delta.sqrMagnitude < 0.16f)
        {
            waypointIndex = (waypointIndex + 1) % waypoints.Length;
            crossingStarted = false;
            if (waypointIndex == 0) idleUntil = Time.time + Random.Range(1.5f, 4f);
            return;
        }
        Vector3 direction = delta.normalized;
        float step = walkSpeed * Time.deltaTime;
        // Wait for the walk signal only at the curb; a walker who has stepped onto the carriageway always finishes crossing.
        if (crossingSignal != null && !crossingStarted && IsCrossingWaitPoint(waypointIndex))
        {
            float toWaitLine = DistanceToWaitLine(direction);
            if (toWaitLine >= 0f)
            {
                if (crossingSignal.PedestrianMayStart(crossingDirection, 2f * curbWaitDistance / walkSpeed)) crossingStarted = toWaitLine <= step;
                else if (toWaitLine < 0.01f) { Animate(false); return; }
                else step = Mathf.Min(step, toWaitLine);
            }
        }
        if (IsBlocked(direction) || IsCrowdedAhead(direction) || IsCarAhead(direction))
        {
            Animate(false);
            return;
        }
        Quaternion desired = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, desired, 4.5f * Time.deltaTime);
        Vector3 motion = direction * step;
        if (controller != null) controller.Move(motion);
        else transform.position += motion;
        Velocity = direction * walkSpeed;
        Animate(true);
    }

    private bool IsCrossingWaitPoint(int index)
    {
        if (crossingWaitAtWaypointIndices == null) return false;
        for (int i = 0; i < crossingWaitAtWaypointIndices.Length; i++)
            if (crossingWaitAtWaypointIndices[i] == index) return true;
        return false;
    }

    // Distance along the walk to the wait line, curbWaitDistance from the signal across the road; -1 once past it or when walking away.
    private float DistanceToWaitLine(Vector3 direction)
    {
        Vector3 axis = Vector3.ProjectOnPlane(crossingDirection, Vector3.up).normalized;
        float along = Vector3.Dot(Vector3.ProjectOnPlane(transform.position - crossingSignal.transform.position, Vector3.up), axis);
        float approach = -Mathf.Sign(along) * Vector3.Dot(direction, axis);
        float beyond = Mathf.Abs(along) - curbWaitDistance;
        if (approach < 0.2f || beyond < -0.05f) return -1f;
        return Mathf.Max(0f, beyond) / approach;
    }

    private bool IsBlocked(Vector3 direction)
    {
        Vector3 origin = transform.position + Vector3.up * 0.7f;
        if (Physics.SphereCast(origin, 0.22f, direction, out RaycastHit hit, 0.9f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.root != transform.root && hit.collider.GetComponentInParent<ExteriorPedestrianAgent>() == null)
                return true;
        }
        return false;
    }

    private bool IsCrowdedAhead(Vector3 direction)
    {
        for (int i = 0; i < Active.Count; i++)
        {
            ExteriorPedestrianAgent other = Active[i];
            if (other == null || other == this) continue;
            Vector3 offset = other.transform.position - transform.position;
            offset.y = 0f;
            if (Vector3.Dot(offset, direction) > 0f && offset.magnitude < 0.85f) return true;
        }
        return false;
    }

    // Cars have no colliders, so walkers check the traffic registry and wait while a car body is right ahead (e.g. one queued across the route).
    private bool IsCarAhead(Vector3 direction)
    {
        for (int i = 0; i < ExteriorTrafficCarAgent.Active.Count; i++)
        {
            ExteriorTrafficCarAgent car = ExteriorTrafficCarAgent.Active[i];
            if (car == null) continue;
            Vector3 offset = Vector3.ProjectOnPlane(car.transform.position - transform.position, Vector3.up);
            if (ExteriorTrafficCarAgent.FreeDistance(offset, direction, 0.3f, 0.3f, car.Heading, 2.25f, 0.95f) < 0.8f) return true;
        }
        return false;
    }

    private void CacheBases()
    {
        if (leftUpperArm != null) leftArmBase = leftUpperArm.localRotation;
        if (rightUpperArm != null) rightArmBase = rightUpperArm.localRotation;
        if (leftLowerArm != null) leftForearmBase = leftLowerArm.localRotation;
        if (rightLowerArm != null) rightForearmBase = rightLowerArm.localRotation;
        if (leftUpperLeg != null) leftLegBase = leftUpperLeg.localRotation;
        if (rightUpperLeg != null) rightLegBase = rightUpperLeg.localRotation;
        if (leftLowerLeg != null) leftShinBase = leftLowerLeg.localRotation;
        if (rightLowerLeg != null) rightShinBase = rightLowerLeg.localRotation;
    }

    private void Animate(bool walking)
    {
        gaitWeight = Mathf.MoveTowards(gaitWeight, walking ? 1f : 0f, 4f * Time.deltaTime);
        gaitClock += Time.deltaTime * (walking ? 8.5f : 2.2f);
        float swing = Mathf.Sin(gaitClock) * gaitWeight;
        float legs = 28f * swing;
        // On the Mixamo rig a positive local X rotation swings a thigh or shin forward, so the trailing knee bends with a negative angle.
        if (leftUpperLeg != null) leftUpperLeg.localRotation = leftLegBase * Quaternion.Euler(legs, 0f, 0f);
        if (rightUpperLeg != null) rightUpperLeg.localRotation = rightLegBase * Quaternion.Euler(-legs, 0f, 0f);
        if (leftLowerLeg != null) leftLowerLeg.localRotation = leftShinBase * Quaternion.Euler(-Mathf.Max(0f, -legs) * 0.72f, 0f, 0f);
        if (rightLowerLeg != null) rightLowerLeg.localRotation = rightShinBase * Quaternion.Euler(-Mathf.Max(0f, legs) * 0.72f, 0f, 0f);
        // The rest pose is a T-pose: drop each arm about the body's forward axis, then swing it against its own leg.
        Vector3 forward = transform.forward, right = transform.right;
        PoseArm(leftUpperArm, leftArmBase, leftLowerArm, leftForearmBase,
            Quaternion.AngleAxis(ArmSwing * swing, right) * Quaternion.AngleAxis(ArmDrop, forward), right);
        PoseArm(rightUpperArm, rightArmBase, rightLowerArm, rightForearmBase,
            Quaternion.AngleAxis(-ArmSwing * swing, right) * Quaternion.AngleAxis(-ArmDrop, forward), right);
    }

    // World-space pose from the cached rest pose, so it does not depend on how the rig's bone axes are oriented.
    private static void PoseArm(Transform upper, Quaternion upperRest, Transform lower, Quaternion lowerRest, Quaternion turn, Vector3 right)
    {
        if (upper == null) return;
        upper.rotation = turn * (upper.parent.rotation * upperRest);
        if (lower != null) lower.rotation = Quaternion.AngleAxis(-ElbowBend, right) * (upper.rotation * lowerRest);
    }

    private void ApplyAppearance()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        Color[] shirts = { new Color(.20f,.31f,.43f), new Color(.48f,.28f,.17f), new Color(.24f,.42f,.33f), new Color(.46f,.40f,.30f), new Color(.35f,.25f,.42f) };
        Color[] trousers = { new Color(.14f,.17f,.21f), new Color(.29f,.27f,.23f), new Color(.19f,.25f,.32f), new Color(.22f,.24f,.20f) };
        Color shirt = shirts[Mathf.Abs(appearanceSeed) % shirts.Length];
        Color pants = trousers[Mathf.Abs(appearanceSeed * 7 + 3) % trousers.Length];
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            string n = renderer.name.ToLowerInvariant();
            Color tint = n.Contains("top") ? shirt : n.Contains("bottom") ? pants : Color.white;
            if (tint == Color.white) continue;
            // A textured outfit (the builder's crowd look) already has its colour; a block's _BaseColor would replace it.
            Material shared = renderer.sharedMaterial;
            if (shared != null && shared.HasProperty("_BaseMap") && shared.GetTexture("_BaseMap") != null) continue;
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", tint);
            block.SetColor("_Color", tint);
            renderer.SetPropertyBlock(block);
        }
    }
}
