using System.Collections.Generic;
using UnityEngine;

/// <summary>Moves a bicycle and rider along a dedicated cycling route.</summary>
[DisallowMultipleComponent]
public sealed class ExteriorCyclistAgent : MonoBehaviour
{
    internal static readonly List<ExteriorCyclistAgent> Active = new List<ExteriorCyclistAgent>();
    private const float HalfLength = 0.95f, HalfWidth = 0.35f;
    // Rider pose: forward roll of the pelvis (degrees from the bike's vertical), height of the Hips bone above the saddle's top,
    // the range the torso may lean while it brings each grip within ArmReach of that arm's length, share of the whole forward
    // roll the neck takes back so the rider looks ahead, grip position as a fraction of the handlebar's half-length, and how far
    // each hand bends from the forearm toward straight down.
    private const float PelvisTilt = 15f, SeatLift = 0.03f, MinLean = 15f, MaxLean = 65f, ArmReach = 0.97f, HeadLift = 0.7f,
        GripSpan = 0.85f, WristWrap = 0.4f;
    // Legs: how far each knee turns out from straight ahead (share of the bike's width axis), the ball joint's height above the
    // sole as a share of the foot's length, and the foot's toe-down pitch (degrees) at the top and the bottom of the stroke.
    private const float KneeOut = 0.15f, SoleShare = 0.1f, ToeDownTop = 2f, ToeDownBottom = 18f;
    [SerializeField] private Transform[] route;
    [SerializeField] private ExteriorTrafficSignal[] signals;
    [SerializeField, Min(1f)] private float speed = 5f;
    [SerializeField, Min(0.1f)] private float acceleration = 2.5f;
    [SerializeField] private Transform rider;
    [SerializeField] private Transform leftCrank, rightCrank;
    private readonly Leg leftLeg = new Leg(), rightLeg = new Leg();
    private static readonly RaycastHit[] GroundHits = new RaycastHit[8];
    private Transform frontWheel, rearWheel;
    private float tyreBottomBelowRoot = 0.025f;
    private Quaternion frontWheelRest, rearWheelRest;
    private int targetIndex;
    private bool hasStartIndex, riderPosed;
    private float phase, currentSpeed, waitTime, ignoreStoppedCarsUntil, wheelAngle, wheelRadius = 0.4f;
    private Vector3 heading = Vector3.forward;

    internal Vector3 Heading => heading;
    internal Vector3 Velocity => heading * currentSpeed;

    // A leg and the crank it pedals. Every solve starts the thigh, shin and foot from the rotations they had when the rider was
    // seated, so the same crank angle always gives the same pose and nothing drifts over many turns.
    private sealed class Leg
    {
        public Transform Thigh, Shin, Foot, Toe, ToeTip, Crank, Pedal;
        public Quaternion ThighSeat, ShinSeat, FootSeat;
        // Pedal spindle in the crank's space, and the pedal's half-thickness the sole rests on.
        public Vector3 Spindle;
        public float Tread, Side;
        // The pedal body sits off the axle on a crank arm and is kept level as the crank turns.
        public bool Level;
    }

    private sealed class Arm
    {
        public Transform Upper, Lower, Hand, Finger;
        public Vector3 Grip;
        public bool HasGrip;
    }

    private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
    private void OnDisable() => Active.Remove(this);

    public void Configure(Transform[] path, float rideSpeed, Transform riderRoot, Transform leftPedal, Transform rightPedal,
        ExteriorTrafficSignal[] trafficSignals = null)
    {
        route = path; speed = rideSpeed; currentSpeed = rideSpeed; rider = riderRoot; leftCrank = leftPedal; rightCrank = rightPedal;
        signals = trafficSignals;
        PoseRider();
    }

    public void SetStartIndex(int index)
    {
        if (route == null || route.Length == 0) return;
        targetIndex = (index % route.Length + route.Length) % route.Length;
        hasStartIndex = true;
    }

