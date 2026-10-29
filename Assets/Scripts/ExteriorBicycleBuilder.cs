#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor-only procedural city bicycle for the exterior cyclists, shared by <c>ExteriorEnvironmentBuilder.CreateCyclist</c>
/// and <b>Aurelle &gt; Realism &gt; Bicycles</b> (<c>AurelleBicycles</c>), so a district rebuild and the in-place tool make
/// the same bike: 700x35c wheels with tread, alloy rims, 32 three-cross spokes, valves and spoke reflectors, an aluminium
/// diamond frame and fork, a 1x8 drivetrain (38T chainring with a guard ring, 11-32 cassette, rear derailleur, chain),
/// platform pedals, disc brakes with their hoses, a swept city handlebar with grips, levers, shifter and bell, a sprung
/// saddle on a seat post, and (by palette) fenders, a rear rack, a kickstand and dynamo lights.
/// Lengths are metres in the bike's space: x right, y up, z forward, the ground at y = 0 under the middle of the wheelbase.
/// Meshes are written to Assets/Models/Bicycle and materials to Assets/Materials/Realism/Bicycle, updated in place (their
/// GUIDs stay) and byte-identical for the same input.
/// The objects ExteriorCyclistAgent relies on keep their names: Bicycle/Front Alloy Wheel and Rear Alloy Wheel (pivots on
/// the hub axles, the axle along local X), Bicycle/Left Pedal and Right Pedal (crank pivots on the bottom-bracket axis with
/// the arm along local +Y, each with its Pedal body on the pedal spindle), Bicycle/Saddle with its Sit Point, and
/// Bicycle/Handlebar with its Left Grip and Right Grip, 0.221 m either side of the handlebar's pivot.
/// </summary>
public static class ExteriorBicycleBuilder
{
    public const string MeshFolder = "Assets/Models/Bicycle";
    public const string MaterialFolder = "Assets/Materials/Realism/Bicycle";
    public const string RootName = "Bicycle";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ------------------------------------------------------------------------------------------------ geometry

    // Frame and fork (city / trekking size M): chain stay, bottom-bracket drop, wheelbase, seat and head angles, fork
    // offset and axle-to-crown length, head tube, the seat tube up to its collar and the top tube's joint on it.
    private const float ChainStay = 0.445f, BottomBracketDrop = 0.07f, Wheelbase = 1.08f, SeatAngle = 73f, HeadAngle = 71f;
    private const float ForkOffset = 0.045f, AxleToCrown = 0.41f, HeadTubeLength = 0.165f, SeatTubeLength = 0.50f, TopTubeJoint = 0.46f;
    // Stem: rise above horizontal, length, headset cup and spacers under it (an upright city cockpit). The handlebar's clamp
    // sits this far from the grips' midpoint (its pivot): the bar rises 50 mm and sweeps back 160 mm to grips 0.442 m apart
    // (30 degree backsweep).
    private const float StemRise = 35f, StemLength = 0.07f, StemStack = 0.072f;
    private static readonly Vector3 BarClamp = new Vector3(0f, -0.05f, 0.16f);
    public const float GripHalfSpan = 0.221f;
    private const float GripSweep = 30f, GripLength = 0.12f;
    // Saddle: its top above the rail clamp, the clamp's setback, and the range the seat post may be set to (saddle top
    // above the bottom-bracket axis, along the seat tube). The rider is fitted so the knee bends BottomKneeFlexion degrees
    // with the pedal at the bottom of the stroke.
    private const float SaddleTop = 0.055f, MinSaddleHeight = 0.60f, MaxSaddleHeight = 0.86f, DefaultSaddleHeight = 0.71f;
    public const float BottomKneeFlexion = 30f;
    // The Hips bone of the seated rider sits this far above the saddle's top at the sit bones (ExteriorCyclistAgent adds its
    // own SeatLift above the Sit Point).
    private const float HipsAboveSaddle = 0.075f, AgentSeatLift = 0.03f, SitBack = -0.07f;
    // Drivetrain: crank length, crank-arm plane, pedal centre, chainline, chainring and the cassette (inboard first).
    public const float CrankLength = 0.17f;
    private const float CrankArmX = 0.080f, PedalX = 0.142f, ChainringX = 0.045f;
    private const int ChainringTeeth = 38, ChainCog = 3;
    private static readonly int[] Cassette = { 32, 28, 24, 21, 18, 15, 13, 11 };
    private const float CogPitch = 0.0045f, FirstCogX = 0.030f, ChainPitch = 0.0127f;
    // Tyre (700x35c on a 622 mm bead seat): casing centre radius and semi-axes, tread blocks.
    private const float CasingCentre = 0.3285f, CasingRadial = 0.017f, CasingAxial = 0.0175f, RimSeat = 0.311f, NippleRadius = 0.2915f;

    private struct Geometry
    {
        public float R;
        public Vector3 RearAxle, FrontAxle, BB, SeatDir, SteerDir, SteerNormal, StemDir;
        public Vector3 SeatCluster, SeatTubeTop, Crown, HeadBottom, HeadTop, StemBase, StemClamp, Handlebar;

        public static Geometry Create(float wheelRadius)
        {
            var g = new Geometry { R = wheelRadius };
            g.RearAxle = new Vector3(0f, wheelRadius, -0.535f);
            g.FrontAxle = new Vector3(0f, wheelRadius, g.RearAxle.z + Wheelbase);
            g.BB = new Vector3(0f, wheelRadius - BottomBracketDrop, g.RearAxle.z + Mathf.Sqrt(ChainStay * ChainStay - BottomBracketDrop * BottomBracketDrop));
            float sa = SeatAngle * Mathf.Deg2Rad, ha = HeadAngle * Mathf.Deg2Rad, sr = StemRise * Mathf.Deg2Rad;
            g.SeatDir = new Vector3(0f, Mathf.Sin(sa), -Mathf.Cos(sa));
            g.SteerDir = new Vector3(0f, Mathf.Sin(ha), -Mathf.Cos(ha));
            g.SteerNormal = new Vector3(0f, Mathf.Cos(ha), Mathf.Sin(ha));
            g.StemDir = new Vector3(0f, Mathf.Sin(sr), Mathf.Cos(sr));
            g.SeatCluster = g.BB + g.SeatDir * TopTubeJoint;
            g.SeatTubeTop = g.BB + g.SeatDir * SeatTubeLength;
            Vector3 axis = g.FrontAxle - g.SteerNormal * ForkOffset;
            g.Crown = axis + g.SteerDir * Mathf.Sqrt(AxleToCrown * AxleToCrown - ForkOffset * ForkOffset);
            g.HeadBottom = g.Crown + g.SteerDir * 0.012f;
            g.HeadTop = g.HeadBottom + g.SteerDir * HeadTubeLength;
            g.StemBase = g.HeadTop + g.SteerDir * StemStack;
            g.StemClamp = g.StemBase + g.StemDir * StemLength;
            g.Handlebar = g.StemClamp - BarClamp;
            return g;
        }

    }

    // ------------------------------------------------------------------------------------------------ palettes

    private enum Slot { Paint, Tread, Sidewall, Stripe, RimAlloy, Cockpit, DarkAlloy, Steel, Drivetrain, SaddleCover, GripCover, Fender, Rack, Amber, Red, Lens }

    private sealed class Palette
    {
        public string Name;
        public Color Paint;
        public float PaintMetallic;
        public bool Gumwall, Fenders, Rack, Kickstand, Lights, SilverCockpit, SilverFenders, Leather;
    }

    private static readonly Palette[] Palettes =
    {
        new Palette { Name = "RacingGreen", Paint = new Color(0.035f, 0.16f, 0.105f), PaintMetallic = 0.45f, Gumwall = true, Fenders = true, Rack = true,
            Kickstand = true, Lights = true, SilverCockpit = true, SilverFenders = true, Leather = true },
        new Palette { Name = "MidnightBlue", Paint = new Color(0.04f, 0.07f, 0.17f), PaintMetallic = 0.55f, Fenders = true, Kickstand = true },
        new Palette { Name = "SignalRed", Paint = new Color(0.52f, 0.045f, 0.035f), PaintMetallic = 0.35f },
    };

    public static int PaletteCount => Palettes.Length;

    /// <summary>The transforms of one bicycle that the cyclist agent and the seat fit use.</summary>
    public sealed class Parts
    {
        public Transform Root, Frame, SeatPost, Saddle, Handlebar, FrontWheel, RearWheel, LeftCrank, RightCrank;
        public int Triangles;
        public string PaletteName;
    }

    // ------------------------------------------------------------------------------------------------ build

    /// <summary>Builds the bicycle under <paramref name="cyclist"/> (at its origin, facing its +Z) in palette
    /// <paramref name="variant"/>, writing its meshes and materials. The saddle starts at the default height; <see cref="Seat"/>
    /// fits it to the rider.</summary>
    public static Parts Build(Transform cyclist, int variant)
    {
        EnsureFolder(MeshFolder);
        EnsureFolder(MaterialFolder);
        Palette palette = Palettes[Mathf.Abs(variant) % Palettes.Length];
        Dictionary<Slot, Material> materials = Materials(palette);

        Kit front = BuildWheel(false), rear = BuildWheel(true);
        Mesh frontMesh = SaveMesh(front, "Bicycle_FrontWheel");
        Mesh rearMesh = SaveMesh(rear, "Bicycle_RearWheel");
        float radius = Mathf.Max(frontMesh.bounds.extents.y, frontMesh.bounds.extents.z);
        Geometry g = Geometry.Create(radius);

        var parts = new Parts { PaletteName = palette.Name };
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build Bicycle");
        parts.Root = root.transform;
        parts.Root.SetParent(cyclist, false);

        string frameName = "Bicycle_Frame" + (palette.Fenders ? "_Fenders" : "") + (palette.Rack ? "_Rack" : "") + (palette.Kickstand ? "_Stand" : "") + (palette.Lights ? "_Lights" : "");
        Kit frame = BuildFrame(g, palette);
        parts.Frame = Part("Frame", parts.Root, Vector3.zero, frame, frameName, materials, ref parts.Triangles);
        parts.SeatPost = Part("Seat Post", parts.Root, Vector3.zero, BuildSeatPost(g), "Bicycle_SeatPost", materials, ref parts.Triangles);
        parts.Saddle = Part("Saddle", parts.Root, Vector3.zero, BuildSaddle(), "Bicycle_Saddle", materials, ref parts.Triangles);
        Transform sit = new GameObject("Sit Point").transform;
        sit.SetParent(parts.Saddle, false);
        sit.localPosition = new Vector3(0f, SaddleSurface(SitBack) + HipsAboveSaddle - AgentSeatLift, SitBack);
        SetSaddleHeight(parts, g, DefaultSaddleHeight);

        parts.Handlebar = Part("Handlebar", parts.Root, g.Handlebar, BuildHandlebar(), "Bicycle_Handlebar", materials, ref parts.Triangles);
        for (int side = -1; side <= 1; side += 2)
        {
            Transform grip = new GameObject(side < 0 ? "Left Grip" : "Right Grip").transform;
            grip.SetParent(parts.Handlebar, false);
            grip.localPosition = new Vector3(side * GripHalfSpan, 0f, 0f);
        }

        parts.FrontWheel = Part("Front Alloy Wheel", parts.Root, g.FrontAxle, front, null, materials, ref parts.Triangles, frontMesh);
        parts.RearWheel = Part("Rear Alloy Wheel", parts.Root, g.RearAxle, rear, null, materials, ref parts.Triangles, rearMesh);

        // Crank pivots on the bottom-bracket axis; ExteriorCyclistAgent turns them about local X (the right one half a turn on).
        parts.LeftCrank = new GameObject("Left Pedal").transform;
        parts.LeftCrank.SetParent(parts.Root, false);
        parts.LeftCrank.localPosition = g.BB;
        parts.RightCrank = new GameObject("Right Pedal").transform;
        parts.RightCrank.SetParent(parts.Root, false);
        parts.RightCrank.localPosition = g.BB;
        parts.RightCrank.localRotation = Quaternion.AngleAxis(180f, Vector3.right);
        Part("Crank Arm", parts.LeftCrank, Vector3.zero, BuildCrank(-1), "Bicycle_CrankLeft", materials, ref parts.Triangles);
        Part("Crank Arm", parts.RightCrank, Vector3.zero, BuildCrank(1), "Bicycle_CrankRight", materials, ref parts.Triangles);
        Part("Pedal", parts.LeftCrank, new Vector3(-PedalX, CrankLength, 0f), BuildPedal(-1), "Bicycle_PedalLeft", materials, ref parts.Triangles);
        Part("Pedal", parts.RightCrank, new Vector3(PedalX, CrankLength, 0f), BuildPedal(1), "Bicycle_PedalRight", materials, ref parts.Triangles);
        return parts;
    }

