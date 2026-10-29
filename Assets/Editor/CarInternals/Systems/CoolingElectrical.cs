#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CarInternals
{
    /// <summary>
    /// Hardpoints shared by the cooling and electrical builders, in car space, from the measured layout (V6 renderer
    /// boxes, wheel wells) and from ray maps of the free space (the inner skin of the nose, the bay floor, the rear
    /// compartment). The engine's belt end is on the left: the accessory drive runs in one plane just outboard of the
    /// crank pulley; the radiator stands in the front-left corner ahead of the left wheel well, angled to the nose.
    /// </summary>
    public sealed class ThermalElectricalGeometry
    {
        private static readonly ConditionalWeakTable<CarLayout, ThermalElectricalGeometry> Cache = new ConditionalWeakTable<CarLayout, ThermalElectricalGeometry>();

        // Accessory drive (belt plane at BeltX; pulley centres carry the plane's x).
        public float BeltX, CoverX, CrankFaceX;
        public Vector3 CrankC, PumpC, AltC, CompC;
        public const float CrankR = 0.070f, PumpR = 0.042f, AltR = 0.030f, CompR = 0.055f, BeltWidth = 0.021f;
        // Radiator module frame: origin at the core centre, U along the core toward the engine, N forward (air side).
        public Vector3 RadO, RadU, RadN;
        public Quaternion RadRot;
        public const float RadHalfW = 0.1725f, CoreHalfW = 0.135f, CoreHalfH = 0.16f, CoreHalfD = 0.015f;
        // Thermostat flange on the front head's end face; coolant spigots.
        public Vector3 ThermoFlange;
        // Condenser in the nose.
        public Vector3 CondC;
        public const float CondHalfW = 0.37f, CondHalfH = 0.07f;
        // HVAC module (left, against the bulkhead) and the electrical boxes.
        public Bounds Hvac, Pdb, Ecu, Battery, RearFuseBox;
        public Vector3 StarterC;
        public float BayFloorY;

        public static ThermalElectricalGeometry Of(CarLayout l, InternalsProbe probe)
        {
            if (Cache.TryGetValue(l, out ThermalElectricalGeometry g)) return g;
            g = new ThermalElectricalGeometry();
            g.Compute(l, probe);
            Cache.Add(l, g);
            return g;
        }

        private static Bounds Part(CarLayout l, string name, Vector3 min, Vector3 max)
        {
            if (l.V6Parts.TryGetValue(name, out Bounds b)) return b;
            var f = new Bounds();
            f.SetMinMax(min, max);
            return f;
        }

        private void Compute(CarLayout l, InternalsProbe probe)
        {
            Bounds crank = Part(l, "Engine belt wheels 2", new Vector3(-0.265f, 0.191f, 1.796f), new Vector3(-0.219f, 0.357f, 1.962f));
            Bounds cover = Part(l, "Engine side cover", new Vector3(-0.269f, 0.264f, 1.640f), new Vector3(-0.217f, 0.579f, 2.118f));
            Bounds headFront = Part(l, "Engine cylinder head001", new Vector3(-0.217f, 0.414f, 2.018f), new Vector3(0.291f, 0.575f, 2.189f));
            CrankFaceX = crank.min.x;
            CoverX = cover.min.x;
            BeltX = CrankFaceX - 0.022f;
            float cy = crank.center.y, cz = crank.center.z;
            CrankC = new Vector3(BeltX, cy, cz);
            // Pump high at the rear, alternator low at the rear, compressor low at the front (all clear of the
            // superchargers above y 0.369 and of the rack below z 1.68).
            PumpC = new Vector3(BeltX, cy + 0.046f, cz - 0.147f);
            AltC = new Vector3(BeltX, cy - 0.072f, cz - 0.127f);
            CompC = new Vector3(BeltX, cy - 0.052f, cz + 0.166f);
            BayFloorY = probe.Down(new Vector3(-0.38f, 0.3f, 1.9f), 0.4f, out float fy) ? fy : 0.13f;

            // Radiator: chord of the nose's inner skin from (-0.89, 1.95) to (-0.52, 2.16) (rays at y 0.16..0.49),
            // core mid-plane 4 cm behind it, module centred on the chord between |x| 0.84 and 0.54.
            Vector3 pOut = new Vector3(-0.89f, 0f, 1.95f), pIn = new Vector3(-0.52f, 0f, 2.16f);
            RadU = (pIn - pOut).normalized;
            RadN = Vector3.Cross(RadU, Vector3.up).normalized; // (-0.49, 0, 0.87): forward and outward
            if (RadN.z < 0f) RadN = -RadN;
            Vector3 o = pOut + RadU * 0.23f - RadN * 0.040f;
            RadO = new Vector3(o.x, 0.335f, o.z);
            RadRot = Quaternion.LookRotation(RadN, Vector3.up);

            ThermoFlange = new Vector3(headFront.min.x + 0.001f, headFront.min.y + 0.051f, headFront.max.z - 0.044f);
            CondC = new Vector3(0f, 0.2425f, 2.172f);

            Hvac = MinMax(new Vector3(-0.575f, 0.545f, 1.625f), new Vector3(-0.385f, 0.715f, 1.84f));
            // Integration (combined car): the brake booster, its pedal cross-shaft and lever fill the old spot (z 1.525..1.615),
            // so the front box sits 9 cm further back on the wheel-arch wall (probed free space).
            Pdb = MinMax(new Vector3(-0.58f, 0.285f, 1.435f), new Vector3(-0.50f, 0.435f, 1.525f));
            Ecu = MinMax(new Vector3(0.385f, 0.60f, 1.64f), new Vector3(0.565f, 0.64f, 1.80f));
            // Integration (combined car): battery 6 cm forward and 1.7 cm inboard, clear of the rear impact beam and of
            // the rear subframe's body-mount bolt ends; rear fuse box 3 cm inboard, clear of the left rear crash box.
            Battery = MinMax(new Vector3(-0.3635f, 0.335f, -1.80f), new Vector3(-0.1565f, 0.51f, -1.64f));
            RearFuseBox = MinMax(new Vector3(-0.49f, 0.335f, -1.80f), new Vector3(-0.39f, 0.465f, -1.725f));
            // Integration: the drivetrain's bell housing (r 0.128 about the crank) and torque rod fill the space below
            // and behind the crank, and the lower dash shell reaches z 1.72 at floor level, so the starter rides on top
            // of the bell housing, behind the clutch actuator (probed free space).
            StarterC = new Vector3(0.30f, 0.425f, 1.71f);
        }

        public static Bounds MinMax(Vector3 a, Vector3 b)
        {
            var r = new Bounds();
            r.SetMinMax(Vector3.Min(a, b), Vector3.Max(a, b));
            return r;
        }

        /// <summary>Radiator-frame point: along the core (toward the engine), up, and forward (air side).</summary>
        public Vector3 Rad(float u, float up, float n) => RadO + RadU * u + Vector3.up * up + RadN * n;
    }

    /// <summary>Geometry helpers of the cooling and electrical builders (candidates for the kit).</summary>
    public static class ThermalKit
    {
        /// <summary>
        /// Closed belt path around pulleys in one plane x = const, given in counter-clockwise order in (z, y); the
        /// belt runs on the outside of every pulley. radialOffset moves the path off the pulley rims (half the belt
        /// thickness).
        /// </summary>
        public static List<Vector3> BeltPath(IList<Vector3> centres, IList<float> radii, float radialOffset, int arcSamples = 10)
        {
            int n = centres.Count;
            var normals = new Vector2[n]; // normal of the segment from i to i+1
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector2 ci = new Vector2(centres[i].z, centres[i].y), cj = new Vector2(centres[j].z, centres[j].y);
                float d = Vector2.Distance(ci, cj);
                Vector2 e = (cj - ci) / d;
                Vector2 right = new Vector2(e.y, -e.x);
                float a = Mathf.Clamp((radii[i] - radii[j]) / d, -0.99f, 0.99f);
                normals[i] = (e * a + right * Mathf.Sqrt(1f - a * a)).normalized;
            }
            var path = new List<Vector3>();
            float x = centres[0].x;
            for (int j = 0; j < n; j++)
            {
                int i = (j + n - 1) % n;
                Vector2 c = new Vector2(centres[j].z, centres[j].y);
                float r = radii[j] + radialOffset;
                float a0 = Mathf.Atan2(normals[i].y, normals[i].x);
                float a1 = Mathf.Atan2(normals[j].y, normals[j].x);
                while (a1 < a0) a1 += Mathf.PI * 2f;
                int steps = Mathf.Max(2, Mathf.CeilToInt((a1 - a0) / (Mathf.PI * 2f) * arcSamples * 3f));
                for (int s = 0; s <= steps; s++)
                {
                    float a = Mathf.Lerp(a0, a1, (float)s / steps);
                    Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    path.Add(new Vector3(x, p.y, p.x));
                }
            }
            path.Add(path[0]);
            return path;
        }

        /// <summary>Poly-V pulley along local +Y from 0 to width: grooved rim, web and hub. Faces -Y at y = 0.</summary>
        public static void PolyVPulley(MeshKit k, float r, float width, int ribs, float hubR, int segments = 32)
        {
            var p = new List<Vector2> { new Vector2(hubR * 0.55f, 0f), new Vector2(r + 0.002f, 0f) };
            float y0 = width * 0.08f, y1 = width * 0.92f;
            p.Add(new Vector2(r, y0));
            for (int i = 0; i < ribs; i++)
            {
                float a = Mathf.Lerp(y0, y1, (float)i / ribs), b = Mathf.Lerp(y0, y1, (i + 0.5f) / ribs), c = Mathf.Lerp(y0, y1, (i + 1f) / ribs);
                p.Add(new Vector2(r - 0.0022f, b));
                p.Add(new Vector2(r, c));
            }
            p.Add(new Vector2(r + 0.002f, width));
            p.Add(new Vector2(hubR, width));
            p.Add(new Vector2(hubR, width + 0.006f));
            p.Add(new Vector2(hubR * 0.55f, width + 0.006f));
            p.Add(new Vector2(hubR * 0.55f, 0f));
            k.Lathe(p, segments, 50f);
        }

        /// <summary>Worm-drive hose clamp around local +Y at y = 0: band and screw housing (on +X).</summary>
        public static void HoseClamp(MeshKit k, float r)
        {
            k.Sleeve(r + 0.0015f, r, -0.0045f, 0.0045f, 18);
            k.RoundedBox(new Vector3(r + 0.004f, 0f, 0f), new Vector3(0.006f, 0.009f, 0.012f), 0.0015f, 1);
            k.Push(new Vector3(r + 0.005f, 0f, 0f), Quaternion.Euler(90f, 0f, 0f));
            k.Cylinder(0.0022f, -0.009f, 0.009f, 8);
            k.Pop();
        }

        /// <summary>Sealed connector body along local +Y (mating face at y = 0, cable exit at y = length), with a latch.</summary>
        public static void Connector(MeshKit k, float w, float h, float length)
        {
            k.RoundedBox(new Vector3(0f, length * 0.5f, 0f), new Vector3(w, length, h), Mathf.Min(w, h) * 0.18f, 2);
            k.Box(new Vector3(0f, length * 0.55f, h * 0.5f + 0.0015f), new Vector3(w * 0.4f, length * 0.5f, 0.003f));
            k.Lathe(new[] { new Vector2(0f, length), new Vector2(Mathf.Min(w, h) * 0.32f, length), new Vector2(Mathf.Min(w, h) * 0.22f, length + 0.01f), new Vector2(0f, length + 0.01f) }, 10);
        }

        /// <summary>Axial fan along local +Y: hub, n pitched blades and the tip ring (air leaves toward +Y).</summary>
        public static void Fan(MeshKit k, float r, float hubR, int blades, float depth)
        {
            k.Lathe(new[] { new Vector2(0f, -depth * 0.5f), new Vector2(hubR, -depth * 0.5f), new Vector2(hubR, depth * 0.4f), new Vector2(hubR * 0.6f, depth * 0.55f), new Vector2(0f, depth * 0.55f) }, 20, 40f);
            for (int i = 0; i < blades; i++)
            {
                float a = 360f * i / blades;
                k.Push(Vector3.zero, Quaternion.Euler(0f, a, 0f));
                float pitch = 0.42f;
                var c = new List<Vector3>
                {
                    new Vector3(hubR * 0.95f, -depth * 0.32f, -0.016f), new Vector3(r - 0.004f, -depth * 0.3f * pitch, -0.03f),
                    new Vector3(r - 0.004f, depth * 0.3f * pitch, 0.03f), new Vector3(hubR * 0.95f, depth * 0.32f, 0.016f)
                };
                k.PlanarPrism(c, 0.0022f);
                k.Pop();
            }
            k.Sleeve(r + 0.004f, r, -depth * 0.45f, depth * 0.45f, 32);
        }

        /// <summary>
        /// Brazed core of a heat exchanger in local X (width) and Y (height), depth along Z: flat tubes along X every
        /// pitch and a serpentine fin field between them (submesh 0 tubes, submesh 1 fins).
        /// </summary>
        public static void Core(MeshKit k, float halfW, float halfH, float halfD, float pitch, float finPitch)
        {
            int rows = Mathf.Max(2, Mathf.RoundToInt(halfH * 2f / pitch));
            float step = halfH * 2f / rows;
            k.Sub(1);
            // fin field: zig-zag faces front and back, sides closed
            int ridges = Mathf.Max(4, Mathf.RoundToInt(halfW * 2f / finPitch));
            foreach (float zSign in new[] { 1f, -1f })
            {
                float z = zSign * halfD;
                for (int i = 0; i < ridges; i++)
                {
                    float x0 = -halfW + halfW * 2f * i / ridges, x1 = -halfW + halfW * 2f * (i + 0.5f) / ridges, x2 = -halfW + halfW * 2f * (i + 1f) / ridges;
                    float zin = z - zSign * 0.0016f;
                    Vector3 n0 = new Vector3(-0.6f, 0f, zSign).normalized, n1 = new Vector3(0.6f, 0f, zSign).normalized;
                    // planar UVs in metres across the core face (the fin texture's passages run along v)
                    int a = k.Vertex(new Vector3(x0, -halfH, z), n0, new Vector2(x0, -halfH)), b = k.Vertex(new Vector3(x1, -halfH, zin), n0, new Vector2(x1, -halfH));
                    int c = k.Vertex(new Vector3(x1, halfH, zin), n0, new Vector2(x1, halfH)), d = k.Vertex(new Vector3(x0, halfH, z), n0, new Vector2(x0, halfH));
                    k.Quad(a, b, c, d);
                    a = k.Vertex(new Vector3(x1, -halfH, zin), n1, new Vector2(x1, -halfH)); b = k.Vertex(new Vector3(x2, -halfH, z), n1, new Vector2(x2, -halfH));
                    c = k.Vertex(new Vector3(x2, halfH, z), n1, new Vector2(x2, halfH)); d = k.Vertex(new Vector3(x1, halfH, zin), n1, new Vector2(x1, halfH));
                    k.Quad(a, b, c, d);
                }
            }
            k.Box(new Vector3(-halfW + 0.001f, 0f, 0f), new Vector3(0.002f, halfH * 2f, halfD * 2f - 0.002f));
            k.Box(new Vector3(halfW - 0.001f, 0f, 0f), new Vector3(0.002f, halfH * 2f, halfD * 2f - 0.002f));
            k.Sub(0);
            for (int i = 0; i <= rows; i++)
            {
                float y = -halfH + step * i;
                k.Box(new Vector3(0f, y, 0f), new Vector3(halfW * 2f, 0.0022f, halfD * 2f + 0.0016f));
            }
        }

        /// <summary>Rounded tube along a polyline with bends (pipes, cables): car-space points, mesh relative to origin.</summary>
        public static void Run(MeshKit k, Vector3 origin, IList<Vector3> corners, float radius, int sides, float bend, bool caps = true)
        {
            k.Push(Matrix4x4.Translate(-origin));
            k.Tube(MeshKit.Bends(corners, bend, 6), radius, sides, caps, caps);
            k.Pop();
        }

        /// <summary>Smooth tube through points (hoses): car-space points, mesh relative to origin.</summary>
        public static void Smooth(MeshKit k, Vector3 origin, IList<Vector3> points, float radius, int sides, int samples = 8, Func<float, float> scale = null)
        {
            k.Push(Matrix4x4.Translate(-origin));
            k.Tube(MeshKit.CatmullRom(points, samples), radius, sides, true, true, null, scale);
            k.Pop();
        }

        /// <summary>Point and tangent at a fraction of a CatmullRom path (for clamps and clips on a hose).</summary>
        public static void At(IList<Vector3> points, float t, out Vector3 p, out Vector3 dir, int samples = 8)
        {
            List<Vector3> path = MeshKit.CatmullRom(points, samples);
            float total = 0f;
            for (int i = 1; i < path.Count; i++) total += Vector3.Distance(path[i - 1], path[i]);
            float want = total * Mathf.Clamp01(t), acc = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                float seg = Vector3.Distance(path[i - 1], path[i]);
                if (acc + seg >= want || i == path.Count - 1)
                {
                    float f = seg > 1e-6f ? (want - acc) / seg : 0f;
                    p = Vector3.Lerp(path[i - 1], path[i], Mathf.Clamp01(f));
                    dir = (path[i] - path[i - 1]).normalized;
                    return;
                }
                acc += seg;
            }
            p = path[path.Count - 1];
            dir = (path[path.Count - 1] - path[path.Count - 2]).normalized;
        }

        /// <summary>The same for a Bends polyline.</summary>
        public static void AtBends(IList<Vector3> corners, float bend, float t, out Vector3 p, out Vector3 dir)
        {
            List<Vector3> path = MeshKit.Bends(corners, bend, 6);
            float total = 0f;
            for (int i = 1; i < path.Count; i++) total += Vector3.Distance(path[i - 1], path[i]);
            float want = total * Mathf.Clamp01(t), acc = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                float seg = Vector3.Distance(path[i - 1], path[i]);
                if (acc + seg >= want || i == path.Count - 1)
                {
                    float f = seg > 1e-6f ? (want - acc) / seg : 0f;
                    p = Vector3.Lerp(path[i - 1], path[i], Mathf.Clamp01(f));
                    dir = (path[i] - path[i - 1]).normalized;
                    return;
                }
                acc += seg;
            }
            p = path[path.Count - 1];
            dir = (path[path.Count - 1] - path[path.Count - 2]).normalized;
        }

        /// <summary>Flat bar bent along a polyline (brackets, straps): width along the up hint, thickness across.</summary>
        public static void Strap(MeshKit k, Vector3 origin, IList<Vector3> corners, float width, float thickness, Vector3 up, float bend = 0.01f)
        {
            k.Push(Matrix4x4.Translate(-origin));
            k.Sweep(Profiles.Rect(thickness, width), MeshKit.Bends(corners, bend, 4), up, true, true, 30f);
            k.Pop();
        }

        /// <summary>Rectangular plate (half sizes hx, hy, thickness t, centred on z = 0) with a round hole at the centre.</summary>
        public static void PlateWithHole(MeshKit k, float hx, float hy, float t, float r, int segments = 32)
        {
            foreach (float s in new[] { 1f, -1f })
            {
                float z = s * t * 0.5f;
                for (int i = 0; i < segments; i++)
                {
                    float a0 = Mathf.PI * 2f * i / segments, a1 = Mathf.PI * 2f * (i + 1) / segments;
                    Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)), d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                    Vector2 o0 = d0 * Mathf.Min(hx / Mathf.Max(1e-4f, Mathf.Abs(d0.x)), hy / Mathf.Max(1e-4f, Mathf.Abs(d0.y)));
                    Vector2 o1 = d1 * Mathf.Min(hx / Mathf.Max(1e-4f, Mathf.Abs(d1.x)), hy / Mathf.Max(1e-4f, Mathf.Abs(d1.y)));
                    Vector3 n = new Vector3(0f, 0f, s);
                    int a = k.Vertex(new Vector3(d0.x * r, d0.y * r, z), n, d0 * r), b = k.Vertex(new Vector3(d1.x * r, d1.y * r, z), n, d1 * r);
                    int c = k.Vertex(new Vector3(o1.x, o1.y, z), n, o1), d = k.Vertex(new Vector3(o0.x, o0.y, z), n, o0);
                    k.Quad(a, b, c, d);
                }
            }
            k.Push(Vector3.zero, Quaternion.Euler(90f, 0f, 0f));
            k.Lathe(new[] { new Vector2(r, t * 0.5f), new Vector2(r, -t * 0.5f) }, segments);
            k.Pop();
            // outer rim
            k.Box(new Vector3(0f, hy - 0.001f, 0f), new Vector3(hx * 2f, 0.002f, t));
            k.Box(new Vector3(0f, -hy + 0.001f, 0f), new Vector3(hx * 2f, 0.002f, t));
            k.Box(new Vector3(hx - 0.001f, 0f, 0f), new Vector3(0.002f, hy * 2f, t));
            k.Box(new Vector3(-hx + 0.001f, 0f, 0f), new Vector3(0.002f, hy * 2f, t));
        }
    }

    // ==================================================================================================== cooling

    /// <summary>
    /// Cooling, climate and the accessory belt: the radiator module in the front-left corner, the coolant circuit
    /// between the engine's belt end and the radiator (pump, thermostat, hoses, expansion tank, heater hoses), the
    /// poly-V belt drive, the A/C circuit (compressor, nose condenser with receiver-drier, lines) and the HVAC module
    /// against the left bulkhead.
    /// </summary>
    [CarInternalsSubsystem("Cooling", 70)]
    public sealed class CoolingSystem : ICarInternalsSubsystem
    {
        /// <summary>False in the combined car: the left bulkhead pocket is the brake booster's (see Build).</summary>
        public static readonly bool BuildHvac = false;

        private InternalsContext ctx;
        private CarPalette m;
        private ThermalElectricalGeometry g;
        private Material fin, hoseRubber;

        public void Build(InternalsContext context)
        {
            ctx = context;
            m = ctx.Mats;
            g = ThermalElectricalGeometry.Of(ctx.Layout, ctx.Probe);
            fin = m.Get("CI_Aluminium_HeatExchangerFin", new Color(0.50f, 0.51f, 0.52f), 0.85f, 0.3f);
            hoseRubber = m.Rubber;
            Transform system = ctx.System("Cooling");
            ctx.Note("cooling: radiator centre " + CarLayout.V(g.RadO) + " normal " + CarLayout.V(g.RadN) + ", belt plane x " + CarLayout.F(g.BeltX) +
                     ", crank " + CarLayout.V(g.CrankC) + " pump " + CarLayout.V(g.PumpC) + " alternator " + CarLayout.V(g.AltC) + " compressor " + CarLayout.V(g.CompC) +
                     ", thermostat flange " + CarLayout.V(g.ThermoFlange) + ", condenser " + CarLayout.V(g.CondC) + ", HVAC " + CarLayout.B(g.Hvac) + ", bay floor y " + CarLayout.F(g.BayFloorY));
            RadiatorModule(system);
            CoolantCircuit(system);
            BeltDrive(system);
            AcCircuit(system);
            // Integration (combined car): the left bulkhead pocket is the brake booster's and master cylinder's (the
            // pedal cross-shaft drives it), and no other place in the bay holds the climate module, so the HVAC module,
            // the heater hoses and the evaporator side of the A/C circuit stay out until the module can sit behind the
            // dash (see BuildHvac).
            if (BuildHvac) Hvac(system);
            else ctx.Note("cooling: HVAC module, heater hoses and A/C evaporator side not built in the combined car (left bulkhead pocket is the brake booster's)");
            Contacts();
        }

        private void Contacts()
        {
            // Bolted to the bay floor, the engine and the bulkhead; hoses slid over spigots of the other assemblies.
            ctx.AllowContact("cooling.radiator-module.carrier", "CarConcept");
            ctx.AllowContact("cooling.radiator-module.carrier", "InteriorFloor");
            ctx.AllowContact("cooling.coolant-circuit.water-pump", "V6/");
            ctx.AllowContact("cooling.coolant-circuit.thermostat", "V6/");
            ctx.AllowContact("cooling.coolant-circuit.inlet-neck", "V6/");
            ctx.AllowContact("cooling.belt-drive.crank-pulley", "V6/");
            ctx.AllowContact("cooling.ac-circuit.compressor.bracket", "V6/");
            ctx.AllowContact("cooling.coolant-circuit", "cooling.radiator-module.radiator");
            ctx.AllowContact("cooling.coolant-circuit.expansion-tank.bracket", "cooling.radiator-module");
            ctx.AllowContact("cooling.coolant-circuit.heater-hoses", "cooling.hvac");
            ctx.AllowContact("cooling.belt-drive.belt", "cooling.coolant-circuit.water-pump.pulley");
            ctx.AllowContact("cooling.belt-drive.belt", "cooling.ac-circuit.compressor.clutch");
            ctx.AllowContact("cooling.belt-drive.belt", "electrical.charging-starting.alternator.pulley");
            ctx.AllowContact("cooling.ac-circuit.lines", "cooling.ac-circuit");
            ctx.AllowContact("cooling.ac-circuit.lines", "cooling.hvac");
            ctx.AllowContact("cooling.ac-circuit.condenser.bracket", "CarConcept");
            ctx.AllowContact("cooling.ac-circuit.condenser.bracket", "Body");
            ctx.AllowContact("cooling.ac-circuit.condenser.bracket", "InteriorFloor");
            ctx.AllowContact("cooling.hvac.ducts", "InteriorDash");
            ctx.AllowContact("cooling.hvac.case.bracket", "InteriorDash");
            ctx.AllowContact("cooling.hvac.case.bracket", "InteriorFloor");
            ctx.AllowContact("cooling.hvac.case.bracket", "CarConcept");
        }

        private static Txt T(string en, string uz, string de, string du) => new Txt(en, uz, de, du);

        // ------------------------------------------------------------------------------------------------ radiator

        private void RadiatorModule(Transform system)
        {
            string id = "cooling.radiator-module";
            Transform asm = ctx.Assembly(system, id, T("Radiator Module", "Radiator moduli",
                    "Two-pass aluminium radiator with twin electric fans, angled into the front-left corner behind the nose intake.",
                    "Burun havo kiritgichi ortida old chap burchakka qiyshaytirib o‘rnatilgan, ikki elektr ventilyatorli ikki yo‘lli alyuminiy radiator."),
                "Aluminium and glass-filled nylon", g.RadN + Vector3.up * 0.3f, 1.3f);
            Quaternion rot = g.RadRot;
            const float hw = ThermalElectricalGeometry.CoreHalfW, hh = ThermalElectricalGeometry.CoreHalfH, hd = ThermalElectricalGeometry.CoreHalfD;

            Transform rad = ctx.SubAssembly(asm, id + ".radiator", T("Radiator", "Radiator",
                    "Brazed core between two moulded end tanks; a baffle in the engine-side tank sends the coolant across the core and back.",
                    "Ikki quyma bak orasidagi kavsharlangan o‘zak; dvigatel tomondagi bakdagi to‘siq suyuqlikni o‘zak bo‘ylab borib-qaytaradi."),
                "Aluminium 3003, PA66-GF30", g.RadN, 0.8f);
            Mesh core = ctx.Mesh("radiator-core", k => ThermalKit.Core(k, hw, hh, hd, 0.012f, 0.004f), 2);
            ctx.Part(rad, id + ".radiator.core", T("Radiator core", "Radiator o‘zagi",
                    "27 flat tubes with louvred serpentine fins, 30 mm deep, brazed in one piece; sheds about 60 kW at full load.",
                    "Jalyuzli ilonsimon qovurg‘ali 27 ta yassi quvur, 30 mm qalinlik, bir butun kavsharlangan; to‘liq yukda taxminan 60 kVt issiqlik chiqaradi."),
                "Brazed aluminium 3003", core, new[] { m.MachinedAluminium, fin }, g.RadO, rot, false, g.RadN, 0.5f);
            float tankC = (ThermalElectricalGeometry.RadHalfW + hw) * 0.5f;
            Mesh outer = ctx.Mesh("radiator-tank-outer", k => Tank(k, false), 2);
            Mesh inner = ctx.Mesh("radiator-tank-inner", k => Tank(k, true), 2);
            ctx.Part(rad, id + ".radiator.outer-tank", T("Outer end tank", "Tashqi bak",
                    "Glass-filled nylon return tank crimped onto the aluminium header plate; carries the drain cock.",
                    "Alyuminiy kollektor plastinasiga qisib o‘rnatilgan shisha tolali neylon qaytish baki; to‘kish jo‘mragi shu yerda."),
                "PA66-GF30, aluminium header", outer, new[] { m.BlackPlastic, m.CastAluminium }, g.Rad(-tankC, 0f, 0f), rot, false, -g.RadU, 0.5f);
            ctx.Part(rad, id + ".radiator.inner-tank", T("Inlet and outlet tank", "Kirish-chiqish baki",
                    "Divided tank on the engine side: hot coolant enters at the top half, cooled coolant leaves from the bottom half.",
                    "Dvigatel tomondagi bo‘lingan bak: issiq suyuqlik yuqori yarmiga kiradi, sovigan suyuqlik pastki yarmidan chiqadi."),
                "PA66-GF30, aluminium header", inner, new[] { m.BlackPlastic, m.CastAluminium }, g.Rad(tankC, 0f, 0f), rot, false, g.RadU, 0.5f);
            Mesh drain = ctx.Mesh("radiator-drain-cock", k => k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.006f, 0f), new Vector2(0.006f, 0.008f), new Vector2(0.011f, 0.008f), new Vector2(0.011f, 0.014f), new Vector2(0f, 0.014f) }, 8, 40f, 0f, 360f, true));
            ctx.Part(rad, id + ".radiator.drain-cock", T("Drain cock", "To‘kish jo‘mragi", "Quarter-turn plastic plug for draining the coolant.", "Sovutish suyuqligini to‘kish uchun chorak aylanadigan plastik tiqin."),
                "PA66", drain, new[] { m.GreyPlastic }, g.Rad(-tankC, -hh - 0.015f, -0.003f), Quaternion.LookRotation(g.RadN, Vector3.down), true, Vector3.down, 0.4f);

            Transform fans = ctx.SubAssembly(asm, id + ".fans", T("Cooling Fans", "Sovutish ventilyatorlari",
                    "Two 260 W brushless puller fans in a moulded shroud, speed-controlled by their own PWM module.",
                    "Quyma g‘ilofdagi ikkita 260 Vt cho‘tkasiz tortuvchi ventilyator; tezligi o‘z PWM moduli bilan boshqariladi."),
                "PA66-GF30 and steel", -g.RadN, 0.9f);
            Mesh shroud = ctx.Mesh("fan-shroud", k =>
            {
                foreach (float sx in new[] { -1f, 1f })
                {
                    k.Push(new Vector3(sx * 0.068f, 0f, -0.024f), Quaternion.identity);
                    ThermalKit.PlateWithHole(k, 0.068f, hh + 0.004f, 0.003f, 0.066f, 28);
                    k.Push(Vector3.zero, Quaternion.Euler(-90f, 0f, 0f));
                    k.Sleeve(0.069f, 0.066f, 0f, 0.034f, 28);
                    k.Pop();
                    // three motor struts from the venturi ring to the motor flange
                    for (int i = 0; i < 3; i++)
                    {
                        float a = (90f + 120f * i) * Mathf.Deg2Rad;
                        Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                        k.Box(d * 0.0495f + new Vector3(0f, 0f, -0.034f), new Vector3(Mathf.Abs(d.x) * 0.033f + 0.006f, Mathf.Abs(d.y) * 0.033f + 0.006f, 0.004f));
                    }
                    k.Pop();
                }
                // side flanges that clip onto the tanks
                foreach (float sx in new[] { -1f, 1f })
                    k.Box(new Vector3(sx * 0.1395f, 0f, -0.0195f), new Vector3(0.006f, hh * 2f, 0.012f));
            });
            ctx.Part(fans, id + ".fans.shroud", T("Fan shroud", "Ventilyator g‘ilofi",
                    "Moulded shroud with venturi rings: makes the fans pull air through the whole core, not around it.",
                    "Venturi halqali quyma g‘ilof: ventilyatorlar havoni o‘zak atrofidan emas, butun o‘zak orqali tortadi."),
                "PA66-GF30", shroud, new[] { m.BlackPlastic }, g.RadO, rot, false, -g.RadN, 0.5f);
            Mesh fan = ctx.Mesh("fan-rotor-r060", k => ThermalKit.Fan(k, 0.060f, 0.024f, 7, 0.026f));
            Mesh motor = ctx.Mesh("fan-motor", k =>
            {
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.03f, 0f), new Vector2(0.032f, 0.004f), new Vector2(0.032f, 0.024f), new Vector2(0.028f, 0.028f), new Vector2(0f, 0.028f) }, 24, 40f);
                for (int i = 0; i < 12; i++)
                {
                    float a = 30f * i * Mathf.Deg2Rad;
                    k.Box(new Vector3(Mathf.Cos(a) * 0.031f, 0.014f, Mathf.Sin(a) * 0.031f), new Vector3(0.003f, 0.016f, 0.003f));
                }
            });
            Mesh plug = ctx.Mesh("connector-fan", k => ThermalKit.Connector(k, 0.018f, 0.012f, 0.022f));
            foreach (float sx in new[] { -1f, 1f })
            {
                string side = sx < 0 ? "outer" : "inner";
                string sideUz = sx < 0 ? "tashqi" : "ichki";
                Quaternion back = InternalsContext.AlongY(-g.RadN, Vector3.up);
                ctx.Part(fans, id + ".fans." + side + "-rotor", T((sx < 0 ? "Outer" : "Inner") + " fan rotor", sideUz.Substring(0, 1).ToUpper() + sideUz.Substring(1) + " ventilyator parragi",
                        "Seven-blade 250 mm rotor with swept, pitched blades and a tip ring that cuts noise.",
                        "Shovqinni kamaytiruvchi uch halqali, yetti qanotli 250 mm parrak."),
                    "PA66-GF30", fan, new[] { m.BlackPlastic }, g.Rad(sx * 0.068f, 0f, -0.044f), back, false, -g.RadN, 0.5f);
                ctx.Part(fans, id + ".fans." + side + "-motor", T((sx < 0 ? "Outer" : "Inner") + " fan motor", sideUz.Substring(0, 1).ToUpper() + sideUz.Substring(1) + " ventilyator motori",
                        "Brushless 12 V motor with cooling fins, carried by the shroud's three struts.",
                        "G‘ilofning uchta tirgovichida turgan, sovutish qovurg‘ali cho‘tkasiz 12 V motor."),
                    "Steel can, copper windings", motor, new[] { m.BlackSteel }, g.Rad(sx * 0.068f, 0f, -0.060f), back, false, -g.RadN, 0.7f);
                ctx.Part(fans, id + ".fans." + side + "-connector", T("Fan motor connector", "Ventilyator motori ulagichi",
                        "Sealed two-pin power connector of the fan motor.", "Ventilyator motorining zichlangan ikki kontaktli quvvat ulagichi."),
                    "PBT", plug, new[] { m.BlackPlastic }, g.Rad(sx * 0.068f, 0.04f, -0.075f), InternalsContext.AlongY(Vector3.up, g.RadN), true, Vector3.up, 0.4f);
            }
            Mesh pwm = ctx.Mesh("fan-pwm-module", k =>
            {
                k.RoundedBox(new Vector3(0f, 0f, 0f), new Vector3(0.05f, 0.04f, 0.016f), 0.003f, 1);
                for (int i = 0; i < 6; i++) k.Box(new Vector3(-0.02f + i * 0.008f, 0f, -0.011f), new Vector3(0.0025f, 0.034f, 0.006f));
            });
            ctx.Part(fans, id + ".fans.controller", T("Fan control module", "Ventilyator boshqaruv moduli",
                    "Finned PWM power stage that sets fan speed from the engine computer's request.",
                    "Dvigatel kompyuteri so‘roviga ko‘ra ventilyator tezligini o‘rnatuvchi qovurg‘ali PWM quvvat bloki."),
                "Die-cast aluminium", pwm, new[] { m.CastAluminium }, g.Rad(0f, -0.115f, -0.034f), rot, false, -g.RadN, 0.4f);

            Transform carrier = ctx.SubAssembly(asm, id + ".carrier", T("Radiator Carrier", "Radiator tayanchi",
                    "Pressed-steel cradle on the bay floor with rubber isolators below and a clamped upper bar above the tanks.",
                    "Bo‘linma tagidagi presslangan po‘lat tayanch: pastda rezina amortizatorlar, yuqorida baklarni qisuvchi to‘sin."),
                "Steel and EPDM", Vector3.down, 0.8f);
            float floorY = g.BayFloorY;
            float bottomY = g.RadO.y - hh - 0.015f; // tank bottoms
            Mesh cradle = ctx.Mesh("radiator-cradle", k =>
            {
                float h = bottomY - 0.006f - floorY;
                float yc = floorY + h * 0.5f - g.RadO.y;
                k.Box(new Vector3(0f, floorY + 0.0015f - g.RadO.y, 0f), new Vector3(ThermalElectricalGeometry.RadHalfW * 2f + 0.03f, 0.003f, 0.05f));
                foreach (float sz in new[] { -1f, 1f })
                    k.Box(new Vector3(0f, yc, sz * 0.0235f), new Vector3(ThermalElectricalGeometry.RadHalfW * 2f + 0.03f, h, 0.003f));
            });
            ctx.Part(carrier, id + ".carrier.cradle", T("Lower cradle", "Pastki tayanch",
                    "U-section steel cradle bolted to the bay floor; the radiator sits on it through two isolators.",
                    "Bo‘linma tagiga boltlangan U-kesimli po‘lat tayanch; radiator unga ikki amortizator orqali tayanadi."),
                "Pressed steel, e-coated", cradle, new[] { m.BlackSteel }, g.RadO, rot, false, Vector3.down, 0.5f);
            Mesh iso = ctx.Mesh("radiator-isolator", k => k.Lathe(new[] { new Vector2(0.004f, 0f), new Vector2(0.012f, 0f), new Vector2(0.013f, 0.003f), new Vector2(0.009f, 0.006f), new Vector2(0.004f, 0.006f), new Vector2(0.004f, 0f) }, 14, 40f));
            Mesh pin = ctx.Mesh("bolt-M6x16", k => Fasteners.HexBolt(k, 0.006f, 0.016f));
            foreach (float sx in new[] { -1f, 1f })
            {
                string s = sx < 0 ? "outer" : "inner";
                ctx.Part(carrier, id + ".carrier.isolator-" + s, T("Lower isolator", "Pastki amortizator", "EPDM grommet that keeps engine and road vibration out of the brazed core.", "Dvigatel va yo‘l tebranishini kavsharlangan o‘zakdan ajratuvchi EPDM vtulka."),
                    "EPDM", iso, new[] { m.Rubber }, g.Rad(sx * tankC, -hh - 0.015f - 0.006f, -0.003f), Quaternion.identity, true, Vector3.down, 0.4f);
                ctx.Part(carrier, id + ".carrier.bolt-" + s, T("Floor bolt M6", "Pol bolti M6", "Bolts the cradle to the bay floor.", "Tayanchni bo‘linma tagiga mahkamlaydi."),
                    "Steel 8.8", pin, new[] { m.ZincSteel }, g.Rad(sx * (ThermalElectricalGeometry.RadHalfW + 0.006f), floorY + 0.003f - g.RadO.y, 0.012f), Quaternion.identity, true, Vector3.up, 0.4f);
            }
            float barY = g.RadO.y + hh + 0.03f;
            Mesh bar = ctx.Mesh("radiator-upper-bar", k =>
            {
                Vector3 a = new Vector3(-ThermalElectricalGeometry.RadHalfW - 0.012f, barY - g.RadO.y, -0.036f), b = new Vector3(ThermalElectricalGeometry.RadHalfW + 0.012f, barY - g.RadO.y, -0.036f);
                a.x -= 0.008f; b.x += 0.008f;
                k.Tube(new[] { a, b }, 0.007f, 12);
                foreach (float sx in new[] { -1f, 1f })
                {
                    Vector3 top = new Vector3(sx * (ThermalElectricalGeometry.RadHalfW + 0.02f), barY - g.RadO.y, -0.036f);
                    Vector3 bottom = new Vector3(sx * (ThermalElectricalGeometry.RadHalfW + 0.02f), floorY + 0.003f - g.RadO.y, -0.05f);
                    k.Tube(MeshKit.Bends(new[] { top, bottom }, 0.01f), 0.007f, 12);
                    // clamp tab over the tank top
                    k.Box(new Vector3(sx * tankC, barY - g.RadO.y - 0.008f, -0.02f), new Vector3(0.03f, 0.004f, 0.036f));
                }
            });
            ctx.Part(carrier, id + ".carrier.frame", T("Upper bar and posts", "Yuqori to‘sin va ustunlar",
                    "Steel tube frame from the cradle over the tanks; its tabs clamp the radiator's top through rubber pads.",
                    "Tayanchdan baklar ustiga o‘tuvchi po‘lat quvur rama; tilchalari radiator ustini rezina yostiqlar orqali qisadi."),
                "Steel tube, e-coated", bar, new[] { m.BlackSteel }, g.RadO, rot, false, Vector3.up, 0.6f);
        }

        // Radiator end tank in its own frame (x along the core, y up, z forward); the inner tank carries the spigots.
        private void Tank(MeshKit k, bool inner)
        {
            float s = inner ? 1f : -1f;
            k.Sub(0);
            k.RoundedBox(new Vector3(s * 0.002f, 0f, -0.003f), new Vector3(0.033f, ThermalElectricalGeometry.CoreHalfH * 2f + 0.03f, 0.036f), 0.009f, 2);
            for (int i = 0; i < 5; i++)
                k.Box(new Vector3(s * 0.0185f, -0.12f + i * 0.06f, -0.003f), new Vector3(0.004f, 0.004f, 0.03f));
            if (inner)
            {
                foreach (float y in new[] { 0.14f, -0.13f })
                {
                    k.Push(new Vector3(0.016f, y, -0.003f), InternalsContext.AlongY(Vector3.right));
                    k.Lathe(new[] { new Vector2(0.012f, 0f), new Vector2(0.016f, 0f), new Vector2(0.016f, 0.024f), new Vector2(0.0185f, 0.028f), new Vector2(0.016f, 0.031f), new Vector2(0.016f, 0.0345f), new Vector2(0.012f, 0.0345f), new Vector2(0.012f, 0f) }, 18, 40f);
                    k.Pop();
                }
                // degas spigot on top
                k.Push(new Vector3(0f, ThermalElectricalGeometry.CoreHalfH + 0.012f, -0.003f), Quaternion.identity);
                k.Sleeve(0.0055f, 0.0035f, 0f, 0.022f, 12);
                k.Pop();
            }
            k.Sub(1);
            k.Box(new Vector3(-s * 0.0172f, 0f, -0.002f), new Vector3(0.003f, ThermalElectricalGeometry.CoreHalfH * 2f + 0.034f, 0.042f));
        }

        // ------------------------------------------------------------------------------------------------ coolant

        private void CoolantCircuit(Transform system)
        {
            string id = "cooling.coolant-circuit";
            Transform asm = ctx.Assembly(system, id, T("Coolant Circuit", "Sovutish suyuqligi konturi",
                    "Belt-driven pump, wax thermostat, radiator and heater hoses and the pressurised expansion tank of the engine's 50/50 glycol loop.",
                    "Dvigatelning 50/50 glikolli konturi: tasmali nasos, mumli termostat, radiator va isitgich shlanglari hamda bosimli kengaytirish baki."),
                "Aluminium and EPDM", new Vector3(-0.6f, 0.8f, 0.2f), 1.1f);
            WaterPump(asm, id);
            Thermostat(asm, id);
            Hoses(asm, id);
            ExpansionTank(asm, id);
            if (BuildHvac) HeaterHoses(asm, id);
        }

        private void WaterPump(Transform asm, string id)
        {
            Transform pump = ctx.SubAssembly(asm, id + ".water-pump", T("Water Pump", "Suv nasosi",
                    "Centrifugal pump on the timing cover: its impeller runs in a volute cast into the cover, fed by the inlet neck.",
                    "Taqsimlash qopqog‘idagi markazdan qochma nasos: parragi qopqoqqa quyilgan spiral kamerada aylanadi, kirish patrubkasidan oziqlanadi."),
                "Cast aluminium and steel", Vector3.left, 0.9f);
            Vector3 c = g.PumpC;
            float face = g.CrankFaceX;
            Quaternion outward = InternalsContext.AlongY(Vector3.left, Vector3.up);
            Mesh body = ctx.Mesh("water-pump-body", k =>
            {
                // flange plate on the cover, bearing snout toward the pulley
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.044f, 0f), new Vector2(0.045f, 0.004f), new Vector2(0.03f, 0.008f), new Vector2(0.018f, 0.009f), new Vector2(0.016f, 0.0105f), new Vector2(0f, 0.0105f) }, 28, 35f);
                for (int i = 0; i < 4; i++)
                {
                    float a = (45f + 90f * i) * Mathf.Deg2Rad;
                    k.Push(new Vector3(Mathf.Cos(a) * 0.036f, 0f, Mathf.Sin(a) * 0.036f), Quaternion.identity);
                    k.Cylinder(0.0055f, 0f, 0.006f, 10);
                    k.Pop();
                }
            });
            ctx.Part(pump, id + ".water-pump.body", T("Pump body", "Nasos korpusi",
                    "Die-cast flange with the sealed bearing snout; four bolts close the volute in the timing cover.",
                    "Zichlangan podshipnik bo‘yinli quyma flanets; to‘rt bolt taqsimlash qopqog‘idagi spiral kamerani yopadi."),
                "Die-cast aluminium", body, new[] { m.CastAluminium }, new Vector3(face, c.y, c.z), outward, false, Vector3.left, 0.4f);
            float pw = ThermalElectricalGeometry.BeltWidth + 0.002f;
            Mesh pulley = ctx.Mesh("pulley-water-pump", k => ThermalKit.PolyVPulley(k, ThermalElectricalGeometry.PumpR, pw, 6, 0.016f));
            ctx.Part(pump, id + ".water-pump.pulley", T("Water pump pulley", "Suv nasosi shkivi",
                    "Six-rib poly-V pulley pressed on the pump shaft; the belt turns it at about 1.2 times crank speed.",
                    "Nasos valiga presslangan olti ariqchali poly-V shkiv; tasma uni tirsakli valdan taxminan 1,2 marta tez aylantiradi."),
                "Pressed steel, zinc", pulley, new[] { m.ZincSteel }, new Vector3(g.BeltX + pw * 0.5f, c.y, c.z), outward, false, Vector3.left, 0.6f);
            Mesh bolt = ctx.Mesh("bolt-M6x25", k => Fasteners.HexBolt(k, 0.006f, 0.025f));
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + 90f * i) * Mathf.Deg2Rad;
                Vector3 p = new Vector3(face - 0.006f, c.y + Mathf.Sin(a) * 0.036f, c.z + Mathf.Cos(a) * 0.036f);
                ctx.Part(pump, id + ".water-pump.bolt-" + (i + 1), T("Pump bolt M6", "Nasos bolti M6", "Holds the pump body to the timing cover.", "Nasos korpusini taqsimlash qopqog‘iga mahkamlaydi."),
                    "Steel 8.8", bolt, new[] { m.ZincSteel }, p, outward, true, Vector3.left, 0.3f);
            }
            // Inlet neck on the block's front face: the lower hose feeds the pump through a passage in the cover.
            Transform neck = ctx.SubAssembly(asm, id + ".inlet-neck", T("Coolant Inlet Neck", "Kirish patrubkasi",
                    "Cast elbow on the block's front face that takes the cooled coolant from the lower hose to the pump's suction passage.",
                    "Blokning old yuzidagi quyma tirsak: sovigan suyuqlikni pastki shlangdan nasosning so‘rish kanaliga olib boradi."),
                "Cast aluminium", Vector3.forward, 0.6f);
            Vector3 n0 = NeckBase();
            Mesh neckMesh = ctx.Mesh("coolant-inlet-neck", k =>
            {
                k.RoundedBox(new Vector3(0f, 0f, 0.003f), new Vector3(0.05f, 0.044f, 0.006f), 0.004f, 1);
                k.Tube(MeshKit.Bends(new[] { new Vector3(0f, 0f, 0.004f), new Vector3(0f, 0f, 0.03f), new Vector3(-0.03f, 0f, 0.05f) }, 0.018f, 6), 0.0165f, 16);
                k.Push(new Vector3(-0.03f, 0f, 0.05f), InternalsContext.AlongY(Vector3.left));
                k.Lathe(new[] { new Vector2(0.012f, 0f), new Vector2(0.016f, 0f), new Vector2(0.016f, 0.018f), new Vector2(0.0185f, 0.021f), new Vector2(0.016f, 0.024f), new Vector2(0.016f, 0.03f), new Vector2(0.012f, 0.03f), new Vector2(0.012f, 0f) }, 18, 40f);
                k.Pop();
            });
            ctx.Part(neck, id + ".inlet-neck.housing", T("Inlet neck", "Kirish patrubkasi", "Elbow with a beaded spigot for the lower hose.", "Pastki shlang uchun bo‘rtmali uchli tirsak."),
                "Die-cast aluminium", neckMesh, new[] { m.CastAluminium }, n0, Quaternion.identity, false, Vector3.forward, 0.4f);
            foreach (float sx in new[] { -1f, 1f })
                ctx.Part(neck, id + ".inlet-neck.bolt-" + (sx < 0 ? "left" : "right"), T("Neck bolt M6", "Patrubka bolti M6", "Bolts the neck to the block.", "Patrubkani blokka mahkamlaydi."),
                    "Steel 8.8", bolt, new[] { m.ZincSteel }, n0 + new Vector3(sx * 0.019f, 0.012f, 0.006f), InternalsContext.AlongY(Vector3.forward), true, Vector3.forward, 0.3f);
        }

        private Vector3 NeckBase() => new Vector3(-0.205f, 0.345f, 2.043f);
        private Vector3 NeckTip() => NeckBase() + new Vector3(-0.06f, 0f, 0.05f);

        private Vector3 ThermoOutlet() => g.ThermoFlange + new Vector3(-0.074f, 0f, 0f);

        private void Thermostat(Transform asm, string id)
        {
            Transform th = ctx.SubAssembly(asm, id + ".thermostat", T("Thermostat Housing", "Termostat korpusi",
                    "Coolant outlet on the front head: the wax thermostat opens at 88 °C and lets coolant to the radiator; two ports feed the heater.",
                    "Old kallakdagi suyuqlik chiqishi: mumli termostat 88 °C da ochilib suyuqlikni radiatorga o‘tkazadi; ikki teshik isitgichni ta’minlaydi."),
                "Cast aluminium", Vector3.left, 0.9f);
            Vector3 f = g.ThermoFlange;
            Mesh housing = ctx.Mesh("thermostat-housing", k =>
            {
                Quaternion left = InternalsContext.AlongY(Vector3.left, Vector3.up);
                k.Push(Vector3.zero, left);
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.034f, 0f), new Vector2(0.034f, 0.005f), new Vector2(0.025f, 0.008f), new Vector2(0.024f, 0.034f), new Vector2(0.016f, 0.04f), new Vector2(0.016f, 0.054f), new Vector2(0.0185f, 0.058f), new Vector2(0.016f, 0.062f), new Vector2(0.016f, 0.074f), new Vector2(0.012f, 0.074f), new Vector2(0.012f, 0.04f), new Vector2(0f, 0.04f) }, 22, 40f);
                k.Pop();
                foreach (float dz in new[] { -0.012f, 0.012f })
                {
                    k.Push(new Vector3(-0.02f, 0.016f, dz), Quaternion.identity);
                    k.Lathe(new[] { new Vector2(0.0065f, 0f), new Vector2(0.0095f, 0f), new Vector2(0.0095f, 0.024f), new Vector2(0.0112f, 0.027f), new Vector2(0.0095f, 0.03f), new Vector2(0.0065f, 0.03f), new Vector2(0.0065f, 0f) }, 14, 40f);
                    k.Pop();
                }
            });
            ctx.Part(th, id + ".thermostat.housing", T("Thermostat housing", "Termostat korpusi",
                    "Cast outlet elbow with the radiator spigot and two heater ports, sealed to the head by an O-ring.",
                    "Radiator ucli va ikki isitgich teshikli quyma chiqish tirsagi; kallakka halqasimon zichlagich bilan zichlangan."),
                "Die-cast aluminium", housing, new[] { m.CastAluminium }, f, Quaternion.identity, false, Vector3.left, 0.5f);
            Mesh wax = ctx.Mesh("thermostat-element", k =>
            {
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.024f, 0f), new Vector2(0.024f, 0.003f), new Vector2(0.012f, 0.004f), new Vector2(0.009f, 0.02f), new Vector2(0.005f, 0.028f), new Vector2(0f, 0.028f) }, 18, 40f);
                k.Push(new Vector3(0f, 0.004f, 0f), Quaternion.identity);
                k.Cylinder(0.0035f, -0.014f, 0f, 8);
                k.Pop();
            });
            ctx.Part(th, id + ".thermostat.element", T("Wax thermostat", "Mumli termostat",
                    "Wax capsule and spring valve: the wax melts and expands at 88 °C, pushing the valve open.",
                    "Mumli kapsula va prujinali klapan: 88 °C da mum erib kengayadi va klapanni ochadi."),
                "Brass and steel", wax, new[] { m.Brass }, f + new Vector3(-0.004f, 0f, 0f), InternalsContext.AlongY(Vector3.left, Vector3.up), false, Vector3.left, 1.2f);
            Mesh bolt = ctx.Mesh("bolt-M6x25", k => Fasteners.HexBolt(k, 0.006f, 0.025f));
            foreach (float s in new[] { -1f, 1f })
                ctx.Part(th, id + ".thermostat.bolt-" + (s < 0 ? "rear" : "front"), T("Housing bolt M6", "Korpus bolti M6", "Holds the housing to the cylinder head.", "Korpusni silindrlar kallagiga mahkamlaydi."),
                    "Steel 8.8", bolt, new[] { m.ZincSteel }, f + new Vector3(-0.006f, -0.022f, s * 0.026f), InternalsContext.AlongY(Vector3.left, Vector3.up), true, Vector3.left, 0.3f);
            Mesh sensor = ctx.Mesh("coolant-temp-sensor", k =>
            {
                k.Lathe(new[] { new Vector2(0f, -0.008f), new Vector2(0.004f, -0.008f), new Vector2(0.004f, 0f), new Vector2(0.0085f, 0f), new Vector2(0.0085f, 0.007f), new Vector2(0.006f, 0.009f), new Vector2(0.006f, 0.022f), new Vector2(0f, 0.022f) }, 6, 40f, 30f, 360f, true);
            });
            ctx.Part(th, id + ".thermostat.temp-sensor", T("Coolant temperature sensor", "Suyuqlik harorati datchigi",
                    "NTC thermistor that reports outlet temperature to the engine computer and the fan control.",
                    "Chiqish haroratini dvigatel kompyuteri va ventilyator boshqaruviga yetkazuvchi NTC termistor."),
                "Brass and PBT", sensor, new[] { m.Brass }, f + new Vector3(-0.022f, 0.004f, 0.026f), InternalsContext.AlongY(Vector3.forward, Vector3.up), true, Vector3.forward, 0.4f);
        }

        private void Hoses(Transform asm, string id)
        {
            // Upper hose: thermostat outlet to the radiator's top spigot; lower hose: radiator bottom spigot to the neck.
            Vector3 tOut = ThermoOutlet();
            Vector3 rIn = g.Rad(ThermalElectricalGeometry.RadHalfW + 0.03f, 0.14f, -0.003f);
            Vector3 rOut = g.Rad(ThermalElectricalGeometry.RadHalfW + 0.03f, -0.13f, -0.003f);
            var upper = new[]
            {
                tOut + Vector3.right * 0.022f, tOut, tOut + new Vector3(-0.05f, 0.004f, 0.006f), new Vector3(-0.40f, tOut.y + 0.008f, tOut.z + 0.002f),
                rIn + g.RadU * 0.035f + Vector3.up * 0.002f, rIn, rIn - g.RadU * 0.022f
            };
            Hose(asm, id + ".upper-hose", T("Upper Radiator Hose", "Yuqori radiator shlangi",
                    "Moulded EPDM hose that carries hot coolant from the thermostat to the radiator's top half.",
                    "Issiq suyuqlikni termostatdan radiatorning yuqori yarmiga olib boruvchi quyma EPDM shlang."), upper, 0.0195f, "upper-radiator-hose", Vector3.up);
            Vector3 nt = NeckTip();
            var lower = new[]
            {
                nt + Vector3.right * 0.02f, nt, nt + new Vector3(-0.045f, 0f, 0.035f), new Vector3(-0.33f, nt.y + 0.002f, 2.142f), new Vector3(-0.40f, 0.31f, 2.142f),
                rOut + g.RadU * 0.04f + Vector3.up * 0.02f, rOut, rOut - g.RadU * 0.022f
            };
            Hose(asm, id + ".lower-hose", T("Lower Radiator Hose", "Pastki radiator shlangi",
                    "Wire-reinforced EPDM hose that returns cooled coolant from the radiator to the pump inlet; it resists collapse under suction.",
                    "Sovigan suyuqlikni radiatordan nasos kirishiga qaytaruvchi simli EPDM shlang; so‘rishda bujmaymaydi."), lower, 0.0195f, "lower-radiator-hose", Vector3.down);
        }

        private void Hose(Transform asm, string id, Txt text, Vector3[] pts, float r, string key, Vector3 explode)
        {
            Transform sub = ctx.SubAssembly(asm, id, text, "EPDM, steel clamps", explode, 0.7f);
            Vector3 o = pts[0];
            Mesh hose = ctx.Mesh(key, k => ThermalKit.Smooth(k, o, pts, r, 12, 6));
            ctx.Part(sub, id + ".hose", T("Hose", "Shlang", "Peroxide-cured EPDM with an aramid knit layer, rated 2 bar at 125 °C.", "Aramid to‘qimali peroksidli EPDM, 125 °C da 2 bar bosimga mo‘ljallangan."),
                "EPDM", hose, new[] { hoseRubber }, o, Quaternion.identity, false, explode, 0.4f);
            Mesh clamp = ctx.Mesh("hose-clamp-r" + Mathf.RoundToInt(r * 1000f), k => ThermalKit.HoseClamp(k, r));
            foreach (float t in new[] { 0.035f, 0.965f })
            {
                ThermalKit.At(pts, t, out Vector3 p, out Vector3 d);
                ctx.Part(sub, id + ".clamp-" + (t < 0.5f ? "engine" : "radiator"), T("Hose clamp", "Shlang xomuti", "Worm-drive clamp that seals the hose on its beaded spigot.", "Shlangni bo‘rtmali uchga zich qisuvchi chervyakli xomut."),
                    "Stainless steel", clamp, new[] { m.ZincSteel }, p, InternalsContext.AlongY(d, Vector3.up), true, d, 0.3f);
            }
        }

        private void ExpansionTank(Transform asm, string id)
        {
            Transform sub = ctx.SubAssembly(asm, id + ".expansion-tank", T("Expansion Tank", "Kengaytirish baki",
                    "Translucent tank at the circuit's highest point: takes the coolant's thermal expansion and bleeds air out through the degas hose.",
                    "Konturning eng yuqori nuqtasidagi shaffof bak: suyuqlikning issiqlikdan kengayishini qabul qiladi va havoni degaz shlangi orqali chiqaradi."),
                "PP, EPDM", Vector3.up, 0.8f);
            Bounds b = TankBounds();
            Mesh tank = ctx.Mesh("expansion-tank", k =>
            {
                k.RoundedBox(Vector3.zero, b.size, 0.018f, 3);
                k.Push(new Vector3(0f, b.extents.y, b.extents.z * 0.35f), Quaternion.identity);
                k.Lathe(new[] { new Vector2(0f, -0.004f), new Vector2(0.019f, -0.004f), new Vector2(0.019f, 0.012f), new Vector2(0f, 0.012f) }, 18);
                k.Pop();
                for (int i = 0; i < 3; i++) k.Box(new Vector3(b.extents.x + 0.0005f, -0.02f + i * 0.02f, 0f), new Vector3(0.002f, 0.002f, b.size.z * 0.6f));
            });
            ctx.Part(sub, id + ".expansion-tank.tank", T("Expansion tank", "Kengaytirish baki",
                    "Blow-moulded polypropylene tank with MIN and MAX ribs; holds about 1 litre.",
                    "MIN va MAX belgili, puflab quyilgan polipropilen bak; taxminan 1 litr sig‘adi."),
                "Polypropylene, translucent", tank, new[] { m.Reservoir }, b.center, Quaternion.identity, false, Vector3.up, 0.4f);
            Mesh cap = ctx.Mesh("pressure-cap", k =>
            {
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.024f, 0f), new Vector2(0.025f, 0.004f), new Vector2(0.025f, 0.014f), new Vector2(0.021f, 0.018f), new Vector2(0f, 0.018f) }, 24, 40f);
                for (int i = 0; i < 12; i++)
                {
                    float a = 30f * i * Mathf.Deg2Rad;
                    k.Box(new Vector3(Mathf.Cos(a) * 0.025f, 0.009f, Mathf.Sin(a) * 0.025f), new Vector3(0.003f, 0.01f, 0.003f));
                }
            });
            ctx.Part(sub, id + ".expansion-tank.cap", T("Pressure cap", "Bosim qopqog‘i",
                    "1.4 bar pressure and vacuum valve cap: raises the boiling point to about 125 °C.",
                    "1,4 bar bosim va vakuum klapanli qopqoq: qaynash nuqtasini taxminan 125 °C gacha ko‘taradi."),
                "PA66", cap, new[] { m.GreyPlastic }, new Vector3(b.center.x, b.max.y + 0.012f, b.center.z + b.extents.z * 0.35f), Quaternion.identity, false, Vector3.up, 0.8f);
            // Bracket from the tank's front foot down to the radiator's upper bar.
            Vector3 foot = new Vector3(b.center.x - 0.02f, b.min.y, b.max.z - 0.02f);
            Vector3 barPt = g.Rad(ThermalElectricalGeometry.RadHalfW + 0.004f, ThermalElectricalGeometry.CoreHalfH + 0.03f, -0.036f);
            Mesh bracket = ctx.Mesh("expansion-tank-bracket", k => ThermalKit.Strap(k, foot, new[] { foot, new Vector3(foot.x, barPt.y + 0.0085f, foot.z), new Vector3(barPt.x + 0.002f, barPt.y + 0.0085f, barPt.z), barPt + Vector3.up * 0.0085f + g.RadU * 0.004f }, 0.022f, 0.003f, Vector3.forward));
            ctx.Part(sub, id + ".expansion-tank.bracket", T("Tank bracket", "Bak kronshteyni", "Steel strap that carries the tank on the radiator's upper bar.", "Bakni radiatorning yuqori to‘siniga tayantiruvchi po‘lat tasma."),
                "Steel, e-coated", bracket, new[] { m.BlackSteel }, foot, Quaternion.identity, false, Vector3.down, 0.4f);
            // Degas hose from the radiator's top spigot; make-up hose down to the lower hose.
            Vector3 degasFrom = g.Rad((ThermalElectricalGeometry.RadHalfW + ThermalElectricalGeometry.CoreHalfW) * 0.5f, ThermalElectricalGeometry.CoreHalfH + 0.03f, -0.003f);
            var degas = new[] { degasFrom, degasFrom + Vector3.up * 0.02f, new Vector3(b.min.x + 0.01f, b.min.y + 0.03f, b.max.z + 0.012f), new Vector3(b.min.x + 0.03f, b.min.y + 0.06f, b.max.z - 0.004f) };
            Mesh dh = ctx.Mesh("degas-hose", k => ThermalKit.Smooth(k, degasFrom, degas, 0.0055f, 10, 8));
            ctx.Part(sub, id + ".expansion-tank.degas-hose", T("Degas hose", "Degaz shlangi", "Small hose that vents air and steam from the radiator's top into the tank.", "Radiator tepasidagi havo va bug‘ni bakka chiqaruvchi ingichka shlang."),
                "EPDM", dh, new[] { hoseRubber }, degasFrom, Quaternion.identity, true, Vector3.up, 0.4f);
            Vector3 mFrom = new Vector3(b.center.x - 0.015f, b.min.y + 0.004f, b.min.z + 0.02f);
            var makeup = new[] { mFrom, mFrom + Vector3.down * 0.03f, new Vector3(-0.432f, 0.42f, 1.878f), new Vector3(-0.432f, 0.352f, 1.90f), new Vector3(-0.43f, 0.35f, 2.06f), new Vector3(-0.41f, 0.336f, 2.13f) };
            Mesh mh = ctx.Mesh("makeup-hose", k => ThermalKit.Smooth(k, mFrom, makeup, 0.008f, 12, 8));
            ctx.Part(sub, id + ".expansion-tank.makeup-hose", T("Make-up hose", "To‘ldirish shlangi", "Feeds coolant from the tank back into the pump's suction side as the engine cools.", "Dvigatel sovuganda bakdan suyuqlikni nasosning so‘rish tomoniga qaytaradi."),
                "EPDM", mh, new[] { hoseRubber }, mFrom, Quaternion.identity, false, Vector3.down, 0.4f);
        }

        private Bounds TankBounds() => ThermalElectricalGeometry.MinMax(new Vector3(-0.48f, 0.54f, 1.895f), new Vector3(-0.385f, 0.645f, 2.015f));

        private void HeaterHoses(Transform asm, string id)
        {
            Transform sub = ctx.SubAssembly(asm, id + ".heater-hoses", T("Heater Hoses", "Isitgich shlanglari",
                    "Supply and return hoses between the thermostat housing and the heater core in the climate module.",
                    "Termostat korpusi va iqlim modulidagi isitgich radiatori orasidagi uzatish va qaytarish shlanglari."),
                "EPDM", Vector3.up, 0.7f);
            Vector3 f = g.ThermoFlange;
            Bounds h = g.Hvac;
            for (int i = 0; i < 2; i++)
            {
                bool supply = i == 0;
                float dz = supply ? -0.012f : 0.012f;
                float runY = supply ? 0.546f : 0.537f, runX = supply ? -0.345f : -0.367f;
                Vector3 port = f + new Vector3(-0.02f, 0.044f, dz);
                Vector3 end = new Vector3(supply ? -0.405f : -0.445f, supply ? 0.64f : 0.67f, h.max.z + 0.002f);
                var pts = new[]
                {
                    port + Vector3.down * 0.016f, port, port + Vector3.up * 0.016f, new Vector3(-0.29f, runY, f.z + dz * 0.6f), new Vector3(runX, runY, f.z + (supply ? -0.02f : -0.03f) + dz),
                    new Vector3(runX, runY, 1.93f), new Vector3(end.x + 0.025f, end.y - 0.03f, h.max.z + 0.035f), end + Vector3.forward * 0.012f, end - Vector3.forward * 0.012f
                };
                Vector3 o = pts[0];
                Mesh mesh = ctx.Mesh(supply ? "heater-hose-supply" : "heater-hose-return", k => ThermalKit.Smooth(k, o, pts, 0.0095f, 10, 6));
                ctx.Part(sub, id + ".heater-hoses." + (supply ? "supply" : "return"), supply
                        ? T("Heater supply hose", "Isitgichga uzatish shlangi", "Hot coolant from the engine to the heater core.", "Issiq suyuqlikni dvigateldan isitgich radiatoriga olib boradi.")
                        : T("Heater return hose", "Isitgichdan qaytarish shlangi", "Coolant back from the heater core to the engine.", "Suyuqlikni isitgich radiatoridan dvigatelga qaytaradi."),
                    "EPDM", mesh, new[] { hoseRubber }, o, Quaternion.identity, false, Vector3.up, 0.4f);
            }
        }

        // ------------------------------------------------------------------------------------------------ belt

        private void BeltDrive(Transform system)
        {
            string id = "cooling.belt-drive";
            Transform asm = ctx.Assembly(system, id, T("Accessory Belt Drive", "Yordamchi agregatlar tasmali uzatmasi",
                    "Six-rib poly-V belt from the crank damper pulley to the water pump, alternator and A/C compressor at the engine's left end.",
                    "Dvigatelning chap uchida tirsakli val dempfer shkividan suv nasosi, generator va konditsioner kompressoriga boruvchi olti ariqchali poly-V tasma."),
                "EPDM and steel", Vector3.left, 1.2f);
            Quaternion outward = InternalsContext.AlongY(Vector3.left, Vector3.up);
            float pw = ThermalElectricalGeometry.BeltWidth + 0.002f;
            Transform crank = ctx.SubAssembly(asm, id + ".crank-pulley", T("Crank Damper Pulley", "Tirsakli val dempfer shkivi",
                    "Accessory pulley bolted to the crank nose with a rubber-bonded torsional damper ring.",
                    "Tirsakli val uchiga boltlangan, rezina bilan bog‘langan buralish dempfer halqali yordamchi shkiv."),
                "Cast iron and rubber", Vector3.left, 0.7f);
            Mesh pulley = ctx.Mesh("pulley-crank-damper", k =>
            {
                ThermalKit.PolyVPulley(k, ThermalElectricalGeometry.CrankR, pw, 6, 0.03f, 36);
                k.Push(new Vector3(0f, -0.011f, 0f), Quaternion.identity);
                k.Lathe(new[] { new Vector2(0.02f, 0f), new Vector2(0.045f, 0f), new Vector2(0.045f, 0.011f), new Vector2(0.02f, 0.011f), new Vector2(0.02f, 0f) }, 28);
                k.Pop();
            }, 1);
            ctx.Part(crank, id + ".crank-pulley.pulley", T("Damper pulley", "Dempfer shkivi",
                    "Grey-iron pulley and inertia ring on a rubber layer that absorbs the crank's torsional vibration.",
                    "Tirsakli valning buralish tebranishini yutuvchi rezina qatlamdagi kulrang cho‘yan shkiv va inersiya halqasi."),
                "Grey cast iron, NBR", pulley, new[] { m.CastIron }, new Vector3(g.BeltX + pw * 0.5f, g.CrankC.y, g.CrankC.z), outward, false, Vector3.left, 0.5f);
            Mesh bolt = ctx.Mesh("bolt-M14x40", k => Fasteners.HexBolt(k, 0.014f, 0.04f));
            ctx.Part(crank, id + ".crank-pulley.bolt", T("Crank bolt M14", "Tirsakli val bolti M14", "Torque-to-yield bolt that clamps the pulley to the crank nose.", "Shkivni tirsakli val uchiga qisuvchi oquvchanlikkacha tortiladigan bolt."),
                "Steel 10.9", bolt, new[] { m.MachinedSteel }, new Vector3(g.BeltX - pw * 0.5f - 0.006f, g.CrankC.y, g.CrankC.z), outward, true, Vector3.left, 0.6f);

            Transform beltSub = ctx.SubAssembly(asm, id + ".belt", T("Poly-V Belt", "Poly-V tasma",
                    "Serpentine belt wrapped round the four pulleys; its back side is smooth, its ribs face the grooves.",
                    "To‘rtta shkivni o‘rab oluvchi ilonsimon tasma; orqasi silliq, ariqchalari shkiv ariqlariga qaragan."),
                "EPDM with aramid cords", Vector3.left, 0.9f);
            var centres = new[] { g.AltC, g.CompC, g.CrankC, g.PumpC };
            var radii = new[] { ThermalElectricalGeometry.AltR, ThermalElectricalGeometry.CompR, ThermalElectricalGeometry.CrankR, ThermalElectricalGeometry.PumpR };
            List<Vector3> path = ThermalKit.BeltPath(centres, radii, 0.0025f, 12);
            Vector3 o = path[0];
            Mesh belt = ctx.Mesh("belt-poly-v-6pk", k =>
            {
                k.Push(Matrix4x4.Translate(-o));
                k.Sweep(Profiles.Rect(0.005f, ThermalElectricalGeometry.BeltWidth), path, Vector3.right, false, false, 30f);
                k.Pop();
            });
            ctx.Part(beltSub, id + ".belt.belt", T("Belt 6PK", "Tasma 6PK", "Six-rib EPDM belt, about 1.1 m long, with aramid tension cords.", "Olti ariqchali EPDM tasma, uzunligi taxminan 1,1 m, aramid tortuvchi iplar bilan."),
                "EPDM, aramid", belt, new[] { m.Rubber }, o, Quaternion.identity, false, Vector3.left, 0.5f);
            float length = 0f;
            for (int i = 1; i < path.Count; i++) length += Vector3.Distance(path[i - 1], path[i]);
            ctx.Note("belt-drive: belt length " + CarLayout.F(length) + " m");
        }

        // ------------------------------------------------------------------------------------------------ A/C

        private void AcCircuit(Transform system)
        {
            string id = "cooling.ac-circuit";
            Transform asm = ctx.Assembly(system, id, T("A/C Refrigerant Circuit", "Konditsioner sovituvchi konturi",
                    "R1234yf vapour-compression loop: belt-driven compressor, nose condenser with receiver-drier, lines to the evaporator.",
                    "R1234yf bug‘-siqish konturi: tasmali kompressor, quritgichli burun kondensatori va bug‘latkichga boruvchi quvurlar."),
                "Aluminium", new Vector3(0f, 0.5f, 1f), 1.1f);
            Compressor(asm, id);
            Condenser(asm, id);
            Lines(asm, id);
        }

        private void Compressor(Transform asm, string id)
        {
            Transform sub = ctx.SubAssembly(asm, id + ".compressor", T("A/C Compressor", "Konditsioner kompressori",
                    "Variable-displacement swash-plate compressor driven through an electromagnetic clutch.",
                    "Elektromagnit mufta orqali aylanadigan o‘zgaruvchan hajmli qiya diskli kompressor."),
                "Die-cast aluminium", Vector3.left, 0.9f);
            Vector3 c = g.CompC;
            float pw = ThermalElectricalGeometry.BeltWidth + 0.002f;
            Quaternion outward = InternalsContext.AlongY(Vector3.left, Vector3.up);
            Mesh body = ctx.Mesh("ac-compressor-body", k =>
            {
                var p = new List<Vector2> { new Vector2(0f, 0f), new Vector2(0.02f, 0f), new Vector2(0.03f, 0.006f), new Vector2(0.05f, 0.01f) };
                for (int i = 0; i < 4; i++)
                {
                    float y = 0.016f + i * 0.022f;
                    p.Add(new Vector2(0.052f, y)); p.Add(new Vector2(0.055f, y + 0.003f)); p.Add(new Vector2(0.055f, y + 0.009f)); p.Add(new Vector2(0.052f, y + 0.012f));
                }
                p.Add(new Vector2(0.052f, 0.104f)); p.Add(new Vector2(0.04f, 0.11f)); p.Add(new Vector2(0f, 0.11f));
                k.Lathe(p, 28, 40f);
                // suction and discharge ports on top
                foreach (float z in new[] { -0.022f, 0.022f })
                    k.RoundedBox(new Vector3(0.056f, 0.07f, z), new Vector3(0.018f, 0.022f, 0.02f), 0.003f, 1);
            });
            // Body axis along -x from the belt plane outward; ports on top (local +x of the lathe frame = car up).
            ctx.Part(sub, id + ".compressor.body", T("Compressor body", "Kompressor korpusi",
                    "Seven-piston swash-plate pump with its suction and discharge ports on top; displacement varies 10-170 cc.",
                    "So‘rish va haydash teshiklari tepasida bo‘lgan yetti porshenli qiya diskli nasos; hajmi 10-170 sm³ oralig‘ida o‘zgaradi."),
                "Die-cast aluminium", body, new[] { m.CastAluminium }, new Vector3(g.BeltX - pw * 0.5f - 0.004f, c.y, c.z), BodyRot(), false, Vector3.left, 0.5f);
            Mesh clutch = ctx.Mesh("ac-clutch-pulley", k =>
            {
                ThermalKit.PolyVPulley(k, ThermalElectricalGeometry.CompR, pw, 6, 0.024f, 32);
                k.Push(new Vector3(0f, pw + 0.0005f, 0f), Quaternion.identity);
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.044f, 0f), new Vector2(0.044f, 0.003f), new Vector2(0.015f, 0.0035f), new Vector2(0f, 0.0035f) }, 28, 40f);
                k.Pop();
            });
            ctx.Part(sub, id + ".compressor.clutch", T("Clutch and pulley", "Mufta va shkiv",
                    "Free-running pulley with an electromagnetic clutch plate that couples the shaft when cooling is asked for.",
                    "Erkin aylanuvchi shkiv va sovitish so‘ralganda valni ulaydigan elektromagnit mufta diski."),
                "Steel", clutch, new[] { m.ZincSteel }, new Vector3(g.BeltX - pw * 0.5f, c.y, c.z), InternalsContext.AlongY(Vector3.right, Vector3.up), false, Vector3.right, 0.6f);
            // Bracket over the pulley to the block's front face.
            Vector3 b0 = new Vector3(g.BeltX - pw - 0.02f, c.y + 0.058f, c.z + 0.03f);
            var bp = new[] { b0, new Vector3(b0.x, 0.30f, b0.z), new Vector3(-0.215f, 0.30f, b0.z + 0.006f), new Vector3(-0.215f, 0.30f, 2.036f) };
            Mesh bracket = ctx.Mesh("ac-compressor-bracket", k => ThermalKit.Strap(k, b0, bp, 0.03f, 0.006f, Vector3.forward, 0.008f));
            ctx.Part(sub, id + ".compressor.bracket", T("Compressor bracket", "Kompressor kronshteyni",
                    "Cast bracket that reaches over the pulley and bolts the compressor's ear to the block's front face.",
                    "Shkiv ustidan o‘tib kompressor qulog‘ini blokning old yuziga boltlovchi quyma kronshteyn."),
                "Cast aluminium", bracket, new[] { m.CastAluminium }, b0, Quaternion.identity, false, Vector3.up, 0.5f);
            Mesh plug = ctx.Mesh("connector-small-2pin", k => ThermalKit.Connector(k, 0.014f, 0.01f, 0.018f));
            ctx.Part(sub, id + ".compressor.clutch-connector", T("Clutch coil connector", "Mufta g‘altagi ulagichi", "Two-pin feed for the clutch coil.", "Mufta g‘altagi uchun ikki kontaktli ulagich."),
                "PBT", plug, new[] { m.BlackPlastic }, new Vector3(-0.40f, c.y + 0.04f, c.z - 0.04f), InternalsContext.AlongY(Vector3.up), true, Vector3.up, 0.4f);
        }

        private Quaternion BodyRot()
        {
            // Lathe +Y along -x (out of the belt plane), lathe +X up (ports on top).
            return Quaternion.LookRotation(Vector3.forward, Vector3.left) * Quaternion.Euler(0f, 0f, 0f);
        }

        private void Condenser(Transform asm, string id)
        {
            Transform sub = ctx.SubAssembly(asm, id + ".condenser", T("Condenser", "Kondensator",
                    "Slim multi-flow condenser behind the nose intake; the refrigerant gives up its heat and turns liquid.",
                    "Burun havo kiritgichi ortidagi yupqa ko‘p oqimli kondensator; sovituvchi issiqlikni berib suyuqlikka aylanadi."),
                "Brazed aluminium", Vector3.forward, 0.9f);
            Vector3 c = CondCentre();
            const float hw = 0.25f, hh = ThermalElectricalGeometry.CondHalfH, hd = 0.008f;
            Mesh core = ctx.Mesh("condenser-core", k => ThermalKit.Core(k, hw, hh, hd, 0.009f, 0.0035f), 2);
            ctx.Part(sub, id + ".condenser.core", T("Condenser core", "Kondensator o‘zagi",
                    "16 mm micro-channel tubes with fins; about 8 kW of heat leaves the refrigerant here.",
                    "Qovurg‘ali 16 mm mikro-kanalli quvurlar; sovituvchidan taxminan 8 kVt issiqlik shu yerda chiqadi."),
                "Brazed aluminium 3003", core, new[] { m.MachinedAluminium, fin }, c, Quaternion.identity, false, Vector3.forward, 0.4f);
            Mesh header = ctx.Mesh("condenser-header", k =>
            {
                k.Cylinder(0.013f, -hh - 0.012f, hh + 0.012f, 16);
                k.Push(new Vector3(0f, hh + 0.012f, 0f), Quaternion.identity);
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.013f, 0f), new Vector2(0.008f, 0.006f), new Vector2(0f, 0.006f) }, 16);
                k.Pop();
            });
            ctx.Part(sub, id + ".condenser.inlet-header", T("Inlet header", "Kirish kollektori", "Round header that spreads the hot gas over the tube passes.", "Issiq gazni quvur yo‘llariga taqsimlovchi dumaloq kollektor."),
                "Aluminium", header, new[] { m.CastAluminium }, c + new Vector3(-hw - 0.012f, 0f, 0f), Quaternion.identity, false, Vector3.left, 0.4f);
            ctx.Part(sub, id + ".condenser.outlet-header", T("Outlet header", "Chiqish kollektori", "Collects the condensed liquid at the far end.", "Kondensatsiyalangan suyuqlikni qarama-qarshi uchda yig‘adi."),
                "Aluminium", header, new[] { m.CastAluminium }, c + new Vector3(hw + 0.012f, 0f, 0f), Quaternion.identity, false, Vector3.right, 0.4f);
            Mesh drier = ctx.Mesh("receiver-drier", k =>
            {
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.014f, 0f), new Vector2(0.018f, 0.006f), new Vector2(0.018f, 0.11f), new Vector2(0.014f, 0.118f), new Vector2(0.006f, 0.12f), new Vector2(0.006f, 0.128f), new Vector2(0f, 0.128f) }, 20, 40f);
            });
            ctx.Part(sub, id + ".condenser.receiver-drier", T("Receiver-drier", "Resiver-quritgich",
                    "Bottle on the outlet header with desiccant that holds the liquid reserve and traps moisture.",
                    "Chiqish kollektoridagi namlik yutuvchi to‘ldirgichli ballon: suyuqlik zaxirasini saqlaydi va namlikni ushlaydi."),
                "Aluminium, desiccant", drier, new[] { m.CastAluminium }, c + new Vector3(hw + 0.04f, -hh - 0.004f, -0.004f), Quaternion.identity, false, Vector3.right, 0.6f);
            Mesh bracket = ctx.Mesh("condenser-bracket", k =>
            {
                k.Box(new Vector3(0f, 0f, 0f), new Vector3(0.03f, 0.003f, 0.03f));
                k.Box(new Vector3(0f, 0.02f, -0.0135f), new Vector3(0.03f, 0.04f, 0.003f));
            });
            foreach (float sx in new[] { -1f, 1f })
                ctx.Part(sub, id + ".condenser.bracket-" + (sx < 0 ? "left" : "right"), T("Condenser foot", "Kondensator tayanchi", "Rubber-lined steel foot that stands the condenser on the bay floor.", "Kondensatorni bo‘linma tagiga tiradigan rezina qoplamali po‘lat tayanch."),
                    "Steel, EPDM", bracket, new[] { m.BlackSteel }, new Vector3(c.x + sx * (hw - 0.05f), g.BayFloorY + 0.0045f, c.z - 0.002f), Quaternion.identity, false, Vector3.down, 0.4f);
        }

        private Vector3 CondCentre() => new Vector3(0.02f, g.CondC.y, g.CondC.z);

        private void Lines(Transform asm, string id)
        {
            Transform sub = ctx.SubAssembly(asm, id + ".lines", T("Refrigerant Lines", "Sovituvchi quvurlari",
                    "Discharge hose, liquid line and suction line with crimped rubber sections where the engine moves.",
                    "Dvigatel tebranadigan joylarda qisilgan rezina qismli haydash shlangi, suyuqlik va so‘rish quvurlari."),
                "Aluminium, HNBR", Vector3.up, 0.8f);
            Vector3 c = g.CompC;
            Vector3 cond = CondCentre();
            Bounds h = g.Hvac;
            float portX = g.BeltX - ThermalElectricalGeometry.BeltWidth * 0.5f - 0.0055f - 0.07f;
            Vector3 dischargePort = new Vector3(portX, c.y + 0.065f, c.z + 0.022f);
            Vector3 inletSide = cond + new Vector3(-0.25f - 0.012f - 0.012f, 0.0575f, 0f);
            var discharge = new[] { dischargePort, dischargePort + Vector3.up * 0.012f, new Vector3(-0.34f, 0.298f, 2.12f), new Vector3(-0.29f, 0.298f, inletSide.z - 0.007f), inletSide + Vector3.left * 0.008f, inletSide };
            Line(sub, id + ".lines.discharge", T("Discharge hose", "Haydash shlangi", "Hot high-pressure gas from the compressor to the condenser, about 15 bar.", "Kompressordan kondensatorga boruvchi issiq yuqori bosimli gaz, taxminan 15 bar."),
                "HNBR hose, aluminium ends", "ac-discharge-hose", discharge, 0.008f, m.Rubber, false);
            if (!BuildHvac) return;
            Vector3 drierBottom = cond + new Vector3(0.25f + 0.04f, -ThermalElectricalGeometry.CondHalfH - 0.004f, -0.004f);
            Vector3 txv = new Vector3(-0.42f, 0.585f, h.max.z + 0.008f);
            var liquid = new[]
            {
                drierBottom, drierBottom + Vector3.down * 0.006f + Vector3.back * 0.02f, new Vector3(0.25f, 0.178f, 2.132f), new Vector3(-0.30f, 0.178f, 2.132f),
                new Vector3(-0.44f, 0.178f, 2.128f), new Vector3(-0.47f, 0.18f, 2.06f), new Vector3(-0.47f, 0.18f, 1.886f), new Vector3(-0.47f, 0.52f, 1.886f),
                new Vector3(-0.45f, txv.y, txv.z + 0.01f), txv + new Vector3(-0.012f, 0f, 0f)
            };
            Line(sub, id + ".lines.liquid", T("Liquid line", "Suyuqlik quvuri", "Small-bore aluminium pipe that carries the liquid refrigerant to the expansion valve.", "Suyuq sovituvchini kengaytirish klapaniga olib boruvchi ingichka alyuminiy quvur."),
                "Aluminium 3003 tube", "ac-liquid-line", liquid, 0.005f, m.MachinedAluminium, true);
            Vector3 suctionPort = new Vector3(portX, c.y + 0.065f, c.z - 0.022f);
            var suction = new[]
            {
                txv + new Vector3(0.012f, -0.006f, 0f), new Vector3(-0.40f, 0.57f, txv.z + 0.01f), new Vector3(-0.40f, 0.50f, 1.878f), new Vector3(-0.40f, 0.32f, 1.878f),
                new Vector3(-0.375f, 0.31f, 1.96f), suctionPort + Vector3.up * 0.012f, suctionPort
            };
            Line(sub, id + ".lines.suction", T("Suction line", "So‘rish quvuri", "Large-bore cold line from the evaporator back to the compressor, about 2 bar.", "Bug‘latkichdan kompressorga qaytuvchi yo‘g‘on sovuq quvur, taxminan 2 bar."),
                "Aluminium tube, HNBR section", "ac-suction-line", suction, 0.008f, m.MachinedAluminium, true);
            Mesh valve = ctx.Mesh("ac-expansion-valve", k =>
            {
                k.RoundedBox(Vector3.zero, new Vector3(0.036f, 0.034f, 0.016f), 0.003f, 1);
                k.Push(new Vector3(0f, 0.017f, 0f), Quaternion.identity);
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.011f, 0f), new Vector2(0.011f, 0.008f), new Vector2(0f, 0.012f) }, 14);
                k.Pop();
            });
            ctx.Part(sub, id + ".lines.expansion-valve", T("Expansion valve", "Kengaytirish klapani",
                    "Block thermostatic valve on the evaporator: meters the liquid so the vapour leaves 5 K superheated.",
                    "Bug‘latkichdagi blokli termostatik klapan: bug‘ 5 K o‘ta qizigan holda chiqishi uchun suyuqlikni me’yorlaydi."),
                "Aluminium", valve, new[] { m.CastAluminium }, txv, Quaternion.identity, false, Vector3.forward, 0.5f);
            Mesh port = ctx.Mesh("ac-service-port", k => k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0075f, 0f), new Vector2(0.0075f, 0.016f), new Vector2(0.006f, 0.02f), new Vector2(0f, 0.02f) }, 10));
            ctx.Part(sub, id + ".lines.service-port-high", T("High-side service port", "Yuqori bosim xizmat porti", "Schrader valve with cap for charging and gauges.", "Zaryadlash va manometr uchun qopqoqli Shreder klapani."),
                "Aluminium, PA cap", port, new[] { m.BlackPlastic }, new Vector3(0.10f, 0.178f + 0.005f, 2.132f), Quaternion.identity, true, Vector3.up, 0.4f);
            ctx.Part(sub, id + ".lines.service-port-low", T("Low-side service port", "Past bosim xizmat porti", "Schrader valve on the suction line.", "So‘rish quvuridagi Shreder klapani."),
                "Aluminium, PA cap", port, new[] { m.BlackPlastic }, new Vector3(-0.40f, 0.40f, 1.878f) + Vector3.right * 0.008f, InternalsContext.AlongY(Vector3.right), true, Vector3.right, 0.4f);
            Mesh sensor = ctx.Mesh("ac-pressure-sensor", k => k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.008f, 0f), new Vector2(0.008f, 0.006f), new Vector2(0.006f, 0.008f), new Vector2(0.006f, 0.024f), new Vector2(0f, 0.024f) }, 6, 40f, 30f, 360f, true));
            ctx.Part(sub, id + ".lines.pressure-sensor", T("Refrigerant pressure sensor", "Sovituvchi bosim datchigi", "Reads high-side pressure to protect the system and to run the fans.", "Tizimni himoya qilish va ventilyatorlarni boshqarish uchun yuqori bosimni o‘lchaydi."),
                "Brass, PBT", sensor, new[] { m.Brass }, new Vector3(0.20f, 0.178f + 0.005f, 2.132f), Quaternion.identity, true, Vector3.up, 0.4f);
        }

        private void Line(Transform sub, string id, Txt text, string material, string key, Vector3[] pts, float r, Material mat, bool rigid)
        {
            Vector3 o = pts[0];
            Mesh mesh = rigid ? ctx.Mesh(key, k => ThermalKit.Run(k, o, pts, r, 10, 0.025f)) : ctx.Mesh(key, k => ThermalKit.Smooth(k, o, pts, r, 10, 8));
            ctx.Part(sub, id, text, material, mesh, new[] { mat }, o, Quaternion.identity, r < 0.008f, Vector3.up, 0.4f);
        }

        // ------------------------------------------------------------------------------------------------ HVAC

        private void Hvac(Transform system)
        {
            string id = "cooling.hvac";
            Bounds h = g.Hvac;
            Transform asm = ctx.Assembly(system, id, T("HVAC Module", "Iqlim moduli",
                    "Compact climate module against the left bulkhead: blower, evaporator and heater core in one case, air mixed by a blend door.",
                    "Chap bo‘lim devoriga tiralgan ixcham iqlim moduli: ventilyator, bug‘latkich va isitgich radiatori bitta korpusda, havo aralashtirish eshikchasi bilan."),
                "Polypropylene and aluminium", Vector3.up, 1.2f);
            Transform caseSub = ctx.SubAssembly(asm, id + ".case", T("Evaporator and Heater Case", "Bug‘latkich va isitgich korpusi",
                    "Split polypropylene case holding the evaporator, the heater core and the blend door between them.",
                    "Bug‘latkich, isitgich radiatori va ular orasidagi aralashtirish eshikchasini saqlovchi ikki bo‘lakli polipropilen korpus."),
                "PP-T20", Vector3.up, 0.8f);
            Vector3 c = h.center;
            Vector3 s = h.size;
            float caseLen = s.z * 0.58f;
            Vector3 caseC = new Vector3(c.x, c.y, h.max.z - caseLen * 0.5f);
            Mesh caseMesh = ctx.Mesh("hvac-case", k =>
            {
                k.RoundedBox(Vector3.zero, new Vector3(s.x, s.y, caseLen), 0.02f, 3);
                for (int i = 0; i < 4; i++) k.Box(new Vector3(-s.x * 0.5f + 0.03f + i * 0.045f, s.y * 0.5f + 0.0015f, 0f), new Vector3(0.004f, 0.003f, caseLen * 0.9f));
                k.Box(new Vector3(0f, 0f, 0f), new Vector3(s.x + 0.004f, s.y * 0.9f, 0.005f));
            });
            ctx.Part(caseSub, id + ".case.housing", T("Case halves", "Korpus yarmlari",
                    "Two talc-filled polypropylene halves clipped and screwed round the cores, with a sealed seam.",
                    "Radiatorlar atrofida qisqich va vintlar bilan yig‘ilgan, choki zichlangan ikki talkli polipropilen yarm."),
                "PP-T20", caseMesh, new[] { m.GreyPlastic }, caseC, Quaternion.identity, false, Vector3.up, 0.5f);
            Mesh evap = ctx.Mesh("hvac-evaporator", k => ThermalKit.Core(k, s.x * 0.42f, s.y * 0.4f, 0.018f, 0.01f, 0.004f), 2);
            ctx.Part(caseSub, id + ".case.evaporator", T("Evaporator core", "Bug‘latkich o‘zagi",
                    "Plate-fin evaporator where the refrigerant boils at about 2 °C, cooling and drying the cabin air.",
                    "Sovituvchi taxminan 2 °C da qaynab salon havosini sovitadigan va quritadigan plastinka-qovurg‘ali bug‘latkich."),
                "Brazed aluminium", evap, new[] { m.MachinedAluminium, fin }, new Vector3(c.x, c.y, h.max.z - 0.03f), Quaternion.identity, false, Vector3.left, 0.9f);
            Mesh heater = ctx.Mesh("hvac-heater-core", k => ThermalKit.Core(k, s.x * 0.36f, s.y * 0.3f, 0.014f, 0.009f, 0.004f), 2);
            ctx.Part(caseSub, id + ".case.heater-core", T("Heater core", "Isitgich radiatori",
                    "Small tube-and-fin radiator heated by engine coolant; the blend door sends part of the air through it.",
                    "Dvigatel suyuqligi bilan isitiladigan kichik quvur-qovurg‘ali radiator; aralashtirish eshikchasi havoning bir qismini u orqali o‘tkazadi."),
                "Brazed copper-brass", heater, new[] { m.Brass, m.Copper }, new Vector3(c.x, c.y, h.max.z - caseLen + 0.03f), Quaternion.identity, false, Vector3.left, 1.1f);
            Mesh actuator = ctx.Mesh("hvac-blend-actuator", k =>
            {
                k.RoundedBox(Vector3.zero, new Vector3(0.012f, 0.05f, 0.06f), 0.004f, 1);
                k.Push(new Vector3(0.006f, 0.01f, 0.012f), InternalsContext.AlongY(Vector3.right));
                k.Cylinder(0.008f, 0f, 0.006f, 12);
                k.Pop();
            });
            ctx.Part(caseSub, id + ".case.blend-actuator", T("Blend flap actuator", "Aralashtirish eshikchasi yuritmasi",
                    "Stepper motor that turns the blend door to mix hot and cold air to the set temperature.",
                    "Issiq va sovuq havoni berilgan haroratgacha aralashtirish uchun eshikchani buruvchi qadamli motor."),
                "PBT", actuator, new[] { m.BlackPlastic }, new Vector3(h.max.x + 0.006f, c.y + 0.02f, caseC.z - 0.01f), Quaternion.identity, false, Vector3.right, 0.6f);
            Mesh drain = ctx.Mesh("hvac-drain", k => k.Tube(MeshKit.Bends(new[] { Vector3.zero, new Vector3(0f, -0.03f, 0f), new Vector3(0.02f, -0.06f, 0.01f) }, 0.01f), 0.005f, 8));
            ctx.Part(caseSub, id + ".case.drain", T("Condensate drain", "Kondensat to‘kgichi", "Rubber spout that lets water condensed on the evaporator out of the case.", "Bug‘latkichda kondensatlangan suvni korpusdan chiqaruvchi rezina nay."),
                "EPDM", drain, new[] { m.Rubber }, new Vector3(c.x - 0.03f, h.min.y, h.max.z - 0.03f), Quaternion.identity, true, Vector3.down, 0.4f);
            // Bracket to the bulkhead (dash) behind.
            float wall = ctx.Layout.FrontWellWallX;
            Mesh bracket = ctx.Mesh("hvac-bracket", k =>
            {
                float span = wall + 0.004f + h.min.x;
                k.Box(new Vector3(-span * 0.5f, 0f, 0f), new Vector3(span, 0.003f, 0.05f));
                k.Box(new Vector3(-span + 0.0015f, -0.018f, 0f), new Vector3(0.003f, 0.036f, 0.05f));
                k.Box(new Vector3(-0.0015f, 0.018f, 0f), new Vector3(0.003f, 0.036f, 0.05f));
            });
            ctx.Part(caseSub, id + ".case.bracket", T("Wheel-arch bracket", "G‘ildirak arkasi kronshteyni", "Steel bracket that hangs the module on the inner wheel-arch wall.", "Modulni g‘ildirak arkasining ichki devoriga osuvchi po‘lat kronshteyn."),
                "Steel, e-coated", bracket, new[] { m.BlackSteel }, new Vector3(h.min.x, h.max.y - 0.05f, c.z), Quaternion.identity, false, Vector3.left, 0.4f);

            Transform blower = ctx.SubAssembly(asm, id + ".blower", T("Blower", "Ventilyator",
                    "Centrifugal blower that draws fresh air from the cowl through the cabin filter and pushes it through the cores.",
                    "Toza havoni kapot ostidan salon filtri orqali tortib, radiatorlar orqali haydovchi markazdan qochma ventilyator."),
                "PP and steel", Vector3.back, 0.8f);
            float blowLen = s.z - caseLen;
            Vector3 bc = new Vector3(c.x, c.y, h.min.z + blowLen * 0.5f);
            float scrollR = 0.068f;
            Mesh scroll = ctx.Mesh("hvac-blower-scroll", k =>
            {
                // spiral volute along local +Y (= car -x), outlet tangent toward the case (+z)
                var outline = new List<Vector2>();
                for (int i = 0; i <= 28; i++)
                {
                    float a = Mathf.PI * 2f * i / 28f * 0.92f;
                    float r = scrollR * (0.78f + 0.22f * i / 28f);
                    outline.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r));
                }
                outline.Add(new Vector2(scrollR, -scrollR * 0.35f));
                k.Push(Vector3.zero, Quaternion.LookRotation(Vector3.left, Vector3.up));
                k.Extrude(outline, s.x * 0.9f);
                k.Pop();
            });
            ctx.Part(blower, id + ".blower.scroll", T("Blower scroll", "Ventilyator spiral korpusi", "Volute that turns the wheel's radial flow into one duct toward the evaporator.", "G‘ildirakning radial oqimini bug‘latkich tomon bitta kanalga aylantiruvchi spiral korpus."),
                "PP-T20", scroll, new[] { m.GreyPlastic }, bc, Quaternion.identity, false, Vector3.back, 0.5f);
            Mesh wheel = ctx.Mesh("hvac-blower-wheel", k =>
            {
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(scrollR * 0.62f, 0f), new Vector2(scrollR * 0.62f, 0.004f), new Vector2(0f, 0.004f) }, 24);
                for (int i = 0; i < 30; i++)
                {
                    float a = 12f * i * Mathf.Deg2Rad;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    k.Push(d * scrollR * 0.56f + Vector3.up * s.x * 0.33f, Quaternion.LookRotation(Quaternion.Euler(0f, 25f, 0f) * d, Vector3.up));
                    k.Box(Vector3.zero, new Vector3(0.0015f, s.x * 0.62f, 0.012f));
                    k.Pop();
                }
            });
            ctx.Part(blower, id + ".blower.wheel", T("Blower wheel", "Ventilyator g‘ildiragi", "Forward-curved squirrel-cage wheel with 30 blades.", "30 ta oldinga egilgan kurakli \"olmaxon katagi\" g‘ildiragi."),
                "PP", wheel, new[] { m.BlackPlastic }, bc + new Vector3(s.x * 0.33f, 0f, 0f), InternalsContext.AlongY(Vector3.left, Vector3.up), false, Vector3.left, 0.9f);
            Mesh motor = ctx.Mesh("hvac-blower-motor", k =>
            {
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.03f, 0f), new Vector2(0.032f, 0.004f), new Vector2(0.032f, 0.04f), new Vector2(0.026f, 0.046f), new Vector2(0f, 0.046f) }, 22, 40f);
                k.Cylinder(0.004f, -0.012f, 0f, 8);
            });
            ctx.Part(blower, id + ".blower.motor", T("Blower motor", "Ventilyator motori", "Brushless 12 V motor, about 300 W, speed set by the climate controller.", "Taxminan 300 Vt cho‘tkasiz 12 V motor; tezligini iqlim boshqaruvchisi belgilaydi."),
                "Steel can", motor, new[] { m.BlackSteel }, new Vector3(h.min.x + 0.004f, bc.y, bc.z), InternalsContext.AlongY(Vector3.right, Vector3.up), false, Vector3.left, 0.8f);
            Mesh filterCover = ctx.Mesh("hvac-filter-cover", k =>
            {
                k.RoundedBox(Vector3.zero, new Vector3(s.x * 0.8f, 0.012f, blowLen * 0.8f), 0.004f, 1);
                for (int i = 0; i < 6; i++) k.Box(new Vector3(-s.x * 0.3f + i * s.x * 0.12f, 0.007f, 0f), new Vector3(0.005f, 0.003f, blowLen * 0.7f));
            });
            ctx.Part(blower, id + ".blower.filter-cover", T("Cabin filter cover", "Salon filtri qopqog‘i", "Louvred cover over the pleated activated-carbon cabin filter.", "Burmali faollashtirilgan ko‘mirli salon filtri ustidagi jalyuzli qopqoq."),
                "PP", filterCover, new[] { m.BlackPlastic }, new Vector3(c.x, h.max.y + 0.006f, h.max.z - blowLen * 0.5f), Quaternion.identity, false, Vector3.up, 0.6f);

            Transform ducts = ctx.SubAssembly(asm, id + ".ducts", T("Air Ducts", "Havo kanallari",
                    "Blow-moulded ducts from the case through the bulkhead to the face vents and the demister outlet.",
                    "Korpusdan bo‘lim devori orqali yuz dеflektorlari va oyna puflagichiga boruvchi puflab quyilgan kanallar."),
                "HDPE", Vector3.back, 0.8f);
            DashDuct(ducts, id + ".ducts.face", T("Face vent duct", "Yuz deflektori kanali", "Oval duct to the centre face vents.", "Markaziy yuz deflektorlariga boruvchi oval kanal."), "hvac-duct-face",
                new[] { new Vector3(-0.42f, 0.66f, h.min.z + 0.006f), new Vector3(-0.42f, 0.665f, h.min.z - 0.02f), new Vector3(-0.36f, 0.668f, 1.565f), new Vector3(-0.29f, 0.67f, 1.515f) }, 0.022f);
            DashDuct(ducts, id + ".ducts.demister", T("Demister duct", "Oyna puflagich kanali", "Flat duct to the outlet under the windscreen base.", "Old oyna asosidagi chiqishga boruvchi yassi kanal."), "hvac-duct-demister",
                new[] { new Vector3(-0.50f, 0.70f, h.min.z + 0.006f), new Vector3(-0.50f, 0.705f, h.min.z - 0.02f), new Vector3(-0.47f, 0.725f, 1.53f), new Vector3(-0.44f, 0.742f, 1.41f) }, 0.018f);
        }

        private void DashDuct(Transform parent, string id, Txt text, string key, Vector3[] pts, float r)
        {
            Vector3 o = pts[0];
            Mesh mesh = ctx.Mesh(key, k =>
            {
                k.Push(Matrix4x4.Translate(-o));
                k.Tube(MeshKit.CatmullRom(pts, 8), r, 14, true, true, Vector3.up, null, new Vector2(0.65f, 1.3f));
                k.Pop();
            });
            ctx.Part(parent, id, text, "HDPE", mesh, new[] { m.GreyPlastic }, o, Quaternion.identity, false, Vector3.back, 0.5f);
        }
    }

    // ==================================================================================================== electrical

    /// <summary>
    /// Electrical: the 12 V AGM battery in the rear compartment with its main fuse and battery sensor, power
    /// distribution (front fuse and relay box at the left bulkhead, rear fuse box for the cabin circuits, ground
    /// straps), the alternator and starter, the engine control unit, and the main harness: battery cable and body
    /// harness along the left sill, branches to the lamps, the fans, the engine computer, the steering rack's assist
    /// unit and the ABS modulator.
    /// </summary>
    [CarInternalsSubsystem("Electrical", 70)]
    public sealed class ElectricalSystem : ICarInternalsSubsystem
    {
        private InternalsContext ctx;
        private CarPalette m;
        private ThermalElectricalGeometry g;
        private Material lead, fuseRed, fuseYellow;
        private float rearFloorY;
        private Bounds bat;
        private Vector3 absConnectorAt;

        // Hardpoints other builders and the report use.
        public static readonly Vector3 AbsConnectorFace = new Vector3(-0.421f, 0.36f, 1.59f);

        public void Build(InternalsContext context)
        {
            ctx = context;
            m = ctx.Mats;
            g = ThermalElectricalGeometry.Of(ctx.Layout, ctx.Probe);
            lead = m.Get("CI_Lead_Alloy", new Color(0.40f, 0.41f, 0.43f), 0.85f, 0.42f);
            fuseRed = m.AnodisedRed;
            fuseYellow = m.AnodisedYellow;
            rearFloorY = ctx.Probe.Down(new Vector3(-0.30f, 0.62f, -1.78f), 0.5f, out float fy) ? fy : 0.315f;
            float h = g.Battery.size.y;
            bat = ThermalElectricalGeometry.MinMax(new Vector3(g.Battery.min.x, rearFloorY + 0.014f, g.Battery.min.z), new Vector3(g.Battery.max.x, rearFloorY + 0.014f + h, g.Battery.max.z));
            Transform system = ctx.System("Electrical");
            ctx.Note("electrical: battery " + CarLayout.B(bat) + " (rear floor y " + CarLayout.F(rearFloorY) + "), front PDB " + CarLayout.B(g.Pdb) + ", ECU " + CarLayout.B(g.Ecu) +
                     ", starter nose " + CarLayout.V(g.StarterC));
            Battery(system);
            PowerDistribution(system);
            ChargingStarting(system);
            Ecu(system);
            Harness(system);
            Contacts();
            ctx.Note("electrical: ABS branch plug face " + CarLayout.V(absConnectorAt) + " (on the modulator's connector when the brakes builder made one)");
        }

        private void Contacts()
        {
            ctx.AllowContact("electrical.battery.mount", "InteriorFloor");
            ctx.AllowContact("electrical.battery.mount", "CarConcept");
            ctx.AllowContact("electrical.battery.negative.ground-bolt", "InteriorFloor");
            ctx.AllowContact("electrical.battery.negative.ground-bolt", "CarConcept");
            ctx.AllowContact("electrical.battery.negative.ground-lead", "InteriorFloor");
            ctx.AllowContact("electrical.battery.negative.ground-lead", "CarConcept");
            ctx.AllowContact("electrical.power-distribution.front-box.bracket", "InteriorFloor");
            ctx.AllowContact("electrical.power-distribution.front-box.bracket", "CarConcept");
            ctx.AllowContact("electrical.power-distribution.rear-box.bracket", "InteriorFloor");
            ctx.AllowContact("electrical.power-distribution.rear-box.bracket", "CarConcept");
            ctx.AllowContact("electrical.power-distribution.grounds", "V6/");
            ctx.AllowContact("electrical.power-distribution.grounds", "InteriorFloor");
            ctx.AllowContact("electrical.power-distribution.grounds", "CarConcept");
            ctx.AllowContact("electrical.charging-starting.alternator.bracket", "V6/");
            ctx.AllowContact("electrical.charging-starting.alternator", "cooling.belt-drive");
            // The starter's pinion meshes with the flywheel ring gear and its flange bolts to the bell housing.
            ctx.AllowContact("electrical.charging-starting.starter.pinion", "powertrain");
            ctx.AllowContact("electrical.charging-starting.starter.flange", "powertrain");
            ctx.AllowContact("electrical.charging-starting.starter.bolt", "V6/");
            ctx.AllowContact("electrical.charging-starting.starter.bolt", "powertrain");
            ctx.AllowContact("electrical.ecu.mounting.bracket", "InteriorFloor");
            ctx.AllowContact("electrical.ecu.mounting.bracket", "CarConcept");
            // Harness: clips on the floor, grommets through the bulkhead (the glTF's dash shell), plugs on the
            // components they feed.
            ctx.AllowContact("electrical.harness", "InteriorFloor");
            ctx.AllowContact("electrical.harness", "CarConcept");
            ctx.AllowContact("electrical.harness", "InteriorDash");
            ctx.AllowContact("electrical.harness", "electrical.battery");
            ctx.AllowContact("electrical.harness", "electrical.power-distribution");
            ctx.AllowContact("electrical.harness", "electrical.charging-starting");
            ctx.AllowContact("electrical.harness", "electrical.ecu");
            ctx.AllowContact("electrical.harness.front-branches.fan-lead", "cooling.radiator-module.fans");
            ctx.AllowContact("electrical.harness.front-branches.eps", "steering.rack.eps.harness");
            // The ABS plug mates with the modulator's connector (integration: the brakes builder's control unit).
            ctx.AllowContact("electrical.harness.front-branches.abs", "brakes.abs.control-unit.connector");
        }

        private static Txt T(string en, string uz, string de, string du) => new Txt(en, uz, de, du);

        // ------------------------------------------------------------------------------------------------ battery

        private void Battery(Transform system)
        {
            string id = "electrical.battery";
            Transform asm = ctx.Assembly(system, id, T("Battery", "Akkumulyator",
                    "12 V 60 Ah AGM battery in the rear compartment, with the main fuse on its positive post and a current sensor on its negative post.",
                    "Orqa bo‘linmadagi 12 V 60 Ah AGM akkumulyator: musbat qutbida asosiy saqlagich, manfiy qutbida tok datchigi."),
                "Lead-calcium, polypropylene", Vector3.up, 1.1f);
            Vector3 c = bat.center, s = bat.size;
            Vector3 pos = new Vector3(bat.max.x - 0.027f, bat.max.y, bat.min.z + 0.026f);
            Vector3 neg = new Vector3(bat.min.x + 0.03f, bat.max.y, bat.min.z + 0.026f);

            Transform unit = ctx.SubAssembly(asm, id + ".unit", T("AGM Battery", "AGM akkumulyator",
                    "Six 2 V cells of lead-calcium plates in absorbent glass mat: sealed, spill-proof and vibration-proof.",
                    "Shisha matoga shimdirilgan qo‘rg‘oshin-kalsiy plastinali oltita 2 V elementi: germetik, to‘kilmaydi, tebranishga chidamli."),
                "Lead-calcium, PP", Vector3.up, 0.8f);
            float caseH = s.y - 0.016f;
            Mesh box = ctx.Mesh("battery-case", k =>
            {
                k.RoundedBox(new Vector3(0f, -s.y * 0.5f + caseH * 0.5f, 0f), new Vector3(s.x, caseH, s.z), 0.006f, 2);
                for (int i = 0; i < 5; i++) k.Box(new Vector3(-s.x * 0.5f + s.x * (i + 1) / 6f, -s.y * 0.5f + caseH * 0.5f, s.z * 0.5f + 0.0008f), new Vector3(0.004f, caseH * 0.86f, 0.0016f));
            });
            ctx.Part(unit, id + ".unit.case", T("Battery case", "Akkumulyator korpusi",
                    "Polypropylene monobloc with six cell compartments; the ribs mark the cell walls.",
                    "Oltita element bo‘linmali polipropilen monoblok; qovurg‘alar element devorlarini ko‘rsatadi."),
                "Polypropylene", box, new[] { m.BatteryCase }, c, Quaternion.identity, false, Vector3.up, 0.3f);
            Mesh lid = ctx.Mesh("battery-lid", k =>
            {
                k.RoundedBox(new Vector3(0f, 0.008f, 0f), new Vector3(s.x - 0.004f, 0.016f, s.z - 0.004f), 0.005f, 2);
                for (int i = 0; i < 6; i++)
                {
                    k.Push(new Vector3(-s.x * 0.5f + s.x * (i + 0.5f) / 6f, 0.016f, 0.02f), Quaternion.identity);
                    k.Cylinder(0.008f, 0f, 0.003f, 12);
                    k.Pop();
                }
                k.Box(new Vector3(0f, 0.018f, -0.035f), new Vector3(0.09f, 0.004f, 0.016f));
            });
            ctx.Part(unit, id + ".unit.lid", T("Lid", "Qopqoq", "Heat-sealed lid with the valve-regulated vents of the six cells and a carry handle.", "Olti element klapanli shamollatgichlari va ko‘tarish dastasi bo‘lgan issiqda payvandlangan qopqoq."),
                "Polypropylene", lid, new[] { m.BlackPlastic }, new Vector3(c.x, bat.max.y - 0.016f, c.z), Quaternion.identity, false, Vector3.up, 0.5f);
            Mesh post = ctx.Mesh("battery-post", k => k.Lathe(new[] { new Vector2(0f, -0.002f), new Vector2(0.0105f, -0.002f), new Vector2(0.0095f, 0.012f), new Vector2(0.0088f, 0.018f), new Vector2(0f, 0.018f) }, 16, 40f));
            ctx.Part(unit, id + ".unit.positive-post", T("Positive post", "Musbat qutb", "Tapered lead post of the positive plate group.", "Musbat plastinalar guruhining konussimon qo‘rg‘oshin qutbi."),
                "Lead alloy", post, new[] { lead }, pos, Quaternion.identity, true, Vector3.up, 0.6f);
            ctx.Part(unit, id + ".unit.negative-post", T("Negative post", "Manfiy qutb", "Slightly thinner tapered post of the negative plates.", "Manfiy plastinalarning biroz ingichkaroq konussimon qutbi."),
                "Lead alloy", post, new[] { lead }, neg, Quaternion.identity, true, Vector3.up, 0.6f);
            Vector3 ventFrom = new Vector3(bat.max.x - 0.004f, bat.max.y - 0.006f, c.z + 0.035f);
            var vent = new[] { ventFrom, ventFrom + Vector3.right * 0.02f, new Vector3(bat.max.x + 0.022f, rearFloorY + 0.06f, c.z + 0.04f), new Vector3(bat.max.x + 0.026f, rearFloorY + 0.006f, c.z + 0.05f) };
            Mesh ventMesh = ctx.Mesh("battery-vent-tube", k => ThermalKit.Smooth(k, ventFrom, vent, 0.004f, 8, 6));
            ctx.Part(unit, id + ".unit.vent-tube", T("Vent tube", "Shamollatish naychasi", "Leads any gas from the central vent through the floor to the outside.", "Markaziy shamollatgichdan chiqqan gazni pol orqali tashqariga olib chiqadi."),
                "PVC", ventMesh, new[] { m.BlackPlastic }, ventFrom, Quaternion.identity, true, Vector3.right, 0.4f);

            Transform mount = ctx.SubAssembly(asm, id + ".mount", T("Tray and Hold-Down", "Tagliq va mahkamlagich",
                    "Moulded tray bolted to the rear floor and a steel bar clamped over the battery by two J-bolts.",
                    "Orqa polga boltlangan quyma tagliq va ikki J-bolt bilan akkumulyator ustidan qisilgan po‘lat planka."),
                "PP-GF and steel", Vector3.down, 0.8f);
            Mesh tray = ctx.Mesh("battery-tray", k =>
            {
                k.Box(new Vector3(0f, 0.004f, 0f), new Vector3(s.x + 0.03f, 0.008f, s.z + 0.03f));
                foreach (float sz in new[] { -1f, 1f }) k.Box(new Vector3(0f, 0.01f, sz * (s.z * 0.5f + 0.012f)), new Vector3(s.x + 0.03f, 0.02f, 0.006f));
                foreach (float sx in new[] { -1f, 1f }) k.Box(new Vector3(sx * (s.x * 0.5f + 0.012f), 0.01f, 0f), new Vector3(0.006f, 0.02f, s.z + 0.03f));
            });
            ctx.Part(mount, id + ".mount.tray", T("Battery tray", "Akkumulyator tagligi", "Glass-filled polypropylene tray with a lip that locates the battery foot.", "Akkumulyator asosini joylashtiruvchi bortli shisha tolali polipropilen tagliq."),
                "PP-GF30", tray, new[] { m.BlackPlastic }, new Vector3(c.x, rearFloorY + 0.001f, c.z), Quaternion.identity, false, Vector3.down, 0.4f);
            float barX = c.x - 0.02f;
            Mesh holdBar = ctx.Mesh("battery-hold-down", k => k.Box(Vector3.zero, new Vector3(0.024f, 0.008f, s.z + 0.04f)));
            ctx.Part(mount, id + ".mount.hold-down", T("Hold-down bar", "Mahkamlash plankasi", "Pressed-steel bar over the lid that keeps the battery from moving in a crash.", "To‘qnashuvda akkumulyator siljimasligi uchun qopqoq ustidagi presslangan po‘lat planka."),
                "Steel, e-coated", holdBar, new[] { m.BlackSteel }, new Vector3(barX, bat.max.y + 0.004f, c.z), Quaternion.identity, false, Vector3.up, 0.7f);
            float jLen = bat.max.y + 0.008f - rearFloorY - 0.006f;
            Mesh jBolt = ctx.Mesh("battery-j-bolt", k =>
            {
                k.Tube(MeshKit.Bends(new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, -jLen, 0f), new Vector3(0f, -jLen, -0.012f) }, 0.004f), 0.003f, 8);
            });
            Mesh nut = ctx.Mesh("nut-M6", k => Fasteners.HexNut(k, 0.006f, true));
            foreach (float sz in new[] { -1f, 1f })
            {
                string side = sz < 0 ? "rear" : "front";
                float z = c.z + sz * (s.z * 0.5f + 0.015f);
                ctx.Part(mount, id + ".mount.j-bolt-" + side, T("J-bolt M6", "J-bolt M6", "Hooks under the tray lip and pulls the bar down.", "Tagliq bortining ostiga ilinib plankani pastga tortadi."),
                    "Steel, zinc", jBolt, new[] { m.ZincSteel }, new Vector3(barX, bat.max.y + 0.012f, z), sz < 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f), true, Vector3.up, 0.5f);
                ctx.Part(mount, id + ".mount.nut-" + side, T("Nut M6", "Gayka M6", "Flanged nut on the J-bolt.", "J-boltdagi flanetsli gayka."),
                    "Steel, zinc", nut, new[] { m.ZincSteel }, new Vector3(barX, bat.max.y + 0.008f, z), Quaternion.identity, true, Vector3.up, 0.6f);
            }

            Transform plus = ctx.SubAssembly(asm, id + ".positive", T("Positive Terminal and Main Fuse", "Musbat klemma va asosiy saqlagich",
                    "Clamp on the positive post carrying a 250 A MEGA fuse block: every cable leaves the battery through it.",
                    "Musbat qutbdagi qisqich va 250 A MEGA saqlagich bloki: barcha kabellar akkumulyatordan shu orqali chiqadi."),
                "Tinned brass, PA66", new Vector3(0.6f, 1f, 0.4f), 0.8f);
            Mesh clamp = ctx.Mesh("battery-clamp", k =>
            {
                k.Sleeve(0.0145f, 0.0098f, 0f, 0.014f, 18);
                k.Box(new Vector3(0f, 0.007f, 0.022f), new Vector3(0.016f, 0.012f, 0.02f));
                k.Push(new Vector3(0.012f, 0.007f, 0f), InternalsContext.AlongY(Vector3.right));
                k.Cylinder(0.003f, -0.006f, 0.008f, 8);
                k.Pop();
            });
            ctx.Part(plus, id + ".positive.clamp", T("Positive clamp", "Musbat qisqich", "Tinned-brass clamp tightened round the post by a pinch bolt.", "Qutb atrofida qisish bolti bilan tortiladigan qalaylangan latun qisqich."),
                "Tinned brass", clamp, new[] { m.Brass }, pos + Vector3.up * 0.002f, Quaternion.identity, true, Vector3.up, 0.5f);
            Vector3 fuseC = new Vector3(pos.x, bat.max.y + 0.012f, pos.z + 0.035f);
            Mesh fuseBox = ctx.Mesh("mega-fuse-holder", k =>
            {
                k.RoundedBox(Vector3.zero, new Vector3(0.036f, 0.022f, 0.05f), 0.003f, 1);
                k.Box(new Vector3(0f, 0.012f, 0f), new Vector3(0.028f, 0.003f, 0.042f));
            });
            ctx.Part(plus, id + ".positive.fuse-holder", T("Main fuse holder", "Asosiy saqlagich korpusi", "Moulded holder with a clear window over the fuse link.", "Saqlagich plastinkasi ustida shaffof oynali quyma korpus."),
                "PA66", fuseBox, new[] { m.BlackPlastic }, fuseC, Quaternion.identity, false, Vector3.up, 0.6f);
            Mesh link = ctx.Mesh("mega-fuse-link", k =>
            {
                k.Box(new Vector3(0f, 0f, 0f), new Vector3(0.012f, 0.002f, 0.046f));
                k.Box(new Vector3(0f, 0.004f, 0f), new Vector3(0.016f, 0.006f, 0.016f));
            });
            ctx.Part(plus, id + ".positive.fuse-link", T("MEGA fuse 250 A", "MEGA saqlagich 250 A", "Bolt-in copper-alloy link that melts on a dead short of the main cable.", "Asosiy kabelda qisqa tutashuv bo‘lganda eriydigan boltli mis qotishma plastinka."),
                "Copper alloy", link, new[] { m.Copper }, fuseC + Vector3.up * 0.0145f, Quaternion.identity, true, Vector3.up, 1.1f);
            Mesh cover = ctx.Mesh("terminal-cover-red", k => k.RoundedBox(Vector3.zero, new Vector3(0.03f, 0.016f, 0.034f), 0.006f, 2));
            ctx.Part(plus, id + ".positive.cover", T("Terminal cover", "Klemma qopqog‘i", "Red insulating cover that keeps tools off the live terminal.", "Kuchlanishli klemmaga asbob tegmasligi uchun qizil izolyatsion qopqoq."),
                "PVC", cover, new[] { m.WireRed }, pos + new Vector3(0f, 0.012f, 0f), Quaternion.identity, false, Vector3.up, 0.9f);

            Transform minus = ctx.SubAssembly(asm, id + ".negative", T("Negative Terminal and Battery Sensor", "Manfiy klemma va akkumulyator datchigi",
                    "Intelligent battery sensor on the negative post: a shunt that measures current, voltage and temperature for charge management.",
                    "Manfiy qutbdagi aqlli akkumulyator datchigi: zaryadni boshqarish uchun tok, kuchlanish va haroratni o‘lchovchi shunt."),
                "Brass, PBT", new Vector3(-0.6f, 1f, 0.4f), 0.8f);
            Mesh ibs = ctx.Mesh("battery-sensor", k =>
            {
                k.Sleeve(0.0145f, 0.0098f, 0f, 0.014f, 18);
                k.RoundedBox(new Vector3(0f, 0.009f, 0.03f), new Vector3(0.03f, 0.018f, 0.036f), 0.003f, 1);
                k.RoundedBox(new Vector3(0.018f, 0.012f, 0.03f), new Vector3(0.008f, 0.01f, 0.014f), 0.002f, 1);
            });
            ctx.Part(minus, id + ".negative.sensor", T("Battery sensor", "Akkumulyator datchigi", "Shunt with a microcontroller that reports the state of charge over LIN.", "Zaryad holatini LIN orqali bildiruvchi mikrokontrollerli shunt."),
                "Brass, PBT", ibs, new[] { m.BlackPlastic }, neg + Vector3.up * 0.002f, Quaternion.identity, false, Vector3.up, 0.6f);
            Vector3 gFrom = neg + new Vector3(0f, 0.012f, 0.05f);
            Vector3 gTo = new Vector3(bat.min.x - 0.016f, rearFloorY + 0.004f, bat.max.z - 0.06f); // integration: clear of the rear fuse box stands and the subframe's body-mount bushing
            var ground = new[] { gFrom, gFrom + Vector3.forward * 0.02f, new Vector3(gTo.x + 0.01f, bat.max.y - 0.02f, gTo.z + 0.004f), new Vector3(gTo.x, rearFloorY + 0.03f, gTo.z), gTo + Vector3.up * 0.008f };
            Mesh gl = ctx.Mesh("battery-ground-lead", k => ThermalKit.Smooth(k, gFrom, ground, 0.0065f, 10, 8));
            ctx.Part(minus, id + ".negative.ground-lead", T("Ground lead 35 mm²", "Massa kabeli 35 mm²", "Short black cable from the sensor to the body ground stud.", "Datchikdan kuzov massa shpilkasigacha boruvchi qisqa qora kabel."),
                "Copper, PVC", gl, new[] { m.WireBlack }, gFrom, Quaternion.identity, false, Vector3.left, 0.5f);
            Mesh stud = ctx.Mesh("bolt-M8x20", k => Fasteners.HexBolt(k, 0.008f, 0.02f));
            ctx.Part(minus, id + ".negative.ground-bolt", T("Ground stud M8", "Massa shpilkasi M8", "Weld stud on the rear floor where the battery's negative meets the body.", "Akkumulyator manfiysi kuzov bilan ulanadigan orqa poldagi payvand shpilka."),
                "Steel, zinc", stud, new[] { m.ZincSteel }, gTo, Quaternion.identity, true, Vector3.up, 0.4f);
        }

        // ------------------------------------------------------------------------------------------------ distribution

        private void PowerDistribution(Transform system)
        {
            string id = "electrical.power-distribution";
            Transform asm = ctx.Assembly(system, id, T("Power Distribution", "Quvvat taqsimoti",
                    "Fuse and relay boxes that split the battery feed into protected circuits, and the ground straps that bring the current back.",
                    "Akkumulyator ta’minotini himoyalangan zanjirlarga bo‘luvchi saqlagich va rele bloklari hamda tokni qaytaruvchi massa tasmalari."),
                "PA66 and copper", Vector3.up, 1f);

            Bounds b = g.Pdb;
            Transform front = ctx.SubAssembly(asm, id + ".front-box", T("Front Fuse and Relay Box", "Old saqlagich va rele bloki",
                    "Sealed box at the left bulkhead: main fuses for the starter, alternator and fans, engine relays and the jump-start post.",
                    "Chap bo‘lim devoridagi germetik blok: starter, generator va ventilyatorlar asosiy saqlagichlari, dvigatel relelari va tashqi ishga tushirish qutbi."),
                "PA66-GF, copper", Vector3.up, 0.8f);
            Mesh housing = ctx.Mesh("pdb-front-housing", k =>
            {
                Vector3 s = b.size;
                k.RoundedBox(new Vector3(0f, -0.006f, 0f), new Vector3(s.x, s.y - 0.012f, s.z), 0.006f, 2);
                k.Box(new Vector3(0f, s.y * 0.5f - 0.013f, 0f), new Vector3(s.x + 0.004f, 0.004f, s.z + 0.004f));
            });
            ctx.Part(front, id + ".front-box.housing", T("Box housing", "Blok korpusi", "Glass-filled nylon housing with the busbar frame and the harness grommets underneath.", "Ostida shina ramasi va sim zichlagichlari bo‘lgan shisha tolali neylon korpus."),
                "PA66-GF30", housing, new[] { m.BlackPlastic }, b.center, Quaternion.identity, false, Vector3.down, 0.4f);
            Mesh lidMesh = ctx.Mesh("pdb-front-lid", k =>
            {
                k.RoundedBox(new Vector3(0f, 0.006f, 0f), new Vector3(b.size.x + 0.002f, 0.012f, b.size.z + 0.002f), 0.005f, 2);
                k.Box(new Vector3(b.size.x * 0.5f + 0.002f, 0.004f, 0f), new Vector3(0.004f, 0.008f, 0.02f));
            });
            ctx.Part(front, id + ".front-box.lid", T("Box lid", "Blok qopqog‘i", "Clip-on lid with the fuse map moulded inside.", "Ichida saqlagichlar sxemasi quyilgan qisqichli qopqoq."),
                "PA66", lidMesh, new[] { m.GreyPlastic }, new Vector3(b.center.x, b.max.y - 0.012f, b.center.z), Quaternion.identity, false, Vector3.up, 1.2f);
            Mesh relay = ctx.Mesh("relay-iso-micro", k => k.RoundedBox(new Vector3(0f, 0.012f, 0f), new Vector3(0.022f, 0.024f, 0.022f), 0.002f, 1));
            Mesh midi = ctx.Mesh("fuse-midi", k =>
            {
                k.RoundedBox(new Vector3(0f, 0.006f, 0f), new Vector3(0.012f, 0.012f, 0.034f), 0.002f, 1);
                foreach (float z in new[] { -0.014f, 0.014f }) { k.Push(new Vector3(0f, 0.012f, z), Quaternion.identity); k.Cylinder(0.004f, 0f, 0.004f, 6); k.Pop(); }
            });
            for (int i = 0; i < 3; i++)
                ctx.Part(front, id + ".front-box.relay-" + (i + 1), T("Relay " + (i + 1), (i + 1) + "-rele", i == 0 ? "Main engine relay: powers the injectors, coils and the engine computer." : i == 1 ? "Starter relay: switches the solenoid's pull-in current." : "Fan relay: switches the radiator fans' supply.",
                        i == 0 ? "Asosiy dvigatel relesi: injektorlar, g‘altaklar va dvigatel kompyuterini ta’minlaydi." : i == 1 ? "Starter relesi: solenoidning tortish tokini ulaydi." : "Ventilyator relesi: radiator ventilyatorlari ta’minotini ulaydi."),
                    "PBT, copper", relay, new[] { m.GreyPlastic }, new Vector3(b.center.x + 0.018f, b.max.y - 0.04f, b.min.z + 0.018f + i * 0.026f), Quaternion.identity, true, Vector3.up, 0.8f);
            for (int i = 0; i < 2; i++)
                ctx.Part(front, id + ".front-box.fuse-" + (i + 1), T("MIDI fuse " + (i == 0 ? "125 A" : "60 A"), "MIDI saqlagich " + (i == 0 ? "125 A" : "60 A"), i == 0 ? "Bolted fuse of the alternator charge line." : "Bolted fuse of the radiator fan circuit.",
                        i == 0 ? "Generator zaryad liniyasining boltli saqlagichi." : "Radiator ventilyatorlari zanjirining boltli saqlagichi."),
                    "PA66, zinc alloy", midi, new[] { i == 0 ? fuseRed : fuseYellow }, new Vector3(b.center.x - 0.02f, b.max.y - 0.036f, b.center.z - 0.012f + i * 0.026f), Quaternion.identity, true, Vector3.up, 0.9f);
            Mesh post = ctx.Mesh("jump-start-post", k =>
            {
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.012f, 0f), new Vector2(0.012f, 0.004f), new Vector2(0.006f, 0.006f), new Vector2(0.006f, 0.024f), new Vector2(0f, 0.024f) }, 16, 40f);
            });
            ctx.Part(front, id + ".front-box.jump-post", T("Jump-start post", "Tashqi ishga tushirish qutbi", "Remote positive stud on the box: jump leads clamp here because the battery is at the rear.", "Blokdagi masofaviy musbat shpilka: akkumulyator orqada bo‘lgani uchun simlar shu yerga ulanadi."),
                "Copper, zinc", post, new[] { m.Brass }, new Vector3(b.center.x - 0.012f, b.max.y, b.max.z - 0.018f), Quaternion.identity, false, Vector3.up, 1f);
            Mesh cap = ctx.Mesh("jump-start-cap", k => k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.015f, 0f), new Vector2(0.015f, 0.02f), new Vector2(0.01f, 0.028f), new Vector2(0f, 0.028f) }, 16, 40f));
            ctx.Part(front, id + ".front-box.jump-cap", T("Post cap", "Qutb qopqog‘i", "Red hinged cap marked +.", "\"+\" belgili qizil ochiladigan qopqoq."),
                "PVC", cap, new[] { m.WireRed }, new Vector3(b.center.x - 0.012f, b.max.y + 0.002f, b.max.z - 0.018f), Quaternion.identity, false, Vector3.up, 1.4f);
            float wall = ctx.Layout.FrontWellWallX;
            Mesh bracket = ctx.Mesh("pdb-front-bracket", k =>
            {
                float span = wall + 0.004f + b.min.x;
                k.Box(new Vector3(-span * 0.5f, 0f, 0f), new Vector3(span, 0.003f, 0.05f));
                k.Box(new Vector3(-span + 0.0015f, 0.02f, 0f), new Vector3(0.003f, 0.04f, 0.05f));
            });
            ctx.Part(front, id + ".front-box.bracket", T("Box bracket", "Blok kronshteyni", "Steel bracket that holds the box on the wheel-arch wall.", "Blokni g‘ildirak arkasi devorida ushlab turuvchi po‘lat kronshteyn."),
                "Steel, e-coated", bracket, new[] { m.BlackSteel }, new Vector3(b.min.x, b.center.y + 0.02f, b.center.z), Quaternion.identity, false, Vector3.left, 0.4f);

            Bounds r = RearBox();
            Transform rear = ctx.SubAssembly(asm, id + ".rear-box", T("Rear Fuse Box", "Orqa saqlagichlar bloki",
                    "Fuse box for the cabin and body circuits beside the battery: lamps, seats, climate blower and the infotainment.",
                    "Akkumulyator yonidagi salon va kuzov zanjirlari saqlagichlari bloki: chiroqlar, o‘rindiqlar, iqlim ventilyatori va multimedia."),
                "PA66, copper", Vector3.left, 0.8f);
            Mesh rh = ctx.Mesh("rear-fuse-box", k =>
            {
                k.RoundedBox(Vector3.zero, r.size, 0.006f, 2);
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 3; j++)
                        k.Box(new Vector3(-r.size.x * 0.3f + i * r.size.x * 0.2f, r.size.y * 0.5f + 0.002f, -r.size.z * 0.25f + j * r.size.z * 0.25f), new Vector3(0.012f, 0.004f, 0.012f));
            });
            ctx.Part(rear, id + ".rear-box.housing", T("Fuse box", "Saqlagichlar bloki", "Housing with twelve blade-fuse positions and the body-harness sockets.", "O‘n ikki pichoqsimon saqlagich o‘rni va kuzov simlari uyalari bo‘lgan korpus."),
                "PA66", rh, new[] { m.BlackPlastic }, r.center, Quaternion.identity, false, Vector3.up, 0.4f);
            Mesh rl = ctx.Mesh("rear-fuse-box-lid", k => k.RoundedBox(new Vector3(0f, 0.005f, 0f), new Vector3(r.size.x + 0.004f, 0.01f, r.size.z + 0.004f), 0.004f, 1));
            ctx.Part(rear, id + ".rear-box.lid", T("Fuse box lid", "Blok qopqog‘i", "Snap-on cover with the fuse table.", "Saqlagichlar jadvali bo‘lgan qisqichli qopqoq."),
                "PA66", rl, new[] { m.GreyPlastic }, new Vector3(r.center.x, r.max.y + 0.006f, r.center.z), Quaternion.identity, false, Vector3.up, 1f);
            // Integration: two stands at the box's front and rear edges lift it over the anti-roll bar clamp's bolt ends.
            float footH = r.min.y - rearFloorY + 0.001f;
            Mesh foot = ctx.Mesh("rear-fuse-box-bracket", k =>
            {
                foreach (float e in new[] { -1f, 1f }) k.Box(new Vector3(0f, footH * 0.5f, e * (r.size.z * 0.5f - 0.006f)), new Vector3(r.size.x + 0.02f, footH, 0.012f));
            });
            ctx.Part(rear, id + ".rear-box.bracket", T("Box stands", "Blok tayanchlari", "Pair of steel stands bolted to the rear floor; they lift the box over the anti-roll bar clamp bolts.", "Blokni stabilizator qisqichi boltlari ustiga ko‘taruvchi, orqa polga boltlangan juft po‘lat tayanch."),
                "Steel", foot, new[] { m.BlackSteel }, new Vector3(r.center.x, rearFloorY - 0.001f, r.center.z), Quaternion.identity, false, Vector3.down, 0.4f);

            Transform grounds = ctx.SubAssembly(asm, id + ".grounds", T("Ground Straps", "Massa tasmalari",
                    "Braided straps that return the starter and alternator current from the engine to the body.",
                    "Starter va generator tokini dvigateldan kuzovga qaytaruvchi to‘qilgan tasmalar."),
                "Tinned copper braid", Vector3.down, 0.7f);
            Vector3 gEngine = new Vector3(-0.214f, 0.19f, 1.935f);
            float floor = ctx.Probe.Down(new Vector3(-0.24f, 0.25f, 1.955f), 0.3f, out float fy2) ? fy2 : 0.13f;
            Vector3 gBody = new Vector3(-0.238f, floor + 0.002f, 1.955f);
            var strapPts = new[] { gEngine, gEngine + Vector3.left * 0.008f, new Vector3(-0.232f, (gEngine.y + gBody.y) * 0.5f, 1.946f), gBody + Vector3.up * 0.01f, gBody };
            Mesh strap = ctx.Mesh("engine-ground-strap", k =>
            {
                k.Push(Matrix4x4.Translate(-gEngine));
                k.Sweep(Profiles.RoundRect(0.004f, 0.016f, 0.0015f, 2), MeshKit.CatmullRom(strapPts, 6), Vector3.forward, true, true, 30f);
                k.Pop();
            });
            ctx.Part(grounds, id + ".grounds.engine-strap", T("Engine ground strap", "Dvigatel massa tasmasi", "Flexible braid from the sump flange to the bay floor; carries the starter's 400 A return.", "Karter flanetsidan bo‘linma tagiga boruvchi egiluvchan tasma; starterning 400 A qaytish tokini o‘tkazadi."),
                "Tinned copper braid", strap, new[] { m.BraidedHose }, gEngine, Quaternion.identity, false, Vector3.down, 0.5f);
            Mesh bolt = ctx.Mesh("bolt-M8x20", k => Fasteners.HexBolt(k, 0.008f, 0.02f));
            ctx.Part(grounds, id + ".grounds.body-stud", T("Ground stud M8", "Kuzov massa shpilkasi M8", "Weld stud on the bay floor with a star washer.", "Bo‘linma tagidagi yulduzsimon shaybali payvand shpilka."),
                "Steel, zinc", bolt, new[] { m.ZincSteel }, gBody, Quaternion.identity, true, Vector3.up, 0.4f);
        }

        private Bounds RearBox()
        {
            Bounds r = g.RearFuseBox;
            float h = r.size.y;
            // Integration: 2 cm higher on its bracket, over the rear anti-roll bar clamp's bolt ends in the floor.
            return ThermalElectricalGeometry.MinMax(new Vector3(r.min.x, rearFloorY + 0.026f, r.min.z), new Vector3(r.max.x, rearFloorY + 0.026f + h, r.max.z));
        }

        // ------------------------------------------------------------------------------------------------ charging

        private void ChargingStarting(Transform system)
        {
            string id = "electrical.charging-starting";
            Transform asm = ctx.Assembly(system, id, T("Charging and Starting", "Zaryadlash va ishga tushirish",
                    "Belt-driven alternator at the engine's left end and the starter motor that meshes with the flywheel at its right end.",
                    "Dvigatelning chap uchidagi tasmali generator va o‘ng uchida maxovik bilan ilashadigan starter."),
                "Aluminium, steel, copper", Vector3.down, 1f);
            Alternator(asm, id);
            Starter(asm, id);
        }

        private void Alternator(Transform asm, string id)
        {
            Transform sub = ctx.SubAssembly(asm, id + ".alternator", T("Alternator", "Generator",
                    "14 V 180 A claw-pole alternator with a built-in rectifier and regulator, driven at about 3 times crank speed.",
                    "O‘rnatilgan to‘g‘rilagich va regulyatorli 14 V 180 A tirnoqli qutbli generator; tirsakli valdan taxminan 3 marta tez aylanadi."),
                "Aluminium, copper", Vector3.left, 0.9f);
            Vector3 c = g.AltC;
            float pw = ThermalElectricalGeometry.BeltWidth + 0.002f;
            float x0 = g.BeltX - pw * 0.5f - 0.004f;
            Quaternion left = InternalsContext.AlongY(Vector3.left, Vector3.up);
            Mesh driveEnd = ctx.Mesh("alternator-drive-end", k =>
            {
                var p = new List<Vector2> { new Vector2(0f, 0f), new Vector2(0.012f, 0f), new Vector2(0.03f, 0.004f), new Vector2(0.05f, 0.012f), new Vector2(0.054f, 0.02f), new Vector2(0.054f, 0.05f), new Vector2(0f, 0.05f) };
                k.Lathe(p, 28, 40f);
                for (int i = 0; i < 16; i++)
                {
                    float a = 22.5f * i * Mathf.Deg2Rad;
                    k.Box(new Vector3(Mathf.Cos(a) * 0.04f, 0.008f, Mathf.Sin(a) * 0.04f), new Vector3(0.005f, 0.008f, 0.005f));
                }
                foreach (float sz in new[] { -1f, 1f }) k.Box(new Vector3(0.05f, 0.035f, sz * 0.05f), new Vector3(0.016f, 0.02f, 0.014f));
            });
            ctx.Part(sub, id + ".alternator.drive-end", T("Drive-end housing", "Uzatma tomoni korpusi", "Vented die-cast housing with the front bearing and the mounting ears.", "Old podshipnik va mahkamlash quloqlari bo‘lgan teshikli quyma korpus."),
                "Die-cast aluminium", driveEnd, new[] { m.CastAluminium }, new Vector3(x0, c.y, c.z), left, false, Vector3.right, 0.5f);
            Mesh rearEnd = ctx.Mesh("alternator-rear-end", k =>
            {
                var p = new List<Vector2> { new Vector2(0f, 0f), new Vector2(0.054f, 0f) };
                for (int i = 0; i < 4; i++)
                {
                    float y = 0.006f + i * 0.011f;
                    p.Add(new Vector2(0.054f, y)); p.Add(new Vector2(0.051f, y + 0.003f)); p.Add(new Vector2(0.051f, y + 0.007f)); p.Add(new Vector2(0.054f, y + 0.01f));
                }
                p.Add(new Vector2(0.052f, 0.05f)); p.Add(new Vector2(0.04f, 0.056f)); p.Add(new Vector2(0f, 0.056f));
                k.Lathe(p, 28, 40f);
            });
            ctx.Part(sub, id + ".alternator.rear-end", T("Slip-ring-end housing", "Kontakt halqalar tomoni korpusi", "Rear housing with the rectifier diodes, regulator and brush holder under the cooling slots.", "Sovutish tirqishlari ostida to‘g‘rilagich diodlari, regulyator va cho‘tka ushlagichi bo‘lgan orqa korpus."),
                "Die-cast aluminium", rearEnd, new[] { m.CastAluminium }, new Vector3(x0 - 0.05f, c.y, c.z), left, false, Vector3.left, 0.6f);
            Mesh pulley = ctx.Mesh("alternator-pulley", k =>
            {
                ThermalKit.PolyVPulley(k, ThermalElectricalGeometry.AltR, pw, 6, 0.014f, 24);
                k.Push(new Vector3(0f, pw, 0f), Quaternion.identity);
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.012f, 0f), new Vector2(0.012f, 0.003f), new Vector2(0f, 0.004f) }, 16);
                k.Pop();
            });
            ctx.Part(sub, id + ".alternator.pulley", T("Overrunning pulley", "Erkin yurish shkivi", "Pulley with a one-way clutch that lets the heavy rotor overrun when the engine slows, sparing the belt.", "Dvigatel sekinlashganda og‘ir rotorga erkin aylanishga imkon beruvchi bir tomonlama muftali shkiv; tasmani asraydi."),
                "Steel", pulley, new[] { m.ZincSteel }, new Vector3(g.BeltX - pw * 0.5f, c.y, c.z), InternalsContext.AlongY(Vector3.right, Vector3.up), false, Vector3.right, 0.7f);
            Mesh term = ctx.Mesh("alternator-b-terminal", k =>
            {
                k.Cylinder(0.004f, 0f, 0.018f, 10);
                k.Push(new Vector3(0f, 0.008f, 0f), Quaternion.identity);
                Fasteners.HexNut(k, 0.008f);
                k.Pop();
            });
            Vector3 bPlus = new Vector3(x0 - 0.106f, c.y + 0.026f, c.z + 0.02f);
            ctx.Part(sub, id + ".alternator.b-terminal", T("B+ terminal M8", "B+ klemmasi M8", "Output stud where the charge cable is bolted.", "Zaryad kabeli boltlanadigan chiqish shpilkasi."),
                "Copper, zinc", term, new[] { m.ZincSteel }, bPlus, left, true, Vector3.left, 0.5f);
            Mesh plug = ctx.Mesh("connector-small-2pin", k => ThermalKit.Connector(k, 0.014f, 0.01f, 0.018f));
            ctx.Part(sub, id + ".alternator.regulator-connector", T("Regulator connector", "Regulyator ulagichi", "LIN connector through which the engine computer sets the charge voltage.", "Dvigatel kompyuteri zaryad kuchlanishini o‘rnatadigan LIN ulagichi."),
                "PBT", plug, new[] { m.BlackPlastic }, new Vector3(x0 - 0.106f, c.y - 0.02f, c.z - 0.022f), left, true, Vector3.left, 0.5f);
            // Bracket from the drive-end ear across the belt loop to the block.
            Vector3 b0 = new Vector3(x0 - 0.02f, c.y + 0.054f, c.z);
            var bp = new[] { b0, new Vector3(b0.x, 0.262f, c.z), new Vector3(-0.214f, 0.262f, c.z) };
            Mesh bracket = ctx.Mesh("alternator-bracket", k => ThermalKit.Strap(k, b0, bp, 0.03f, 0.006f, Vector3.forward, 0.006f));
            ctx.Part(sub, id + ".alternator.bracket", T("Alternator bracket", "Generator kronshteyni", "Cast bracket that ties the alternator's ear to the block across the belt loop.", "Generator qulog‘ini tasma halqasi ichidan blokka bog‘lovchi quyma kronshteyn."),
                "Cast aluminium", bracket, new[] { m.CastAluminium }, b0, Quaternion.identity, false, Vector3.up, 0.5f);
            Mesh bolt = ctx.Mesh("bolt-M8x40", k => Fasteners.HexBolt(k, 0.008f, 0.04f));
            ctx.Part(sub, id + ".alternator.pivot-bolt", T("Pivot bolt M8", "Aylanish bolti M8", "Through-bolt in the lower ears that carries the alternator.", "Generatorni ko‘taruvchi pastki quloqlardagi o‘tkazma bolt."),
                "Steel 8.8", bolt, new[] { m.ZincSteel }, new Vector3(x0 - 0.035f, c.y - 0.045f, c.z), InternalsContext.AlongY(Vector3.forward), true, Vector3.forward, 0.4f);
        }

        private void Starter(Transform asm, string id)
        {
            Transform sub = ctx.SubAssembly(asm, id + ".starter", T("Starter Motor", "Starter",
                    "1.7 kW gear-reduction starter: the solenoid throws the pinion into the flywheel ring gear and closes the motor circuit.",
                    "1,7 kVt reduktorli starter: solenoid shesternyani maxovik toj tishiga kiritadi va motor zanjirini ulaydi."),
                "Steel, aluminium", new Vector3(1f, -0.4f, 0f), 0.9f);
            Vector3 s = g.StarterC;
            Quaternion right = InternalsContext.AlongY(Vector3.right, Vector3.up);
            Mesh nose = ctx.Mesh("starter-nose", k =>
            {
                k.Lathe(new[] { new Vector2(0.016f, 0f), new Vector2(0.026f, 0f), new Vector2(0.034f, 0.02f), new Vector2(0.034f, 0.034f), new Vector2(0.016f, 0.034f), new Vector2(0.016f, 0f) }, 22, 40f);
            });
            ctx.Part(sub, id + ".starter.nose", T("Drive housing", "Uzatma korpusi", "Cast nose that carries the pinion's bearing and opens toward the ring gear.", "Shesternya podshipnigini ko‘taruvchi va toj tishiga ochiladigan quyma burun."),
                "Die-cast aluminium", nose, new[] { m.CastAluminium }, s, right, false, Vector3.right, 0.5f);
            Mesh pinion = ctx.Mesh("starter-pinion", k =>
            {
                k.Cylinder(0.006f, -0.004f, 0.02f, 10);
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0125f, 0f), new Vector2(0.0125f, 0.016f), new Vector2(0f, 0.016f) }, 11, 40f, 0f, 360f, true);
            });
            ctx.Part(sub, id + ".starter.pinion", T("Pinion", "Shesternya", "Eleven-tooth pinion on a one-way clutch; it meshes with the ring gear only while cranking.", "Bir tomonlama muftadagi o‘n bir tishli shesternya; faqat aylantirish paytida toj tish bilan ilashadi."),
                "Case-hardened steel", pinion, new[] { m.MachinedSteel }, s + Vector3.right * 0.004f, right, false, Vector3.left, 0.6f);
            Mesh yoke = ctx.Mesh("starter-yoke", k =>
            {
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.036f, 0f), new Vector2(0.038f, 0.004f), new Vector2(0.038f, 0.1f), new Vector2(0.034f, 0.108f), new Vector2(0.012f, 0.112f), new Vector2(0f, 0.112f) }, 24, 40f);
                foreach (float a in new[] { 45f, 225f }) { float r = a * Mathf.Deg2Rad; k.Box(new Vector3(Mathf.Cos(r) * 0.038f, 0.055f, Mathf.Sin(r) * 0.038f), new Vector3(0.004f, 0.11f, 0.004f)); }
            });
            ctx.Part(sub, id + ".starter.motor", T("Motor yoke", "Motor korpusi", "Permanent-magnet motor with a planetary reduction gear inside the steel yoke.", "Po‘lat korpus ichida planetar reduktorli doimiy magnitli motor."),
                "Steel, e-coated", yoke, new[] { m.BlackSteel }, s + Vector3.right * 0.034f, right, false, Vector3.right, 0.7f);
            Mesh solenoid = ctx.Mesh("starter-solenoid", k =>
            {
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.02f, 0f), new Vector2(0.022f, 0.004f), new Vector2(0.022f, 0.074f), new Vector2(0.018f, 0.08f), new Vector2(0f, 0.08f) }, 20, 40f);
                foreach (float z in new[] { -0.009f, 0.009f }) { k.Push(new Vector3(0f, 0.08f, z), Quaternion.identity); k.Cylinder(0.004f, 0f, 0.012f, 8); k.Pop(); }
            });
            Vector3 solAt = s + new Vector3(0.044f, 0.06f, 0f);
            ctx.Part(sub, id + ".starter.solenoid", T("Solenoid", "Solenoid", "Engagement solenoid with the battery (30) and ignition (50) terminals on its end cap.", "Uchidagi qopqoqda akkumulyator (30) va o‘t oldirish (50) klemmalari bo‘lgan ilashtirish solenoidi."),
                "Steel, zinc; copper contacts", solenoid, new[] { m.ZincSteel }, solAt, right, false, Vector3.up, 0.8f);
            Mesh flange = ctx.Mesh("starter-flange", k =>
            {
                var o = new List<Vector2> { new Vector2(-0.04f, -0.02f), new Vector2(0.04f, -0.02f), new Vector2(0.05f, 0.03f), new Vector2(0.03f, 0.105f), new Vector2(-0.03f, 0.105f), new Vector2(-0.05f, 0.03f) };
                k.Extrude(o, 0.008f);
            });
            Quaternion flangeRot = Quaternion.LookRotation(Vector3.right, Vector3.up);
            ctx.Part(sub, id + ".starter.flange", T("Mounting flange", "Mahkamlash flanetsi", "Two-bolt flange that locates the starter on the bell housing.", "Starterni mufta korpusiga joylashtiruvchi ikki boltli flanets."),
                "Die-cast aluminium", flange, new[] { m.CastAluminium }, s + new Vector3(0.03f, 0f, 0f), flangeRot, false, Vector3.right, 0.5f);
            Mesh bolt = ctx.Mesh("bolt-M10x45", k => Fasteners.HexBolt(k, 0.01f, 0.045f));
            foreach (float sz in new[] { -1f, 1f })
                ctx.Part(sub, id + ".starter.bolt-" + (sz < 0 ? "rear" : "front"), T("Starter bolt M10", "Starter bolti M10", "Holds the starter to the bell housing.", "Starterni mufta korpusiga mahkamlaydi."),
                    "Steel 10.9", bolt, new[] { m.ZincSteel }, s + new Vector3(0.036f, 0.09f, sz * 0.03f), InternalsContext.AlongY(Vector3.right), true, Vector3.right, 0.4f);
        }

        // ------------------------------------------------------------------------------------------------ ECU

        private void Ecu(Transform system)
        {
            string id = "electrical.ecu";
            Bounds e = g.Ecu;
            Transform asm = ctx.Assembly(system, id, T("Engine Control Unit", "Dvigatel boshqaruv bloki",
                    "Engine computer on the right of the bay: runs injection, ignition, boost and the cooling fans from its sensors.",
                    "Bo‘linmaning o‘ng tomonidagi dvigatel kompyuteri: datchiklar asosida purkash, o‘t oldirish, bosim va sovutish ventilyatorlarini boshqaradi."),
                "Die-cast aluminium", Vector3.up, 1f);
            Transform unit = ctx.SubAssembly(asm, id + ".controller", T("Controller", "Boshqaruvchi",
                    "Sealed aluminium case with a 32-bit multicore processor board and two high-density connectors.",
                    "32 bitli ko‘p yadroli protsessor platasi va ikki zich ulagichli germetik alyuminiy korpus."),
                "Die-cast aluminium", Vector3.up, 0.7f);
            Mesh housing = ctx.Mesh("ecu-housing", k =>
            {
                Vector3 s = e.size;
                k.RoundedBox(new Vector3(0f, -0.004f, 0f), new Vector3(s.x, s.y - 0.008f, s.z), 0.006f, 2);
                for (int i = 0; i < 9; i++) k.Box(new Vector3(0f, s.y * 0.5f - 0.004f, -s.z * 0.44f + i * s.z * 0.11f), new Vector3(s.x * 0.84f, 0.008f, 0.003f));
            });
            ctx.Part(unit, id + ".controller.housing", T("Finned housing", "Qovurg‘ali korpus", "Die-cast housing whose fins shed the driver stages' heat.", "Qovurg‘alari boshqaruv kaskadlari issig‘ini tarqatuvchi quyma korpus."),
                "Die-cast aluminium", housing, new[] { m.CastAluminium }, e.center, Quaternion.identity, false, Vector3.up, 0.4f);
            Mesh baseMesh = ctx.Mesh("ecu-base", k => k.RoundedBox(Vector3.zero, new Vector3(e.size.x + 0.006f, 0.006f, e.size.z + 0.006f), 0.003f, 1));
            ctx.Part(unit, id + ".controller.base", T("Base plate", "Asos plastinasi", "Steel base with the gasket that seals the board compartment.", "Plata bo‘linmasini zichlovchi qistirmali po‘lat asos."),
                "Steel", baseMesh, new[] { m.BlackSteel }, new Vector3(e.center.x, e.min.y - 0.003f, e.center.z), Quaternion.identity, false, Vector3.down, 0.5f);
            Mesh plug = ctx.Mesh("ecu-connector", k => ThermalKit.Connector(k, 0.03f, 0.022f, 0.026f));
            for (int i = 0; i < 2; i++)
                ctx.Part(unit, id + ".controller.connector-" + (i + 1), T("Connector " + (i == 0 ? "A (engine)" : "B (vehicle)"), (i == 0 ? "A (dvigatel)" : "B (avtomobil)") + " ulagichi",
                        i == 0 ? "94-pin connector of the engine harness: injectors, coils, sensors." : "60-pin connector of the vehicle side: power, CAN bus, pedals.",
                        i == 0 ? "Dvigatel simlarining 94 kontaktli ulagichi: injektorlar, g‘altaklar, datchiklar." : "Avtomobil tomonining 60 kontaktli ulagichi: quvvat, CAN shinasi, pedallar."),
                    "PBT-GF", plug, new[] { m.BlackPlastic }, new Vector3(e.min.x, e.center.y, e.min.z + 0.045f + i * 0.07f), InternalsContext.AlongY(Vector3.left, Vector3.up), true, Vector3.left, 0.6f);
            Transform mounting = ctx.SubAssembly(asm, id + ".mounting", T("ECU Mounting", "Blok mahkamlamasi",
                    "Bracket on the wheel-arch wall and rubber isolators that keep vibration off the board.",
                    "G‘ildirak arkasi devoridagi kronshteyn va platadan tebranishni ajratuvchi rezina amortizatorlar."),
                "Steel and EPDM", Vector3.right, 0.7f);
            float wall = ctx.Layout.FrontWellWallX;
            Mesh bracket = ctx.Mesh("ecu-bracket", k =>
            {
                float span = wall + 0.004f - e.max.x;
                foreach (float sz in new[] { -1f, 1f })
                {
                    k.Box(new Vector3(span * 0.5f - 0.04f, -0.0045f, sz * e.size.z * 0.35f), new Vector3(span + 0.08f, 0.003f, 0.025f));
                    k.Box(new Vector3(span - 0.0015f, 0.012f, sz * e.size.z * 0.35f), new Vector3(0.003f, 0.036f, 0.025f));
                }
            });
            ctx.Part(mounting, id + ".mounting.bracket", T("ECU bracket", "Blok kronshteyni", "Two steel straps under the base, bolted to the inner wheel-arch wall.", "Asos ostidagi, ichki g‘ildirak arkasi devoriga boltlangan ikki po‘lat tasma."),
                "Steel, e-coated", bracket, new[] { m.BlackSteel }, new Vector3(e.max.x, e.min.y - 0.006f, e.center.z), Quaternion.identity, false, Vector3.right, 0.5f);
            Mesh iso = ctx.Mesh("ecu-isolator", k => k.Lathe(new[] { new Vector2(0.003f, 0f), new Vector2(0.008f, 0f), new Vector2(0.008f, 0.006f), new Vector2(0.003f, 0.006f), new Vector2(0.003f, 0f) }, 12));
            int n = 0;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    ctx.Part(mounting, id + ".mounting.isolator-" + (++n), T("Isolator", "Amortizator", "Rubber grommet between the base and the bracket.", "Asos va kronshteyn orasidagi rezina vtulka."),
                        "EPDM", iso, new[] { m.Rubber }, new Vector3(e.center.x + sx * e.size.x * 0.38f, e.min.y - 0.006f, e.center.z + sz * e.size.z * 0.35f), Quaternion.identity, true, Vector3.down, 0.4f);
        }

        // ------------------------------------------------------------------------------------------------ harness

        private void Harness(Transform system)
        {
            string id = "electrical.harness";
            Transform asm = ctx.Assembly(system, id, T("Main Harness", "Asosiy simlar to‘plami",
                    "Battery cable and body harness along the left sill to the front box, with branches to the lamps, fans, engine computer, steering assist and ABS.",
                    "Chap ostona bo‘ylab old blokka boruvchi akkumulyator kabeli va kuzov simlari; chiroqlar, ventilyatorlar, dvigatel kompyuteri, rul kuchaytirgichi va ABS ga tarmoqlar."),
                "Copper, PVC, PA tape", Vector3.up, 1f);
            Bounds b = g.Pdb;
            float fr = rearFloorY;
            Bounds rb = RearBox();
            Vector3 fuseC = new Vector3(bat.max.x - 0.027f, bat.max.y + 0.012f, bat.min.z + 0.026f + 0.035f);

            // ---- power cables
            Transform power = ctx.SubAssembly(asm, id + ".power-cables", T("Power Cables", "Quvvat kabellari",
                    "Heavy unfused and main-fused cables: battery to the front box, front box to the starter and to the alternator.",
                    "Yo‘g‘on kabellar: akkumulyatordan old blokka, old blokdan starter va generatorga."),
                "Copper 50 mm², XLPE", Vector3.up, 0.8f);
            var battery = new[]
            {
                fuseC + new Vector3(0f, 0.004f, 0.026f), fuseC + new Vector3(0f, -0.004f, 0.045f), new Vector3(fuseC.x - 0.015f, fr + 0.022f, bat.max.z + 0.025f), new Vector3(-0.30f, fr + 0.03f, -1.625f),
                new Vector3(-0.53f, fr + 0.03f, -1.60f), new Vector3(-0.53f, fr + 0.02f, -1.515f), new Vector3(-0.53f, 0.30f, -1.485f), new Vector3(-0.53f, 0.143f, -1.47f),
                new Vector3(-0.53f, 0.143f, -1.13f), new Vector3(-0.55f, 0.14f, -1.045f), new Vector3(-0.64f, 0.135f, -0.98f), new Vector3(-0.72f, 0.128f, -0.925f), new Vector3(-0.735f, 0.122f, -0.84f),
                new Vector3(-0.735f, 0.122f, 0.60f), new Vector3(-0.735f, 0.133f, 0.72f), new Vector3(-0.722f, 0.143f, 0.80f), new Vector3(-0.712f, 0.143f, 0.862f), new Vector3(-0.69f, 0.143f, 0.935f),
                new Vector3(-0.63f, 0.143f, 0.995f), new Vector3(-0.58f, 0.143f, 1.055f), new Vector3(-0.557f, 0.148f, 1.12f), new Vector3(-0.557f, 0.15f, 1.40f), new Vector3(-0.563f, 0.20f, 1.435f),
                new Vector3(-0.565f, b.min.y - 0.02f, 1.446f), new Vector3(-0.565f, b.min.y + 0.004f, 1.448f)
            };
            Cable(power, id + ".power-cables.battery", T("Battery cable", "Akkumulyator kabeli", "50 mm² red cable from the main fuse along the left sill to the front box, about 4 m.", "Asosiy saqlagichdan chap ostona bo‘ylab old blokka boruvchi 50 mm² qizil kabel, taxminan 4 m."),
                "harness-battery-cable", battery, 0.0065f, m.WireRed, 0.03f, false);
            var starter = new[]
            {
                new Vector3(b.min.x + 0.026f, b.max.y + 0.004f, b.center.z + 0.02f), new Vector3(b.min.x + 0.026f, b.max.y + 0.03f, b.center.z + 0.02f), new Vector3(-0.556f, 0.62f, b.center.z + 0.02f), new Vector3(-0.50f, 0.672f, 1.53f), new Vector3(-0.43f, 0.688f, 1.59f),
                new Vector3(-0.38f, 0.69f, 1.65f), new Vector3(-0.33f, 0.69f, 1.72f), new Vector3(-0.20f, 0.69f, 1.765f), new Vector3(0.20f, 0.69f, 1.765f), new Vector3(0.335f, 0.68f, 1.76f),
                new Vector3(0.355f, 0.60f, 1.735f), new Vector3(0.40f, 0.56f, 1.725f), new Vector3(g.StarterC.x + 0.044f + 0.08f + 0.01f, g.StarterC.y + 0.06f + 0.009f, g.StarterC.z + 0.009f)
            };
            Cable(power, id + ".power-cables.starter", T("Starter cable", "Starter kabeli", "35 mm² cable from the front box over the back of the engine to the solenoid's terminal 30.", "Old blokdan dvigatel orqasi ustidan solenoidning 30-klemmasiga boruvchi 35 mm² kabel."),
                "harness-starter-cable", starter, 0.0062f, m.WireRed, 0.03f, false);
            Vector3 bPlus = new Vector3(g.BeltX - (ThermalElectricalGeometry.BeltWidth + 0.002f) * 0.5f - 0.004f - 0.106f, g.AltC.y + 0.026f, g.AltC.z + 0.02f);
            // Integration (combined car): the last metres pass behind the ABS bracket's rear leg and drop to the B+ stud
            // inboard of it (the modulator stands above the alternator's slip-ring end).
            var alt = new[] { new Vector3(b.center.x, b.min.y + 0.065f, b.max.z), new Vector3(b.center.x + 0.005f, b.min.y + 0.065f, b.max.z + 0.03f), new Vector3(-0.515f, 0.352f, 1.64f), new Vector3(-0.465f, 0.27f, 1.70f),
                new Vector3(-0.42f, 0.25f, 1.705f), new Vector3(-0.42f, bPlus.y + 0.008f, 1.74f), bPlus + Vector3.left * 0.016f };
            Cable(power, id + ".power-cables.alternator", T("Alternator cable", "Generator kabeli", "25 mm² charge cable from the alternator's B+ stud to its 125 A fuse in the front box.", "Generator B+ shpilkasidan old blokdagi 125 A saqlagichigacha boruvchi 25 mm² zaryad kabeli."),
                "harness-alternator-cable", alt, 0.0055f, m.WireRed, 0.02f, false);
            Mesh lug = ctx.Mesh("cable-lug-m8", k =>
            {
                k.Cylinder(0.0075f, 0f, 0.003f, 12);
                k.Tube(new[] { new Vector3(0f, 0.0015f, 0.004f), new Vector3(0f, 0.0015f, 0.02f) }, 0.0065f, 10);
            });
            ctx.Part(power, id + ".power-cables.lug-starter", T("Cable lug M8", "Kabel uchligi M8", "Crimped tinned-copper lug on the solenoid stud.", "Solenoid shpilkasidagi qisilgan qalaylangan mis uchlik."),
                "Tinned copper", lug, new[] { m.Copper }, starter[starter.Length - 1] + Vector3.down * 0.004f, Quaternion.LookRotation(Vector3.left, Vector3.up), true, Vector3.up, 0.4f);
            ctx.Part(power, id + ".power-cables.lug-alternator", T("Cable lug M8", "Kabel uchligi M8", "Crimped lug under the alternator's B+ nut.", "Generator B+ gaykasi ostidagi qisilgan uchlik."),
                "Tinned copper", lug, new[] { m.Copper }, bPlus + Vector3.left * 0.008f, Quaternion.LookRotation(Vector3.forward, Vector3.left), true, Vector3.left, 0.4f);

            // ---- body harness
            Transform body = ctx.SubAssembly(asm, id + ".body-harness", T("Main Bundle", "Asosiy to‘plam",
                    "Taped bundle of about 80 circuits from the rear fuse box along the left sill to the front box.",
                    "Orqa saqlagichlar blokidan chap ostona bo‘ylab old blokka boruvchi taxminan 80 zanjirli lentalangan to‘plam."),
                "Copper, PVC, PET tape", Vector3.up, 0.8f);
            var bundle = new[]
            {
                new Vector3(rb.center.x, rb.min.y + 0.03f, rb.max.z), new Vector3(rb.center.x, rb.min.y + 0.02f, rb.max.z + 0.025f), new Vector3(-0.45f, fr + 0.011f, -1.655f), new Vector3(-0.522f, fr + 0.011f, -1.615f), new Vector3(-0.522f, fr + 0.011f, -1.595f),
                new Vector3(-0.522f, fr + 0.024f, -1.515f), new Vector3(-0.522f, 0.30f, -1.485f), new Vector3(-0.522f, 0.175f, -1.47f), new Vector3(-0.522f, 0.175f, -1.12f),
                new Vector3(-0.53f, 0.142f, -1.03f), new Vector3(-0.62f, 0.137f, -0.965f), new Vector3(-0.70f, 0.13f, -0.915f), new Vector3(-0.712f, 0.125f, -0.84f),
                new Vector3(-0.712f, 0.125f, 0.60f), new Vector3(-0.712f, 0.135f, 0.72f), new Vector3(-0.70f, 0.145f, 0.80f), new Vector3(-0.69f, 0.145f, 0.86f), new Vector3(-0.67f, 0.145f, 0.93f),
                new Vector3(-0.61f, 0.145f, 0.99f), new Vector3(-0.56f, 0.145f, 1.05f), new Vector3(-0.535f, 0.15f, 1.12f), new Vector3(-0.535f, 0.152f, 1.40f), new Vector3(-0.535f, 0.20f, 1.465f),
                new Vector3(-0.535f, b.min.y - 0.02f, 1.478f), new Vector3(-0.535f, b.min.y + 0.004f, 1.48f)
            };
            Cable(body, id + ".body-harness.bundle", T("Main bundle", "Asosiy to‘plam", "Body circuits, CAN and LIN buses wrapped in PET cloth tape.", "PET mato lentaga o‘ralgan kuzov zanjirlari, CAN va LIN shinalari."),
                "harness-main-bundle", bundle, 0.009f, m.WireBlack, 0.035f, false);
            Mesh clip = ctx.Mesh("harness-clip", k =>
            {
                k.Box(new Vector3(0f, 0f, 0f), new Vector3(0.05f, 0.003f, 0.016f));
                k.Push(new Vector3(0.011f, 0.0105f, 0f), Quaternion.Euler(-90f, 0f, 0f));
                k.Lathe(new[] { new Vector2(0.0105f, -0.007f), new Vector2(0.0125f, -0.007f), new Vector2(0.0125f, 0.007f), new Vector2(0.0105f, 0.007f), new Vector2(0.0105f, -0.007f) }, 12, 40f, 0f, 180f);
                k.Pop();
                k.Push(new Vector3(-0.011f, 0.008f, 0f), Quaternion.Euler(-90f, 0f, 0f));
                k.Lathe(new[] { new Vector2(0.0075f, -0.007f), new Vector2(0.0095f, -0.007f), new Vector2(0.0095f, 0.007f), new Vector2(0.0075f, 0.007f), new Vector2(0.0075f, -0.007f) }, 12, 40f, 0f, 180f);
                k.Pop();
                k.Cylinder(0.003f, -0.016f, 0f, 8);
            });
            int ci = 0;
            // Integration (combined car): the rear-run clip sits behind the hybrid battery pack, under the battery cable.
            foreach (float z in new[] { -1.36f, -0.60f, -0.20f, 0.20f, 0.55f })
            {
                bool rearRun = z < -1.0f;
                float x = rearRun ? -0.519f : -0.7235f;
                float y = rearRun ? 0.135f : 0.114f;
                ctx.Part(body, id + ".body-harness.clip-" + (++ci), T("Harness clip", "Sim qisqichi", "Fir-tree clip that holds the bundle and the battery cable to the floor.", "To‘plam va akkumulyator kabelini polga ushlab turuvchi archasimon qisqich."),
                    "PA66", clip, new[] { m.BlackPlastic }, new Vector3(x, y, z), Quaternion.Euler(0f, 0f, 0f), true, Vector3.down, 0.4f);
            }
            Mesh grommet = ctx.Mesh("harness-bulkhead-grommet", k => k.Lathe(new[] { new Vector2(0.012f, -0.012f), new Vector2(0.018f, -0.012f), new Vector2(0.03f, -0.002f), new Vector2(0.03f, 0.002f), new Vector2(0.018f, 0.01f), new Vector2(0.012f, 0.01f), new Vector2(0.012f, -0.012f) }, 18, 40f));
            Vector3 gAt = new Vector3(-0.70f, 0.144f, 0.865f);
            ctx.Part(body, id + ".body-harness.grommet", T("Bulkhead grommet", "Bo‘lim devori zichlagichi", "Rubber grommet where the bundle and cable leave the footwell for the front bay.", "To‘plam va kabel oyoq bo‘linmasidan old bo‘linmaga o‘tadigan joydagi rezina zichlagich."),
                "EPDM", grommet, new[] { m.Rubber }, gAt, InternalsContext.AlongY(new Vector3(0.25f, 0f, 1f).normalized, Vector3.up), false, Vector3.back, 0.4f);

            // ---- front branches
            Transform frontB = ctx.SubAssembly(asm, id + ".front-branches", T("Front Branches", "Old tarmoqlar",
                    "Branches from the front box: lamps, radiator fans, the engine computer, the steering-assist unit and the ABS modulator.",
                    "Old blokdan tarmoqlar: chiroqlar, radiator ventilyatorlari, dvigatel kompyuteri, rul kuchaytirgichi va ABS modulyatori."),
                "Copper, PVC, PA conduit", Vector3.up, 0.8f);
            Mesh lampPlug = ctx.Mesh("connector-lamp", k => ThermalKit.Connector(k, 0.024f, 0.016f, 0.024f));
            Vector3 lampL = new Vector3(-0.66f, 0.585f, 1.975f);
            // Integration (combined car): along the modulator at y 0.355, under the front-left brake line where it rises
            // from the modulator's port to the wheel-well wall and over the ABS pump motor.
            var left = new[] { new Vector3(b.center.x - 0.02f, b.max.y - 0.03f, b.max.z), new Vector3(b.center.x - 0.02f, b.max.y - 0.035f, b.max.z + 0.03f), new Vector3(-0.548f, 0.355f, 1.62f), new Vector3(-0.548f, 0.355f, 1.80f), new Vector3(-0.545f, 0.43f, 1.855f), new Vector3(-0.53f, 0.528f, 1.86f), new Vector3(-0.56f, 0.55f, 1.93f), new Vector3(-0.62f, 0.572f, 1.962f), lampL + Vector3.right * 0.018f };
            Cable(frontB, id + ".front-branches.left-lamp", T("Left lamp branch", "Chap chiroq tarmog‘i", "Feeds the left headlamp module and indicator.", "Chap fara moduli va burilish chirog‘ini ta’minlaydi."),
                "harness-left-lamp", left, 0.006f, m.WireBlack, 0.03f, false);
            ctx.Part(frontB, id + ".front-branches.left-lamp-connector", T("Left headlamp connector", "Chap fara ulagichi", "Sealed 12-pin connector at the back of the headlamp.", "Faraning orqasidagi germetik 12 kontaktli ulagich."),
                "PBT", lampPlug, new[] { m.BlackPlastic }, lampL, InternalsContext.AlongY(Vector3.right, Vector3.up), true, Vector3.left, 0.5f);
            Vector3 fanSplit = new Vector3(-0.53f, 0.528f, 1.86f);
            Vector3 fanIn = g.Rad(0.068f, 0.04f + 0.032f, -0.075f), fanOut = g.Rad(-0.068f, 0.04f + 0.032f, -0.075f);
            var fan = new[] { fanSplit, new Vector3(-0.56f, 0.47f, 1.91f), new Vector3(fanIn.x, fanIn.y + 0.03f, fanIn.z), fanIn };
            Cable(frontB, id + ".front-branches.fan-lead-inner", T("Inner fan lead", "Ichki ventilyator simi", "Power pair to the inner fan motor.", "Ichki ventilyator motoriga quvvat juftligi."),
                "harness-fan-inner", fan, 0.004f, m.WireBlack, 0.02f, true);
            var fan2 = new[] { new Vector3(-0.56f, 0.47f, 1.91f), new Vector3(-0.64f, 0.44f, 1.915f), new Vector3(fanOut.x, fanOut.y + 0.03f, fanOut.z), fanOut };
            Cable(frontB, id + ".front-branches.fan-lead-outer", T("Outer fan lead", "Tashqi ventilyator simi", "Power pair to the outer fan motor.", "Tashqi ventilyator motoriga quvvat juftligi."),
                "harness-fan-outer", fan2, 0.004f, m.WireBlack, 0.02f, true);
            Vector3 ecuPlug = new Vector3(g.Ecu.min.x - 0.026f - 0.01f, g.Ecu.center.y, g.Ecu.min.z + 0.045f);
            var engine = new[]
            {
                new Vector3(b.center.x + 0.012f, b.max.y - 0.055f, b.max.z), new Vector3(b.center.x + 0.015f, b.max.y - 0.05f, b.max.z + 0.03f), new Vector3(-0.518f, 0.392f, 1.62f), new Vector3(-0.518f, 0.395f, 1.80f), new Vector3(-0.515f, 0.41f, 1.864f),
                new Vector3(-0.47f, 0.545f, 1.872f), new Vector3(-0.40f, 0.62f, 1.86f), new Vector3(-0.33f, 0.665f, 1.77f), new Vector3(-0.20f, 0.665f, 1.745f), new Vector3(0.20f, 0.665f, 1.745f), new Vector3(0.32f, 0.655f, 1.735f),
                new Vector3(ecuPlug.x - 0.02f, ecuPlug.y + 0.01f, ecuPlug.z + 0.01f), ecuPlug
            };
            Cable(frontB, id + ".front-branches.engine", T("Engine computer branch", "Dvigatel kompyuteri tarmog‘i", "Bundle in corrugated conduit over the back of the engine to the engine computer.", "Dvigatel orqasi ustidan dvigatel kompyuteriga boruvchi gofrali quvurdagi to‘plam."),
                "harness-engine-branch", engine, 0.0075f, m.WireBlack, 0.03f, false);
            Vector3 lampR = new Vector3(0.66f, 0.585f, 1.975f);
            var right = new[] { new Vector3(0.32f, 0.655f, 1.735f), new Vector3(0.40f, 0.668f, 1.81f), new Vector3(0.50f, 0.60f, 1.86f), new Vector3(0.56f, 0.585f, 1.93f), new Vector3(0.62f, 0.585f, 1.962f), lampR + Vector3.left * 0.018f };
            Cable(frontB, id + ".front-branches.right-lamp", T("Right lamp branch", "O‘ng chiroq tarmog‘i", "Feeds the right headlamp module and indicator.", "O‘ng fara moduli va burilish chirog‘ini ta’minlaydi."),
                "harness-right-lamp", right, 0.006f, m.WireBlack, 0.03f, false);
            ctx.Part(frontB, id + ".front-branches.right-lamp-connector", T("Right headlamp connector", "O‘ng fara ulagichi", "Sealed 12-pin connector at the back of the headlamp.", "Faraning orqasidagi germetik 12 kontaktli ulagich."),
                "PBT", lampPlug, new[] { m.BlackPlastic }, lampR, InternalsContext.AlongY(Vector3.left, Vector3.up), true, Vector3.right, 0.5f);
            // Steering-assist branch: mates with the end of the EPS harness from the steering builder.
            EpsEnd(out Vector3 epsEnd, out Vector3 epsDir);
            Vector3 inline = epsEnd + epsDir * 0.012f;
            // Integration (combined car): leaves the box's underside on its outboard half and crosses under the two rear
            // brake lines (y 0.25 along x -0.50) and in front of the fuel lines' climb before it runs to the rack.
            var eps = new[]
            {
                new Vector3(b.min.x + 0.02f, b.min.y + 0.002f, b.max.z - 0.012f), new Vector3(b.min.x + 0.02f, b.min.y - 0.025f, b.max.z - 0.006f), new Vector3(-0.555f, 0.215f, 1.535f),
                new Vector3(-0.50f, 0.20f, 1.54f), new Vector3(-0.45f, 0.195f, 1.54f), new Vector3(-0.35f, 0.175f, 1.545f), new Vector3(-0.29f, 0.18f, 1.535f),
                epsEnd + epsDir * 0.04f + Vector3.left * 0.01f, epsEnd + epsDir * 0.024f
            };
            Cable(frontB, id + ".front-branches.eps", T("Steering-assist branch", "Rul kuchaytirgichi tarmog‘i", "40 A feed and CAN pair to the electric power-steering unit.", "Elektr rul kuchaytirgichiga 40 A ta’minot va CAN juftligi."),
                "harness-eps-branch", eps, 0.0055f, m.WireBlack, 0.025f, false);
            Mesh inlineMesh = ctx.Mesh("connector-inline", k => ThermalKit.Connector(k, 0.02f, 0.016f, 0.026f));
            ctx.Part(frontB, id + ".front-branches.eps-connector", T("Inline connector", "Oraliq ulagich", "Sealed inline pair that joins the branch to the steering unit's harness.", "Tarmoqni rul bloki simlariga ulovchi germetik oraliq juftlik."),
                "PBT", inlineMesh, new[] { m.BlackPlastic }, epsEnd - epsDir * 0.002f, InternalsContext.AlongY(epsDir, Vector3.up), true, epsDir, 0.4f);
            // ABS branch: its plug mates from above with the 46-pin connector on the brakes builder's modulator control unit
            // (read from the scene; the constant is the fallback when no modulator is built). The plug is not fine detail:
            // at Low the branch must still end on something.
            Vector3 absFace = AbsConnectorFace;
            Vector3 absUp = Vector3.left;
            foreach (CarPartInfo p in ctx.Root.GetComponentsInChildren<CarPartInfo>(true))
                if (p.id == "brakes.abs.control-unit.connector")
                {
                    // On the connector's top face, 5 mm forward so the plug clears the control unit's front edge.
                    absFace = ctx.Root.InverseTransformPoint(p.transform.position) + Vector3.up * 0.025f + Vector3.forward * 0.005f;
                    absUp = Vector3.up;
                    break;
                }
            absConnectorAt = absFace;
            Vector3 absBoot = absFace + absUp * 0.036f;
            var abs = absUp == Vector3.up
                ? new[]
                {
                    new Vector3(b.max.x, b.center.y - 0.01f, b.center.z), new Vector3(b.max.x + 0.012f, b.center.y - 0.015f, b.center.z + 0.008f), new Vector3(-0.475f, 0.36f, 1.575f),
                    new Vector3(-0.43f, 0.375f, 1.62f), new Vector3(absFace.x - 0.004f, 0.385f, 1.70f), new Vector3(absFace.x, absBoot.y + 0.012f, absFace.z - 0.03f), absBoot + Vector3.up * 0.004f
                }
                : new[] { new Vector3(b.max.x, b.center.y - 0.01f, b.center.z), new Vector3(b.max.x + 0.012f, b.center.y - 0.02f, b.center.z + 0.008f), absBoot + Vector3.left * 0.012f, absBoot };
            Cable(frontB, id + ".front-branches.abs", T("ABS branch", "ABS tarmog‘i", "Pump-motor feed, valve supply and CAN pair to the ABS / ESC modulator.", "ABS / ESC modulyatoriga nasos motori ta’minoti, klapan ta’minoti va CAN juftligi."),
                "harness-abs-branch", abs, 0.0055f, m.WireBlack, 0.015f, false);
            Mesh absMesh = ctx.Mesh("connector-abs", k => ThermalKit.Connector(k, 0.03f, 0.02f, 0.026f));
            ctx.Part(frontB, id + ".front-branches.abs-connector", T("ABS plug", "ABS vilkasi", "Sealed 46-pin plug with a locking lever that mates with the modulator's connector from above.", "Modulyator ulagichiga yuqoridan ulanadigan, qulflash dastakli germetik 46 kontaktli vilka."),
                "PBT", absMesh, new[] { m.BlackPlastic }, absFace, InternalsContext.AlongY(absUp, Vector3.forward), false, absUp, 0.4f);

            // ---- rear branches
            Transform rearB = ctx.SubAssembly(asm, id + ".rear-branches", T("Rear Branches", "Orqa tarmoqlar",
                    "Lamp branch from the rear fuse box up to the tail lamps.",
                    "Orqa saqlagichlar blokidan orqa chiroqlarga ko‘tariluvchi tarmoq."),
                "Copper, PVC", Vector3.up, 0.8f);
            Vector3 tailL = new Vector3(-0.50f, 0.74f, -1.62f);
            var tail = new[]
            {
                new Vector3(rb.center.x, rb.max.y + 0.004f, rb.center.z), new Vector3(rb.center.x, rb.max.y + 0.03f, rb.center.z), new Vector3(-0.49f, 0.58f, -1.72f), new Vector3(-0.50f, 0.68f, -1.65f), tailL + Vector3.down * 0.002f
            };
            Cable(rearB, id + ".rear-branches.tail", T("Tail lamp branch", "Orqa chiroqlar tarmog‘i", "Rises from the rear fuse box to the tail-lamp connector under the rear deck.", "Orqa saqlagichlar blokidan orqa paluba ostidagi orqa chiroq ulagichiga ko‘tariladi."),
                "harness-tail-branch", tail, 0.0055f, m.WireBlack, 0.03f, false);
            Mesh tailPlug = ctx.Mesh("connector-tail", k => ThermalKit.Connector(k, 0.02f, 0.014f, 0.022f));
            ctx.Part(rearB, id + ".rear-branches.tail-connector", T("Tail lamp connector", "Orqa chiroq ulagichi", "Twelve-pin connector that feeds the tail-lamp light bar.", "Orqa chiroqlar chizig‘ini ta’minlovchi o‘n ikki kontaktli ulagich."),
                "PBT", tailPlug, new[] { m.BlackPlastic }, tailL, Quaternion.identity, true, Vector3.up, 0.4f);
        }

        // End of the steering builder's EPS harness and its direction (as Steering.cs computes it), from the scene's
        // connector when present.
        private void EpsEnd(out Vector3 end, out Vector3 dir)
        {
            var geo = FrontChassisGeometry.Of(ctx.Layout, ctx.Probe);
            Vector3 plug = new Vector3(-0.236f, geo.RackY, geo.RackZ - 0.052f);
            foreach (CarPartInfo p in ctx.Root.GetComponentsInChildren<CarPartInfo>(true))
                if (p.id == "steering.rack.eps.connector") { plug = ctx.Root.InverseTransformPoint(p.transform.position); break; }
            float toBulkhead = Mathf.Max(0.04f, geo.RackZ - 0.052f - ctx.Layout.FirewallZ + 0.03f);
            Vector3 b = plug + new Vector3(-0.004f, -0.006f, -toBulkhead * 0.5f), c = plug + new Vector3(-0.016f, -0.012f, -toBulkhead);
            end = c;
            dir = (c - b).normalized;
        }

        private void Cable(Transform parent, string id, Txt text, string key, Vector3[] pts, float r, Material mat, float bend, bool fineDefault)
        {
            Vector3 o = pts[0];
            Mesh mesh = ctx.Mesh(key, k => ThermalKit.Run(k, o, pts, r, r < 0.0085f ? 8 : 10, bend));
            ctx.Part(parent, id, text, r < 0.008f ? "Copper, PVC" : "Copper, PVC, PET tape", mesh, new[] { mat }, o, Quaternion.identity, fineDefault || r < 0.004f, Vector3.up, 0.4f);
        }
    }
}
#endif
