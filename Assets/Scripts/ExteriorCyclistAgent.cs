using System.Collections.Generic;
using UnityEngine;

/// <summary>Moves a bicycle and rider along a dedicated cycling route.</summary>
[DisallowMultipleComponent]
public sealed class ExteriorCyclistAgent : MonoBehaviour
{
    private static readonly List<ExteriorCyclistAgent> Active = new List<ExteriorCyclistAgent>();
    [SerializeField] private Transform[] route;
    [SerializeField] private ExteriorTrafficSignal[] signals;
    [SerializeField, Min(1f)] private float speed = 5f;
    [SerializeField, Min(0.1f)] private float acceleration = 2.5f;
    [SerializeField] private Transform rider;
    [SerializeField] private Transform leftCrank, rightCrank;
    private Transform leftUpperLeg, rightUpperLeg;
    private Quaternion leftLegBase, rightLegBase;
    private int targetIndex;
    private float phase, currentSpeed;

    private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
    private void OnDisable() => Active.Remove(this);

    public void Configure(Transform[] path, float rideSpeed, Transform riderRoot, Transform leftPedal, Transform rightPedal,
        ExteriorTrafficSignal[] trafficSignals = null)
    {
        route = path; speed = rideSpeed; currentSpeed = rideSpeed; rider = riderRoot; leftCrank = leftPedal; rightCrank = rightPedal;
        signals = trafficSignals;
        if (riderRoot != null)
        {
            Animator animator = riderRoot.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                if (leftUpperLeg != null) leftLegBase = leftUpperLeg.localRotation;
                if (rightUpperLeg != null) rightLegBase = rightUpperLeg.localRotation;
                animator.enabled = false;
            }
        }
    }

    public void SetStartIndex(int index)
    {
        if (route == null || route.Length == 0) return;
        targetIndex = (index % route.Length + route.Length) % route.Length;
    }

    private void Update()
    {
        if (route == null || route.Length < 2) return;
        Transform target = route[targetIndex % route.Length];
        if (target == null) { targetIndex++; return; }
        Vector3 direction = target.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 1.5f)
        {
            targetIndex = (targetIndex + 1) % route.Length;
            target = route[targetIndex];
            direction = target.position - transform.position;
            direction.y = 0f;
        }
        if (direction.sqrMagnitude < 0.001f) return;
        direction.Normalize();
        float desiredSpeed = BlockedBySignal(direction) || FollowingTooClosely(direction) ? 0f : speed;
        currentSpeed = Mathf.MoveTowards(currentSpeed, desiredSpeed, acceleration * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction, Vector3.up), 4f * Time.deltaTime);
        transform.position += direction * (currentSpeed * Time.deltaTime);
        phase += Time.deltaTime * 9f;
        if (leftCrank != null) leftCrank.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(phase) * 35f);
        if (rightCrank != null) rightCrank.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(phase + Mathf.PI) * 35f);
        if (leftUpperLeg != null) leftUpperLeg.localRotation = leftLegBase * Quaternion.Euler(Mathf.Sin(phase) * 18f, 0f, 0f);
        if (rightUpperLeg != null) rightUpperLeg.localRotation = rightLegBase * Quaternion.Euler(Mathf.Sin(phase + Mathf.PI) * 18f, 0f, 0f);
    }

    private bool BlockedBySignal(Vector3 direction)
    {
        if (signals == null) return false;
        float nearestDistance = float.PositiveInfinity;
        ExteriorTrafficSignal nearest = null;
        for (int i = 0; i < signals.Length; i++)
        {
            ExteriorTrafficSignal signal = signals[i];
            if (signal == null) continue;
            Vector3 offset = Vector3.ProjectOnPlane(signal.transform.position - transform.position, Vector3.up);
            float ahead = Vector3.Dot(offset, direction);
            float lateral = Mathf.Abs(offset.x * direction.z - offset.z * direction.x);
            if (ahead >= 0f && ahead < 10f && lateral < 7f && ahead < nearestDistance)
            {
                nearest = signal;
                nearestDistance = ahead;
            }
        }
        return nearest != null && !nearest.AllowsVehicle(direction);
    }

    private bool FollowingTooClosely(Vector3 direction)
    {
        for (int i = 0; i < Active.Count; i++)
        {
            ExteriorCyclistAgent other = Active[i];
            if (other == null || other == this) continue;
            Vector3 offset = Vector3.ProjectOnPlane(other.transform.position - transform.position, Vector3.up);
            if (Vector3.Dot(offset, direction) > 0f && offset.magnitude < 3.5f) return true;
        }
        return false;
    }
}