    /// <summary>Replaces the bicycle of a cyclist in place (the rider, route and configuration stay), seats the rider on it
    /// again and returns a report line.</summary>
    public static string Rebuild(ExteriorCyclistAgent agent, int variant)
    {
        Transform cyclist = agent.transform;
        int index = -1;
        for (int i = cyclist.childCount - 1; i >= 0; i--)
        {
            Transform child = cyclist.GetChild(i);
            if (child.name != RootName) continue;
            index = child.GetSiblingIndex();
            Undo.DestroyObjectImmediate(child.gameObject);
        }
        Parts parts = Build(cyclist, variant);
        parts.Root.SetSiblingIndex(index >= 0 ? index : 0);
        return Seat(agent, parts);
    }

    /// <summary>Puts the cyclist's tyres on the surface under it (as ExteriorCyclistAgent does at runtime), rebinds the agent
    /// to the bike's cranks and fits the seat post so the rider's knee bends <see cref="BottomKneeFlexion"/> degrees at the
    /// bottom of the stroke; the posed rider is recorded on its prefab instance.</summary>
    public static string Seat(ExteriorCyclistAgent agent, Parts parts)
    {
        Geometry g = Geometry.Create(WheelRadius(parts));
        PlaceOnSurface(agent.transform);
        Transform rider = agent.transform.Find("Cyclist Rider");
        Transform hip = Bone(rider, "RightUpLeg"), knee = Bone(rider, "RightLeg"), ankle = Bone(rider, "RightFoot");
        float height = DefaultSaddleHeight;
        if (hip != null && knee != null && ankle != null)
        {
            // Knee flexion at the bottom falls as the saddle rises: bisect on it (the crank pose at rebind puts the right pedal down).
            float low = MinSaddleHeight, high = MaxSaddleHeight;
            for (int i = 0; i < 16; i++)
            {
                height = 0.5f * (low + high);
                SetSaddleHeight(parts, g, height);
                agent.Rebind(parts.LeftCrank, parts.RightCrank);
                if (Flexion(hip, knee, ankle) > BottomKneeFlexion) low = height;
                else high = height;
            }
            height = 0.5f * (low + high);
        }
        SetSaddleHeight(parts, g, height);
        agent.Rebind(parts.LeftCrank, parts.RightCrank);
        EditorUtility.SetDirty(agent);
        var report = new StringBuilder();
        report.Append(agent.name).Append(": palette ").Append(parts.PaletteName).Append(", ").Append(parts.Triangles).Append(" triangles, tyre radius ")
            .Append(Fmt(g.R)).Append(" m, saddle height ").Append(Fmt(height)).Append(" m");
        if (rider != null)
        {
            if (hip != null && knee != null && ankle != null)
                report.Append(", knee at the bottom ").Append(Flexion(hip, knee, ankle).ToString("F1", Inv)).Append(" deg");
            Transform lHip = Bone(rider, "LeftUpLeg"), lKnee = Bone(rider, "LeftLeg"), lAnkle = Bone(rider, "LeftFoot");
            if (lHip != null && lKnee != null && lAnkle != null)
                report.Append(", at the top ").Append(Flexion(lHip, lKnee, lAnkle).ToString("F1", Inv)).Append(" deg");
            foreach (Transform t in rider.GetComponentsInChildren<Transform>(true)) PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            foreach (SkinnedMeshRenderer skin in rider.GetComponentsInChildren<SkinnedMeshRenderer>(true)) PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
        }
        else report.Append(", no rider");
        return report.ToString();
    }

