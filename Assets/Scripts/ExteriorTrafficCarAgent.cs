using System.Collections.Generic;
using UnityEngine;

/// <summary>Closed-loop road follower: rounds corners, stops at signal stop lines, keeps a braking-distance gap
/// and yields to cyclists, pedestrians and crossing cars.</summary>
[DisallowMultipleComponent]
public sealed class ExteriorTrafficCarAgent : MonoBehaviour
{
    internal static readonly List<ExteriorTrafficCarAgent> Active = new List<ExteriorTrafficCarAgent>();
    private const float HalfWidth = 0.95f, TurnRadius = 2.5f, PassingClearance = 2.8f, MaxLaneShift = 1.8f;
    [SerializeField] private Transform[] route;
    [SerializeField] private ExteriorTrafficSignal[] signals;
    [SerializeField, Min(1f)] private float cruisingSpeed = 8f;
    [SerializeField, Min(0.1f)] private float acceleration = 3f;
    [SerializeField, Min(0.5f)] private float deceleration = 3.5f;
    [SerializeField, Min(2f)] private float signalLookAhead = 13f;
    [SerializeField, Min(1f)] private float cornerLookAhead = 3f;
    [SerializeField, Min(1f)] private float cornerSpeed = 3.5f;
    [SerializeField, Min(2f)] private float bodyLength = 4.5f;
    private int targetIndex;
    private bool hasStartIndex;
    private float speed, crossingWait, ignoreCrossingUntil;
    private Vector3 heading = Vector3.forward;
    private Vector3[] lane = System.Array.Empty<Vector3>();
    private Transform[] wheelPivots = System.Array.Empty<Transform>();
    private Quaternion[] wheelRestRotations = System.Array.Empty<Quaternion>();
    private float[] wheelRadii = System.Array.Empty<float>();
    private float[] wheelAngles = System.Array.Empty<float>();

