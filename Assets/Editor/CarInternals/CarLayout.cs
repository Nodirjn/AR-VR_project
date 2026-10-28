#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace CarInternals
{
    /// <summary>
    /// One wheel of the hero car as measured from its meshes, in car space. The wheel frame is steered: Outward is the
    /// axle pointing away from the car's centre line, Rolling the direction the wheel rolls, Up is vertical.
    /// </summary>
    public sealed class WheelInfo
    {
        public string Node;
        public bool Front, Left;
        /// <summary>-1 on the left, +1 on the right.</summary>
        public float Side;
        public Vector3 Centre;
        public Vector3 Outward;
        public Vector3 Rolling;
        /// <summary>Rotation of the wheel about the vertical, degrees, positive toward the car's right.</summary>
        public float SteerDeg;
        public float TyreRadius, TyreWidth, RimRadius;
        /// <summary>Axial extent of the rim along Outward from the centre plane (negative = inboard).</summary>
        public float RimAxialMin, RimAxialMax;
        public float DiscOuterRadius, DiscInnerRadius, DiscAxialMin, DiscAxialMax;
        /// <summary>Caliper position around the axle: degrees from Rolling toward Up.</summary>
        public float CaliperAngle, CaliperRadiusMin, CaliperRadiusMax, CaliperAxialMin, CaliperAxialMax;

        /// <summary>Car-space point from wheel-frame coordinates: along Outward, along Rolling and up.</summary>
        public Vector3 Point(float axial, float rolling, float up) => Centre + Outward * axial + Rolling * rolling + Vector3.up * up;

        /// <summary>Rotation whose +Y is Outward (lathe parts on the axle) and +Z is Rolling.</summary>
        public Quaternion AxleRotation => Quaternion.LookRotation(Rolling, Outward);

        /// <summary>The same point for the car without steering (steering removed about the wheel centre).</summary>
        public Vector3 Unsteered(float inboard, float forward, float up) => Centre + new Vector3(-Side * inboard, up, forward);
    }

    /// <summary>
    /// The hero car's layout measured from the scene, in car space: metres, x to the right, y up from the ground (the
    /// lowest tyre point), z forward from the middle of the wheelbase, on the track centre line. Every subsystem
    /// builder places its parts from these numbers (never from world coordinates), and keeps to its reserved volume.
    /// </summary>
    public sealed class CarLayout
    {
        public const string V6Name = "V6_Engine_Prototype(Clone)";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public Transform Car;
        public Transform V6;
        /// <summary>Car space to world, and back.</summary>
        public Matrix4x4 CarToWorld, WorldToCar;
        public Vector3 WorldOrigin;
        public Quaternion WorldRotation;

        public WheelInfo FrontLeft, FrontRight, RearLeft, RearRight;
        public float Wheelbase, TrackFront, TrackRear;
        /// <summary>Front and rear axle lines (z of the wheel centres) and the wheel-centre height.</summary>
        public float FrontAxleZ, RearAxleZ, WheelCentreY;

        /// <summary>Top of the cabin floor and bottom of the underbody (ground clearance) at the middle of the car.</summary>
        public float FloorTopY, UnderbodyY;
        /// <summary>Inner wall of each wheel well (|x|), measured inboard of the wheel at three heights.</summary>
        public float FrontWellWallX, RearWellWallX;
        /// <summary>Underside of the wheel-arch roof above the inner half of each wheel (y).</summary>
        public float FrontArchTopY, RearArchTopY;
        /// <summary>Front face of the cabin (toe board / dash) on the centre line at footwell height (z).</summary>
        public float FirewallZ;

        public Bounds Body;            // body shell (Body* and License*)
        public Bounds Seats, Dash, Pedals, Floor, Axles, AxleBarFront, AxleBarRear;
        public Bounds V6Bounds;
        public readonly Dictionary<string, Bounds> V6Parts = new Dictionary<string, Bounds>();
        public float FrontBumperZ, RearBumperZ;

        /// <summary>Steering wheel: rim centre and the column axis (unit, pointing from the wheel into the dash).</summary>
        public Vector3 SteeringWheelCentre, SteeringAxis;
        public float SteeringWheelRadius;

        /// <summary>Reserved volume per subsystem key (see the brief): builders stay inside their own.</summary>
        public readonly Dictionary<string, Bounds> Reserved = new Dictionary<string, Bounds>();

        public IEnumerable<WheelInfo> Wheels
        {
            get
            {
                yield return FrontLeft; yield return FrontRight; yield return RearLeft; yield return RearRight;
            }
        }

        public WheelInfo Wheel(bool front, bool left) => front ? (left ? FrontLeft : FrontRight) : (left ? RearLeft : RearRight);

        public Vector3 ToCar(Vector3 world) => WorldToCar.MultiplyPoint3x4(world);
        public Vector3 DirToCar(Vector3 world) => WorldToCar.MultiplyVector(world);

        /// <summary>Measures the car. Throws when a required node is missing.</summary>
        public static CarLayout Measure(Transform car, InternalsProbe probe)
        {
            var l = new CarLayout { Car = car, V6 = car.Find(V6Name) };
            Transform hood = car.Find("BodyHood");
            if (hood == null) throw new InvalidOperationException("CarConcept/BodyHood not found");
            string[] wheelNames = { "WheelFrontL", "WheelFrontR", "WheelRearL", "WheelRearR" };
            var centresWorld = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                Transform w = car.Find(wheelNames[i]);
                if (w == null) throw new InvalidOperationException("CarConcept/" + wheelNames[i] + " not found");
                Renderer tyre = TyreOf(w);
                if (tyre == null) throw new InvalidOperationException(wheelNames[i] + " has no tyre mesh");
                centresWorld[i] = WorldVertexBounds(tyre).center;
            }

            // Car frame: forward from the wheels' centre toward the hood, up = world up, origin on the ground.
            Vector3 mid = (centresWorld[0] + centresWorld[1] + centresWorld[2] + centresWorld[3]) * 0.25f;
            Vector3 hoodCentre = WorldVertexBounds(hood.GetComponent<Renderer>()).center;
            Vector3 forward = Vector3.ProjectOnPlane(hoodCentre - mid, Vector3.up).normalized;
            // Align forward with the axle midpoints so the frame follows the wheelbase exactly.
            Vector3 axleDir = Vector3.ProjectOnPlane((centresWorld[0] + centresWorld[1]) - (centresWorld[2] + centresWorld[3]), Vector3.up);
            if (axleDir.sqrMagnitude > 1e-6f && Vector3.Dot(axleDir, forward) > 0f) forward = axleDir.normalized;
            float groundY = float.MaxValue;
            foreach (string n in wheelNames) groundY = Mathf.Min(groundY, WorldVertexBounds(TyreOf(car.Find(n))).min.y);
            l.WorldOrigin = new Vector3(mid.x, groundY, mid.z);
            l.WorldRotation = Quaternion.LookRotation(forward, Vector3.up);
            l.CarToWorld = Matrix4x4.TRS(l.WorldOrigin, l.WorldRotation, Vector3.one);
            l.WorldToCar = l.CarToWorld.inverse;
            probe.Frame.CarToWorld = l.CarToWorld;
            probe.Frame.WorldToCar = l.WorldToCar;

            l.FrontLeft = l.MeasureWheel(car.Find("WheelFrontL"), true, true);
            l.FrontRight = l.MeasureWheel(car.Find("WheelFrontR"), true, false);
            l.RearLeft = l.MeasureWheel(car.Find("WheelRearL"), false, true);
            l.RearRight = l.MeasureWheel(car.Find("WheelRearR"), false, false);
            l.FrontAxleZ = (l.FrontLeft.Centre.z + l.FrontRight.Centre.z) * 0.5f;
            l.RearAxleZ = (l.RearLeft.Centre.z + l.RearRight.Centre.z) * 0.5f;
            l.Wheelbase = l.FrontAxleZ - l.RearAxleZ;
            l.TrackFront = l.FrontRight.Centre.x - l.FrontLeft.Centre.x;
            l.TrackRear = l.RearRight.Centre.x - l.RearLeft.Centre.x;
            l.WheelCentreY = l.Wheels.Average(w => w.Centre.y);

            // Body, cabin and engine volumes.
            l.Body = l.BoundsOf(car, t => TopName(car, t).StartsWith("Body", StringComparison.Ordinal) || TopName(car, t).StartsWith("License", StringComparison.Ordinal));
            l.Seats = l.BoundsOf(car, t => TopName(car, t).StartsWith("InteriorSeats", StringComparison.Ordinal));
            l.Dash = l.BoundsOf(car, t => TopName(car, t).StartsWith("InteriorDash", StringComparison.Ordinal));
            l.Pedals = l.BoundsOf(car, t => TopName(car, t).StartsWith("InteriorPedal", StringComparison.Ordinal));
            l.Floor = l.BoundsOf(car, t => TopName(car, t).StartsWith("InteriorFloor", StringComparison.Ordinal) && !t.name.Contains("mats"));
            l.Axles = l.BoundsOf(car, t => TopName(car, t).StartsWith("Axles", StringComparison.Ordinal));
            l.FrontBumperZ = l.Body.max.z;
            l.RearBumperZ = l.Body.min.z;
            l.MeasureAxleBars(car.Find("Axles"));
            if (l.V6 != null)
            {
                // Keyed by renderer name, so the numbers do not depend on how the V6 is grouped.
                bool any = false;
                foreach (Renderer r in l.V6.GetComponentsInChildren<Renderer>(true))
                {
                    Bounds b = l.BoundsOf(r.transform, t => t == r.transform);
                    if (b.size == Vector3.zero) continue;
                    l.V6Parts[r.name] = b;
                    if (any) l.V6Bounds.Encapsulate(b); else { l.V6Bounds = b; any = true; }
                }
            }
            l.MeasureSteeringWheel(car);

            // Probes against the car's own surfaces.
            Vector3 cabin = new Vector3(0f, l.WheelCentreY + 0.2f, (l.FrontAxleZ + l.RearAxleZ) * 0.5f);
            l.FloorTopY = probe.Down(cabin, 1.5f, out float floorTop) ? floorTop : l.Floor.min.y + 0.04f;
            l.UnderbodyY = probe.Up(new Vector3(0f, -0.05f, cabin.z), 1f, out float under) ? under : l.Floor.min.y;
            l.FrontWellWallX = l.WellWall(probe, l.FrontLeft, l.FrontRight);
            l.RearWellWallX = l.WellWall(probe, l.RearLeft, l.RearRight);
            l.FrontArchTopY = l.ArchTop(probe, l.FrontLeft, l.FrontRight, l.FrontWellWallX);
            l.RearArchTopY = l.ArchTop(probe, l.RearLeft, l.RearRight, l.RearWellWallX);
            Vector3 toe = new Vector3(0f, l.FloorTopY + 0.08f, l.FrontAxleZ - 0.3f);
            l.FirewallZ = probe.Cast(toe, Vector3.forward, 1.2f, out float firewall, "InteriorDash", "InteriorFloor", "CarConcept")
                ? toe.z + firewall : l.Dash.max.z;
            l.ReserveVolumes();
            return l;
        }

        private WheelInfo MeasureWheel(Transform node, bool front, bool left)
        {
            var w = new WheelInfo { Node = node.name, Front = front, Left = left, Side = left ? -1f : 1f };
            Renderer tyre = TyreOf(node);
            Bounds tb = VertexBounds(tyre);
            w.Centre = tb.center;
            // Axle: the node axis closest to the car's x axis, pointing outward.
            Vector3[] axes = { DirToCar(node.right), DirToCar(node.up), DirToCar(node.forward) };
            Vector3 axle = axes.OrderByDescending(a => Mathf.Abs(a.x)).First();
            axle.y = 0f;
            axle.Normalize();
            if (axle.x * w.Side < 0f) axle = -axle;
            w.Outward = axle;
            Vector3 right = axle * w.Side;
            w.SteerDeg = Mathf.Atan2(-right.z, right.x) * Mathf.Rad2Deg;
            w.Rolling = Quaternion.AngleAxis(w.SteerDeg, Vector3.up) * Vector3.forward;
            RadialAxial(w, tyre, out _, out w.TyreRadius, out float tMin, out float tMax, out _);
            w.TyreWidth = tMax - tMin;
            foreach (Renderer r in node.GetComponentsInChildren<Renderer>(true))
            {
                if (r == tyre) continue;
                RadialAxial(w, r, out float rMin, out float rMax, out float aMin, out float aMax, out float angle);
                if (r.name.Contains("Rim")) { w.RimRadius = rMax; w.RimAxialMin = aMin; w.RimAxialMax = aMax; }
                else if (r.name.Contains("Disc")) { w.DiscInnerRadius = rMin; w.DiscOuterRadius = rMax; w.DiscAxialMin = aMin; w.DiscAxialMax = aMax; }
                else if (r.name.Contains("Pad") || r.name.Contains("Caliper"))
                {
                    w.CaliperAngle = angle; w.CaliperRadiusMin = rMin; w.CaliperRadiusMax = rMax; w.CaliperAxialMin = aMin; w.CaliperAxialMax = aMax;
                }
            }
            return w;
        }

        // Radial and axial extents of a mesh about the wheel's axle; angle of its centroid (0 = Rolling, 90 = up).
        private void RadialAxial(WheelInfo w, Renderer r, out float rMin, out float rMax, out float aMin, out float aMax, out float angle)
        {
            rMin = float.MaxValue; rMax = 0f; aMin = float.MaxValue; aMax = float.MinValue;
            Vector3 sum = Vector3.zero;
            Vector3[] verts = Vertices(r, out Matrix4x4 toCar);
            foreach (Vector3 v in verts)
            {
                Vector3 p = toCar.MultiplyPoint3x4(v) - w.Centre;
                float a = Vector3.Dot(p, w.Outward);
                Vector3 radial = p - w.Outward * a;
                float rad = radial.magnitude;
                rMin = Mathf.Min(rMin, rad); rMax = Mathf.Max(rMax, rad);
                aMin = Mathf.Min(aMin, a); aMax = Mathf.Max(aMax, a);
                sum += radial;
            }
            angle = Mathf.Atan2(Vector3.Dot(sum, Vector3.up), Vector3.Dot(sum, w.Rolling)) * Mathf.Rad2Deg;
            if (verts.Length == 0) { rMin = rMax = aMin = aMax = 0f; }
        }

        private void MeasureAxleBars(Transform axles)
        {
            if (axles == null) return;
            Vector3[] verts = Vertices(axles.GetComponent<Renderer>(), out Matrix4x4 toCar);
            bool f = false, r = false;
            float midZ = (FrontAxleZ + RearAxleZ) * 0.5f;
            foreach (Vector3 v in verts)
            {
                Vector3 p = toCar.MultiplyPoint3x4(v);
                if (p.z > midZ) { if (f) AxleBarFront.Encapsulate(p); else { AxleBarFront = new Bounds(p, Vector3.zero); f = true; } }
                else { if (r) AxleBarRear.Encapsulate(p); else { AxleBarRear = new Bounds(p, Vector3.zero); r = true; } }
            }
        }

        // Rim centre and normal of the steering wheel (principal axes of the rim vertices).
        private void MeasureSteeringWheel(Transform car)
        {
            var pts = new List<Vector3>();
            foreach (Renderer r in car.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.name.StartsWith("InteriorSteeringWheel0", StringComparison.Ordinal)) continue;
                Vector3[] verts = Vertices(r, out Matrix4x4 toCar);
                foreach (Vector3 v in verts) pts.Add(toCar.MultiplyPoint3x4(v));
            }
            if (pts.Count < 3)
            {
                SteeringWheelCentre = new Vector3(0f, 0.6f, FrontAxleZ - 0.6f);
                SteeringAxis = new Vector3(0f, -0.35f, 1f).normalized;
                SteeringWheelRadius = 0.17f;
                return;
            }
            Vector3 c = Vector3.zero;
            foreach (Vector3 p in pts) c += p;
            c /= pts.Count;
            // Covariance; the axis is the eigenvector with the smallest eigenvalue (power iteration on the inverse
            // is overkill here: deflate the two largest instead).
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (Vector3 p in pts)
            {
                Vector3 d = p - c;
                xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
            }
            Vector3 Mul(Vector3 v) => new Vector3(xx * v.x + xy * v.y + xz * v.z, xy * v.x + yy * v.y + yz * v.z, xz * v.x + yz * v.y + zz * v.z);
            Vector3 e1 = new Vector3(1f, 0.1f, 0.1f).normalized;
            for (int i = 0; i < 60; i++) e1 = Mul(e1).normalized;
            Vector3 e2 = Vector3.Cross(e1, Vector3.up).normalized;
            for (int i = 0; i < 60; i++) { e2 = Mul(e2); e2 = (e2 - Vector3.Dot(e2, e1) * e1).normalized; }
            Vector3 axis = Vector3.Cross(e1, e2).normalized;
            if (axis.z < 0f) axis = -axis; // from the wheel into the dash (forward)
            SteeringAxis = axis;
            SteeringWheelCentre = c;
            float rad = 0f;
            foreach (Vector3 p in pts) rad = Mathf.Max(rad, Vector3.ProjectOnPlane(p - c, axis).magnitude);
            SteeringWheelRadius = rad;
        }

        // |x| of the wheel-well inner wall: casts from inside the wheel toward the car's centre line.
        private float WellWall(InternalsProbe probe, WheelInfo left, WheelInfo right)
        {
            var hits = new List<float>();
            foreach (WheelInfo w in new[] { left, right })
                foreach (float y in new[] { 0.2f, 0.35f, 0.5f })
                {
                    Vector3 from = new Vector3(w.Centre.x - w.Side * (w.TyreWidth * 0.5f + 0.01f), y, w.Centre.z);
                    if (probe.Cast(from, new Vector3(-w.Side, 0f, 0f), Mathf.Abs(from.x), out float d, "InteriorFloor", "CarConcept"))
                        hits.Add(Mathf.Abs(from.x) - d);
                }
            return hits.Count > 0 ? hits.Max() : 0.6f;
        }

        private float ArchTop(InternalsProbe probe, WheelInfo left, WheelInfo right, float wallX)
        {
            var hits = new List<float>();
            foreach (WheelInfo w in new[] { left, right })
            {
                Vector3 from = new Vector3(w.Side * (wallX + 0.06f), w.Centre.y + 0.1f, w.Centre.z);
                if (probe.Cast(from, Vector3.up, 1.2f, out float d, "InteriorFloor", "CarConcept", "Body"))
                    hits.Add(from.y + d);
            }
            return hits.Count > 0 ? hits.Min() : WheelCentreY + 0.45f;
        }

        private void ReserveVolumes()
        {
            float midZ = (FrontAxleZ + RearAxleZ) * 0.5f;
            foreach (bool front in new[] { true, false })
            {
                WheelInfo l = Wheel(front, true), r = Wheel(front, false);
                float wall = front ? FrontWellWallX : RearWellWallX;
                float arch = front ? FrontArchTopY : RearArchTopY;
                foreach (WheelInfo w in new[] { l, r })
                {
                    string key = "suspension." + (front ? "front" : "rear") + "-" + (w.Left ? "left" : "right");
                    float inner = wall - 0.04f, outer = Mathf.Abs(w.Centre.x) + w.TyreWidth * 0.5f;
                    Reserved[key] = MinMax(new Vector3(w.Side * inner, 0.02f, w.Centre.z - 0.42f), new Vector3(w.Side * outer, arch, w.Centre.z + 0.42f));
                }
            }
            float fz = FrontAxleZ;
            Reserved["steering.rack"] = MinMax(new Vector3(-FrontWellWallX - 0.35f, FloorTopY + 0.05f, FirewallZ - 0.01f), new Vector3(FrontWellWallX + 0.35f, FloorTopY + 0.24f, FirewallZ + 0.11f));
            Reserved["steering.column"] = MinMax(new Vector3(-0.3f, FloorTopY + 0.05f, SteeringWheelCentre.z - 0.05f), new Vector3(0.35f, SteeringWheelCentre.y + 0.1f, FirewallZ + 0.12f));
            Reserved["suspension.front-subframe"] = MinMax(new Vector3(-FrontWellWallX - 0.06f, FloorTopY, fz - 0.3f), new Vector3(FrontWellWallX + 0.06f, FrontArchTopY, FirewallZ + 0.16f));
            // Proposals for the next builders (see the brief); they may refine them inside these limits with rays.
            float frontWellEnd = FrontAxleZ + 0.47f, rearWellEnd = RearAxleZ - 0.47f;
            float bayFloor = FloorTopY + 0.03f;
            Reserved["powertrain.transaxle"] = MinMax(new Vector3(V6Bounds.max.x + 0.01f, bayFloor, FirewallZ + 0.06f), new Vector3(Mathf.Min(Body.max.x - 0.25f, FrontWellWallX + 0.3f), V6Bounds.center.y + 0.12f, V6Bounds.max.z - 0.02f));
            Reserved["cooling.front-left"] = MinMax(new Vector3(-Body.max.x + 0.28f, bayFloor, frontWellEnd), new Vector3(V6Bounds.min.x - 0.02f, V6Bounds.max.y - 0.12f, FrontBumperZ - 0.14f));
            Reserved["cooling.nose"] = MinMax(new Vector3(-0.45f, bayFloor, V6Bounds.max.z + 0.005f), new Vector3(0.45f, V6Bounds.center.y, FrontBumperZ - 0.05f));
            Reserved["driveline.front-half-shafts"] = MinMax(new Vector3(-FrontWellWallX - 0.3f, AxleBarFront.min.y, AxleBarFront.min.z), new Vector3(FrontWellWallX + 0.3f, AxleBarFront.max.y, AxleBarFront.max.z));
            Reserved["driveline.propshaft"] = MinMax(new Vector3(-0.1f, FloorTopY, RearAxleZ + 0.3f), new Vector3(0.1f, FloorTopY + 0.14f, FirewallZ));
            Reserved["driveline.rear-axle"] = MinMax(new Vector3(-RearWellWallX, FloorTopY, RearAxleZ - 0.3f), new Vector3(RearWellWallX, WheelCentreY + 0.2f, RearAxleZ + 0.3f));
            Reserved["exhaust"] = MinMax(new Vector3(0.12f, FloorTopY, RearBumperZ + 0.05f), new Vector3(0.45f, FloorTopY + 0.16f, FirewallZ));
            Reserved["fuel.tank"] = MinMax(new Vector3(-0.55f, FloorTopY + 0.01f, RearAxleZ + 0.5f), new Vector3(-0.13f, FloorTopY + 0.2f, midZ - 0.2f));
            Reserved["brakes.booster"] = MinMax(new Vector3(-0.42f, FloorTopY + 0.1f, FirewallZ - 0.02f), new Vector3(-0.2f, FloorTopY + 0.32f, FirewallZ + 0.1f));
            // Over the rear axle, between the rear wells (there is no room behind them).
            Reserved["electrical.battery"] = MinMax(new Vector3(-0.45f, WheelCentreY + 0.22f, RearAxleZ - 0.35f), new Vector3(0f, WheelCentreY + 0.45f, RearAxleZ + 0.2f));
            Reserved["structure.sills"] = MinMax(new Vector3(-Body.max.x + 0.02f, FloorTopY - 0.04f, rearWellEnd + 0.9f), new Vector3(Body.max.x - 0.02f, FloorTopY + 0.18f, frontWellEnd - 0.9f));
            Reserved["structure.cross-car-beam"] = MinMax(new Vector3(-Body.max.x + 0.1f, Dash.center.y - 0.05f, Dash.center.z - 0.1f), new Vector3(Body.max.x - 0.1f, Dash.center.y + 0.12f, Dash.center.z + 0.1f));
        }

        private static Bounds MinMax(Vector3 a, Vector3 b)
        {
            var bounds = new Bounds();
            bounds.SetMinMax(Vector3.Min(a, b), Vector3.Max(a, b));
            return bounds;
        }

        public string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("car space origin (world) " + V(WorldOrigin) + ", yaw " + F(WorldRotation.eulerAngles.y) + " deg");
            sb.AppendLine("wheelbase " + F(Wheelbase) + ", track front " + F(TrackFront) + ", rear " + F(TrackRear) + ", axle z front " + F(FrontAxleZ) + " rear " + F(RearAxleZ) + ", wheel centre y " + F(WheelCentreY));
            foreach (WheelInfo w in Wheels)
                sb.AppendLine(w.Node + ": centre " + V(w.Centre) + " steer " + F(w.SteerDeg) + " deg, tyre r " + F(w.TyreRadius) + " w " + F(w.TyreWidth) +
                              ", rim r " + F(w.RimRadius) + " axial " + F(w.RimAxialMin) + ".." + F(w.RimAxialMax) +
                              ", disc r " + F(w.DiscInnerRadius) + ".." + F(w.DiscOuterRadius) + " axial " + F(w.DiscAxialMin) + ".." + F(w.DiscAxialMax) +
                              ", caliper at " + F(w.CaliperAngle) + " deg r " + F(w.CaliperRadiusMin) + ".." + F(w.CaliperRadiusMax) + " axial " + F(w.CaliperAxialMin) + ".." + F(w.CaliperAxialMax));
            sb.AppendLine("floor top y " + F(FloorTopY) + ", underbody y " + F(UnderbodyY) + ", wheel-well wall |x| front " + F(FrontWellWallX) + " rear " + F(RearWellWallX) +
                          ", arch top y front " + F(FrontArchTopY) + " rear " + F(RearArchTopY) + ", firewall z " + F(FirewallZ));
            sb.AppendLine("body " + B(Body) + ", bumpers z " + F(FrontBumperZ) + " / " + F(RearBumperZ));
            sb.AppendLine("seats " + B(Seats) + ", dash " + B(Dash) + ", pedals " + B(Pedals));
            sb.AppendLine("axle bars front " + B(AxleBarFront) + " rear " + B(AxleBarRear));
            sb.AppendLine("V6 " + B(V6Bounds) + " (" + V6Parts.Count + " renderers)");
            sb.AppendLine("steering wheel centre " + V(SteeringWheelCentre) + " axis " + V(SteeringAxis) + " radius " + F(SteeringWheelRadius));
            foreach (var kv in Reserved) sb.AppendLine("reserved " + kv.Key + ": " + B(kv.Value));
            return sb.ToString();
        }

        public static string F(float v) => v.ToString("F3", Inv);
        public static string V(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";
        public static string B(Bounds b) => V(b.min) + ".." + V(b.max);

        // ------------------------------------------------------------------------------------------------ helpers

        private Bounds BoundsOf(Transform root, Func<Transform, bool> filter)
        {
            bool any = false;
            var b = new Bounds();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (V6 != null && root != V6 && !root.IsChildOf(V6) && r.transform.IsChildOf(V6)) continue;
                if (!filter(r.transform)) continue;
                Vector3[] verts = Vertices(r, out Matrix4x4 toCar);
                foreach (Vector3 v in verts)
                {
                    Vector3 p = toCar.MultiplyPoint3x4(v);
                    if (any) b.Encapsulate(p); else { b = new Bounds(p, Vector3.zero); any = true; }
                }
            }
            return b;
        }

        private Vector3[] Vertices(Renderer r, out Matrix4x4 toCar)
        {
            toCar = WorldToCar * r.transform.localToWorldMatrix;
            Mesh m = r != null ? r.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (m == null) return Array.Empty<Vector3>();
            try { return m.vertices; } catch { return Array.Empty<Vector3>(); }
        }

        private static Bounds WorldVertexBounds(Renderer r)
        {
            Mesh m = r.GetComponent<MeshFilter>()?.sharedMesh;
            if (m == null) return r.bounds;
            Vector3[] verts;
            try { verts = m.vertices; } catch { return r.bounds; }
            if (verts.Length == 0) return r.bounds;
            Matrix4x4 l2w = r.transform.localToWorldMatrix;
            var b = new Bounds(l2w.MultiplyPoint3x4(verts[0]), Vector3.zero);
            foreach (Vector3 v in verts) b.Encapsulate(l2w.MultiplyPoint3x4(v));
            return b;
        }

        private Bounds VertexBounds(Renderer r)
        {
            Vector3[] verts = Vertices(r, out Matrix4x4 toCar);
            var b = new Bounds(toCar.MultiplyPoint3x4(verts.Length > 0 ? verts[0] : Vector3.zero), Vector3.zero);
            foreach (Vector3 v in verts) b.Encapsulate(toCar.MultiplyPoint3x4(v));
            return b;
        }

        private static Renderer TyreOf(Transform wheel)
        {
            foreach (Renderer r in wheel.GetComponentsInChildren<Renderer>(true))
                if (!r.name.Contains("Rim") && !r.name.Contains("Brake")) return r;
            return null;
        }

        private static string TopName(Transform car, Transform t)
        {
            if (t == car) return "CarConcept";
            while (t.parent != null && t.parent != car) t = t.parent;
            return t.name;
        }
    }
}
#endif
