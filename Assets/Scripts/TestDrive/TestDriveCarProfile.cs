using UnityEngine;

/// <summary>
/// Real-world data for each showroom car the test drive can build: dimensions, mass, drivetrain and the numbers the
/// kiosk shows. The display is matched by name, so a renamed display falls back to <see cref="Generic"/>.
/// </summary>
public sealed class TestDriveCarProfile
{
    public enum Powertrain { Petrol, Electric }

    public string Key;
    public string DisplayName;
    public string Tagline;
    public Powertrain Engine = Powertrain.Petrol;

    // Body.
    public float Length = 4.6f;          // m, the clone is scaled to this when its own size is implausible
    public float Mass = 1500f;           // kg
    public float CentreOfMassHeight = 0.48f;
    public float WeightFront = 0.52f;    // share of the weight on the front axle
    public float DragArea = 0.68f;       // Cd * frontal area, m^2
    public float DoorLength = 1.08f;     // driver's door, front to rear edge (used when the door is cut from the body)

    // Engine (petrol) or motor (electric).
    public int Cylinders = 6;
    public float IdleRpm = 850f;
    public float PeakTorqueRpm = 4500f;
    public float RedlineRpm = 7000f;
    public float MaxTorque = 400f;       // Nm at the crank (or motor shaft)
    public float MaxPower = 200f;        // kW
    public float[] Gears = { 3.5f, 2.1f, 1.4f, 1.0f, 0.8f };
    public float ReverseGear = 3.4f;
    public float FinalDrive = 3.6f;
    public float TopSpeedKmh = 240f;     // electronic limit
    public float ShiftTime = 0.28f;

    // Chassis.
    public float MaxSteerAngle = 34f;    // road-wheel angle at walking pace
    public float SteeringRatio = 15f;    // steering-wheel degrees per road-wheel degree
    public float BrakeTorque = 3600f;    // Nm per front wheel at full pedal
    public float RideFrequency = 1.55f;  // Hz

    // Kiosk figures.
    public string PowerText;
    public string SprintText;
    public string TopSpeedText;

    public bool IsElectric => Engine == Powertrain.Electric;

    public static TestDriveCarProfile ForDisplay(string displayName)
    {
        string n = displayName ?? string.Empty;
        if (n.IndexOf("E30", System.StringComparison.OrdinalIgnoreCase) >= 0) return E30();
        if (n.IndexOf("M4", System.StringComparison.OrdinalIgnoreCase) >= 0) return M4();
        if (n.IndexOf("Tesla", System.StringComparison.OrdinalIgnoreCase) >= 0) return Model3();
        if (n.IndexOf("Concept", System.StringComparison.OrdinalIgnoreCase) >= 0) return Concept();
        TestDriveCarProfile generic = Generic();
        generic.DisplayName = n.Replace(" Display", string.Empty);
        return generic;
    }

    // BMW M3 E30 (1988): 2.3 l S14 four, 147 kW, Getrag 265 close-ratio five-speed, 1,200 kg.
    private static TestDriveCarProfile E30()
    {
        return new TestDriveCarProfile
        {
            Key = "E30",
            DisplayName = "BMW M3 E30",
            Tagline = "1988 · 2.3 l four · rear-wheel drive",
            Length = 4.345f,
            Mass = 1200f,
            CentreOfMassHeight = 0.46f,
            WeightFront = 0.51f,
            DragArea = 0.72f,
            DoorLength = 1.1f,
            Cylinders = 4,
            IdleRpm = 900f,
            PeakTorqueRpm = 4750f,
            RedlineRpm = 7250f,
            MaxTorque = 240f,
            MaxPower = 147f,
            Gears = new[] { 3.72f, 2.40f, 1.77f, 1.26f, 1.00f },
            ReverseGear = 4.23f,
            FinalDrive = 4.10f,
            TopSpeedKmh = 235f,
            ShiftTime = 0.32f,
            MaxSteerAngle = 35f,
            SteeringRatio = 19.6f,
            BrakeTorque = 2600f,
            RideFrequency = 1.6f,
            PowerText = "200 hp",
            SprintText = "6.7 s",
            TopSpeedText = "235 km/h"
        };
    }