    internal Vector3 Heading => heading;
    internal float Speed => speed;

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
        lane = System.Array.Empty<Vector3>();
        CacheWheels();
    }

    public void SetStartIndex(int index)
    {
        if (route == null || route.Length == 0) return;
        targetIndex = (index % route.Length + route.Length) % route.Length;
        hasStartIndex = true;
    }

    // The builder's start index and wheel cache are not serialized, so a loaded scene rebuilds them here.
    private void Start()
    {
        Vector3 forward = Flat(transform.forward);
        if (forward.sqrMagnitude > 0.0001f) heading = forward.normalized;
        if (!hasStartIndex) targetIndex = NearestTargetIndex(route, transform.position, heading);
        if (wheelPivots.Length == 0) CacheWheels();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (route == null || route.Length < 2 || dt <= 0f) return;
        Vector3 position = transform.position;
        if (lane.Length != route.Length) BuildLane();
        if (!FindSegment(position, out Vector3 from, out Vector3 direction, out float along, out float segmentLength)) return;
        Vector3 next = NextDirection(out _);
        float remaining = segmentLength - along;
        // Turning rate grows with speed (a minimum turning radius), which also smooths the target sliding round a corner.
        Vector3 travel = Carrot(Flat(position), from, direction, along, segmentLength, out Vector3 pathDirection) - Flat(position);
        Vector3 turned = travel.sqrMagnitude > 0.0001f ? Flat(Vector3.RotateTowards(heading, travel.normalized, (speed + 0.5f) / TurnRadius * dt, 0f)) : heading;
        if (turned.sqrMagnitude > 0.0001f) heading = turned.normalized;

        float halfLength = bodyLength * 0.5f;
        float limit = cruisingSpeed;
        if (Vector3.Angle(heading, pathDirection) > 20f) limit = Mathf.Min(limit, cornerSpeed);
        if (next != Vector3.zero)
        {
            float turnSpeed = Mathf.Lerp(cruisingSpeed, cornerSpeed, Mathf.InverseLerp(15f, 90f, Vector3.Angle(direction, next)));
            limit = Mathf.Min(limit, Mathf.Sqrt(turnSpeed * turnSpeed + 2f * deceleration * Mathf.Max(0f, remaining - cornerLookAhead)));
        }
        float signalRange = Mathf.Max(signalLookAhead, cruisingSpeed * cruisingSpeed / (2f * deceleration) + 5f);
        ExteriorTrafficSignal signal = ExteriorTrafficSignal.NextOnApproach(signals, position, direction, halfLength, signalRange, 5f, out float toStopLine);
        // Stop for anything but green unless the line is already too close to stop at with full braking.
        if (signal != null && !signal.AllowsVehicle(direction)
            && toStopLine + ExteriorTrafficSignal.StopLineTolerance >= speed * speed / (4f * deceleration))
            limit = Mathf.Min(limit, StoppingSpeed(toStopLine - 0.3f));

        // Cars ahead in the same direction set a braking-distance gap; crossing cars are obstacles; oncoming cars keep to their lane.
        float crossingLimit = float.PositiveInfinity;
        for (int i = 0; i < Active.Count; i++)
        {
            ExteriorTrafficCarAgent other = Active[i];
            if (other == null || other == this) continue;
            float alignment = Vector3.Dot(other.heading, heading);
            if (alignment < -0.5f) continue;
            float gap = FreeDistance(Flat(other.transform.position - position), heading, halfLength, HalfWidth, other.heading, other.bodyLength * 0.5f, HalfWidth);
            if (alignment > 0.7f) limit = Mathf.Min(limit, StoppingSpeed(gap - 2f));
            else if (Time.time >= ignoreCrossingUntil) crossingLimit = Mathf.Min(crossingLimit, StoppingSpeed(gap - 1f));
        }
        for (int i = 0; i < ExteriorCyclistAgent.Active.Count; i++)
        {
            ExteriorCyclistAgent cyclist = ExteriorCyclistAgent.Active[i];
            if (cyclist != null) limit = Mathf.Min(limit, YieldSpeed(cyclist.transform.position, cyclist.Velocity, cyclist.Heading, 0.95f, 0.35f, position, halfLength));
        }
        for (int i = 0; i < ExteriorPedestrianAgent.Active.Count; i++)
        {
            ExteriorPedestrianAgent pedestrian = ExteriorPedestrianAgent.Active[i];
            if (pedestrian != null) limit = Mathf.Min(limit, YieldSpeed(pedestrian.transform.position, pedestrian.Velocity, heading, 0.35f, 0.35f, position, halfLength));
        }
        // Deadlock breaker: after waiting long for a car with a clearly different heading, ignore crossing cars briefly.
        if (crossingLimit < limit && speed < 0.3f) crossingWait += dt;
        else crossingWait = 0f;
        if (crossingWait > 4f)
        {
            ignoreCrossingUntil = Time.time + 3f;
            crossingWait = 0f;
        }
        limit = Mathf.Min(limit, crossingLimit);

        speed = limit < speed ? Mathf.Max(limit, speed - 2f * deceleration * dt) : Mathf.Min(limit, speed + acceleration * dt);
        float distance = speed * dt;
        transform.SetPositionAndRotation(position + heading * distance, Quaternion.LookRotation(heading, Vector3.up));
        SpinWheels(distance);
    }

    // Moves targetIndex past every segment the car has left: its end is behind the car, or the car cutting the corner
    // is already nearer the next segment's line than this one's.
    private bool FindSegment(Vector3 position, out Vector3 from, out Vector3 direction, out float along, out float length)
    {
        int count = lane.Length;
        for (int guard = 0; guard < count; guard++)
        {
            from = lane[(targetIndex - 1 + count) % count];
            Vector3 segment = lane[targetIndex] - from;
            length = segment.magnitude;
            if (length > 0.01f)
            {
                direction = segment / length;
                along = Vector3.Dot(Flat(position) - from, direction);
                if (along < length && !PastCorner(Flat(position) - lane[targetIndex], direction, length - along)) return true;
            }
            targetIndex = (targetIndex + 1) % count;
        }
        from = direction = Vector3.zero;
        along = length = 0f;
        return false;
    }

    private bool PastCorner(Vector3 fromCorner, Vector3 direction, float remaining)
    {
        if (remaining > cornerLookAhead) return false;
        Vector3 next = NextDirection(out _);
        return next != Vector3.zero
            && Mathf.Abs(fromCorner.x * next.z - fromCorner.z * next.x) <= Mathf.Abs(fromCorner.x * direction.z - fromCorner.z * direction.x);
    }

    // Pure-pursuit target: the route point ahead that is cornerLookAhead away from the car. It slides continuously onto the
    // next segment before a corner, so turns are rounded within that distance of the corner (inside the junction box).
    private Vector3 Carrot(Vector3 position, Vector3 from, Vector3 direction, float along, float length, out Vector3 segmentDirection)
    {
        float radius = cornerLookAhead;
        Vector3 start = from, end = lane[targetIndex];
        segmentDirection = direction;
        int index = targetIndex;
        for (int guard = 0; guard < lane.Length; guard++)
        {
            if ((end - position).sqrMagnitude > radius * radius)
            {
                Vector3 offset = start - position;
                float b = Vector3.Dot(offset, segmentDirection), discriminant = b * b - offset.sqrMagnitude + radius * radius;
                if (discriminant < 0f) break;
                return start + segmentDirection * Mathf.Clamp(Mathf.Sqrt(discriminant) - b, 0f, length);
            }
            index = (index + 1) % lane.Length;
            Vector3 segment = lane[index] - end;
            if (segment.sqrMagnitude < 0.0001f) continue;
            length = segment.magnitude;
            segmentDirection = segment / length;
            start = end;
            end = lane[index];
        }
        // Farther than the look-ahead from the route (or a broken route): head for a point ahead on the current segment.
        segmentDirection = direction;
        return from + direction * (Mathf.Max(along, 0f) + radius);
    }

    private Vector3 NextDirection(out float length)
    {
        Vector3 segment = lane[(targetIndex + 1) % lane.Length] - lane[targetIndex];
        length = segment.magnitude;
        return length > 0.01f ? segment / length : Vector3.zero;
    }

    // Flat waypoints (a missing one repeats its predecessor), nudged right wherever another car loop runs the opposite way
    // closer than PassingClearance: the straight loops of the two directions converge near the south junctions, and this
    // keeps oncoming and queued cars side by side instead of overlapping.
    private void BuildLane()
    {
        int count = route.Length, first = 0;
        while (first < count && route[first] == null) first++;
        var points = new Vector3[count];
        Vector3 last = first < count ? Flat(route[first].position) : Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            int k = (first + i) % count;
            if (route[k] != null) last = Flat(route[k].position);
            points[k] = last;
        }
        lane = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 tangent = (points[(i + 1) % count] - points[(i - 1 + count) % count]).normalized;
            float shift = Mathf.Clamp((PassingClearance - OncomingSeparation(points[i], tangent)) * 0.5f, 0f, MaxLaneShift);
            lane[i] = points[i] + new Vector3(tangent.z, 0f, -tangent.x) * shift;
        }
    }

    // Distance to the nearest segment of another car's loop that runs against the tangent on this car's left.
    private float OncomingSeparation(Vector3 point, Vector3 tangent)
    {
        float nearest = float.PositiveInfinity;
        Vector3 right = new Vector3(tangent.z, 0f, -tangent.x);
        for (int c = 0; c < Active.Count; c++)
        {
            Transform[] other = Active[c] != null ? Active[c].route : null;
            if (other == null || other == route || other.Length < 2 || (other[0] != null && other[0] == route[0])) continue;
            for (int i = 0; i < other.Length; i++)
            {
                Transform a = other[i], b = other[(i + 1) % other.Length];
                if (a == null || b == null) continue;
                Vector3 start = Flat(a.position), segment = Flat(b.position) - start;
                float lengthSq = segment.sqrMagnitude;
                if (lengthSq < 0.0001f || Vector3.Dot(segment, tangent) > -0.5f * Mathf.Sqrt(lengthSq)) continue;
                Vector3 offset = start + segment * Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSq) - point;
                if (Vector3.Dot(offset, right) < 0.5f) nearest = Mathf.Min(nearest, offset.magnitude);
            }
        }
        return nearest;
    }

    /// <summary>End of the nearest route segment ahead of <paramref name="position"/>; ties go to the segment along <paramref name="heading"/>.</summary>
    internal static int NearestTargetIndex(Transform[] route, Vector3 position, Vector3 heading)
    {
        if (route == null || route.Length < 2) return 0;
        position = Flat(position);
        int best = 0;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < route.Length; i++)
        {
            Transform from = route[i], to = route[(i + 1) % route.Length];
            if (from == null || to == null) continue;
            Vector3 a = Flat(from.position), segment = Flat(to.position) - a;
            float lengthSq = segment.sqrMagnitude;
            if (lengthSq < 0.0001f) continue;
            float t = Vector3.Dot(position - a, segment) / lengthSq;
            if (t >= 0.999f) continue;   // standing on this segment's end: the next segment is the one ahead
            float score = (a + segment * Mathf.Max(t, 0f) - position).magnitude + (1f - Vector3.Dot(segment.normalized, heading)) * 0.5f;
            if (score < bestScore)
            {
                bestScore = score;
                best = (i + 1) % route.Length;
            }
        }
        return best;
    }

    /// <summary>Free distance from a mover's front to another footprint that blocks its lane ahead; infinity when it is not in the way.
    /// <paramref name="offset"/> is the flat vector from the mover's centre to the other footprint's centre.</summary>
    internal static float FreeDistance(Vector3 offset, Vector3 heading, float halfLength, float halfWidth,
        Vector3 otherHeading, float otherHalfLength, float otherHalfWidth)
    {
        float ahead = Vector3.Dot(offset, heading);
        if (ahead <= 0f) return float.PositiveInfinity;
        float cos = Mathf.Abs(Vector3.Dot(otherHeading, heading));
        float sin = Mathf.Abs(otherHeading.x * heading.z - otherHeading.z * heading.x);
        float side = Mathf.Abs(offset.x * heading.z - offset.z * heading.x);
        if (side > halfWidth + otherHalfLength * sin + otherHalfWidth * cos + 0.2f) return float.PositiveInfinity;
        return ahead - halfLength - (otherHalfLength * cos + otherHalfWidth * sin);
    }

    // Also checks where the agent will be in a second, so cars slow before a walker or rider steps into the lane.
    private float YieldSpeed(Vector3 agentPosition, Vector3 agentVelocity, Vector3 agentHeading, float agentHalfLength, float agentHalfWidth,
        Vector3 position, float halfLength)
    {
        float gap = float.PositiveInfinity;
        for (int k = 0; k < 2; k++)
            gap = Mathf.Min(gap, FreeDistance(Flat(agentPosition + agentVelocity * k - position), heading, halfLength, HalfWidth,
                agentHeading, agentHalfLength, agentHalfWidth));
        return StoppingSpeed(gap - 1.5f);
    }

    private float StoppingSpeed(float distance) => distance > 0f ? Mathf.Sqrt(2f * deceleration * distance) : 0f;

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // Same name rules as before (tyres first, else wheels), but only nodes that spin about their own axle:
    // the node's local X axis must pass through the centre of its renderers, and no ancestor is already spinning.
    private void CacheWheels()
    {
        Transform[] all = GetComponentsInChildren<Transform>(true);
        var wheels = new List<Transform>(8);
        var radii = new List<float>(8);
        CollectWheels(all, "tire", wheels, radii);
        if (wheels.Count == 0) CollectWheels(all, "wheel", wheels, radii);
        wheelPivots = wheels.ToArray();
        wheelRadii = radii.ToArray();
        wheelRestRotations = new Quaternion[wheelPivots.Length];
        wheelAngles = new float[wheelPivots.Length];
        for (int i = 0; i < wheelPivots.Length; i++) wheelRestRotations[i] = wheelPivots[i].localRotation;
    }

    private void CollectWheels(Transform[] all, string key, List<Transform> wheels, List<float> signedRadii)
    {
        for (int i = 0; i < all.Length; i++)
        {
            Transform node = all[i];
            if (!node.name.ToLowerInvariant().Contains(key) || HasAncestor(node, wheels)) continue;
            Renderer[] renderers = node.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) continue;
            Bounds bounds = renderers[0].bounds;
            for (int r = 1; r < renderers.Length; r++) bounds.Encapsulate(renderers[r].bounds);
            float radius = bounds.extents.y;
            Vector3 axle = node.right, toCentre = bounds.center - node.position;
            if (radius < 0.1f || radius > 0.8f || (toCentre - axle * Vector3.Dot(toCentre, axle)).magnitude > 0.15f * radius) continue;
            wheels.Add(node);
            // Positive spin about the car's right axis rolls forward; the sign maps it onto the node's own X axis.
            signedRadii.Add(Vector3.Dot(axle, transform.right) >= 0f ? radius : -radius);
        }
    }

    private static bool HasAncestor(Transform node, List<Transform> candidates)
    {
        for (Transform parent = node.parent; parent != null; parent = parent.parent)
            if (candidates.Contains(parent)) return true;
        return false;
    }

    private void SpinWheels(float distance)
    {
        if (distance <= 0f) return;
        for (int i = 0; i < wheelPivots.Length; i++)
        {
            if (wheelPivots[i] == null) continue;
            wheelAngles[i] = Mathf.Repeat(wheelAngles[i] + distance / wheelRadii[i] * Mathf.Rad2Deg, 360f);
            wheelPivots[i].localRotation = wheelRestRotations[i] * Quaternion.AngleAxis(wheelAngles[i], Vector3.right);
        }
    }
}
