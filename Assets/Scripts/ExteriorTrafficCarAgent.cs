using System.Collections.Generic;
using UnityEngine;

/// <summary>Lightweight closed-loop road follower with signal stopping and car spacing.</summary>
[DisallowMultipleComponent]
public sealed class ExteriorTrafficCarAgent : MonoBehaviour
{
    private static readonly List<ExteriorTrafficCarAgent> Active = new List<ExteriorTrafficCarAgent>();
    [SerializeField] private Transform[] route;
    [SerializeField] private ExteriorTrafficSignal[] signals;
    [SerializeField, Min(1f)] private float cruisingSpeed = 8f;
    [SerializeField, Min(0.1f)] private float acceleration = 3f;
    [SerializeField, Min(2f)] private float signalLookAhead = 13f;
    private int targetIndex;
    private float speed;
    private Transform[] wheelPivots = System.Array.Empty<Transform>();
    private Quaternion[] wheelRestRotations = System.Array.Empty<Quaternion>();
    private float[] wheelRadii = System.Array.Empty<float>();

    private void OnEnable()
    {
        if (!Active.Contains(this)) Active.Add(this);
    }

    private void OnDisable() => Active.Remove(this);

    public void Configure(Transform[] path, ExteriorTrafficSignal[] trafficSignals, float speedLimit, float startSpeed)
    {
        route = path;
        signals = trafficSignals;
        cruisingSpeed = speedLimit;
        speed = Mathf.Clamp(startSpeed, 0f, speedLimit);
        CacheWheels();
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
        Vector3 flatToTarget = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
        if (flatToTarget.sqrMagnitude < 2.25f)
        {
            targetIndex = (targetIndex + 1) % route.Length;
            target = route[targetIndex];
            if (target == null) return;
            flatToTarget = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
        }
        Vector3 direction = flatToTarget.normalized;
        float wantedSpeed = cruisingSpeed;
        if (BlockedBySignal(direction) || FollowingTooClosely(direction)) wantedSpeed = 0f;
        speed = Mathf.MoveTowards(speed, wantedSpeed, acceleration * Time.deltaTime);
        if (direction.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction, Vector3.up), 2.2f * Time.deltaTime);
        float distance = speed * Time.deltaTime;
        transform.position += direction * distance;
        SpinWheels(distance);
    }

    private void CacheWheels()
    {
        Transform[] all = GetComponentsInChildren<Transform>(true);
        var wheels = new List<Transform>(8);
        for (int i = 0; i < all.Length; i++)
        {
            string n = all[i].name.ToLowerInvariant();
            if (n.Contains("tire")) wheels.Add(all[i]);
        }
        if (wheels.Count == 0)
        {
            for (int i = 0; i < all.Length; i++)
            {
                string n = all[i].name.ToLowerInvariant();
                if (n.Contains("wheel")) wheels.Add(all[i]);
            }
        }
        wheelPivots = wheels.ToArray();
        wheelRestRotations = new Quaternion[wheelPivots.Length];
        wheelRadii = new float[wheelPivots.Length];
        for (int i = 0; i < wheelPivots.Length; i++)
        {
            wheelRestRotations[i] = wheelPivots[i].localRotation;
            Renderer renderer = wheelPivots[i].GetComponent<Renderer>();
            wheelRadii[i] = renderer == null ? 0.32f : Mathf.Max(0.08f, renderer.bounds.extents.y);
        }
    }

    private void SpinWheels(float distance)
    {
        if (distance <= 0f) return;
        for (int i = 0; i < wheelPivots.Length; i++)
        {
            if (wheelPivots[i] == null) continue;
            float degrees = distance / wheelRadii[i] * Mathf.Rad2Deg;
            wheelPivots[i].localRotation = wheelRestRotations[i] * Quaternion.AngleAxis(degrees, Vector3.right);
        }
    }

    private bool BlockedBySignal(Vector3 direction)
    {
        if (signals == null) return false;
        float bestAhead = float.PositiveInfinity;
        ExteriorTrafficSignal nearest = null;
        for (int i = 0; i < signals.Length; i++)
        {
            ExteriorTrafficSignal signal = signals[i];
            if (signal == null) continue;
            Vector3 offset = Vector3.ProjectOnPlane(signal.transform.position - transform.position, Vector3.up);
            float ahead = Vector3.Dot(offset, direction);
            float lateral = Mathf.Abs(offset.x * direction.z - offset.z * direction.x);
            if (ahead >= 0f && ahead < signalLookAhead && lateral < 7f && ahead < bestAhead)
            {
                bestAhead = ahead;
                nearest = signal;
            }
        }
        return nearest != null && !nearest.AllowsVehicle(direction);
    }

    private bool FollowingTooClosely(Vector3 direction)
    {
        for (int i = 0; i < Active.Count; i++)
        {
            ExteriorTrafficCarAgent other = Active[i];
            if (other == null || other == this) continue;
            Vector3 offset = Vector3.ProjectOnPlane(other.transform.position - transform.position, Vector3.up);
            float ahead = Vector3.Dot(offset, direction);
            if (ahead > 0f && ahead < 7.5f && Mathf.Abs(offset.x * direction.z - offset.z * direction.x) < 2f)
                return true;
        }
        return false;
    }
}