    // The showroom's hero car (Unity's open "Car Concept", CC0): a central-seat hybrid grand tourer. Figures in the
    // range of today's V6 hybrid supercars: 3.0 l twin-turbo V6 with an electric motor, 485 kW / 800 Nm, eight speeds.
    private static TestDriveCarProfile Concept()
    {
        return new TestDriveCarProfile
        {
            Key = "Concept",
            DisplayName = "Aurelle GT Concept",
            Tagline = "Concept · 3.0 l twin-turbo V6 hybrid · rear-wheel drive",
            Length = 4.45f,
            Mass = 1580f,
            CentreOfMassHeight = 0.42f,
            WeightFront = 0.44f,
            DragArea = 0.62f,
            DoorLength = 1.25f,
            Cylinders = 6,
            IdleRpm = 800f,
            PeakTorqueRpm = 3000f,
            RedlineRpm = 8000f,
            MaxTorque = 800f,
            MaxPower = 485f,
            Gears = new[] { 4.80f, 3.10f, 2.20f, 1.70f, 1.32f, 1.05f, 0.85f, 0.68f },
            ReverseGear = 3.5f,
            FinalDrive = 3.3f,
            TopSpeedKmh = 320f,
            ShiftTime = 0.1f,
            MaxSteerAngle = 33f,
            SteeringRatio = 13.5f,
            BrakeTorque = 5200f,
            RideFrequency = 1.9f,
            PowerText = "660 hp",
            SprintText = "2.9 s",
            TopSpeedText = "320 km/h"
        };
    }

    // BMW M4 Competition (G82): 3.0 l S58 twin-turbo six, 375 kW / 650 Nm, eight-speed M Steptronic, 1,725 kg.
    private static TestDriveCarProfile M4()
    {
        return new TestDriveCarProfile
        {
            Key = "M4",
            DisplayName = "BMW M4 Competition",
            Tagline = "2021 · 3.0 l twin-turbo six · rear-wheel drive",
            Length = 4.794f,
            Mass = 1725f,
            CentreOfMassHeight = 0.47f,
            WeightFront = 0.53f,
            DragArea = 0.75f,
            DoorLength = 1.22f,
            Cylinders = 6,
            IdleRpm = 750f,
            PeakTorqueRpm = 2750f,
            RedlineRpm = 7200f,
            MaxTorque = 650f,
            MaxPower = 375f,
            Gears = new[] { 5.00f, 3.20f, 2.14f, 1.72f, 1.31f, 1.00f, 0.82f, 0.64f },
            ReverseGear = 3.46f,
            FinalDrive = 3.15f,
            TopSpeedKmh = 250f,
            ShiftTime = 0.14f,
            MaxSteerAngle = 34f,
            SteeringRatio = 14.6f,
            BrakeTorque = 4400f,
            RideFrequency = 1.75f,
            PowerText = "510 hp",
            SprintText = "3.9 s",
            TopSpeedText = "250 km/h"
        };
    }

    // Tesla Model 3 Long Range RWD (2018): 211 kW permanent-magnet motor, single 9:1 reduction, 1,730 kg.
    private static TestDriveCarProfile Model3()
    {
        return new TestDriveCarProfile
        {
            Key = "Tesla",
            DisplayName = "Tesla Model 3",
            Tagline = "2018 · 211 kW electric motor · rear-wheel drive",
            Engine = Powertrain.Electric,
            Length = 4.694f,
            Mass = 1730f,
            CentreOfMassHeight = 0.42f,
            WeightFront = 0.47f,
            DragArea = 0.51f,
            Cylinders = 0,
            IdleRpm = 0f,
            PeakTorqueRpm = 4800f,
            RedlineRpm = 17000f,
            MaxTorque = 420f,
            MaxPower = 211f,
            Gears = new[] { 9.0f },
            ReverseGear = 9.0f,
            FinalDrive = 1f,
            TopSpeedKmh = 225f,
            ShiftTime = 0f,
            MaxSteerAngle = 34f,
            SteeringRatio = 10.3f,
            BrakeTorque = 4000f,
            RideFrequency = 1.6f,
            PowerText = "283 hp",
            SprintText = "5.6 s",
            TopSpeedText = "225 km/h"
        };
    }

    private static TestDriveCarProfile Generic()
    {
        return new TestDriveCarProfile
        {
            Key = "Generic",
            DisplayName = "Showroom car",
            Tagline = "Rear-wheel drive",
            PowerText = "250 hp",
            SprintText = "6.0 s",
            TopSpeedText = "240 km/h",
            MaxTorque = 380f,
            MaxPower = 185f
        };
    }

    /// <summary>Crank torque at <paramref name="rpm"/> with the pedal fully down (petrol: a smooth curve under the
    /// power limit; electric: flat torque, then constant power).</summary>
    public float FullLoadTorque(float rpm)
    {
        float omega = Mathf.Max(1f, rpm * Mathf.PI / 30f);
        float powerLimit = MaxPower * 1000f / omega;
        if (IsElectric) return Mathf.Min(MaxTorque, powerLimit);
        float x = rpm / Mathf.Max(1f, PeakTorqueRpm);
        float shape = x < 1f
            ? Mathf.Lerp(0.55f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rpm - IdleRpm) / Mathf.Max(1f, PeakTorqueRpm - IdleRpm))))
            : Mathf.Lerp(1f, 0.82f, Mathf.Clamp01((rpm - PeakTorqueRpm) / Mathf.Max(1f, RedlineRpm - PeakTorqueRpm)));
        return Mathf.Min(MaxTorque * shape, powerLimit);
    }
}
