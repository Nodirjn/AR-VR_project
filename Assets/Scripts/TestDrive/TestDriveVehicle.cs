using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The drivable car: a rigid body on four wheel colliders with an engine (torque curve, automatic gearbox, rev limiter)
/// or an electric motor, brakes with a simple ABS, traction control, anti-roll bars and air drag. It also moves the
/// visible wheels, the steering wheel and the driver's door. Built by <see cref="TestDriveCarBuilder"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class TestDriveVehicle : MonoBehaviour
{
    public enum Selector { P, R, N, D }

    public sealed class WheelState
    {
        public WheelCollider collider;
        public TestDriveWheelRig.Wheel rig;
        public bool front, left, driven;
        public float spinAngle;
        public float baseSideStiffness;
    }

    // ------------------------------------------------------------------ set by the builder
    public TestDriveCarProfile Profile;
    public Rigidbody Body;
    public readonly List<WheelState> Wheels = new List<WheelState>(4);
    public Transform EyeAnchor;
    public Transform HudAnchor;
    public Transform HandlePoint;
    public Transform ExitPoint;
    public Transform DoorHinge;
    public float DoorOpenAngle = 66f;
    public Transform SteeringPivot;
    public float SteeringRadius = 0.18f;
    public float SeatCushion = float.NaN;
    public float HalfWidth = 0.9f, HalfLength = 2.3f, Height = 1.4f;
    public float WheelBase = 2.7f, Track = 1.55f, WheelRadius = 0.32f;
    public float SuspensionDistance = 0.16f;
    public Light[] Headlights = new Light[0];
    public Light CabinLight;
    public TestDriveAudio Audio;
    public TestDriveTraffic.Mover TrafficMover;
    public TestDriveDriver VisitorBody;

    // ------------------------------------------------------------------ driver input
    private float acceleratorPedal, brakePedalRaw;   // what the driver presses
    private float throttleInput, brakeInput, steerInput; // what the drivetrain gets (swapped in reverse)
    private float reverseHold;
    private bool handbrakeInput;
    private bool hornInput;

    // ------------------------------------------------------------------ state
    private Selector selector = Selector.P;
    private int gear;
    private float rpm;
    private float shiftTimer;
    private float starterTimer;
    private bool engineRunning;
    private bool limiterCut;
    private float steerAngle;
    private float forwardSpeed;
    private float throttleApplied;
    private float maxSlip;
    private float doorT, doorTarget;
    private Quaternion doorClosedRotation = Quaternion.identity;
    private Quaternion steeringRestRotation = Quaternion.identity;
    private bool lightsOn;
    private float shiftFlash;

    public Selector CurrentSelector => selector;
    public int Gear => gear;
    public float Rpm => rpm;
    public float SpeedKmh => Mathf.Abs(forwardSpeed) * 3.6f;
    public float ForwardSpeed => forwardSpeed;
    public bool EngineRunning => engineRunning;
    public bool EngineStarting => starterTimer > 0f;
    public float Throttle => throttleApplied;
    public float BrakePedal => brakeInput;
    /// <summary>Selector letter alone (P, R, N or D), for the instrument cluster.</summary>
    public string SelectorLetter => selector.ToString();
    public bool Handbrake => handbrakeInput || selector == Selector.P;
    public float SteerAngle => steerAngle;
    public bool LightsOn => lightsOn;
    public float MaxSlip => maxSlip;
    public bool DoorOpen => doorT > 0.98f;
    public bool DoorClosed => doorT < 0.02f && doorTarget <= 0f;
    public float DoorAmount => doorT;
    public bool ShiftedRecently => shiftFlash > 0f;

    public string GearLabel
    {
        get
        {
            switch (selector)
            {
                case Selector.P: return "P";
                case Selector.R: return "R";
                case Selector.N: return "N";
                default: return Profile != null && !Profile.IsElectric ? "D" + (gear + 1) : "D";
            }
        }
    }

    private void Awake()
    {
        if (DoorHinge != null) doorClosedRotation = DoorHinge.localRotation;
        if (SteeringPivot != null) steeringRestRotation = SteeringPivot.localRotation;
    }

    /// <summary>Called by the builder once every reference is set.</summary>
    public void Initialise()
    {
        if (DoorHinge != null) doorClosedRotation = DoorHinge.localRotation;
        if (SteeringPivot != null) steeringRestRotation = SteeringPivot.localRotation;
        for (int i = 0; i < Wheels.Count; i++)
            Wheels[i].baseSideStiffness = Wheels[i].collider.sidewaysFriction.stiffness;
    }

    private void OnEnable()
    {
        if (TrafficMover != null) TestDriveTraffic.Register(TrafficMover);
        // Finer wheel sub-steps below 5 m/s keep the car from creeping or jittering at a standstill.
        if (Wheels.Count > 0 && Wheels[0].collider != null) Wheels[0].collider.ConfigureVehicleSubsteps(5f, 12, 15);
    }

    private void OnDisable()
    {
        if (TrafficMover != null) TestDriveTraffic.Unregister(TrafficMover);
    }

    // ------------------------------------------------------------------ commands

    public void SetDriverInput(float throttle, float brake, float steer, bool handbrake, bool horn)
    {
        acceleratorPedal = Mathf.Clamp01(throttle);
        brakePedalRaw = Mathf.Clamp01(brake);
        steerInput = Mathf.Clamp(steer, -1f, 1f);
        handbrakeInput = handbrake;
        hornInput = horn;
    }

    public void ClearDriverInput()
    {
        acceleratorPedal = brakePedalRaw = throttleInput = brakeInput = steerInput = 0f;
        reverseHold = 0f;
        handbrakeInput = hornInput = false;
    }

    /// <summary>Puts the car at rest at a pose (wheels on the ground, engine off, in P).</summary>
    public void Park(Vector3 position, Quaternion rotation)
    {
        Body.isKinematic = true;
        transform.SetPositionAndRotation(position, rotation);
        Body.position = position;
        Body.rotation = rotation;
        Body.isKinematic = false;
        Body.linearVelocity = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
        selector = Selector.P;
        gear = 0;
        rpm = 0f;
        engineRunning = false;
        starterTimer = 0f;
        steerAngle = 0f;
        ClearDriverInput();
        SetDoor(false, true);
        SetLights(false);
        Physics.SyncTransforms();
    }

    public void StartEngine()
    {
        if (engineRunning || starterTimer > 0f) return;
        starterTimer = Profile.IsElectric ? 0.35f : 0.95f;
        if (Audio != null) Audio.PlayStart(Profile.IsElectric);
    }

    public void StopEngine()
    {
        engineRunning = false;
        starterTimer = 0f;
        if (Audio != null) Audio.PlayStop(Profile.IsElectric);
    }

    /// <summary>D and R swap only when the car is (nearly) standing; from P or N the first press selects D.</summary>
    public bool ToggleDirection()
    {
        if (Mathf.Abs(forwardSpeed) > 1.5f) return false;
        selector = selector == Selector.D ? Selector.R : Selector.D;
        gear = 0;
        shiftFlash = 0.6f;
        if (Audio != null) Audio.PlayClick();
        return true;
    }

    public void SetSelector(Selector value)
    {
        if (selector == value) return;
        selector = value;
        gear = 0;
        shiftFlash = 0.6f;
        if (Audio != null) Audio.PlayClick();
    }

    public void SetDoor(bool open, bool instant = false)
    {
        float target = open ? 1f : 0f;
        if (DoorHinge == null)
        {
            doorT = doorTarget = target;
            return;
        }
        if (!instant && !Mathf.Approximately(target, doorTarget) && Audio != null)
        {
            if (open) Audio.PlayDoorOpen();
            else Audio.PlayDoorCloseSoon(0.75f);
        }
        doorTarget = target;
        if (instant)
        {
            doorT = target;
            ApplyDoor();
        }
    }

    public void ToggleLights() => SetLights(!lightsOn);

    public void SetLights(bool on)
    {
        lightsOn = on;
        for (int i = 0; i < Headlights.Length; i++)
            if (Headlights[i] != null) Headlights[i].enabled = on;
    }

    // ------------------------------------------------------------------ simulation

    private void FixedUpdate()
    {
        if (Body == null || Wheels.Count == 0 || Body.isKinematic) return;
        float dt = Time.fixedDeltaTime;
        Vector3 velocity = Body.linearVelocity;
        forwardSpeed = Vector3.Dot(velocity, transform.forward);
        float kmh = Mathf.Abs(forwardSpeed) * 3.6f;

        MapPedals(dt);
        UpdateEngineState(dt);
        Steer(kmh, dt);
        Drive(kmh, dt);
        Brake(kmh);
        AntiRoll();
        Aero(velocity);
    }

    /// <summary>
    /// Arcade-style direction change, as in most driving games: holding the brake with the car standing selects R and
    /// the brake trigger then drives backwards (the accelerator brakes); the accelerator at a standstill selects D again.
    /// The gear button still toggles D and R directly.
    /// </summary>
    private void MapPedals(float dt)
    {
        bool standing = Mathf.Abs(forwardSpeed) < 0.6f;
        if (selector == Selector.R)
        {
            throttleInput = brakePedalRaw;
            brakeInput = acceleratorPedal;
            reverseHold = 0f;
            if (engineRunning && standing && acceleratorPedal > 0.3f && brakePedalRaw < 0.1f)
            {
                SetSelector(Selector.D);
                throttleInput = acceleratorPedal;
                brakeInput = 0f;
            }
            return;
        }
        throttleInput = acceleratorPedal;
        brakeInput = brakePedalRaw;
        if (engineRunning && standing && brakePedalRaw > 0.35f && acceleratorPedal < 0.05f)
        {
            reverseHold += dt;
            if (reverseHold > 0.45f)
            {
                SetSelector(Selector.R);
                reverseHold = 0f;
                throttleInput = brakePedalRaw;
                brakeInput = 0f;
            }
        }
        else reverseHold = 0f;
    }

    private void UpdateEngineState(float dt)
    {
        if (starterTimer > 0f)
        {
            starterTimer -= dt;
            if (starterTimer <= 0f)
            {
                engineRunning = true;
                rpm = Profile.IsElectric ? 0f : Profile.IdleRpm * 1.5f;
            }
        }
        // Pressing the accelerator in P or N with the engine running selects D, like a modern automatic's
        // "shift by wire" when the driver's foot is on the brake: here the brake is not required.
        if (engineRunning && (selector == Selector.P || selector == Selector.N) && throttleInput > 0.25f && brakeInput < 0.2f)
            SetSelector(Selector.D);
        if (shiftTimer > 0f) shiftTimer -= dt;
        if (shiftFlash > 0f) shiftFlash -= dt;
    }

    private void Steer(float kmh, float dt)
    {
        float maxAngle = Mathf.Lerp(Profile.MaxSteerAngle, 6.5f, Mathf.InverseLerp(12f, 170f, kmh));
        float target = steerInput * maxAngle;
        float rate = Mathf.Lerp(240f, 80f, Mathf.InverseLerp(0f, 140f, kmh));
        // Self-centring: the wheel comes back faster than the driver can turn it.
        if (Mathf.Abs(target) < Mathf.Abs(steerAngle)) rate *= 1.4f;
        steerAngle = Mathf.MoveTowards(steerAngle, target, rate * dt);

        float angle = Mathf.Abs(steerAngle);
        float inner = steerAngle, outer = steerAngle;
        if (angle > 0.1f && WheelBase > 0.5f)
        {
            float radius = WheelBase / Mathf.Tan(angle * Mathf.Deg2Rad);
            float innerAngle = Mathf.Atan(WheelBase / Mathf.Max(0.5f, radius - Track * 0.5f)) * Mathf.Rad2Deg;
            float outerAngle = Mathf.Atan(WheelBase / (radius + Track * 0.5f)) * Mathf.Rad2Deg;
            float sign = Mathf.Sign(steerAngle);
            inner = sign * innerAngle;
            outer = sign * outerAngle;
        }
        for (int i = 0; i < Wheels.Count; i++)
        {
            WheelState w = Wheels[i];
            if (!w.front) { w.collider.steerAngle = 0f; continue; }
            // Turning right (positive) the right wheel is the inner one.
            bool isInner = (steerAngle > 0f && !w.left) || (steerAngle < 0f && w.left);
            w.collider.steerAngle = isInner ? inner : outer;
        }
    }

    private float RatioForGear()
    {
        if (selector == Selector.R) return -Profile.ReverseGear * Profile.FinalDrive;
        if (selector != Selector.D) return 0f;
        return Profile.Gears[Mathf.Clamp(gear, 0, Profile.Gears.Length - 1)] * Profile.FinalDrive;
    }

    private void Drive(float kmh, float dt)
    {
        int driven = 0;
        float drivenRpm = 0f;
        for (int i = 0; i < Wheels.Count; i++)
        {
            if (!Wheels[i].driven) continue;
            driven++;
            drivenRpm += Wheels[i].collider.rpm;
        }
        if (driven == 0) return;
        drivenRpm /= driven;

        float ratio = RatioForGear();
        float wheelRpm = Mathf.Abs(drivenRpm * ratio);
        bool inGear = ratio != 0f && engineRunning;
        float redline = Profile.RedlineRpm;
        float idle = Profile.IdleRpm;

        // Creep: an automatic in D or R pulls gently with the foot off both pedals.
        float throttle = throttleInput;
        if (inGear && throttle < 0.08f && brakeInput < 0.05f && kmh < 7f) throttle = Mathf.Max(throttle, Profile.IsElectric ? 0.05f : 0.07f);

        // Rev limiter with hysteresis.
        if (rpm >= redline) limiterCut = true;
        else if (rpm < redline - 250f) limiterCut = false;
        if (limiterCut || shiftTimer > 0f) throttle = 0f;
        if (forwardSpeed * 3.6f > Profile.TopSpeedKmh && selector == Selector.D) throttle = 0f;
        throttleApplied = throttleInput;

        // Engine speed: the wheels set it once the clutch (torque converter) has locked; below that it slips
        // up to a launch speed that grows with the pedal.
        float target;
        if (!engineRunning) target = 0f;
        else if (!inGear) target = Profile.IsElectric ? 0f : Mathf.Lerp(idle, redline * 0.92f, throttleInput);
        else if (Profile.IsElectric) target = wheelRpm;
        else target = Mathf.Max(idle, Mathf.Max(wheelRpm, Mathf.Lerp(idle * 1.05f, Mathf.Min(4300f, redline * 0.6f), throttleInput)));
        float response = inGear ? 16f : 7f;
        rpm = Mathf.Lerp(rpm, target, 1f - Mathf.Exp(-response * dt));
        if (!engineRunning && rpm < 60f) rpm = 0f;

        // Automatic gearbox (D only, petrol): up at a speed that rises with the pedal, down when the engine bogs.
        if (inGear && selector == Selector.D && !Profile.IsElectric && shiftTimer <= 0f)
        {
            float upRpm = Mathf.Max(2300f, Mathf.Lerp(0.5f, 0.94f, throttleInput) * redline);
            float downRpm = Mathf.Lerp(0.27f, 0.56f, throttleInput) * redline;
            if (gear < Profile.Gears.Length - 1 && wheelRpm > upRpm)
            {
                gear++;
                shiftTimer = Profile.ShiftTime;
                shiftFlash = 0.5f;
                if (Audio != null) Audio.PlayShift();
            }
            else if (gear > 0 && wheelRpm < downRpm)
            {
                float lower = wheelRpm * Profile.Gears[gear - 1] / Profile.Gears[gear];
                if (lower < redline * 0.9f)
                {
                    gear--;
                    shiftTimer = Profile.ShiftTime * 0.6f;
                    shiftFlash = 0.5f;
                }
            }
        }
        if (Mathf.Abs(forwardSpeed) < 0.5f && selector == Selector.D) gear = 0;

        float engineTorque = 0f;
        if (inGear)
        {
            engineTorque = Profile.FullLoadTorque(Mathf.Max(rpm, Profile.IsElectric ? 300f : idle)) * throttle;
            if (throttleInput < 0.03f)
            {
                // Engine braking, or regeneration for the electric motor.
                if (Profile.IsElectric) engineTorque -= 0.22f * Profile.MaxTorque * Mathf.Clamp01(kmh / 18f);
                else engineTorque -= 0.16f * Profile.MaxTorque * Mathf.Clamp01((rpm - idle) / Mathf.Max(1f, redline - idle));
            }
        }
        // Reverse is limited to walking-to-jogging pace.
        if (selector == Selector.R && forwardSpeed < -6f) engineTorque = Mathf.Min(engineTorque, 0f);
        float wheelTorque = engineTorque * ratio * 0.93f;
        float perWheel = wheelTorque / driven;
        maxSlip = 0f;
        for (int i = 0; i < Wheels.Count; i++)
        {
            WheelState w = Wheels[i];
            WheelHit hit;
            bool grounded = w.collider.GetGroundHit(out hit);
            if (grounded) maxSlip = Mathf.Max(maxSlip, Mathf.Max(Mathf.Abs(hit.forwardSlip) * 0.8f, Mathf.Abs(hit.sidewaysSlip)));
            if (!w.driven) { w.collider.motorTorque = 0f; continue; }
            float torque = perWheel;
            // Traction control: cut drive on a wheel that spins up.
            if (grounded && torque * Mathf.Sign(ratio) > 0f)
            {
                float slip = Mathf.Abs(hit.forwardSlip);
                if (slip > 0.62f) torque *= Mathf.Clamp(1f - (slip - 0.62f) * 1.6f, 0.35f, 1f);
            }
            w.collider.motorTorque = torque;
        }
    }

    private void Brake(float kmh)
    {
        float pedal = brakeInput;
        bool standing = Mathf.Abs(forwardSpeed) < 0.35f;
        bool hold = standing && throttleInput < 0.05f && (pedal > 0.05f || selector == Selector.P || selector == Selector.N || !engineRunning);
        for (int i = 0; i < Wheels.Count; i++)
        {
            WheelState w = Wheels[i];
            float torque = Profile.BrakeTorque * pedal * (w.front ? 1f : 0.62f);
            WheelHit hit;
            // ABS: release a wheel that is about to lock.
            if (torque > 0f && kmh > 8f && w.collider.GetGroundHit(out hit) && Mathf.Abs(hit.forwardSlip) > 0.42f) torque *= 0.35f;
            if (!w.front && (handbrakeInput || selector == Selector.P)) torque = Mathf.Max(torque, Profile.BrakeTorque * 1.1f);
            if (hold) torque = Mathf.Max(torque, Profile.BrakeTorque * 0.5f);
            w.collider.brakeTorque = torque;

            // The handbrake lets the rear slide.
            if (!w.front)
            {
                WheelFrictionCurve side = w.collider.sidewaysFriction;
                float targetStiffness = handbrakeInput && kmh > 10f ? w.baseSideStiffness * 0.55f : w.baseSideStiffness;
                if (!Mathf.Approximately(side.stiffness, targetStiffness))
                {
                    side.stiffness = Mathf.MoveTowards(side.stiffness, targetStiffness, 0.08f);
                    w.collider.sidewaysFriction = side;
                }
            }
        }
    }

    private void AntiRoll()
    {
        float scale = Profile.Mass / 1500f;
        ApplyAntiRoll(true, 7200f * scale);
        ApplyAntiRoll(false, 5200f * scale);
    }

    private void ApplyAntiRoll(bool front, float stiffness)
    {
        WheelState left = null, right = null;
        for (int i = 0; i < Wheels.Count; i++)
        {
            if (Wheels[i].front != front) continue;
            if (Wheels[i].left) left = Wheels[i];
            else right = Wheels[i];
        }
        if (left == null || right == null) return;
        WheelHit hit;
        float travelLeft = 1f, travelRight = 1f;
        bool groundedLeft = left.collider.GetGroundHit(out hit);
        if (groundedLeft)
            travelLeft = (-left.collider.transform.InverseTransformPoint(hit.point).y - left.collider.radius) / left.collider.suspensionDistance;
        bool groundedRight = right.collider.GetGroundHit(out hit);
        if (groundedRight)
            travelRight = (-right.collider.transform.InverseTransformPoint(hit.point).y - right.collider.radius) / right.collider.suspensionDistance;
        float force = (travelLeft - travelRight) * stiffness;
        if (groundedLeft) Body.AddForceAtPosition(left.collider.transform.up * -force, left.collider.transform.position);
        if (groundedRight) Body.AddForceAtPosition(right.collider.transform.up * force, right.collider.transform.position);
    }

    private void Aero(Vector3 velocity)
    {
        float speed = velocity.magnitude;
        if (speed < 0.05f) return;
        Body.AddForce(-velocity * (0.5f * 1.225f * Profile.DragArea * speed));
        // A little downforce keeps the car planted at speed.
        Body.AddForce(-transform.up * (0.11f * speed * speed));
        // Rolling resistance.
        if (speed > 0.4f) Body.AddForce(-velocity / speed * (0.011f * Profile.Mass * 9.81f));
    }

    // ------------------------------------------------------------------ visuals

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < Wheels.Count; i++)
        {
            WheelState w = Wheels[i];
            if (w.rig == null || w.rig.steer == null) continue;
            Vector3 position;
            Quaternion rotation;
            w.collider.GetWorldPose(out position, out rotation);
            w.rig.steer.localPosition = transform.InverseTransformPoint(position);
            w.rig.steer.localRotation = Quaternion.AngleAxis(w.collider.steerAngle - w.rig.restSteer, Vector3.up);
            w.spinAngle = Mathf.Repeat(w.spinAngle + w.collider.rpm * 6f * dt, 360f);
            if (w.rig.spin != null) w.rig.spin.localRotation = Quaternion.AngleAxis(w.spinAngle, w.rig.axle);
        }
        if (SteeringPivot != null)
            SteeringPivot.localRotation = steeringRestRotation * Quaternion.Euler(0f, 0f, -steerAngle * Profile.SteeringRatio);

        if (DoorHinge != null && !Mathf.Approximately(doorT, doorTarget))
        {
            doorT = Mathf.MoveTowards(doorT, doorTarget, dt / (doorTarget > doorT ? 0.85f : 0.6f));
            ApplyDoor();
        }

        if (Audio != null)
            Audio.SetState(Profile, engineRunning, starterTimer > 0f, rpm, throttleApplied, Mathf.Abs(forwardSpeed), maxSlip, hornInput);
    }

    private void ApplyDoor()
    {
        if (DoorHinge == null) return;
        // Ease in and out; the closing door accelerates into the latch.
        float t = doorTarget > 0f ? Mathf.SmoothStep(0f, 1f, doorT) : doorT * doorT * (3f - 2f * doorT);
        DoorHinge.localRotation = doorClosedRotation * Quaternion.AngleAxis(DoorOpenAngle * t, Vector3.up);
    }
}