    // The builder's start index, rider bones and wheels are not serialized, so a loaded scene rebuilds them here.
    private void Start()
    {
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (forward.sqrMagnitude > 0.0001f) heading = forward.normalized;
        if (!hasStartIndex) targetIndex = ExteriorTrafficCarAgent.NearestTargetIndex(route, transform.position, heading);
        if (!riderPosed) PoseRider();
        CacheWheels();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (route == null || route.Length < 2 || dt <= 0f) return;
        Transform target = route[targetIndex % route.Length];
        if (target == null) { targetIndex = (targetIndex + 1) % route.Length; return; }
        Vector3 direction = target.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 1.5f)
        {
            targetIndex = (targetIndex + 1) % route.Length;
            return;
        }
        direction.Normalize();
        heading = direction;
        float limit = speed;
        ExteriorTrafficSignal signal = ExteriorTrafficSignal.NextOnApproach(signals, transform.position, direction, HalfLength, 12f, 7f, out float toStopLine);
        if (signal != null && !signal.AllowsVehicle(direction)
            && toStopLine + ExteriorTrafficSignal.StopLineTolerance >= currentSpeed * currentSpeed / (4f * acceleration))
            limit = Mathf.Min(limit, StoppingSpeed(toStopLine - 0.3f));
        float trafficLimit = TrafficSpeed(direction);
        waitTime = trafficLimit < limit && currentSpeed < 0.1f ? waitTime + dt : 0f;
        if (waitTime > 3f)
        {
            ignoreStoppedCarsUntil = Time.time + 2f;
            waitTime = 0f;
        }
        limit = Mathf.Min(limit, trafficLimit);
        currentSpeed = limit < currentSpeed ? Mathf.Max(limit, currentSpeed - 2f * acceleration * dt) : Mathf.Min(limit, currentSpeed + acceleration * dt);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction, Vector3.up), 4f * dt);
        float travelled = currentSpeed * dt;
        transform.position += direction * travelled;
        KeepTyresOnSurface(dt);
        phase = Mathf.Repeat(phase + dt * 9f * currentSpeed / speed, 2f * Mathf.PI);
        // The cranks turn about the bottom-bracket axle (each pivot's local X) with the pedalling phase, and the wheels roll
        // about theirs by the distance covered, so both stop with the bike; the feet stay on the pedals.
        float crank = phase * Mathf.Rad2Deg;
        TurnCranks(crank);
        wheelAngle = Mathf.Repeat(wheelAngle + travelled / wheelRadius * Mathf.Rad2Deg, 360f);
        if (frontWheel != null) frontWheel.localRotation = Quaternion.AngleAxis(wheelAngle, Vector3.right) * frontWheelRest;
        if (rearWheel != null) rearWheel.localRotation = Quaternion.AngleAxis(wheelAngle, Vector3.right) * rearWheelRest;
        Pedal(leftLeg, crank);
        Pedal(rightLeg, crank + 180f);
    }

    private void TurnCranks(float crank)
    {
        if (leftCrank != null) leftCrank.localRotation = Quaternion.AngleAxis(crank, Vector3.right);
        if (rightCrank != null) rightCrank.localRotation = Quaternion.AngleAxis(crank + 180f, Vector3.right);
    }

    // Brakes for riders ahead on the track, pedestrians and cars in the way (now or in a second).
    // After a few seconds of waiting, stopped cars are ignored briefly, because one may itself be waiting for this rider.
    private float TrafficSpeed(Vector3 direction)
    {
        Vector3 position = transform.position;
        float gap = float.PositiveInfinity;
        for (int i = 0; i < Active.Count; i++)
        {
            ExteriorCyclistAgent other = Active[i];
            if (other != null && other != this && Vector3.Dot(other.heading, direction) > 0.5f)
                gap = Mathf.Min(gap, Clearance(other.transform.position, Vector3.zero, other.heading, HalfLength, HalfWidth, position, direction));
        }
        for (int i = 0; i < ExteriorPedestrianAgent.Active.Count; i++)
        {
            ExteriorPedestrianAgent pedestrian = ExteriorPedestrianAgent.Active[i];
            if (pedestrian != null) gap = Mathf.Min(gap, Clearance(pedestrian.transform.position, pedestrian.Velocity, direction, 0.35f, 0.35f, position, direction));
        }
        for (int i = 0; i < ExteriorTrafficCarAgent.Active.Count; i++)
        {
            ExteriorTrafficCarAgent car = ExteriorTrafficCarAgent.Active[i];
            if (car != null && (car.Speed > 0.1f || Time.time >= ignoreStoppedCarsUntil))
                gap = Mathf.Min(gap, Clearance(car.transform.position, car.Heading * car.Speed, car.Heading, 2.25f, 0.95f, position, direction));
        }
        return StoppingSpeed(gap - 1f);
    }

    private static float Clearance(Vector3 other, Vector3 velocity, Vector3 otherHeading, float otherHalfLength, float otherHalfWidth,
        Vector3 position, Vector3 direction)
    {
        float gap = float.PositiveInfinity;
        for (int k = 0; k < 2; k++)
        {
            Vector3 offset = Vector3.ProjectOnPlane(other + velocity * k - position, Vector3.up);
            gap = Mathf.Min(gap, ExteriorTrafficCarAgent.FreeDistance(offset, direction, HalfLength, HalfWidth, otherHeading, otherHalfLength, otherHalfWidth));
        }
        return gap;
    }

    private float StoppingSpeed(float distance) => distance > 0f ? Mathf.Sqrt(2f * acceleration * distance) : 0f;

    // Rider bones come from its Animator, which the scene keeps disabled, so it is enabled just long enough to resolve them;
    // should the avatar not report a humanoid yet, the Mixamo bone names are used instead.
    // The rider is then seated: the pelvis rolls forward and the Hips bone moves onto the top of the saddle, the torso leans no
    // further than the grips need, each arm is solved onto its grip and each leg onto its pedal (Update keeps the feet on the
    // turning pedals).
    // Every step aims at an absolute target measured from the current bones, so posing twice (the generator also poses the
    // rider in the editor, and that pose is saved with the scene) changes nothing.
    private void PoseRider()
    {
        if (rider == null) return;
        Animator animator = rider.GetComponent<Animator>();
        if (animator == null) return;
        animator.enabled = true;
        Animator human = animator.isHuman ? animator : null;
        Transform hips = Bone(human, HumanBodyBones.Hips, "Hips");
        BindLeg(leftLeg, human, true, leftCrank);
        BindLeg(rightLeg, human, false, rightCrank);
        Quaternion roll = TiltPelvis(hips);
        SitOnSaddle(hips);
        Transform handlebar = transform.Find("Bicycle/Handlebar");
        Arm left = BindArm(human, true, handlebar), right = BindArm(human, false, handlebar);
        LeanTorso(Bone(human, HumanBodyBones.Spine, "Spine"), Bone(human, HumanBodyBones.Neck, "Neck"), roll, left, right);
        ReachForBar(left, -1f);
        ReachForBar(right, 1f);
        animator.enabled = false;
        // The imported skinned bounds fit a standing pose; a 2 m box around each root bone keeps the leaning, pedalling rider
        // from being culled while any of it is in view.
        foreach (SkinnedMeshRenderer skin in rider.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Transform bone = skin.rootBone != null ? skin.rootBone : skin.transform;
            skin.localBounds = new Bounds(Vector3.zero, Vector3.one * (2f / Mathf.Max(0.0001f, bone.lossyScale.x)));
        }
        riderPosed = true;
        float crank = phase * Mathf.Rad2Deg;
        TurnCranks(crank);
        Pedal(leftLeg, crank);
        Pedal(rightLeg, crank + 180f);
    }

    private Transform Bone(Animator human, HumanBodyBones bone, string mixamoName)
    {
        Transform found = human != null ? human.GetBoneTransform(bone) : null;
        if (found != null) return found;
        foreach (Transform t in rider.GetComponentsInChildren<Transform>(true))
            if (t.name == mixamoName || t.name.EndsWith(":" + mixamoName, System.StringComparison.Ordinal)) return t;
        return null;
    }

    // Finds a leg's bones and its crank's pedal, and keeps the leg's current rotations as the pose every pedal solve starts from.
    // The spindle is the centre of the pedal body, or the outer end of an older pedal rod centred on the axle.
    private void BindLeg(Leg leg, Animator human, bool left, Transform crank)
    {
        string side = left ? "Left" : "Right";
        leg.Thigh = Bone(human, left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg, side + "UpLeg");
        leg.Shin = Bone(human, left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg, side + "Leg");
        leg.Foot = Bone(human, left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot, side + "Foot");
        leg.Toe = Bone(human, left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes, side + "ToeBase");
        leg.ToeTip = leg.Toe != null && leg.Toe.childCount > 0 ? leg.Toe.GetChild(0) : null;
        leg.Side = left ? -1f : 1f;
        leg.Crank = crank;
        if (leg.Thigh != null) leg.ThighSeat = leg.Thigh.localRotation;
        if (leg.Shin != null) leg.ShinSeat = leg.Shin.localRotation;
        if (leg.Foot != null) leg.FootSeat = leg.Foot.localRotation;
        leg.Pedal = crank != null ? crank.Find("Pedal") : null;
        leg.Spindle = Vector3.zero;
        leg.Tread = 0f;
        leg.Level = false;
        if (leg.Pedal == null) return;
        MeshFilter filter = leg.Pedal.GetComponent<MeshFilter>();
        float half = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds.extents.y * leg.Pedal.localScale.y : 0f;
        Vector3 local = leg.Pedal.localPosition;
        leg.Level = local.y * local.y + local.z * local.z > 0.0001f;
        leg.Spindle = leg.Level ? local : local + leg.Pedal.localRotation * Vector3.up * half;
        leg.Tread = leg.Level ? half : 0f;
    }

    private Arm BindArm(Animator human, bool left, Transform handlebar)
    {
        string side = left ? "Left" : "Right";
        var arm = new Arm
        {
            Upper = Bone(human, left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, side + "Arm"),
            Lower = Bone(human, left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, side + "ForeArm"),
            Hand = Bone(human, left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, side + "Hand"),
            Finger = Bone(human, left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal, side + "HandMiddle1"),
        };
        arm.HasGrip = TryGetGrip(handlebar, left ? -1f : 1f, out arm.Grip);
        return arm;
    }

    // Rolls the pelvis forward about the Hips bone until its up axis leans PelvisTilt from the bike's vertical; returns the turn.
    private Quaternion TiltPelvis(Transform hips)
    {
        if (hips == null) return Quaternion.identity;
        Quaternion roll = Quaternion.FromToRotation(hips.up, Lean(PelvisTilt));
        hips.rotation = roll * hips.rotation;
        return roll;
    }

    // Moves the whole rider so the Hips bone sits SeatLift above the middle of the saddle's top, read from the saddle's renderer bounds.
    private void SitOnSaddle(Transform hips)
    {
        Transform saddle = transform.Find("Bicycle/Saddle");
        Renderer seat = saddle != null ? saddle.GetComponent<Renderer>() : null;
        if (hips == null || seat == null) return;
        Bounds bounds = seat.bounds;
        rider.position += new Vector3(bounds.center.x, bounds.max.y, bounds.center.z) + transform.up * SeatLift - hips.position;
    }

    // Leans the torso about the lowest spine joint: the spine-to-neck line tilts forward of the bike's vertical by the least angle
    // in [MinLean, MaxLean] that brings each grip within ArmReach of that arm's length. Then the neck takes back HeadLift of the
    // whole forward roll (pelvis and spine), so the rider looks along the track rather than at the front wheel.
    private void LeanTorso(Transform spine, Transform neck, Quaternion pelvisRoll, Arm left, Arm right)
    {
        if (spine == null || neck == null) return;
        Vector3 torso = neck.position - spine.position;
        if (torso.sqrMagnitude < 0.000001f) return;
        float low = MinLean, high = MaxLean;
        if (Reaches(spine.position, torso, low, left, right)) high = low;
        else if (Reaches(spine.position, torso, high, left, right))
            for (int i = 0; i < 16; i++)
            {
                float mid = 0.5f * (low + high);
                if (Reaches(spine.position, torso, mid, left, right)) high = mid;
                else low = mid;
            }
        Quaternion lean = Quaternion.FromToRotation(torso, Lean(high));
        spine.rotation = lean * spine.rotation;
        neck.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(lean * pelvisRoll), HeadLift) * neck.rotation;
    }

    private Vector3 Lean(float degrees) => Quaternion.AngleAxis(degrees, transform.right) * transform.up;

    // Whether both shoulders, with the torso turned to the given lean, are close enough to their grips; an arm without a grip
    // never holds the lean back.
    private bool Reaches(Vector3 pivot, Vector3 torso, float lean, Arm left, Arm right)
    {
        Quaternion turn = Quaternion.FromToRotation(torso, Lean(lean));
        return Reaches(pivot, turn, left) && Reaches(pivot, turn, right);
    }

    private static bool Reaches(Vector3 pivot, Quaternion turn, Arm arm)
    {
        if (!arm.HasGrip || arm.Upper == null || arm.Lower == null || arm.Hand == null) return true;
        float length = Vector3.Distance(arm.Upper.position, arm.Lower.position) + Vector3.Distance(arm.Lower.position, arm.Hand.position);
        return Vector3.Distance(pivot + turn * (arm.Upper.position - pivot), arm.Grip) <= ArmReach * length;
    }

    // Arm solve: the wrist lands on the grip with the elbow bending down and out (or points at a grip out of reach), then the hand
    // bends forward over the bar.
    private void ReachForBar(Arm arm, float side)
    {
        if (arm.Upper == null || arm.Lower == null || arm.Hand == null) return;
        Vector3 target = arm.HasGrip ? arm.Grip : arm.Upper.position + transform.TransformDirection(new Vector3(side * 0.15f, -0.3f, 0.95f));
        Vector3 wrist = SolveLimb(arm.Upper, arm.Lower, arm.Hand, target, transform.right * side - transform.up);
        if (arm.Finger == null) return;
        Vector3 along = (wrist - arm.Lower.position).normalized;
        arm.Hand.rotation = Quaternion.FromToRotation(arm.Finger.position - arm.Hand.position, Vector3.Slerp(along, -transform.up, WristWrap)) * arm.Hand.rotation;
    }

    // Keeps a foot on its pedal: the pedal body stays level with the bike apart from a toe-down pitch that grows toward the bottom
    // of the stroke, the ball of the foot rests on the pedal's top face with the toes along the pitched pedal, and the thigh and
    // shin are solved with the knee forward and a little out.
    private void Pedal(Leg leg, float crankAngle)
    {
        if (leg.Thigh == null || leg.Shin == null || leg.Foot == null || leg.Crank == null) return;
        float pitch = Mathf.Lerp(ToeDownTop, ToeDownBottom, 0.5f - 0.5f * Mathf.Cos(crankAngle * Mathf.Deg2Rad));
        Quaternion tread = Quaternion.AngleAxis(pitch, transform.right) * transform.rotation;
        if (leg.Level && leg.Pedal != null) leg.Pedal.rotation = tread;
        leg.Thigh.localRotation = leg.ThighSeat;
        leg.Shin.localRotation = leg.ShinSeat;
        leg.Foot.localRotation = leg.FootSeat;
        Vector3 up = tread * Vector3.up, ankle = leg.Crank.TransformPoint(leg.Spindle) + up * leg.Tread;
        Quaternion foot = leg.Foot.rotation;
        if (leg.Toe != null && leg.ToeTip != null)
        {
            Vector3 footLine = leg.Toe.position - leg.Foot.position;
            Quaternion turn = Quaternion.FromToRotation(leg.ToeTip.position - leg.Toe.position, tread * Vector3.forward);
            foot = turn * foot;
            ankle += up * (SoleShare * footLine.magnitude) - turn * footLine;
        }
        SolveLimb(leg.Thigh, leg.Shin, leg.Foot, ankle, transform.forward + transform.right * (leg.Side * KneeOut), true);
        leg.Foot.rotation = foot;
    }

    // Two-bone solve: turns the upper and lower bones so the end joint lands on the target with the middle joint bending toward
    // the pole, or, when the target is out of reach, points the limb at it with the middle joint just short of straight.
    // With squareHinge the upper bone also turns about its own length until its local X axis (the knee hinge of the Mixamo
    // rig) stands square to the bend, so the knee folds as a hinge instead of twisting. Returns where the end joint lands.
    private static Vector3 SolveLimb(Transform upperBone, Transform lowerBone, Transform end, Vector3 target, Vector3 pole, bool squareHinge = false)
    {
        Vector3 root = upperBone.position, toTarget = target - root;
        float upper = Vector3.Distance(root, lowerBone.position), lower = Vector3.Distance(lowerBone.position, end.position);
        if (upper < 0.0001f || lower < 0.0001f || toTarget.sqrMagnitude < 0.000001f) return end.position;
        Vector3 axis = toTarget.normalized;
        float reach = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upper - lower) + 0.001f, (upper + lower) * 0.999f);
        float cos = Mathf.Clamp((upper * upper + reach * reach - lower * lower) / (2f * upper * reach), -1f, 1f);
        Vector3 bend = Vector3.ProjectOnPlane(pole, axis).normalized;
        Vector3 middle = root + upper * (cos * axis + Mathf.Sqrt(1f - cos * cos) * bend);
        upperBone.rotation = Quaternion.FromToRotation(lowerBone.position - root, middle - root) * upperBone.rotation;
        if (squareHinge)
        {
            Vector3 hinge = Vector3.ProjectOnPlane(upperBone.right, middle - root), normal = Vector3.Cross(axis, bend);
            upperBone.rotation = Quaternion.FromToRotation(hinge, Vector3.Dot(hinge, normal) < 0f ? -normal : normal) * upperBone.rotation;
        }
        Vector3 tip = root + axis * reach;
        lowerBone.rotation = Quaternion.FromToRotation(end.position - lowerBone.position, tip - lowerBone.position) * lowerBone.rotation;
        return tip;
    }

    // The grip is read from the handlebar the generator built: GripSpan of the way from the centre of its mesh to the end of
    // its longest axis on this side.
    private bool TryGetGrip(Transform handlebar, float side, out Vector3 grip)
    {
        grip = Vector3.zero;
        MeshFilter filter = handlebar != null ? handlebar.GetComponent<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null) return false;
        Bounds bounds = filter.sharedMesh.bounds;
        Vector3 halfBar = Vector3.zero;
        for (int k = 0; k < 3; k++)
        {
            Vector3 half = Vector3.zero;
            half[k] = bounds.extents[k];
            half = handlebar.TransformVector(half);
            if (half.sqrMagnitude > halfBar.sqrMagnitude) halfBar = half;
        }
        if (Vector3.Dot(halfBar, transform.right) * side < 0f) halfBar = -halfBar;
        grip = handlebar.TransformPoint(bounds.center) + halfBar * GripSpan;
        return true;
    }

    // Wheels roll about their axles (the bike's X axis); the rolling radius is the axle height above the ground.
    private void CacheWheels()
    {
        frontWheel = transform.Find("Bicycle/Front Alloy Wheel");
        rearWheel = transform.Find("Bicycle/Rear Alloy Wheel");
        if (frontWheel != null) frontWheelRest = frontWheel.localRotation;
        if (rearWheel != null) rearWheelRest = rearWheel.localRotation;
        Transform wheel = frontWheel != null ? frontWheel : rearWheel;
        if (wheel != null) wheelRadius = Mathf.Max(0.1f, Vector3.Dot(wheel.position - transform.position, transform.up));
        // The tyre's outer radius comes from its mesh, not from renderer bounds, which grow as the wheel spins.
        MeshFilter tyre = wheel != null ? wheel.GetComponent<MeshFilter>() : null;
        if (tyre != null && tyre.sharedMesh != null)
        {
            Vector3 extents = tyre.sharedMesh.bounds.extents;
            float tyreRadius = Mathf.Max(extents.x, extents.y, extents.z) * Mathf.Abs(wheel.lossyScale.y);
            tyreBottomBelowRoot = tyreRadius - wheelRadius;
            wheelRadius = tyreRadius;
        }
        KeepTyresOnSurface(0f);
    }

    // The route runs at the flat waypoint height, but the cycle track and kerb ramps are raised, so put the tyres on
    // whatever surface is under the bike (ignoring the cyclist itself and character capsules).
    private void KeepTyresOnSurface(float dt)
    {
        Vector3 position = transform.position;
        int count = Physics.RaycastNonAlloc(position + Vector3.up, Vector3.down, GroundHits, 2.5f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float ground = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider hit = GroundHits[i].collider;
            if (hit == null || hit.transform.IsChildOf(transform) || hit is CharacterController) continue;
            ground = Mathf.Max(ground, GroundHits[i].point.y);
        }
        if (float.IsNegativeInfinity(ground)) return;
        float target = ground + tyreBottomBelowRoot;
        position.y = dt > 0f ? Mathf.Lerp(position.y, target, 1f - Mathf.Exp(-12f * dt)) : target;
        transform.position = position;
    }
}
