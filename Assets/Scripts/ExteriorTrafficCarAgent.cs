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
    private Wheel[] wheels = System.Array.Empty<Wheel>();
    private bool wheelsBuilt;
    private float wheelBase, curvature, targetCurvature;

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
    }

    public void SetStartIndex(int index)
    {
        if (route == null || route.Length == 0) return;
        targetIndex = (index % route.Length + route.Length) % route.Length;
        hasStartIndex = true;
    }

    // The builder's start index is not serialized, so a loaded scene rebuilds it here; the wheel rig exists only at runtime.
    private void Start()
    {
        Vector3 forward = Flat(transform.forward);
        if (forward.sqrMagnitude > 0.0001f) heading = forward.normalized;
        if (!hasStartIndex) targetIndex = NearestTargetIndex(route, transform.position, heading);
        if (!wheelsBuilt) BuildWheels();
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
        Vector3 previousHeading = heading;
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
        // Heading change this frame (radians, positive to the right); atan2 stays exact for the tiny turns of short frames.
        float yaw = Mathf.Atan2(previousHeading.z * heading.x - previousHeading.x * heading.z, Vector3.Dot(previousHeading, heading));
        TurnWheels(distance, yaw, dt);
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

    // ---------------------------------------------------------------------------------------------------------- wheels
    // The traffic prefabs hold the showroom GLBs as imported, and none has one node per wheel pivoting on its axle: the E30
    // has a node per tyre, rim, brake disc and caliper (pivots at the hubs, front wheels modelled turned 29 degrees), the
    // Tesla one mesh per part and axle (both wheels and both calipers in it), the M4 one mesh per part with all four wheels.
    // So at start every wheel gets a steer pivot and a spin pivot at its hub, found from the geometry under the car: in each
    // quarter of the car the tyre is the tallest wheel part standing on the ground; its box centre is the hub, half its height
    // the rolling radius, and the long axis of its vertices' spread over the ground its rolling direction. Parts centred on
    // the axle (tyre, rim, disc, cap) roll on the spin pivot; the other parts of the wheel (calipers, knuckles) only steer.
    // A part that spans several wheels is split into one new mesh per wheel, and its own renderer is switched off. Nothing
    // here is serialized, so rebuilt prefabs (Build Exterior District, CarsRealism) need no extra step.
    private const float GroundContact = 0.06f, MaxSteer = 35f, MaxCurvature = 0.6f, SteerSmoothing = 0.1f;

    private sealed class Wheel
    {
        public Transform steer, spin;
        public Vector3 axle;   // spin axis at rest in the steer pivot's space, toward the car's right
        public float radius, side, restSteer, angle;
        public bool front;
    }

    // A wheel part's triangles in one quarter of the car (bit 0: right of the centre line, bit 1: ahead of the middle
    // between the axles), with the car-space box and the horizontal spread of their vertices.
    private sealed class Piece
    {
        public MeshRenderer renderer;
        public Mesh mesh;
        public int quarter;
        public Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        public double sumX, sumZ, sumXX, sumZZ, sumXZ;
        public int count;

        public Vector3 Centre => (min + max) * 0.5f;
        public float Height => max.y - min.y;

        public void Add(Vector3 p)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
            sumX += p.x; sumZ += p.z; sumXX += p.x * p.x; sumZZ += p.z * p.z; sumXZ += p.x * p.z;
            count++;
        }
    }

    // Split meshes are shared by every car built from the same prefab.
    private sealed class SplitMeshes
    {
        public Mesh source;
        public Matrix4x4 toCar;
        public Vector3[] origins;
        public Mesh[] quarters;
    }

    private static readonly List<SplitMeshes> SplitCache = new List<SplitMeshes>();

    private void BuildWheels()
    {
        wheelsBuilt = true;
        Matrix4x4 toCar = transform.worldToLocalMatrix;
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
        var parts = new List<MeshRenderer>();
        var meshes = new List<Mesh>();
        Vector3 groundMin = Vector3.positiveInfinity, groundMax = Vector3.negativeInfinity;
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer renderer = renderers[i];
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (!renderer.enabled || filter == null || filter.sharedMesh == null || !IsWheelPart(renderer.transform)) continue;
            parts.Add(renderer);
            meshes.Add(filter.sharedMesh);
            // The parts that reach the ground (the tyres) set the centre line and the middle between the axles.
            Bounds box = CarBox(toCar * renderer.transform.localToWorldMatrix, filter.sharedMesh.bounds);
            if (box.min.y > GroundContact) continue;
            groundMin = Vector3.Min(groundMin, box.min);
            groundMax = Vector3.Max(groundMax, box.max);
        }
        if (parts.Count == 0 || groundMin.x > groundMax.x) return;
        float middleX = (groundMin.x + groundMax.x) * 0.5f, middleZ = (groundMin.z + groundMax.z) * 0.5f;
        var pieces = new List<Piece>();
        for (int i = 0; i < parts.Count; i++)
            CollectPieces(parts[i], meshes[i], toCar * parts[i].transform.localToWorldMatrix, middleX, middleZ, pieces);

        // The tyre of each quarter: the tallest part standing on the ground, of a road wheel's size.
        var tyres = new Piece[4];
        for (int i = 0; i < pieces.Count; i++)
        {
            Piece piece = pieces[i];
            float height = piece.Height;
            if (piece.min.y > GroundContact || height < 0.3f || height > 1.6f) continue;
            if (tyres[piece.quarter] == null || height > tyres[piece.quarter].Height) tyres[piece.quarter] = piece;
        }
        var built = new Wheel[4];
        var list = new List<Wheel>(4);
        float frontZ = 0f, rearZ = 0f;
        int fronts = 0, rears = 0;
        for (int q = 0; q < 4; q++)
        {
            if (tyres[q] == null) continue;
            Wheel wheel = built[q] = CreateWheel(tyres[q], middleX, q);
            list.Add(wheel);
            if (wheel.front) { frontZ += tyres[q].Centre.z; fronts++; }
            else { rearZ += tyres[q].Centre.z; rears++; }
        }
        wheelBase = fronts > 0 && rears > 0 ? frontZ / fronts - rearZ / rears : 0f;
        for (int start = 0; start < pieces.Count;)
        {
            int end = start + 1;
            while (end < pieces.Count && pieces[end].renderer == pieces[start].renderer) end++;
            AttachPieces(pieces, start, end, built, tyres, middleX, middleZ);
            start = end;
        }
        wheels = list.ToArray();
        TurnWheels(0f, 0f, 0f);
    }

    private Wheel CreateWheel(Piece tyre, float middleX, int quarter)
    {
        // The tyre's footprint is long along its rolling direction; a model's turned front wheel is put straight by
        // its steer pivot.
        float restSteer = 0f;
        if (tyre.count > 2)
        {
            double n = tyre.count, meanX = tyre.sumX / n, meanZ = tyre.sumZ / n;
            double xx = tyre.sumXX / n - meanX * meanX, zz = tyre.sumZZ / n - meanZ * meanZ, xz = tyre.sumXZ / n - meanX * meanZ;
            double major = 0.5 * System.Math.Atan2(2.0 * xz, xx - zz);
            float x = (float)System.Math.Cos(major), z = (float)System.Math.Sin(major);
            if (z < 0f) { x = -x; z = -z; }
            restSteer = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
            if (Mathf.Abs(restSteer) < 0.5f) restSteer = 0f;
        }
        Vector3 hub = tyre.Centre;
        var wheel = new Wheel
        {
            radius = Mathf.Max(0.05f, tyre.Height * 0.5f),
            side = hub.x - middleX,
            front = (quarter & 2) != 0,
            restSteer = restSteer,
            axle = Quaternion.AngleAxis(restSteer, Vector3.up) * Vector3.right
        };
        string label = (wheel.front ? "Front " : "Rear ") + ((quarter & 1) != 0 ? "Right" : "Left");
        wheel.steer = new GameObject(label + " Wheel Steer").transform;
        wheel.steer.SetParent(transform, false);
        wheel.steer.localPosition = hub;
        wheel.spin = new GameObject(label + " Wheel Spin").transform;
        wheel.spin.SetParent(wheel.steer, false);
        return wheel;
    }

    // Moves one renderer's pieces under their wheels' pivots: the renderer itself when it lies in one wheel and carries no
    // child nodes, else one new renderer per quarter (pieces outside every wheel stay with the car body).
    private void AttachPieces(List<Piece> pieces, int start, int end, Wheel[] built, Piece[] tyres, float middleX, float middleZ)
    {
        var targets = new Transform[4];
        bool any = false;
        for (int i = start; i < end; i++)
        {
            Piece piece = pieces[i];
            Wheel wheel = built[piece.quarter];
            Piece tyre = tyres[piece.quarter];
            if (wheel == null || !Inside(piece, tyre, 0.3f * wheel.radius)) continue;
            Vector3 offset = piece.Centre - tyre.Centre;
            bool rolls = (offset - wheel.axle * Vector3.Dot(offset, wheel.axle)).magnitude <= 0.12f * wheel.radius
                && !NamedLike(piece.renderer.transform, "caliper");
            targets[piece.quarter] = rolls ? wheel.spin : wheel.steer;
            any = true;
        }
        if (!any) return;
        Piece first = pieces[start];
        Transform node = first.renderer.transform;
        if (end - start == 1 && node.childCount == 0)
        {
            node.SetParent(targets[first.quarter], true);
            return;
        }
        if (!first.mesh.isReadable) return;
        var origins = new Vector3[4];
        for (int q = 0; q < 4; q++) origins[q] = targets[q] != null ? built[q].steer.localPosition : Vector3.zero;
        Mesh[] split = SplitMesh(first.mesh, transform.worldToLocalMatrix * node.localToWorldMatrix, middleX, middleZ, origins);
        for (int q = 0; q < 4; q++)
            if (split[q] != null) CreatePart(first.renderer, split[q], targets[q] != null ? targets[q] : transform, q);
        first.renderer.enabled = false;
    }

    private static void CollectPieces(MeshRenderer renderer, Mesh mesh, Matrix4x4 toCar, float middleX, float middleZ, List<Piece> pieces)
    {
        if (!mesh.isReadable)
        {
            // Without vertex data the part goes whole to the quarter of its box centre.
            Bounds box = CarBox(toCar, mesh.bounds);
            var whole = new Piece { renderer = renderer, mesh = mesh, quarter = Quarter(box.center, middleX, middleZ) };
            whole.Add(box.min);
            whole.Add(box.max);
            pieces.Add(whole);
            return;
        }
        Vector3[] vertices = mesh.vertices;
        for (int v = 0; v < vertices.Length; v++) vertices[v] = toCar.MultiplyPoint3x4(vertices[v]);
        var seen = new byte[vertices.Length];
        var quarters = new Piece[4];
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
            int[] triangles = mesh.GetTriangles(s);
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int q = Quarter((vertices[triangles[t]] + vertices[triangles[t + 1]] + vertices[triangles[t + 2]]) / 3f, middleX, middleZ);
                Piece piece = quarters[q];
                if (piece == null) quarters[q] = piece = new Piece { renderer = renderer, mesh = mesh, quarter = q };
                byte bit = (byte)(1 << q);
                for (int k = 0; k < 3; k++)
                {
                    int index = triangles[t + k];
                    if ((seen[index] & bit) != 0) continue;
                    seen[index] |= bit;
                    piece.Add(vertices[index]);
                }
            }
        }
        for (int q = 0; q < 4; q++)
            if (quarters[q] != null) pieces.Add(quarters[q]);
    }

    // One mesh per quarter of the source's triangles (null where it has none), with the vertices in the space of the pivot
    // at that quarter's origin (car space moved to the hub, or the car itself).
    private static Mesh[] SplitMesh(Mesh source, Matrix4x4 toCar, float middleX, float middleZ, Vector3[] origins)
    {
        for (int i = 0; i < SplitCache.Count; i++)
        {
            SplitMeshes cached = SplitCache[i];
            if (cached.source == source && Same(cached.toCar, toCar) && Same(cached.origins, origins) && Alive(cached.quarters))
                return cached.quarters;
        }
        Vector3[] vertices = source.vertices;
        Vector3[] normals = source.normals;
        Vector4[] tangents = source.tangents;
        Color32[] colors = source.colors32;
        for (int v = 0; v < vertices.Length; v++) vertices[v] = toCar.MultiplyPoint3x4(vertices[v]);
        bool hasNormals = normals.Length == vertices.Length, hasTangents = tangents.Length == vertices.Length, hasColors = colors.Length == vertices.Length;
        bool flip = toCar.determinant < 0f;
        Matrix4x4 normalMatrix = toCar.inverse.transpose;
        var uvs = new List<Vector4>[4];
        var uvDimensions = new int[4];
        for (int c = 0; c < 4; c++)
        {
            var attribute = (UnityEngine.Rendering.VertexAttribute)((int)UnityEngine.Rendering.VertexAttribute.TexCoord0 + c);
            if (!source.HasVertexAttribute(attribute)) continue;
            uvDimensions[c] = source.GetVertexAttributeDimension(attribute);
            uvs[c] = new List<Vector4>(vertices.Length);
            source.GetUVs(c, uvs[c]);
        }
        var triangles = new int[source.subMeshCount][];
        for (int s = 0; s < triangles.Length; s++)
            triangles[s] = source.GetTopology(s) == MeshTopology.Triangles ? source.GetTriangles(s) : System.Array.Empty<int>();
        var result = new Mesh[4];
        var remap = new int[vertices.Length];
        var positions = new List<Vector3>();
        var outNormals = new List<Vector3>();
        var outTangents = new List<Vector4>();
        var outColors = new List<Color32>();
        var outUvs = new List<Vector4>[4];
        var outUvs2 = new List<Vector2>[4];
        var indices = new List<int>[triangles.Length];
        for (int q = 0; q < 4; q++)
        {
            for (int v = 0; v < remap.Length; v++) remap[v] = -1;
            positions.Clear(); outNormals.Clear(); outTangents.Clear(); outColors.Clear();
            for (int c = 0; c < 4; c++)
            {
                if (uvs[c] == null) continue;
                if (uvDimensions[c] <= 2) (outUvs2[c] ??= new List<Vector2>()).Clear();
                else (outUvs[c] ??= new List<Vector4>()).Clear();
            }
            bool any = false;
            for (int s = 0; s < triangles.Length; s++)
            {
                List<int> list = indices[s] ??= new List<int>();
                list.Clear();
                int[] source3 = triangles[s];
                for (int t = 0; t + 2 < source3.Length; t += 3)
                {
                    if (Quarter((vertices[source3[t]] + vertices[source3[t + 1]] + vertices[source3[t + 2]]) / 3f, middleX, middleZ) != q) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int index = source3[t + (flip && k > 0 ? 3 - k : k)];
                        if (remap[index] < 0)
                        {
                            remap[index] = positions.Count;
                            positions.Add(vertices[index] - origins[q]);
                            if (hasNormals) outNormals.Add(normalMatrix.MultiplyVector(normals[index]).normalized);
                            if (hasTangents)
                            {
                                Vector4 tangent = tangents[index];
                                Vector3 direction = toCar.MultiplyVector(tangent).normalized;
                                outTangents.Add(new Vector4(direction.x, direction.y, direction.z, flip ? -tangent.w : tangent.w));
                            }
                            if (hasColors) outColors.Add(colors[index]);
                            for (int c = 0; c < 4; c++)
                            {
                                if (uvs[c] == null) continue;
                                if (uvDimensions[c] <= 2) outUvs2[c].Add(uvs[c][index]);
                                else outUvs[c].Add(uvs[c][index]);
                            }
                        }
                        list.Add(remap[index]);
                    }
                }
                any |= list.Count > 0;
            }
            if (!any) continue;
            var mesh = new Mesh { name = source.name + " Wheel " + q };
            if (positions.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(positions);
            if (hasNormals) mesh.SetNormals(outNormals);
            if (hasTangents) mesh.SetTangents(outTangents);
            if (hasColors) mesh.SetColors(outColors);
            for (int c = 0; c < 4; c++)
            {
                if (uvs[c] == null) continue;
                if (uvDimensions[c] <= 2) mesh.SetUVs(c, outUvs2[c]);
                else mesh.SetUVs(c, outUvs[c]);
            }
            mesh.subMeshCount = triangles.Length;
            for (int s = 0; s < triangles.Length; s++) mesh.SetTriangles(indices[s], s, false);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            result[q] = mesh;
        }
        SplitCache.Add(new SplitMeshes { source = source, toCar = toCar, origins = (Vector3[])origins.Clone(), quarters = result });
        return result;
    }

    private static void CreatePart(MeshRenderer source, Mesh mesh, Transform parent, int quarter)
    {
        var part = new GameObject(source.name + " (wheel " + quarter + ")") { layer = source.gameObject.layer };
        part.transform.SetParent(parent, false);
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = source.sharedMaterials;
        renderer.shadowCastingMode = source.shadowCastingMode;
        renderer.receiveShadows = source.receiveShadows;
        renderer.lightProbeUsage = source.lightProbeUsage;
        renderer.reflectionProbeUsage = source.reflectionProbeUsage;
        renderer.probeAnchor = source.probeAnchor;
        renderer.motionVectorGenerationMode = source.motionVectorGenerationMode;
        renderer.renderingLayerMask = source.renderingLayerMask;
        renderer.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
        if (!source.HasPropertyBlock()) return;
        var block = new MaterialPropertyBlock();
        source.GetPropertyBlock(block);
        renderer.SetPropertyBlock(block);
    }

    // Rolls every wheel by its own forward travel (the outer wheels of a turn roll farther) over its radius, and steers the
    // front wheels with the car's path: the curvature (heading change over distance driven) is eased, and each front wheel
    // points square to the line from the turn centre on the rear axle (Ackermann). Standing cars keep their wheels still.
    private void TurnWheels(float distance, float yaw, float dt)
    {
        if (distance > 0.0001f) targetCurvature = Mathf.Clamp(yaw / distance, -MaxCurvature, MaxCurvature);
        curvature = dt > 0f ? curvature + (targetCurvature - curvature) * (1f - Mathf.Exp(-dt / SteerSmoothing)) : targetCurvature;
        for (int i = 0; i < wheels.Length; i++)
        {
            Wheel wheel = wheels[i];
            if (wheel.spin == null) continue;
            float steer = 0f;
            if (wheel.front && wheelBase > 0f)
                steer = Mathf.Clamp(Mathf.Atan2(wheelBase * curvature, 1f - wheel.side * curvature) * Mathf.Rad2Deg, -MaxSteer, MaxSteer);
            if (wheel.front || dt <= 0f) wheel.steer.localRotation = Quaternion.AngleAxis(steer - wheel.restSteer, Vector3.up);
            if (distance <= 0f) continue;
            wheel.angle = Mathf.Repeat(wheel.angle + (distance - yaw * wheel.side) / wheel.radius * Mathf.Rad2Deg, 360f);
            wheel.spin.localRotation = Quaternion.AngleAxis(wheel.angle, wheel.axle);
        }
    }

    private static int Quarter(Vector3 p, float middleX, float middleZ) => (p.x >= middleX ? 1 : 0) | (p.z >= middleZ ? 2 : 0);

    private static bool Inside(Piece piece, Piece tyre, float margin) =>
        piece.min.x >= tyre.min.x - margin && piece.max.x <= tyre.max.x + margin
        && piece.min.y >= tyre.min.y - margin && piece.max.y <= tyre.max.y + margin
        && piece.min.z >= tyre.min.z - margin && piece.max.z <= tyre.max.z + margin;

    // Car-space box of a mesh box under the matrix.
    private static Bounds CarBox(Matrix4x4 toCar, Bounds local)
    {
        Vector3 e = local.extents;
        Vector3 x = toCar.MultiplyVector(new Vector3(e.x, 0f, 0f)), y = toCar.MultiplyVector(new Vector3(0f, e.y, 0f)), z = toCar.MultiplyVector(new Vector3(0f, 0f, e.z));
        Vector3 extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        return new Bounds(toCar.MultiplyPoint3x4(local.center), extents * 2f);
    }

    // Road wheel parts by name, on the node or an ancestor below the car: wheels, tyres, rims, hubs, brake discs and calipers.
    // A name that also says "steer" (a steering wheel) does not count by itself.
    private bool IsWheelPart(Transform node)
    {
        for (Transform t = node; t != null && t != transform; t = t.parent)
        {
            string name = t.name;
            if (Has(name, "steer")) continue;
            if (Has(name, "wheel") || Has(name, "tire") || Has(name, "tyre") || Has(name, "caliper")
                || HasWord(name, "rim") || HasWord(name, "rims") || HasWord(name, "hub") || HasWord(name, "brake") || HasWord(name, "disc"))
                return true;
        }
        return false;
    }

    private bool NamedLike(Transform node, string word)
    {
        for (Transform t = node; t != null && t != transform; t = t.parent)
            if (Has(t.name, word)) return true;
        return false;
    }

    private static bool Has(string name, string word) => name.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) >= 0;

    // The word stands alone: no letter right before or after it (so "rim" is not found in "primary").
    private static bool HasWord(string name, string word)
    {
        for (int at = name.IndexOf(word, System.StringComparison.OrdinalIgnoreCase); at >= 0;
             at = name.IndexOf(word, at + 1, System.StringComparison.OrdinalIgnoreCase))
        {
            int after = at + word.Length;
            if ((at == 0 || !char.IsLetter(name[at - 1])) && (after == name.Length || !char.IsLetter(name[after]))) return true;
        }
        return false;
    }

    private static bool Same(Matrix4x4 a, Matrix4x4 b)
    {
        for (int i = 0; i < 16; i++)
            if (Mathf.Abs(a[i] - b[i]) > 0.001f) return false;
        return true;
    }

    private static bool Same(Vector3[] a, Vector3[] b)
    {
        for (int i = 0; i < a.Length; i++)
            if ((a[i] - b[i]).sqrMagnitude > 0.000001f) return false;
        return true;
    }

    private static bool Alive(Mesh[] meshes)
    {
        for (int i = 0; i < meshes.Length; i++)
            if (!ReferenceEquals(meshes[i], null) && meshes[i] == null) return false;
        return true;
    }
}