    // The route runs at the flat waypoint height while the cycle tracks are raised: the highest surface within a metre above
    // and 1.5 m below the cyclist's origin (ignoring the cyclist itself and character capsules) carries the tyres.
    private static void PlaceOnSurface(Transform cyclist)
    {
        Physics.SyncTransforms();
        Vector3 position = cyclist.position;
        float ground = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(position + Vector3.up, Vector3.down, 2.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == null || hit.collider.transform.IsChildOf(cyclist) || hit.collider is CharacterController) continue;
            ground = Mathf.Max(ground, hit.point.y);
        }
        if (float.IsNegativeInfinity(ground) || Mathf.Abs(ground - position.y) < 0.0005f) return;
        Undo.RecordObject(cyclist, "Place Cyclist");
        position.y = ground;
        cyclist.position = position;
    }

    private static float Flexion(Transform hip, Transform knee, Transform ankle) => Vector3.Angle(knee.position - hip.position, ankle.position - knee.position);

    private static float WheelRadius(Parts parts)
    {
        MeshFilter filter = parts.FrontWheel != null ? parts.FrontWheel.GetComponent<MeshFilter>() : null;
        return filter != null && filter.sharedMesh != null ? Mathf.Max(filter.sharedMesh.bounds.extents.y, filter.sharedMesh.bounds.extents.z) : 0.346f;
    }

    // The saddle and its post slide together along the seat tube; height is the saddle's top above the bottom-bracket axis.
    private static void SetSaddleHeight(Parts parts, Geometry g, float height)
    {
        Vector3 clamp = g.BB + g.SeatDir * (height - SaddleTop);
        if (parts.SeatPost != null) parts.SeatPost.localPosition = clamp;
        if (parts.Saddle != null) parts.Saddle.localPosition = clamp;
    }

    private static Transform Bone(Transform root, string name)
    {
        if (root == null) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name || t.name.EndsWith(":" + name, StringComparison.Ordinal)) return t;
        return null;
    }

    private static string Fmt(float v) => v.ToString("F3", Inv);

    private static Transform Part(string name, Transform parent, Vector3 position, Kit kit, string meshName, Dictionary<Slot, Material> materials,
        ref int triangles, Mesh saved = null)
    {
        Mesh mesh = saved != null ? saved : SaveMesh(kit, meshName);
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        var shared = new Material[kit.Slots.Count];
        for (int i = 0; i < shared.Length; i++) shared[i] = materials[kit.Slots[i]];
        renderer.sharedMaterials = shared;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        triangles += kit.TriangleCount;
        return go.transform;
    }

    // ------------------------------------------------------------------------------------------------ wheels

    // Wheel space: the hub at the origin, the axle along +X (to the drive side), angle 0 up (+Y), 90 degrees forward (+Z).
    private static Vector3 Polar(float x, float radius, float degrees)
    {
        float a = degrees * Mathf.Deg2Rad;
        return new Vector3(x, radius * Mathf.Cos(a), radius * Mathf.Sin(a));
    }

    // Lathes revolve about local +Y; this turn puts that axis along the wheel's axle (+X).
    private static readonly Quaternion AxleTurn = Quaternion.Euler(0f, 0f, -90f);

    private static Kit BuildWheel(bool rear)
    {
        var k = new Kit();
        // Tyre casing: an ellipse round the casing centre from bead to bead; sidewalls, reflective band and tread are separate
        // materials (tan gumwall or black with a reflective band, by palette).
        k.Push(Vector3.zero, AxleTurn);
        float[][] bands =
        {
            new[] { -142f, -82f, (float)Slot.Sidewall }, new[] { -82f, -70f, (float)Slot.Stripe }, new[] { -70f, -50f, (float)Slot.Sidewall },
            new[] { -50f, 50f, (float)Slot.Tread }, new[] { 50f, 70f, (float)Slot.Sidewall }, new[] { 70f, 82f, (float)Slot.Stripe },
            new[] { 82f, 142f, (float)Slot.Sidewall }
        };
        foreach (float[] band in bands)
        {
            k.Use((Slot)(int)band[2]);
            int steps = Mathf.Max(2, Mathf.RoundToInt((band[1] - band[0]) / 14f));
            var profile = new List<Vector2>();
            var normal = new List<Vector2>();
            for (int i = 0; i <= steps; i++)
            {
                float t = Mathf.Lerp(band[0], band[1], (float)i / steps) * Mathf.Deg2Rad;
                profile.Add(new Vector2(CasingCentre + CasingRadial * Mathf.Cos(t), CasingAxial * Mathf.Sin(t)));
                normal.Add(new Vector2(Mathf.Cos(t) / CasingRadial, Mathf.Sin(t) / CasingAxial).normalized);
            }
            k.Revolve(profile, normal, 40);
        }
        // Rim: a 22 mm double-wall alloy rim under the beads, with a dark label on each side (it shows the wheel turning).
        k.Use(Slot.RimAlloy);
        k.Lathe(new[]
        {
            new Vector2(0.2920f, -0.0090f), new Vector2(0.3000f, -0.0110f), new Vector2(0.3130f, -0.0110f), new Vector2(0.3142f, -0.0098f),
            new Vector2(0.3142f, 0.0098f), new Vector2(0.3130f, 0.0110f), new Vector2(0.3000f, 0.0110f), new Vector2(0.2920f, 0.0090f),
            new Vector2(0.2893f, 0f), new Vector2(0.2920f, -0.0090f)
        }, 40, 35f);
        k.Use(Slot.DarkAlloy);
        for (int side = -1; side <= 1; side += 2)
            for (int label = 0; label < 2; label++)
                k.Lathe(side < 0
                    ? new[] { new Vector2(0.3005f, -0.01112f), new Vector2(0.3115f, -0.01112f) }
                    : new[] { new Vector2(0.3115f, 0.01112f), new Vector2(0.3005f, 0.01112f) }, 8, 40f, 20f + label * 180f, 38f);
        // Hub: a dynamo hub at the front; a dished hub with its freehub body at the rear.
        k.Use(Slot.DarkAlloy);
        if (rear)
            k.Lathe(new[]
            {
                new Vector2(0f, -0.0675f), new Vector2(0.0095f, -0.0675f), new Vector2(0.0095f, -0.062f), new Vector2(0.013f, -0.060f), new Vector2(0.016f, -0.045f),
                new Vector2(0.016f, -0.0385f), new Vector2(0.0235f, -0.0375f), new Vector2(0.0235f, -0.0335f), new Vector2(0.016f, -0.0325f), new Vector2(0.0145f, 0f),
                new Vector2(0.016f, 0.0165f), new Vector2(0.0235f, 0.0175f), new Vector2(0.0235f, 0.0215f), new Vector2(0.0172f, 0.0225f), new Vector2(0.0172f, 0.066f),
                new Vector2(0.0095f, 0.0665f), new Vector2(0.0095f, 0.0675f), new Vector2(0f, 0.0675f)
            }, 14, 35f);
        else
            k.Lathe(new[]
            {
                new Vector2(0f, -0.050f), new Vector2(0.0085f, -0.050f), new Vector2(0.0085f, -0.0445f), new Vector2(0.014f, -0.0435f), new Vector2(0.020f, -0.040f),
                new Vector2(0.037f, -0.0368f), new Vector2(0.037f, -0.0322f), new Vector2(0.0305f, -0.031f), new Vector2(0.0325f, -0.018f), new Vector2(0.033f, 0f),
                new Vector2(0.0325f, 0.018f), new Vector2(0.0305f, 0.031f), new Vector2(0.037f, 0.0322f), new Vector2(0.037f, 0.0368f), new Vector2(0.020f, 0.040f),
                new Vector2(0.014f, 0.0435f), new Vector2(0.0085f, 0.0445f), new Vector2(0.0085f, 0.050f), new Vector2(0f, 0.050f)
            }, 16, 35f);
        // Disc brake rotor (160 mm) on the left of the hub: braking band, carrier ring and six skewed arms.
        float rotorX = rear ? -0.0465f : -0.0445f;
        k.Use(Slot.Steel);
        k.Lathe(new[] { new Vector2(0.064f, rotorX - 0.0009f), new Vector2(0.080f, rotorX - 0.0009f), new Vector2(0.080f, rotorX + 0.0009f),
            new Vector2(0.064f, rotorX + 0.0009f), new Vector2(0.064f, rotorX - 0.0009f) }, 36, 40f);
        k.Lathe(new[] { new Vector2(0.0165f, rotorX - 0.0014f), new Vector2(0.026f, rotorX - 0.0014f), new Vector2(0.026f, rotorX + 0.0014f),
            new Vector2(0.0165f, rotorX + 0.0014f), new Vector2(0.0165f, rotorX - 0.0014f) }, 12, 40f);
        k.Pop();
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f + 15f;
            Vector3 inner = Polar(rotorX, 0.024f, a), outer = Polar(rotorX, 0.066f, a + 14f);
            k.Push((inner + outer) * 0.5f, Quaternion.LookRotation(outer - inner, Vector3.right));
            k.Box(Vector3.zero, new Vector3(0.0075f, 0.0016f, (outer - inner).magnitude + 0.004f));
            k.Pop();
        }
        // Spokes: 16 per flange, three-cross; leading and trailing spokes alternate, the two sides interleave on the rim.
        float leftFlange = rear ? -0.0355f : -0.0345f, rightFlange = rear ? 0.0195f : 0.0345f, hole = rear ? 0.0205f : 0.0335f;
        for (int side = 0; side < 2; side++)
        {
            float flange = side == 0 ? leftFlange : rightFlange, offset = side * 11.25f;
            for (int i = 0; i < 16; i++)
            {
                float hubAngle = i * 22.5f + offset;
                float rimAngle = hubAngle + (i % 2 == 0 ? 67.5f : -67.5f);
                Vector3 hub = Polar(flange, hole, hubAngle), nipple = Polar(side == 0 ? -0.0018f : 0.0018f, NippleRadius, rimAngle);
                Vector3 along = (nipple - hub).normalized;
                k.Use(Slot.Steel);
                k.Tube(new[] { hub, nipple - along * 0.004f }, 0.00095f, 3, false, false, Vector3.right);
                k.Use(Slot.RimAlloy);
                k.Tube(new[] { nipple - along * 0.013f, nipple + along * 0.0015f }, 0.0018f, 4, false, false, Vector3.right);
            }
        }
        // Valve (Presta, with its lock nut and a black cap) through the rim bed at angle 0, pointing at the hub.
        k.Push(Polar(0f, 0.2895f, 0f), Quaternion.Euler(180f, 0f, 0f));
        k.Use(Slot.Steel);
        k.Lathe(new[] { new Vector2(0f, -0.002f), new Vector2(0.003f, -0.002f), new Vector2(0.003f, 0.013f), new Vector2(0f, 0.013f) }, 10);
        k.Lathe(new[] { new Vector2(0.003f, 0.0015f), new Vector2(0.0052f, 0.0015f), new Vector2(0.0052f, 0.0055f), new Vector2(0.003f, 0.0055f) }, 6, 40f, 30f, 360f, true);
        k.Use(Slot.DarkAlloy);
        k.Lathe(new[] { new Vector2(0f, 0.0105f), new Vector2(0.0042f, 0.0105f), new Vector2(0.0042f, 0.0225f), new Vector2(0.0032f, 0.0235f), new Vector2(0f, 0.0235f) }, 10);
        k.Pop();
        // An amber reflector clipped on the spokes opposite the valve.
        k.Use(Slot.Amber);
        k.Push(Polar(0f, 0.205f, 180f), Quaternion.LookRotation(Vector3.right, Polar(0f, 1f, 180f)));
        k.RoundedBox(Vector3.zero, new Vector3(0.026f, 0.064f, 0.008f), 0.006f, 1);
        k.Pop();
        // Tread: two staggered rows of slanted blocks over the crown.
        k.Use(Slot.Tread);
        const int blocks = 40;
        for (int i = 0; i < blocks; i++)
        {
            float x = (i % 2 == 0 ? 1f : -1f) * 0.0052f;
            float t = Mathf.Asin(x / CasingAxial);
            float surface = CasingCentre + CasingRadial * Mathf.Cos(t);
            float a = i * 360f / blocks;
            Vector3 radial = Polar(0f, 1f, a), tangent = Polar(0f, 1f, a + 90f);
            Quaternion slant = Quaternion.AngleAxis(x > 0f ? 22f : -22f, radial);
            k.Push(Polar(x, surface + 0.0003f, a), slant * Quaternion.LookRotation(tangent, radial));
            k.Box(Vector3.zero, new Vector3(0.0064f, 0.0024f, 0.0135f));
            k.Pop();
        }
        // Cassette (rear): eight cogs on the freehub, 32 teeth inboard to 11 outboard, and the lockring.
        if (rear)
        {
            k.Use(Slot.Drivetrain);
            for (int i = 0; i < Cassette.Length; i++)
            {
                float pitch = PitchRadius(Cassette[i]);
                k.ToothedRing(new Vector3(FirstCogX + i * CogPitch, 0f, 0f), Cassette[i], pitch + 0.0038f, pitch - 0.0042f, 0.0175f, 0.0018f, i * 7f, false, i == 0);
            }
            k.Push(Vector3.zero, AxleTurn);
            k.Lathe(new[] { new Vector2(0.0095f, 0.0632f), new Vector2(0.0175f, 0.0632f), new Vector2(0.0175f, 0.0662f), new Vector2(0.0095f, 0.0662f) }, 16, 40f, 0f, 360f, true);
            k.Pop();
        }
        return k;
    }

    private static float PitchRadius(int teeth) => ChainPitch / (2f * Mathf.Sin(Mathf.PI / teeth));

    // ------------------------------------------------------------------------------------------------ frame

    private static Kit BuildFrame(Geometry g, Palette palette)
    {
        var k = new Kit();
        Vector3 A = g.RearAxle, F = g.FrontAxle, d = g.SteerDir, n = g.SteerNormal;
        k.Use(Slot.Paint);
        // Head tube with flared ends, between the headset cups.
        k.Push(g.HeadBottom, Quaternion.FromToRotation(Vector3.up, d));
        k.Lathe(new[]
        {
            new Vector2(0f, 0f), new Vector2(0.0228f, 0f), new Vector2(0.0228f, 0.012f), new Vector2(0.0205f, 0.02f), new Vector2(0.0200f, HeadTubeLength * 0.5f),
            new Vector2(0.0205f, HeadTubeLength - 0.02f), new Vector2(0.0228f, HeadTubeLength - 0.012f), new Vector2(0.0228f, HeadTubeLength), new Vector2(0f, HeadTubeLength)
        }, 16, 30f);
        k.Pop();
        // Main triangle: top tube (sloping), down tube (oval at the head), seat tube.
        k.Tube(new[] { g.SeatCluster, g.HeadTop - d * 0.028f }, 0.0172f, 16, false, false, Vector3.right);
        k.Tube(new[] { g.BB + new Vector3(0f, 0.004f, 0.006f), g.HeadBottom + d * 0.034f }, 0.0215f, 16, false, false, Vector3.right, null, new Vector2(1f, 1.12f));
        k.Tube(new[] { g.BB, g.SeatTubeTop }, 0.0159f, 16, false, true, Vector3.right);
        // Bottom-bracket shell.
        k.Push(g.BB, AxleTurn);
        k.Lathe(new[] { new Vector2(0f, -0.034f), new Vector2(0.0215f, -0.034f), new Vector2(0.0215f, 0.034f), new Vector2(0f, 0.034f) }, 20);
        k.Pop();
        // Chain stays: narrow past the chainring, then out to the dropouts; seat stays to the seat cluster; a bridge between them.
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 s = new Vector3(side, 1f, 1f);
            var chain = Kit.CatmullRom(new[]
            {
                Vector3.Scale(new Vector3(0.022f, g.BB.y + 0.012f, g.BB.z - 0.016f), s),
                Vector3.Scale(new Vector3(0.027f, g.BB.y + 0.020f, g.BB.z - 0.105f), s),
                Vector3.Scale(new Vector3(0.053f, A.y - 0.010f, A.z + 0.125f), s),
                Vector3.Scale(new Vector3(0.0645f, A.y - 0.004f, A.z + 0.014f), s)
            }, 4);
            k.Tube(chain, 0.0089f, 10, false, true, Vector3.up, t => Mathf.Lerp(1f, 0.8f, t));
            Vector3 stayTop = g.BB + g.SeatDir * (TopTubeJoint - 0.018f) + new Vector3(side * 0.016f, 0f, -0.004f);
            Vector3 stayBottom = new Vector3(side * 0.0635f, A.y + 0.016f, A.z + 0.006f);
            k.Tube(Kit.CatmullRom(new[] { stayBottom, Vector3.Lerp(stayBottom, stayTop, 0.5f) + new Vector3(side * 0.006f, 0f, 0f), stayTop }, 4), 0.0076f, 10, true, false, Vector3.right,
                t => Mathf.Lerp(0.95f, 1.12f, t));
        }
        Vector3 bridge = Vector3.Lerp(new Vector3(0f, A.y + 0.016f, A.z + 0.006f), g.BB + g.SeatDir * (TopTubeJoint - 0.018f), 0.68f);
        k.Tube(new[] { bridge + new Vector3(-0.034f, 0f, 0f), bridge + new Vector3(0.034f, 0f, 0f) }, 0.0055f, 8, false, false, Vector3.up);
        Vector3 csBridge = new Vector3(0f, g.BB.y + 0.022f, g.BB.z - 0.12f);
        k.Tube(new[] { csBridge + new Vector3(-0.03f, 0f, 0f), csBridge + new Vector3(0.03f, 0f, 0f) }, 0.006f, 8, false, false, Vector3.up);
        // Rear dropouts (with the derailleur hanger on the right) and fork tips.
        Vector2[] dropout = { new Vector2(-0.014f, -0.012f), new Vector2(0.03f, -0.014f), new Vector2(0.042f, 0.004f), new Vector2(0.022f, 0.035f), new Vector2(-0.012f, 0.031f), new Vector2(-0.018f, 0.008f) };
        for (int side = -1; side <= 1; side += 2)
            k.Plate(new Vector3(side * 0.0645f, A.y, A.z), Vector3.forward, Vector3.up, Vector3.right, dropout, 0.006f);
        k.Use(Slot.DarkAlloy);
        k.Plate(new Vector3(0.0705f, A.y, A.z), Vector3.forward, Vector3.up, Vector3.right, Kit.Slot2D(new Vector2(-0.002f, -0.004f), new Vector2(-0.011f, -0.027f), 0.0075f, 5), 0.005f);
        k.Push(new Vector3(0f, A.y, A.z), AxleTurn);
        k.Lathe(new[] { new Vector2(0f, -0.081f), new Vector2(0.0085f, -0.081f), new Vector2(0.0085f, -0.0675f), new Vector2(0.0045f, -0.0675f), new Vector2(0.0045f, 0.0675f),
            new Vector2(0.0085f, 0.0675f), new Vector2(0.0085f, 0.077f), new Vector2(0f, 0.077f) }, 12);
        k.Pop();
        k.Push(new Vector3(0f, F.y, F.z), AxleTurn);
        k.Lathe(new[] { new Vector2(0f, -0.062f), new Vector2(0.0085f, -0.062f), new Vector2(0.0085f, -0.0525f), new Vector2(0.0045f, -0.0525f), new Vector2(0.0045f, 0.0525f),
            new Vector2(0.0085f, 0.0525f), new Vector2(0.0085f, 0.062f), new Vector2(0f, 0.062f) }, 12);
        k.Pop();
        // Fork: tapered straight blades on the axle line, an offset crown under the head tube, fork tips.
        k.Use(Slot.Paint);
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 x = new Vector3(side, 0f, 0f);
            k.Tube(new[] { F + x * 0.0505f + d * 0.012f, F + x * 0.046f + d * 0.37f }, 0.0135f, 14, true, false, Vector3.right, t => Mathf.Lerp(0.68f, 1f, t));
            k.Plate(F + x * 0.0505f, Vector3.forward, Vector3.up, Vector3.right,
                Kit.Slot2D(Vector2.zero, new Vector2(d.z, d.y) * 0.028f, 0.0105f, 5), 0.005f);
        }
        k.Push(g.Crown - d * 0.014f + n * 0.024f, Quaternion.LookRotation(n, d));
        k.RoundedBox(Vector3.zero, new Vector3(0.122f, 0.03f, 0.074f), 0.012f, 1);
        k.Pop();
        // Headset cups, spacers, stem (steerer clamp, extension, bar clamp) and top cap.
        k.Use(Slot.Cockpit);
        k.Push(g.HeadBottom, Quaternion.FromToRotation(Vector3.up, d));
        k.Lathe(new[] { new Vector2(0.0195f, -0.014f), new Vector2(0.0245f, -0.012f), new Vector2(0.0245f, 0f), new Vector2(0.0195f, 0.001f) }, 16, 35f);
        k.Lathe(new[] { new Vector2(0.0195f, HeadTubeLength - 0.001f), new Vector2(0.0245f, HeadTubeLength), new Vector2(0.0245f, HeadTubeLength + 0.012f),
            new Vector2(0.0185f, HeadTubeLength + 0.014f), new Vector2(0.0185f, HeadTubeLength + StemStack - 0.02f), new Vector2(0.0205f, HeadTubeLength + StemStack - 0.02f),
            new Vector2(0.0205f, HeadTubeLength + StemStack + 0.02f), new Vector2(0.0178f, HeadTubeLength + StemStack + 0.021f), new Vector2(0.0178f, HeadTubeLength + StemStack + 0.025f),
            new Vector2(0f, HeadTubeLength + StemStack + 0.026f) }, 16, 35f);
        k.Pop();
        k.Tube(new[] { g.StemBase, g.StemClamp }, 0.0158f, 14, false, false, Vector3.right);
        k.Push(g.StemClamp, AxleTurn);
        k.Lathe(new[] { new Vector2(0.0128f, -0.021f), new Vector2(0.0205f, -0.021f), new Vector2(0.0215f, -0.017f), new Vector2(0.0215f, 0.017f), new Vector2(0.0205f, 0.021f), new Vector2(0.0128f, 0.021f) }, 18, 35f);
        k.Pop();
        // Seat collar with its bolt.
        k.Use(Slot.DarkAlloy);
        k.Push(g.SeatTubeTop, Quaternion.FromToRotation(Vector3.up, g.SeatDir));
        k.Lathe(new[] { new Vector2(0.0138f, -0.016f), new Vector2(0.0188f, -0.016f), new Vector2(0.0188f, 0.003f), new Vector2(0.0138f, 0.003f) }, 18, 35f);
        k.Box(new Vector3(0f, -0.006f, -0.021f), new Vector3(0.016f, 0.009f, 0.008f));
        k.Pop();
        // Disc calipers on post mounts: the front behind the left fork blade, the rear on the left chain stay.
        Vector3 frontCaliper = CaliperFrame(k, F, -0.0475f, -55f);
        Vector3 rearCaliper = CaliperFrame(k, A, -0.050f, 65f);
        // Hoses and the gear cable (black housing), from the levers on the bar to the calipers and the derailleur.
        Vector3 bar = g.Handlebar;
        Vector3 leftLever = bar + LeverPort(-1), rightLever = bar + LeverPort(1), shifter = bar + new Vector3(0.155f, -0.026f, 0.018f);
        Vector3 rd = A + new Vector3(0.071f, -0.03f, -0.012f);
        k.Use(Slot.DarkAlloy);
        k.Tube(Kit.CatmullRom(new[]
        {
            leftLever, leftLever + new Vector3(0.01f, -0.05f, 0.07f), g.Crown + new Vector3(-0.07f, 0.03f, 0.03f), F + new Vector3(-0.064f, 0f, 0f) + d * 0.22f - n * 0.006f,
            F + new Vector3(-0.062f, 0f, 0f) + d * 0.1f - n * 0.012f, frontCaliper + new Vector3(-0.004f, 0.012f, 0.008f)
        }, 4), 0.0024f, 5, true, true, Vector3.right);
        Vector3 ttRight = new Vector3(0.012f, 0.018f, 0f);
        k.Tube(Kit.CatmullRom(new[]
        {
            rightLever, rightLever + new Vector3(-0.02f, -0.05f, 0.08f), g.HeadTop + new Vector3(0.03f, 0.0f, 0.04f), Vector3.Lerp(g.HeadTop - d * 0.03f, g.SeatCluster, 0.12f) + ttRight,
            Vector3.Lerp(g.HeadTop - d * 0.03f, g.SeatCluster, 0.85f) + ttRight, g.SeatCluster + new Vector3(-0.022f, -0.02f, -0.012f),
            Vector3.Lerp(g.SeatCluster, new Vector3(-0.0635f, A.y, A.z), 0.55f) + new Vector3(-0.013f, 0f, 0f), rearCaliper + new Vector3(-0.004f, 0.016f, -0.01f)
        }, 4), 0.0024f, 5, true, true, Vector3.right);
        k.Tube(Kit.CatmullRom(new[]
        {
            shifter, shifter + new Vector3(0.0f, -0.05f, 0.075f), g.HeadBottom + new Vector3(0.03f, 0.03f, 0.03f), Vector3.Lerp(g.HeadBottom, g.BB, 0.25f) + new Vector3(0.012f, -0.022f, 0f),
            Vector3.Lerp(g.HeadBottom, g.BB, 0.85f) + new Vector3(0.010f, -0.024f, 0f), g.BB + new Vector3(0.012f, -0.026f, -0.03f),
            new Vector3(0.042f, g.BB.y + 0.035f, g.BB.z - 0.16f), new Vector3(0.068f, A.y + 0.012f, A.z + 0.08f), rd + new Vector3(0.01f, 0.03f, 0.02f), rd + new Vector3(0f, 0.008f, 0f)
        }, 4), 0.0022f, 5, true, true, Vector3.right);
        BuildChain(k, g);
        BuildDerailleur(k, g);
        if (palette.Fenders) BuildFenders(k, g, palette);
        if (palette.Rack) BuildRack(k, g);
        if (palette.Kickstand) BuildKickstand(k, g);
        if (palette.Lights) BuildLights(k, g, palette);
        else
        {
            // A red reflector on the seat stay bridge, a white one under the stem.
            k.Use(Slot.Red);
            k.Push(bridge + new Vector3(0f, 0.004f, -0.012f), Quaternion.LookRotation(Vector3.back, Vector3.up));
            k.RoundedBox(Vector3.zero, new Vector3(0.05f, 0.022f, 0.008f), 0.004f, 1);
            k.Pop();
            k.Use(Slot.Lens);
            k.Push(g.StemClamp + new Vector3(0f, -0.032f, -0.03f), Quaternion.LookRotation(Vector3.forward, Vector3.up));
            k.RoundedBox(Vector3.zero, new Vector3(0.045f, 0.02f, 0.008f), 0.004f, 1);
            k.Pop();
        }
        return k;
    }

    // A caliper straddling the rotor's edge at the given angle round the axle (wheel-space degrees: 0 up, 90 forward).
    private static Vector3 CaliperFrame(Kit k, Vector3 axle, float x, float angle)
    {
        Vector3 radial = Polar(0f, 1f, angle), tangent = Polar(0f, 1f, angle + 90f);
        Vector3 centre = new Vector3(x, axle.y, axle.z) + radial * 0.071f;
        k.Use(Slot.DarkAlloy);
        k.Push(centre, Quaternion.LookRotation(tangent, radial));
        k.RoundedBox(new Vector3(-0.004f, 0.002f, 0f), new Vector3(0.03f, 0.026f, 0.056f), 0.007f, 1);
        k.RoundedBox(new Vector3(-0.012f, -0.016f, 0f), new Vector3(0.012f, 0.014f, 0.05f), 0.004f, 1);
        k.Pop();
        return centre;
    }

    // Cable port on a brake lever body (handlebar space).
    private static Vector3 LeverPort(int side)
    {
        Vector3 g = GripAxis(side), f = GripForward(side), inner = GripCentre(side) - g * (GripLength * 0.5f);
        return inner - g * 0.012f + f * 0.03f + Vector3.up * 0.004f;
    }

    private static Vector3 GripAxis(int side)
    {
        float a = GripSweep * Mathf.Deg2Rad;
        return new Vector3(side * Mathf.Cos(a), 0f, -Mathf.Sin(a));
    }

    private static Vector3 GripForward(int side)
    {
        float a = GripSweep * Mathf.Deg2Rad;
        return new Vector3(side * Mathf.Sin(a), 0f, Mathf.Cos(a));
    }

    private static Vector3 GripCentre(int side) => new Vector3(side * GripHalfSpan, 0f, 0f);

    // A simplified chain: link plates round the chainring, the 21-tooth cog and the derailleur's two pulleys (the chain wraps
    // the guide pulley on its front and the tension pulley on its back), outer and inner plates alternating.
    private static void BuildChain(Kit k, Geometry g)
    {
        Vector2 ring = new Vector2(g.BB.z, g.BB.y), cog = new Vector2(g.RearAxle.z, g.RearAxle.y);
        float rRing = PitchRadius(ChainringTeeth), rCog = PitchRadius(Cassette[ChainCog]), rPulley = PitchRadius(11);
        Vector2 guide = cog + GuideOffset, tension = cog + TensionOffset;
        var path = new List<Vector2>();
        Arc(path, ring, rRing, -90f, 90f);
        Arc(path, cog, rCog, 90f, 270f);
        Arc(path, guide, rPulley, 60f, -70f);
        Arc(path, tension, rPulley, 140f, 270f);
        path.Add(path[0]);
        float cogX = FirstCogX + ChainCog * CogPitch;
        var length = new float[path.Count];
        for (int i = 1; i < path.Count; i++) length[i] = length[i - 1] + Vector2.Distance(path[i], path[i - 1]);
        float total = length[path.Count - 1];
        int links = Mathf.Max(8, Mathf.RoundToInt(total / ChainPitch));
        float pitch = total / links;
        k.Use(Slot.Drivetrain);
        int seg = 0;
        for (int i = 0; i < links; i++)
        {
            float s = (i + 0.5f) * pitch;
            while (seg < path.Count - 2 && length[seg + 1] < s) seg++;
            float t = Mathf.InverseLerp(length[seg], length[seg + 1], s);
            Vector2 p = Vector2.Lerp(path[seg], path[seg + 1], t), dir = (path[seg + 1] - path[seg]).normalized;
            float x = Mathf.Lerp(cogX, ChainringX, Mathf.InverseLerp(g.RearAxle.z, g.BB.z, p.x));
            Vector3 forward = new Vector3(0f, dir.y, dir.x), up = new Vector3(0f, dir.x, -dir.y);
            bool outer = i % 2 == 0;
            k.Push(new Vector3(x, p.y, p.x), Quaternion.LookRotation(forward, up));
            k.Box(Vector3.zero, new Vector3(outer ? 0.0078f : 0.0058f, outer ? 0.0098f : 0.0080f, outer ? pitch + 0.0024f : pitch - 0.0006f));
            k.Pop();
        }
    }

    private static readonly Vector2 GuideOffset = new Vector2(-0.012f, -0.076f), TensionOffset = new Vector2(-0.004f, -0.146f);

    private static void Arc(List<Vector2> path, Vector2 centre, float radius, float from, float to)
    {
        int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(to - from) / 10f));
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(from, to, (float)i / steps) * Mathf.Deg2Rad;
            path.Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
    }

    // Rear derailleur: parallelogram body from the hanger down to the cage, two cage plates and two toothed pulleys.
    private static void BuildDerailleur(Kit k, Geometry g)
    {
        Vector3 A = g.RearAxle;
        float x = FirstCogX + ChainCog * CogPitch;
        Vector3 guide = new Vector3(x, A.y + GuideOffset.y, A.z + GuideOffset.x), tension = new Vector3(x, A.y + TensionOffset.y, A.z + TensionOffset.x);
        Vector3 hanger = new Vector3(0.079f, A.y - 0.026f, A.z - 0.012f), knuckle = new Vector3(0.071f, guide.y + 0.016f, guide.z - 0.022f);
        k.Use(Slot.DarkAlloy);
        k.Push((hanger + knuckle) * 0.5f, Quaternion.LookRotation(knuckle - hanger, Vector3.right));
        k.RoundedBox(Vector3.zero, new Vector3(0.024f, 0.018f, (knuckle - hanger).magnitude + 0.02f), 0.006f, 1);
        k.Pop();
        k.Push(knuckle, AxleTurn);
        k.Lathe(new[] { new Vector2(0f, -0.012f), new Vector2(0.012f, -0.012f), new Vector2(0.012f, 0.008f), new Vector2(0f, 0.008f) }, 14);
        k.Pop();
        Vector2 a = new Vector2(guide.z, guide.y), b = new Vector2(tension.z, tension.y);
        List<Vector2> cage = Kit.Slot2D(Vector2.zero, b - a, 0.027f, 6);
        k.Use(Slot.DarkAlloy);
        k.Plate(new Vector3(x + 0.0068f, guide.y, guide.z), Vector3.forward, Vector3.up, Vector3.right, cage, 0.0022f);
        k.Plate(new Vector3(x - 0.0068f, guide.y, guide.z), Vector3.forward, Vector3.up, Vector3.right, Kit.Slot2D(Vector2.zero, b - a, 0.024f, 6), 0.0018f);
        k.Use(Slot.DarkAlloy);
        float rp = PitchRadius(11);
        k.ToothedRing(guide, 11, rp + 0.003f, rp - 0.003f, 0.004f, 0.0045f, 0f, true);
        k.ToothedRing(tension, 11, rp + 0.003f, rp - 0.003f, 0.004f, 0.0045f, 8f, true);
    }

    // Mudguards: a shallow channel swept round each wheel, with stays to the dropouts and a bracket at the crown or bridge.
    private static void BuildFenders(Kit k, Geometry g, Palette palette)
    {
        float radius = g.R + 0.016f;
        k.Use(Slot.Fender);
        Fender(k, g.FrontAxle, radius, -125f, 75f);
        Fender(k, g.RearAxle, radius, -115f, 96f);
        k.Use(palette.SilverFenders ? Slot.Steel : Slot.DarkAlloy);
        for (int side = -1; side <= 1; side += 2)
        {
            k.Tube(new[] { g.FrontAxle + Polar(side * 0.026f, radius - 0.008f, -100f), new Vector3(side * 0.0555f, g.FrontAxle.y + 0.004f, g.FrontAxle.z + 0.002f) },
                0.0022f, 6, true, true, Vector3.up);
            k.Tube(new[] { g.RearAxle + Polar(side * 0.026f, radius - 0.008f, -105f), new Vector3(side * 0.0695f, g.RearAxle.y + 0.006f, g.RearAxle.z - 0.004f) },
                0.0022f, 6, true, true, Vector3.up);
        }
        k.Use(Slot.DarkAlloy);
        k.Push(g.FrontAxle + Polar(0f, radius + 0.008f, -18f), Quaternion.LookRotation(Polar(0f, 1f, 72f), Polar(0f, 1f, -18f)));
        k.Box(Vector3.zero, new Vector3(0.018f, 0.016f, 0.02f));
        k.Pop();
    }

    private static void Fender(Kit k, Vector3 axle, float radius, float from, float to)
    {
        // Channel cross-section (lateral, radial): curls down toward the tyre at the edges.
        const int across = 5;
        const float halfWidth = 0.028f, curl = 0.011f, thickness = 0.0016f;
        int steps = Mathf.CeilToInt(Mathf.Abs(to - from) / 8f);
        var outer = new int[steps + 1, across];
        var inner = new int[steps + 1, across];
        for (int i = 0; i <= steps; i++)
        {
            float angle = Mathf.Lerp(from, to, (float)i / steps);
            Vector3 radial = Polar(0f, 1f, angle);
            for (int j = 0; j < across; j++)
            {
                float u = Mathf.Lerp(-halfWidth, halfWidth, (float)j / (across - 1));
                float v = -curl * (u / halfWidth) * (u / halfWidth);
                Vector2 slope = new Vector2(1f, -2f * curl * u / (halfWidth * halfWidth));
                Vector2 normal2 = new Vector2(-slope.y, slope.x).normalized;
                Vector3 normal = (Vector3.right * normal2.x + radial * normal2.y).normalized;
                Vector3 p = new Vector3(u, axle.y, axle.z) + radial * (radius + v);
                outer[i, j] = k.Vertex(p, normal, new Vector2(u, angle * 0.006f));
                inner[i, j] = k.Vertex(p - normal * thickness, -normal, new Vector2(u, angle * 0.006f));
            }
        }
        for (int i = 0; i < steps; i++)
            for (int j = 0; j < across - 1; j++)
            {
                k.Quad(outer[i, j], outer[i + 1, j], outer[i + 1, j + 1], outer[i, j + 1]);
                k.Quad(inner[i, j], inner[i, j + 1], inner[i + 1, j + 1], inner[i + 1, j]);
            }
    }

    // Rear rack: a platform of two rails and cross bars over the fender, legs to the dropout eyelets, stays to the seat stays,
    // and a tail light with its reflector at the back.
    private static void BuildRack(Kit k, Geometry g)
    {
        Vector3 A = g.RearAxle;
        float y = A.y + g.R + 0.042f, back = A.z - 0.21f, frontZ = A.z + 0.15f;
        k.Use(Slot.Rack);
        var rail = Kit.Bends(new[]
        {
            new Vector3(-0.068f, y, frontZ), new Vector3(-0.068f, y, back), new Vector3(0.068f, y, back), new Vector3(0.068f, y, frontZ)
        }, 0.03f, 5);
        k.Tube(rail, 0.0052f, 8, true, true, Vector3.up);
        for (int i = 1; i <= 3; i++)
        {
            float z = Mathf.Lerp(back, frontZ, i / 4f);
            k.Tube(new[] { new Vector3(-0.068f, y - 0.003f, z), new Vector3(0.068f, y - 0.003f, z) }, 0.0035f, 6, false, false, Vector3.up);
        }
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 eyelet = new Vector3(side * 0.0705f, A.y + 0.012f, A.z - 0.006f);
            k.Tube(Kit.Bends(new[] { new Vector3(side * 0.068f, y, back + 0.03f), new Vector3(side * 0.074f, A.y + 0.12f, A.z - 0.05f), eyelet }, 0.02f, 3), 0.005f, 8, false, true, Vector3.up);
            Vector3 stay = Vector3.Lerp(new Vector3(side * 0.0635f, A.y + 0.016f, A.z + 0.006f), g.BB + g.SeatDir * (TopTubeJoint - 0.018f) + new Vector3(side * 0.016f, 0f, 0f), 0.78f)
                + new Vector3(side * 0.009f, 0f, 0f);
            k.Tube(new[] { new Vector3(side * 0.068f, y, frontZ - 0.005f), stay }, 0.0032f, 6, true, true, Vector3.up);
        }
        k.Use(Slot.Red);
        k.Push(new Vector3(0f, y - 0.022f, back - 0.006f), Quaternion.LookRotation(Vector3.back, Vector3.up));
        k.RoundedBox(Vector3.zero, new Vector3(0.085f, 0.032f, 0.022f), 0.006f, 1);
        k.Pop();
        k.Use(Slot.DarkAlloy);
        k.Push(new Vector3(0f, y - 0.022f, back + 0.012f), Quaternion.LookRotation(Vector3.back, Vector3.up));
        k.RoundedBox(Vector3.zero, new Vector3(0.09f, 0.036f, 0.022f), 0.006f, 1);
        k.Pop();
    }

    // Kickstand folded back along the left chain stay.
    private static void BuildKickstand(Kit k, Geometry g)
    {
        k.Use(Slot.DarkAlloy);
        Vector3 mount = new Vector3(-0.03f, g.BB.y - 0.006f, g.BB.z - 0.085f);
        k.Push(mount, Quaternion.identity);
        k.RoundedBox(Vector3.zero, new Vector3(0.05f, 0.022f, 0.045f), 0.006f, 1);
        k.Pop();
        Vector3 foot = new Vector3(-0.078f, g.RearAxle.y - 0.045f, g.RearAxle.z + 0.07f);
        k.Tube(new[] { mount + new Vector3(-0.024f, -0.004f, -0.008f), foot }, 0.0078f, 10, true, false, Vector3.up, t => Mathf.Lerp(1f, 0.82f, t));
        k.Use(Slot.Tread);
        k.Push(foot, Quaternion.LookRotation(foot - mount, Vector3.up));
        k.RoundedBox(Vector3.zero, new Vector3(0.016f, 0.014f, 0.022f), 0.005f, 1);
        k.Pop();
    }

    // Dynamo lights: a headlamp on the fork crown with its wire down the right blade, a tail lamp on the rear fender.
    private static void BuildLights(Kit k, Geometry g, Palette palette)
    {
        Vector3 lamp = g.Crown + g.SteerNormal * 0.07f - g.SteerDir * 0.004f + new Vector3(0f, 0.018f, 0f);
        k.Use(Slot.DarkAlloy);
        k.Push(g.Crown + g.SteerNormal * 0.048f - g.SteerDir * 0.012f, Quaternion.LookRotation(g.SteerNormal, g.SteerDir));
        k.Box(Vector3.zero, new Vector3(0.012f, 0.008f, 0.03f));
        k.Pop();
        k.Use(palette.SilverCockpit ? Slot.Steel : Slot.DarkAlloy);
        k.Push(lamp, Quaternion.FromToRotation(Vector3.up, Vector3.forward));
        k.Lathe(new[] { new Vector2(0f, -0.028f), new Vector2(0.012f, -0.028f), new Vector2(0.019f, -0.012f), new Vector2(0.021f, 0.014f), new Vector2(0.021f, 0.02f), new Vector2(0.017f, 0.02f) }, 18, 35f);
        k.Pop();
        k.Use(Slot.Lens);
        k.Push(lamp, Quaternion.FromToRotation(Vector3.up, Vector3.forward));
        k.Lathe(new[] { new Vector2(0.017f, 0.02f), new Vector2(0.012f, 0.025f), new Vector2(0f, 0.0265f) }, 18, 50f);
        k.Pop();
        k.Use(Slot.DarkAlloy);
        k.Tube(Kit.CatmullRom(new[]
        {
            g.FrontAxle + new Vector3(0.058f, 0.012f, -0.006f), g.FrontAxle + new Vector3(0.06f, 0f, 0f) + g.SteerDir * 0.18f - g.SteerNormal * 0.016f,
            g.FrontAxle + new Vector3(0.056f, 0f, 0f) + g.SteerDir * 0.33f - g.SteerNormal * 0.012f, lamp + new Vector3(0.01f, -0.012f, -0.02f)
        }, 6), 0.0012f, 5, true, true, Vector3.right);
        if (!palette.Fenders) return;
        // Tail lamp on the rear fender, below the rack's lamp (a reflector).
        k.Use(Slot.Red);
        float r = g.R + 0.016f;
        k.Push(g.RearAxle + Polar(0f, r + 0.012f, -100f), Quaternion.LookRotation(Vector3.back, Vector3.up));
        k.RoundedBox(Vector3.zero, new Vector3(0.045f, 0.03f, 0.016f), 0.005f, 1);
        k.Pop();
    }

    // ------------------------------------------------------------------------------------------------ seat post and saddle

    // The seat post's pivot is the saddle clamp; the post runs down the seat tube (most of it hidden inside).
    private static Kit BuildSeatPost(Geometry g)
    {
        var k = new Kit();
        k.Use(Slot.Cockpit);
        k.Push(Vector3.zero, Quaternion.FromToRotation(Vector3.up, -g.SeatDir));
        k.Lathe(new[] { new Vector2(0f, 0.30f), new Vector2(0.0136f, 0.30f), new Vector2(0.0136f, 0.012f), new Vector2(0.0145f, 0.004f), new Vector2(0.0145f, -0.004f), new Vector2(0f, -0.004f) }, 18, 35f);
        k.Pop();
        k.Use(Slot.DarkAlloy);
        k.RoundedBox(new Vector3(0f, 0.002f, -0.004f), new Vector3(0.032f, 0.014f, 0.056f), 0.004f, 1);
        return k;
    }

    // Saddle top surface height above the rail clamp at a fore-aft position (a long, slightly dished city saddle).
    private static float SaddleSurface(float z)
    {
        float u = (z + 0.03f) / 0.11f;
        return SaddleTop - 0.006f * Mathf.Clamp01(1f - u * u) + (z < -0.1f ? (-0.1f - z) * 0.12f : 0f);
    }

    private static float SaddleHalfWidth(float z)
    {
        // nose (z = 0.125) to the wide rear (z = -0.14)
        float t = Mathf.InverseLerp(0.125f, -0.14f, z);
        float width = Mathf.Lerp(0.028f, 0.105f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.85f, t)));
        if (t > 0.9f) width *= Mathf.Sqrt(Mathf.Max(0.02f, 1f - (t - 0.9f) / 0.1f * 0.75f));
        return width;
    }

    private static Kit BuildSaddle()
    {
        var k = new Kit();
        k.Use(Slot.SaddleCover);
        const int along = 14, around = 16;
        var grid = new Vector3[along, around];
        var centres = new Vector3[along];
        for (int i = 0; i < along; i++)
        {
            float z = Mathf.Lerp(0.125f, -0.14f, (float)i / (along - 1));
            float half = SaddleHalfWidth(z), top = SaddleSurface(z), depth = Mathf.Lerp(0.022f, 0.04f, Mathf.InverseLerp(0.125f, -0.1f, z));
            float mid = top - depth * 0.5f;
            centres[i] = new Vector3(0f, mid, z);
            for (int j = 0; j < around; j++)
            {
                float a = Mathf.PI * 2f * j / around;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                float x = half * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 0.7f);
                float y = depth * 0.5f * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), s > 0f ? 0.55f : 0.9f);
                // The top is domed across: lower toward the edges.
                if (s > 0f) y -= 0.006f * (x / Mathf.Max(0.001f, half)) * (x / Mathf.Max(0.001f, half));
                grid[i, j] = new Vector3(x, mid + y, z);
            }
        }
        var index = new int[along, around];
        for (int i = 0; i < along; i++)
            for (int j = 0; j < around; j++)
            {
                Vector3 dj = grid[i, (j + 1) % around] - grid[i, (j + around - 1) % around];
                Vector3 di = grid[Mathf.Min(along - 1, i + 1), j] - grid[Mathf.Max(0, i - 1), j];
                Vector3 normal = Vector3.Cross(dj, di).normalized;
                if (Vector3.Dot(normal, grid[i, j] - centres[i]) < 0f) normal = -normal;
                index[i, j] = k.Vertex(grid[i, j], normal, new Vector2(j / (float)around, i / (float)along));
            }
        for (int i = 0; i < along - 1; i++)
            for (int j = 0; j < around; j++)
                k.Quad(index[i, j], index[i, (j + 1) % around], index[i + 1, (j + 1) % around], index[i + 1, j]);
        for (int end = 0; end < 2; end++)
        {
            int i = end == 0 ? 0 : along - 1;
            Vector3 normal = end == 0 ? Vector3.forward : Vector3.back;
            int centre = k.Vertex(centres[i], normal, Vector2.zero);
            var ring = new int[around];
            for (int j = 0; j < around; j++) ring[j] = k.Vertex(grid[i, j], normal, Vector2.zero);
            for (int j = 0; j < around; j++) k.Tri(centre, ring[j], ring[(j + 1) % around]);
        }
        // Rails from the nose to the springs, through the clamp; two coil springs under the rear.
        k.Use(Slot.Steel);
        for (int side = -1; side <= 1; side += 2)
        {
            k.Tube(Kit.CatmullRom(new[]
            {
                new Vector3(side * 0.012f, 0.03f, 0.105f), new Vector3(side * 0.02f, 0.004f, 0.05f), new Vector3(side * 0.022f, 0f, -0.01f),
                new Vector3(side * 0.035f, 0.002f, -0.07f), new Vector3(side * 0.05f, 0.002f, -0.105f)
            }, 4), 0.0035f, 6, true, true, Vector3.up);
            k.Push(new Vector3(side * 0.05f, -0.002f, -0.105f), Quaternion.identity);
            k.Tube(Kit.Helix(0.0105f, 0.03f, 0.0019f, 3f, 8), 0.0019f, 4, true, true, Vector3.right);
            k.Pop();
        }
        return k;
    }

    // ------------------------------------------------------------------------------------------------ cockpit

    // The handlebar's pivot is the grips' midpoint; the bar clamp is at BarClamp.
    private static Kit BuildHandlebar()
    {
        var k = new Kit();
        k.Use(Slot.Cockpit);
        var half = new List<Vector3>
        {
            new Vector3(0.045f, BarClamp.y, BarClamp.z), new Vector3(0.10f, BarClamp.y + 0.008f, BarClamp.z - 0.012f), new Vector3(0.143f, -0.017f, 0.092f)
        };
        Vector3 inner = GripCentre(1) - GripAxis(1) * (GripLength * 0.5f + 0.004f), end = GripCentre(1) + GripAxis(1) * (GripLength * 0.5f + 0.004f);
        half.Add(inner);
        var points = new List<Vector3>();
        points.Add(new Vector3(-end.x, end.y, end.z));
        for (int i = half.Count - 1; i >= 0; i--) points.Add(new Vector3(-half[i].x, half[i].y, half[i].z));
        points.Add(new Vector3(0f, BarClamp.y, BarClamp.z));
        points.AddRange(half);
        points.Add(end);
        List<Vector3> bar = Kit.CatmullRom(points, 3);
        k.Tube(bar, 0.0111f, 12, true, true, Vector3.up, t =>
        {
            float x = Mathf.Abs(Mathf.Lerp(-end.x, end.x, t));
            return x < 0.05f ? Mathf.Lerp(1.144f, 1f, Mathf.InverseLerp(0.03f, 0.05f, x)) : 1f;
        });
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 axis = GripAxis(side), forward = GripForward(side), centre = GripCentre(side);
            Vector3 gripInner = centre - axis * (GripLength * 0.5f);
            // Grip with a flared bar-end flange.
            k.Use(Slot.GripCover);
            k.Push(gripInner, Quaternion.FromToRotation(Vector3.up, axis));
            k.Lathe(new[]
            {
                new Vector2(0.0112f, -0.002f), new Vector2(0.0148f, -0.002f), new Vector2(0.0165f, 0.008f), new Vector2(0.0168f, GripLength * 0.5f),
                new Vector2(0.0165f, GripLength - 0.01f), new Vector2(0.0198f, GripLength - 0.004f), new Vector2(0.0198f, GripLength + 0.002f),
                new Vector2(0.012f, GripLength + 0.005f), new Vector2(0f, GripLength + 0.005f)
            }, 14, 35f);
            k.Pop();
            // Brake lever: clamp body inboard of the grip and a blade in front of it.
            k.Use(Slot.DarkAlloy);
            k.Push(gripInner - axis * 0.012f, Quaternion.LookRotation(forward, Vector3.up));
            k.RoundedBox(new Vector3(0f, 0.002f, 0.012f), new Vector3(0.022f, 0.03f, 0.05f), 0.007f, 1);
            k.Pop();
            k.Use(Slot.Cockpit);
            k.Tube(Kit.CatmullRom(new[]
            {
                gripInner - axis * 0.008f + forward * 0.034f, gripInner + axis * 0.03f + forward * 0.036f - Vector3.up * 0.006f,
                gripInner + axis * 0.088f + forward * 0.03f - Vector3.up * 0.014f
            }, 4), 0.0045f, 8, true, true, Vector3.up, t => Mathf.Lerp(1.2f, 0.8f, t), new Vector2(1.5f, 0.6f));
            if (side > 0)
            {
                // Trigger shifter under the bar, inboard of the right lever.
                k.Use(Slot.DarkAlloy);
                k.Push(gripInner - axis * 0.036f - Vector3.up * 0.022f + forward * 0.008f, Quaternion.LookRotation(forward, Vector3.up));
                k.RoundedBox(Vector3.zero, new Vector3(0.03f, 0.024f, 0.04f), 0.008f, 1);
                k.Box(new Vector3(0f, -0.004f, 0.026f), new Vector3(0.008f, 0.012f, 0.02f));
                k.Pop();
            }
        }
        // Bell on the left of the stem.
        Vector3 bell = new Vector3(-0.072f, BarClamp.y + 0.0175f, BarClamp.z + 0.004f);
        k.Use(Slot.DarkAlloy);
        k.Push(new Vector3(bell.x, BarClamp.y, BarClamp.z), AxleTurn);
        k.Lathe(new[] { new Vector2(0.0108f, -0.006f), new Vector2(0.0135f, -0.006f), new Vector2(0.0135f, 0.006f), new Vector2(0.0108f, 0.006f) }, 14);
        k.Pop();
        k.Use(Slot.Steel);
        k.Push(bell, Quaternion.identity);
        k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.026f, 0f), new Vector2(0.026f, 0.004f), new Vector2(0.0225f, 0.013f), new Vector2(0.013f, 0.0195f), new Vector2(0f, 0.021f) }, 20, 50f);
        k.Box(new Vector3(0.022f, 0.008f, -0.02f), new Vector3(0.006f, 0.006f, 0.02f));
        k.Pop();
        return k;
    }

    // ------------------------------------------------------------------------------------------------ crankset and pedals

    // A crank in its pivot's space (the bottom-bracket axis along X, the arm up +Y). The right crank also carries the
    // spindle, the five-arm spider, the 38T chainring and its guard ring.
    private static Kit BuildCrank(int side)
    {
        var k = new Kit();
        k.Use(Slot.RimAlloy);
        var arm = new List<Vector2>();
        const int round = 8;
        for (int i = 0; i <= round; i++)
        {
            float a = Mathf.Lerp(-90f, 90f, (float)i / round) * Mathf.Deg2Rad;
            arm.Add(new Vector2(CrankLength + Mathf.Cos(a) * 0.0125f, Mathf.Sin(a) * 0.0125f));
        }
        for (int i = 0; i <= round; i++)
        {
            float a = Mathf.Lerp(90f, 270f, (float)i / round) * Mathf.Deg2Rad;
            arm.Add(new Vector2(Mathf.Cos(a) * 0.0195f, Mathf.Sin(a) * 0.0195f));
        }
        k.Plate(new Vector3(side * CrankArmX, 0f, 0f), Vector3.up, Vector3.forward, Vector3.right, arm, 0.015f);
        k.Use(Slot.DarkAlloy);
        k.Push(new Vector3(side * (CrankArmX + 0.0075f), 0f, 0f), side > 0 ? AxleTurn : Quaternion.Euler(0f, 0f, 90f));
        k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0125f, 0f), new Vector2(0.0125f, 0.002f), new Vector2(0.0105f, 0.0035f), new Vector2(0f, 0.0035f) }, 14);
        k.Pop();
        k.Push(new Vector3(side * (CrankArmX + 0.0075f), CrankLength, 0f), side > 0 ? AxleTurn : Quaternion.Euler(0f, 0f, 90f));
        k.Lathe(new[] { new Vector2(0.0065f, 0f), new Vector2(0.0118f, 0f), new Vector2(0.0118f, 0.002f), new Vector2(0.0065f, 0.002f) }, 12);
        k.Pop();
        if (side < 0) return k;
        k.Use(Slot.Steel);
        k.Push(Vector3.zero, AxleTurn);
        k.Lathe(new[] { new Vector2(0f, -CrankArmX), new Vector2(0.0122f, -CrankArmX), new Vector2(0.0122f, CrankArmX), new Vector2(0f, CrankArmX) }, 14);
        k.Pop();
        k.Use(Slot.RimAlloy);
        for (int i = 0; i < 5; i++)
        {
            float a = i * 72f + 36f;
            Vector3 from = new Vector3(CrankArmX - 0.004f, 0f, 0f) + Polar(0f, 0.016f, a), to = new Vector3(ChainringX + 0.003f, 0f, 0f) + Polar(0f, 0.0555f, a);
            k.Tube(new[] { from, to }, 0.0052f, 6, false, true, Vector3.right, t => Mathf.Lerp(1.2f, 0.85f, t), new Vector2(0.75f, 1.3f));
            k.Use(Slot.Steel);
            k.Push(new Vector3(ChainringX, 0f, 0f) + Polar(0f, 0.0555f, a), AxleTurn);
            k.Lathe(new[] { new Vector2(0f, -0.004f), new Vector2(0.0045f, -0.004f), new Vector2(0.0045f, 0.0045f), new Vector2(0f, 0.0045f) }, 8);
            k.Pop();
            k.Use(Slot.RimAlloy);
        }
        float pitch = PitchRadius(ChainringTeeth);
        k.Use(Slot.Steel);
        k.ToothedRing(new Vector3(ChainringX, 0f, 0f), ChainringTeeth, pitch + 0.0038f, pitch - 0.0042f, 0.051f, 0.0025f, 0f, true);
        k.Use(Slot.Cockpit);
        k.Push(new Vector3(ChainringX + 0.0085f, 0f, 0f), AxleTurn);
        k.Lathe(new[] { new Vector2(0.0815f, -0.0015f), new Vector2(0.0905f, -0.0015f), new Vector2(0.0905f, 0.0015f), new Vector2(0.0815f, 0.0015f), new Vector2(0.0815f, -0.0015f) }, 48, 40f);
        k.Pop();
        for (int i = 0; i < 5; i++)
        {
            float a = i * 72f + 36f;
            Vector3 from = new Vector3(ChainringX + 0.0085f, 0f, 0f) + Polar(0f, 0.058f, a), to = new Vector3(ChainringX + 0.0085f, 0f, 0f) + Polar(0f, 0.083f, a);
            k.Push((from + to) * 0.5f, Quaternion.LookRotation(to - from, Vector3.right));
            k.Box(Vector3.zero, new Vector3(0.006f, 0.003f, (to - from).magnitude));
            k.Pop();
        }
        return k;
    }

    // A platform pedal centred on its spindle point (the Pedal transform), its spindle reaching in to the crank arm.
    private static Kit BuildPedal(int side)
    {
        var k = new Kit();
        k.Use(Slot.Tread);
        k.RoundedBox(new Vector3(side * 0.004f, 0f, 0f), new Vector3(0.094f, 0.022f, 0.074f), 0.005f, 1);
        k.Use(Slot.Amber);
        k.Box(new Vector3(side * 0.004f, 0f, 0.0375f), new Vector3(0.05f, 0.011f, 0.003f));
        k.Box(new Vector3(side * 0.004f, 0f, -0.0375f), new Vector3(0.05f, 0.011f, 0.003f));
        k.Use(Slot.Steel);
        k.Push(Vector3.zero, side > 0 ? AxleTurn : Quaternion.Euler(0f, 0f, 90f));
        k.Lathe(new[] { new Vector2(0f, -(PedalX - CrankArmX - 0.0075f)), new Vector2(0.0068f, -(PedalX - CrankArmX - 0.0075f)), new Vector2(0.0068f, 0.05f), new Vector2(0f, 0.05f) }, 10);
        k.Pop();
        return k;
    }

    // ------------------------------------------------------------------------------------------------ materials and assets

    private static Dictionary<Slot, Material> Materials(Palette p)
    {
        Material rubber = Mat("Bicycle_Rubber_Tyre", new Color(0.032f, 0.032f, 0.034f), 0f, 0.3f);
        Material tan = Mat("Bicycle_Rubber_Gumwall", new Color(0.50f, 0.34f, 0.19f), 0f, 0.32f);
        Material reflective = Mat("Bicycle_Rubber_ReflectiveBand", new Color(0.46f, 0.47f, 0.48f), 0.15f, 0.55f);
        Material silver = Mat("Bicycle_Aluminium_Polished", new Color(0.80f, 0.81f, 0.82f), 1f, 0.7f);
        Material black = Mat("Bicycle_Aluminium_BlackAnodised", new Color(0.045f, 0.046f, 0.05f), 0.55f, 0.52f);
        Material steel = Mat("Bicycle_Steel_Chrome", new Color(0.86f, 0.86f, 0.87f), 1f, 0.84f);
        Material leather = Mat("Bicycle_Leather_Brown", new Color(0.30f, 0.16f, 0.07f), 0f, 0.46f);
        Material synthetic = Mat("Bicycle_Synthetic_Black", new Color(0.035f, 0.035f, 0.037f), 0f, 0.4f);
        Material paint = Mat("Bicycle_Paint_" + p.Name, p.Paint, p.PaintMetallic, 0.82f);
        return new Dictionary<Slot, Material>
        {
            [Slot.Paint] = paint,
            [Slot.Tread] = rubber,
            [Slot.Sidewall] = p.Gumwall ? tan : rubber,
            [Slot.Stripe] = p.Gumwall ? tan : reflective,
            [Slot.RimAlloy] = silver,
            [Slot.Cockpit] = p.SilverCockpit ? silver : black,
            [Slot.DarkAlloy] = black,
            [Slot.Steel] = steel,
            [Slot.Drivetrain] = Mat("Bicycle_Steel_Drivetrain", new Color(0.30f, 0.30f, 0.32f), 1f, 0.5f),
            [Slot.SaddleCover] = p.Leather ? leather : synthetic,
            [Slot.GripCover] = p.Leather ? leather : synthetic,
            [Slot.Fender] = p.SilverFenders ? silver : synthetic,
            [Slot.Rack] = black,
            [Slot.Amber] = Mat("Bicycle_Reflector_Amber", new Color(0.95f, 0.42f, 0.02f), 0f, 0.88f),
            [Slot.Red] = Mat("Bicycle_Reflector_Red", new Color(0.62f, 0.02f, 0.02f), 0f, 0.88f),
            [Slot.Lens] = Mat("Bicycle_Lens_Clear", new Color(0.82f, 0.83f, 0.84f), 0.2f, 0.95f),
        };
    }

    // URP Lit, opaque, untextured (SRP Batcher compatible on every quality level); updated in place.
    private static Material Mat(string name, Color color, float metallic, float smoothness)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(lit) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        else if (lit != null && m.shader != lit) m.shader = lit;
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    // An existing mesh asset is refilled in place (its GUID and the references to it stay; the same geometry gives the same
    // bytes). CopySerialized into a loaded mesh leaves its GPU data stale, so the kit writes into it directly.
    private static Mesh SaveMesh(Kit kit, string assetName)
    {
        string path = MeshFolder + "/" + assetName + ".asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            Mesh created = kit.ToMesh(assetName);
            AssetDatabase.CreateAsset(created, path);
            return created;
        }
        kit.Fill(existing);
        existing.name = assetName;
        EditorUtility.SetDirty(existing);
        return existing;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ------------------------------------------------------------------------------------------------ mesh kit

    /// <summary>
    /// Minimal procedural mesh kit (after Assets/Editor/CarInternals/CarMeshKit.cs): primitives placed through a matrix
    /// stack go into the submesh of the current material slot, with analytic normals; triangles are wound to face along
    /// their vertex normals and degenerate ones are dropped.
    /// </summary>
    private sealed class Kit
    {
        private readonly List<Vector3> positions = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<List<int>> submeshes = new List<List<int>>();
        private readonly Stack<Matrix4x4> stack = new Stack<Matrix4x4>();
        private Matrix4x4 matrix = Matrix4x4.identity, normalMatrix = Matrix4x4.identity;
        private List<int> current;
        public readonly List<Slot> Slots = new List<Slot>();

        public int TriangleCount
        {
            get
            {
                int n = 0;
                foreach (List<int> s in submeshes) n += s.Count / 3;
                return n;
            }
        }

        public void Use(Slot slot)
        {
            int index = Slots.IndexOf(slot);
            if (index < 0)
            {
                Slots.Add(slot);
                submeshes.Add(new List<int>());
                index = Slots.Count - 1;
            }
            current = submeshes[index];
        }

        public void Push(Vector3 position, Quaternion rotation)
        {
            stack.Push(matrix);
            matrix = matrix * Matrix4x4.TRS(position, rotation, Vector3.one);
            normalMatrix = matrix.inverse.transpose;
        }

        public void Pop()
        {
            matrix = stack.Count > 0 ? stack.Pop() : Matrix4x4.identity;
            normalMatrix = matrix.inverse.transpose;
        }

        public int Vertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            positions.Add(matrix.MultiplyPoint3x4(position));
            Vector3 n = normalMatrix.MultiplyVector(normal);
            normals.Add(n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up);
            uvs.Add(uv);
            return positions.Count - 1;
        }

        public void Tri(int a, int b, int c)
        {
            Vector3 pa = positions[a], pb = positions[b], pc = positions[c];
            Vector3 face = Vector3.Cross(pb - pa, pc - pa);
            if (face.sqrMagnitude < 1e-18f) return;
            if (Vector3.Dot(face, normals[a] + normals[b] + normals[c]) >= 0f) { current.Add(a); current.Add(b); current.Add(c); }
            else { current.Add(a); current.Add(c); current.Add(b); }
        }

        public void Quad(int a, int b, int c, int d)
        {
            Tri(a, b, c);
            Tri(a, c, d);
        }

        /// <summary>Revolves a (radius, height) profile about local +Y; joints turning more than hardAngle stay hard.</summary>
        public void Lathe(IList<Vector2> profile, int segments, float hardAngle = 40f, float startDeg = 0f, float sweepDeg = 360f, bool faceted = false)
        {
            int count = profile.Count;
            if (count < 2) return;
            var segNormal = new Vector2[count - 1];
            for (int i = 0; i < count - 1; i++)
            {
                Vector2 d = profile[i + 1] - profile[i];
                segNormal[i] = d.sqrMagnitude > 1e-14f ? new Vector2(d.y, -d.x).normalized : Vector2.up;
            }
            float cosHard = Mathf.Cos(hardAngle * Mathf.Deg2Rad);
            float length = 0f;
            for (int i = 0; i < count - 1; i++)
            {
                Vector2 p0 = profile[i], p1 = profile[i + 1];
                float segLength = (p1 - p0).magnitude;
                if (segLength < 1e-7f) continue;
                Vector2 n0 = EndNormal(segNormal, i, true, cosHard), n1 = EndNormal(segNormal, i, false, cosHard);
                float v0 = length, v1 = length + segLength;
                length = v1;
                if (p0.x < 1e-6f && p1.x < 1e-6f) continue;
                int baseIndex = positions.Count;
                for (int s = 0; s <= segments; s++)
                {
                    float a = (startDeg + sweepDeg * s / segments) * Mathf.Deg2Rad;
                    float am = faceted ? (startDeg + sweepDeg * (s + 0.5f) / segments) * Mathf.Deg2Rad : a;
                    float c = Mathf.Cos(a), sn = Mathf.Sin(a), cn = Mathf.Cos(am), snn = Mathf.Sin(am);
                    Vertex(new Vector3(p0.x * c, p0.y, p0.x * sn), new Vector3(n0.x * cn, n0.y, n0.x * snn), new Vector2(a * p0.x, v0));
                    Vertex(new Vector3(p1.x * c, p1.y, p1.x * sn), new Vector3(n1.x * cn, n1.y, n1.x * snn), new Vector2(a * p1.x, v1));
                    if (faceted && s < segments)
                    {
                        float a1 = (startDeg + sweepDeg * (s + 1) / segments) * Mathf.Deg2Rad;
                        Vertex(new Vector3(p0.x * Mathf.Cos(a1), p0.y, p0.x * Mathf.Sin(a1)), new Vector3(n0.x * cn, n0.y, n0.x * snn), Vector2.zero);
                        Vertex(new Vector3(p1.x * Mathf.Cos(a1), p1.y, p1.x * Mathf.Sin(a1)), new Vector3(n1.x * cn, n1.y, n1.x * snn), Vector2.zero);
                    }
                }
                if (faceted)
                    for (int s = 0; s < segments; s++)
                    {
                        int a0 = baseIndex + s * 4;
                        Quad(a0, a0 + 2, a0 + 3, a0 + 1);
                    }
                else
                    for (int s = 0; s < segments; s++)
                    {
                        int a0 = baseIndex + s * 2, a1 = a0 + 2;
                        Quad(a0, a1, a1 + 1, a0 + 1);
                    }
            }
        }

        private static Vector2 EndNormal(Vector2[] segNormal, int segment, bool start, float cosHard)
        {
            Vector2 own = segNormal[segment];
            int other = start ? segment - 1 : segment + 1;
            if (other < 0 || other >= segNormal.Length) return own;
            Vector2 neighbour = segNormal[other];
            if (Vector2.Dot(own, neighbour) < cosHard) return own;
            Vector2 avg = own + neighbour;
            return avg.sqrMagnitude > 1e-10f ? avg.normalized : own;
        }

        /// <summary>Revolves a profile with explicit (radius, height) normals about local +Y.</summary>
        public void Revolve(IList<Vector2> profile, IList<Vector2> profileNormals, int segments)
        {
            int start = positions.Count, count = profile.Count;
            for (int s = 0; s <= segments; s++)
            {
                float a = Mathf.PI * 2f * s / segments, c = Mathf.Cos(a), sn = Mathf.Sin(a);
                for (int i = 0; i < count; i++)
                    Vertex(new Vector3(profile[i].x * c, profile[i].y, profile[i].x * sn),
                        new Vector3(profileNormals[i].x * c, profileNormals[i].y, profileNormals[i].x * sn), new Vector2(a, i));
            }
            for (int s = 0; s < segments; s++)
                for (int i = 0; i < count - 1; i++)
                {
                    int a = start + s * count + i, b = a + count;
                    Quad(a, b, b + 1, a + 1);
                }
        }

        public static List<Vector3> CatmullRom(IList<Vector3> points, int samplesPerSpan)
        {
            var result = new List<Vector3>();
            int n = points.Count;
            if (n < 3) { result.AddRange(points); return result; }
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = i > 0 ? points[i - 1] : points[0] * 2f - points[1];
                Vector3 p1 = points[i], p2 = points[i + 1];
                Vector3 p3 = i + 2 < n ? points[i + 2] : points[n - 1] * 2f - points[n - 2];
                float t0 = 0f;
                float t1 = t0 + Mathf.Pow(Mathf.Max(1e-6f, (p1 - p0).magnitude), 0.5f);
                float t2 = t1 + Mathf.Pow(Mathf.Max(1e-6f, (p2 - p1).magnitude), 0.5f);
                float t3 = t2 + Mathf.Pow(Mathf.Max(1e-6f, (p3 - p2).magnitude), 0.5f);
                for (int s = 0; s < samplesPerSpan; s++)
                {
                    float t = Mathf.Lerp(t1, t2, (float)s / samplesPerSpan);
                    Vector3 a1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1;
                    Vector3 a2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2;
                    Vector3 a3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3;
                    Vector3 b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2;
                    Vector3 b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3;
                    result.Add((t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2);
                }
            }
            result.Add(points[n - 1]);
            return result;
        }

        /// <summary>Polyline with rounded corners (bend radius).</summary>
        public static List<Vector3> Bends(IList<Vector3> corners, float bendRadius, int samplesPerBend)
        {
            var result = new List<Vector3>();
            int n = corners.Count;
            if (n < 3) { result.AddRange(corners); return result; }
            result.Add(corners[0]);
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 a = corners[i - 1], b = corners[i], c = corners[i + 1];
                Vector3 da = (a - b).normalized, dc = (c - b).normalized;
                float angle = Vector3.Angle(da, dc) * Mathf.Deg2Rad;
                if (angle > Mathf.PI - 0.01f) { result.Add(b); continue; }
                float cut = Mathf.Min(bendRadius / Mathf.Tan(angle * 0.5f), Mathf.Min((a - b).magnitude, (c - b).magnitude) * 0.45f);
                Vector3 p0 = b + da * cut, p2 = b + dc * cut;
                for (int s = 0; s <= samplesPerBend; s++)
                {
                    float t = (float)s / samplesPerBend;
                    result.Add((1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * b + t * t * p2);
                }
            }
            result.Add(corners[n - 1]);
            return result;
        }

        /// <summary>Centre line of a coil spring along local +Y.</summary>
        public static List<Vector3> Helix(float coilRadius, float length, float wire, float turns, int samplesPerTurn)
        {
            var path = new List<Vector3>();
            int samples = Mathf.CeilToInt(turns * samplesPerTurn);
            for (int i = 0; i <= samples; i++)
            {
                float turn = turns * i / samples, a = turn * Mathf.PI * 2f;
                path.Add(new Vector3(coilRadius * Mathf.Cos(a), wire + (length - 2f * wire) * turn / turns, coilRadius * Mathf.Sin(a)));
            }
            return path;
        }

        /// <summary>Rotation-minimising frames along a polyline (double reflection).</summary>
        private static void Frames(IList<Vector3> path, Vector3 normalHint, out Vector3[] tangents, out Vector3[] frameNormals)
        {
            int n = path.Count;
            tangents = new Vector3[n];
            frameNormals = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 d = i == 0 ? path[1] - path[0] : i == n - 1 ? path[n - 1] - path[n - 2] : path[i + 1] - path[i - 1];
                tangents[i] = d.sqrMagnitude > 1e-14f ? d.normalized : (i > 0 ? tangents[i - 1] : Vector3.forward);
            }
            Vector3 r0 = Vector3.ProjectOnPlane(normalHint, tangents[0]);
            if (r0.sqrMagnitude < 1e-8f) r0 = Vector3.ProjectOnPlane(Mathf.Abs(tangents[0].y) < 0.9f ? Vector3.up : Vector3.right, tangents[0]);
            frameNormals[0] = r0.normalized;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 v1 = path[i + 1] - path[i];
                float c1 = Vector3.Dot(v1, v1);
                if (c1 < 1e-14f) { frameNormals[i + 1] = frameNormals[i]; continue; }
                Vector3 rL = frameNormals[i] - (2f / c1) * Vector3.Dot(v1, frameNormals[i]) * v1;
                Vector3 tL = tangents[i] - (2f / c1) * Vector3.Dot(v1, tangents[i]) * v1;
                Vector3 v2 = tangents[i + 1] - tL;
                float c2 = Vector3.Dot(v2, v2);
                Vector3 r1 = c2 < 1e-14f ? rL : rL - (2f / c2) * Vector3.Dot(v2, rL) * v2;
                frameNormals[i + 1] = Vector3.ProjectOnPlane(r1, tangents[i + 1]).normalized;
            }
        }

        /// <summary>Round (or elliptical) tube along a path; radiusScale scales the radius over 0..1 of the length.</summary>
        public void Tube(IList<Vector3> path, float radius, int sides, bool capStart, bool capEnd, Vector3 normalHint,
            Func<float, float> radiusScale = null, Vector2? ellipse = null)
        {
            int n = path.Count;
            if (n < 2) return;
            Frames(path, normalHint, out Vector3[] t, out Vector3[] r);
            var along = new float[n];
            for (int i = 1; i < n; i++) along[i] = along[i - 1] + (path[i] - path[i - 1]).magnitude;
            float total = Mathf.Max(1e-6f, along[n - 1]);
            Vector2 e = ellipse ?? Vector2.one;
            int start = positions.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 b = Vector3.Cross(t[i], r[i]);
                float rad = radius * (radiusScale != null ? radiusScale(along[i] / total) : 1f);
                for (int s = 0; s <= sides; s++)
                {
                    float a = Mathf.PI * 2f * s / sides, c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    Vertex(path[i] + r[i] * (c * rad * e.x) + b * (sn * rad * e.y), r[i] * (c / e.x) + b * (sn / e.y), new Vector2(a * radius, along[i]));
                }
            }
            for (int i = 0; i < n - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a0 = start + i * (sides + 1) + s, a1 = a0 + 1, b0 = a0 + sides + 1, b1 = b0 + 1;
                    Quad(a0, a1, b1, b0);
                }
            if (capStart) Cap(path[0], -t[0], r[0], Vector3.Cross(t[0], r[0]), radius * (radiusScale != null ? radiusScale(0f) : 1f), e, sides);
            if (capEnd) Cap(path[n - 1], t[n - 1], r[n - 1], Vector3.Cross(t[n - 1], r[n - 1]), radius * (radiusScale != null ? radiusScale(1f) : 1f), e, sides);
        }

        private void Cap(Vector3 centre, Vector3 normal, Vector3 r, Vector3 b, float radius, Vector2 e, int sides)
        {
            int c = Vertex(centre, normal, Vector2.zero), first = positions.Count;
            for (int s = 0; s < sides; s++)
            {
                float a = Mathf.PI * 2f * s / sides;
                Vertex(centre + r * (Mathf.Cos(a) * radius * e.x) + b * (Mathf.Sin(a) * radius * e.y), normal, Vector2.zero);
            }
            for (int s = 0; s < sides; s++) Tri(c, first + s, first + (s + 1) % sides);
        }

        public void Box(Vector3 center, Vector3 size) => RoundedBox(center, size, 0f, 0);

        /// <summary>Box with rounded edges and corners (fillet radius, segments per quarter round).</summary>
        public void RoundedBox(Vector3 center, Vector3 size, float radius, int segments)
        {
            Vector3 h = size * 0.5f;
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.999f);
            if (radius < 1e-5f) segments = 0;
            Vector3 inner = h - Vector3.one * radius;
            for (int face = 0; face < 6; face++)
            {
                int axis = face / 2;
                float sign = face % 2 == 0 ? 1f : -1f;
                int ua = axis == 0 ? 2 : 0, va = axis == 1 ? 2 : 1;
                float[] su = Samples(h[ua], radius, segments), sv = Samples(h[va], radius, segments);
                Vector3 faceN = Vector3.zero;
                faceN[axis] = sign;
                int nu = su.Length, nv = sv.Length, start = positions.Count;
                for (int j = 0; j < nv; j++)
                    for (int i = 0; i < nu; i++)
                    {
                        Vector3 p = Vector3.zero;
                        p[axis] = sign * h[axis];
                        p[ua] = su[i];
                        p[va] = sv[j];
                        Vector3 q = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                        Vector3 d = p - q;
                        Vector3 nrm = radius > 0f && d.sqrMagnitude > 1e-12f ? d.normalized : faceN;
                        Vertex(center + (radius > 0f ? q + nrm * radius : p), nrm, new Vector2(p[ua], p[va]));
                    }
                for (int j = 0; j < nv - 1; j++)
                    for (int i = 0; i < nu - 1; i++)
                    {
                        int a = start + j * nu + i;
                        Quad(a, a + 1, a + nu + 1, a + nu);
                    }
            }
        }

        private static float[] Samples(float half, float radius, int segments)
        {
            if (segments <= 0) return new[] { -half, half };
            var list = new List<float>();
            float inner = half - radius;
            for (int s = 0; s <= segments; s++) list.Add(-inner - radius * Mathf.Cos(Mathf.PI * 0.5f * s / segments));
            for (int s = 0; s <= segments; s++) list.Add(inner + radius * Mathf.Sin(Mathf.PI * 0.5f * s / segments));
            return list.ToArray();
        }

        /// <summary>Flat plate: a convex outline in (u, v) at origin, extruded along w by its thickness (centred).</summary>
        public void Plate(Vector3 origin, Vector3 u, Vector3 v, Vector3 w, IList<Vector2> outline, float thickness, float hardAngle = 35f)
        {
            int n = outline.Count;
            float area = 0f;
            for (int i = 0; i < n; i++) area += outline[i].x * outline[(i + 1) % n].y - outline[(i + 1) % n].x * outline[i].y;
            float orient = area >= 0f ? 1f : -1f, half = thickness * 0.5f, cosHard = Mathf.Cos(hardAngle * Mathf.Deg2Rad);
            var edge = new Vector2[n];
            for (int j = 0; j < n; j++)
            {
                Vector2 d = outline[(j + 1) % n] - outline[j];
                edge[j] = (new Vector2(d.y, -d.x) * orient).normalized;
            }
            Vector3 At(Vector2 p, float depth) => origin + u * p.x + v * p.y + w * depth;
            Vector3 N(Vector2 e) => (u * e.x + v * e.y).normalized;
            for (int j = 0; j < n; j++)
            {
                Vector2 p0 = outline[j], p1 = outline[(j + 1) % n], n0 = edge[j], n1 = edge[j];
                Vector2 prev = edge[(j + n - 1) % n], next = edge[(j + 1) % n];
                if (Vector2.Dot(n0, prev) >= cosHard) n0 = (n0 + prev).normalized;
                if (Vector2.Dot(n1, next) >= cosHard) n1 = (n1 + next).normalized;
                int a = Vertex(At(p0, -half), N(n0), Vector2.zero), b = Vertex(At(p1, -half), N(n1), Vector2.zero);
                int c = Vertex(At(p1, half), N(n1), Vector2.zero), d = Vertex(At(p0, half), N(n0), Vector2.zero);
                Quad(a, b, c, d);
            }
            Vector2 centroid = Vector2.zero;
            foreach (Vector2 p in outline) centroid += p;
            centroid /= n;
            for (int s = -1; s <= 1; s += 2)
            {
                int c = Vertex(At(centroid, s * half), w * s, Vector2.zero), first = positions.Count;
                for (int j = 0; j < n; j++) Vertex(At(outline[j], s * half), w * s, Vector2.zero);
                for (int j = 0; j < n; j++) Tri(c, first + j, first + (j + 1) % n);
            }
        }

        /// <summary>Rounded slot outline from a to b with the given half width.</summary>
        public static List<Vector2> Slot2D(Vector2 a, Vector2 b, float halfWidth, int segments)
        {
            var list = new List<Vector2>();
            Vector2 d = (b - a).normalized, nrm = new Vector2(-d.y, d.x);
            for (int i = 0; i <= segments; i++)
            {
                float t = Mathf.PI * i / segments;
                list.Add(b + (-nrm * Mathf.Cos(t) + d * Mathf.Sin(t)) * halfWidth);
            }
            for (int i = 0; i <= segments; i++)
            {
                float t = Mathf.PI * i / segments;
                list.Add(a + (nrm * Mathf.Cos(t) - d * Mathf.Sin(t)) * halfWidth);
            }
            return list;
        }

        /// <summary>Toothed ring (sprocket, chainring, pulley) in the plane across X at centre: triangular teeth between the
        /// root and tip radii, faces down to the inner radius, an outer wall and optionally an inner wall.</summary>
        public void ToothedRing(Vector3 c, int teeth, float rTip, float rRoot, float rInner, float thickness, float phaseDeg, bool innerWall = false, bool inboardFace = true)
        {
            int n = teeth * 2;
            var outer = new Vector2[n];
            var angle = new float[n];
            for (int i = 0; i < n; i++)
            {
                angle[i] = phaseDeg * Mathf.Deg2Rad + Mathf.PI * 2f * i / n;
                float r = i % 2 == 0 ? rRoot : rTip;
                outer[i] = new Vector2(Mathf.Cos(angle[i]) * r, Mathf.Sin(angle[i]) * r);
            }
            for (int side = inboardFace ? -1 : 1; side <= 1; side += 2)
            {
                float x = c.x + side * thickness * 0.5f;
                Vector3 normal = new Vector3(side, 0f, 0f);
                int start = positions.Count;
                for (int i = 0; i < n; i++)
                {
                    Vertex(new Vector3(x, c.y + outer[i].x, c.z + outer[i].y), normal, Vector2.zero);
                    Vertex(new Vector3(x, c.y + Mathf.Cos(angle[i]) * rInner, c.z + Mathf.Sin(angle[i]) * rInner), normal, Vector2.zero);
                }
                for (int i = 0; i < n; i++)
                {
                    int i1 = (i + 1) % n;
                    Quad(start + i * 2, start + i1 * 2, start + i1 * 2 + 1, start + i * 2 + 1);
                }
            }
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = outer[i], p1 = outer[(i + 1) % n], d = p1 - p0;
                Vector3 normal = new Vector3(0f, d.y, -d.x).normalized;
                float x0 = c.x - thickness * 0.5f, x1 = c.x + thickness * 0.5f;
                int a = Vertex(new Vector3(x0, c.y + p0.x, c.z + p0.y), normal, Vector2.zero), b = Vertex(new Vector3(x0, c.y + p1.x, c.z + p1.y), normal, Vector2.zero);
                int e = Vertex(new Vector3(x1, c.y + p1.x, c.z + p1.y), normal, Vector2.zero), f = Vertex(new Vector3(x1, c.y + p0.x, c.z + p0.y), normal, Vector2.zero);
                Quad(a, b, e, f);
            }
            if (!innerWall) return;
            int wall = positions.Count;
            for (int i = 0; i <= n; i++)
            {
                float a = angle[i % n];
                Vector3 inward = new Vector3(0f, -Mathf.Cos(a), -Mathf.Sin(a));
                Vertex(new Vector3(c.x - thickness * 0.5f, c.y + Mathf.Cos(a) * rInner, c.z + Mathf.Sin(a) * rInner), inward, Vector2.zero);
                Vertex(new Vector3(c.x + thickness * 0.5f, c.y + Mathf.Cos(a) * rInner, c.z + Mathf.Sin(a) * rInner), inward, Vector2.zero);
            }
            for (int i = 0; i < n; i++) Quad(wall + i * 2, wall + i * 2 + 2, wall + i * 2 + 3, wall + i * 2 + 1);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            Fill(mesh);
            return mesh;
        }

        /// <summary>Writes the geometry into a mesh, replacing what it held.</summary>
        public void Fill(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshes.Count;
            for (int s = 0; s < submeshes.Count; s++) mesh.SetTriangles(submeshes[s], s, false);
            mesh.RecalculateBounds();
        }
    }
}
#endif
