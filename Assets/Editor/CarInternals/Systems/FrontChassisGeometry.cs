#if UNITY_EDITOR
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CarInternals
{
    /// <summary>Hardpoints of one front corner, in car space.</summary>
    public sealed class FrontCorner
    {
        public WheelInfo W;
        public float S;
        public string Side, SideUz, Key;
        /// <summary>Lower arm: front and rear pivot (bushing centres, axis along z) and ball-joint centre.</summary>
        public Vector3 LcaFront, LcaRear, Lbj;
        /// <summary>Upper arm: front and rear pivot and ball-joint centre.</summary>
        public Vector3 UcaFront, UcaRear, Ubj;
        /// <summary>|x| of the wheel-well wall behind each pivot (where its bracket sits).</summary>
        public float WallLcaFront, WallLcaRear, WallUcaFront, WallUcaRear;
        /// <summary>Coilover: lower eye on the lower arm, top-mount centre under the wheel-arch roof.</summary>
        public Vector3 CoilBottom, CoilTop;
        /// <summary>Outer tie-rod end (ball centre) on the knuckle's steering arm.</summary>
        public Vector3 Tre;
        /// <summary>Drop link: lower ball on the lower arm, upper ball on the anti-roll bar lever.</summary>
        public Vector3 LinkLow, LinkHigh;
        /// <summary>Hub flange face (axial along the steered axle, from the wheel centre).</summary>
        public float HubFace;
        /// <summary>Point on the arm's leg at fraction t from the ball joint toward the pivot.</summary>
        public Vector3 LcaLeg(bool front, float t) => Vector3.Lerp(Lbj, front ? LcaFront : LcaRear, t);
    }

    /// <summary>
    /// Front chassis hardpoints derived from the measured layout, shared by the suspension and steering builders.
    /// Double-wishbone corners with the pivots on the wheel-well walls (each pivot is placed against the wall measured
    /// behind it), the coilover on the lower arm behind the drive shaft, a front-steer rack in the slot between the
    /// bulkhead and the engine, and the anti-roll bar just above it. The front wheels stand steered in the scene, so
    /// knuckle-side points follow the steered wheel frame.
    /// </summary>
    public sealed class FrontChassisGeometry
    {
        private static readonly ConditionalWeakTable<CarLayout, FrontChassisGeometry> Cache = new ConditionalWeakTable<CarLayout, FrontChassisGeometry>();
        public const float CoilRadius = 0.046f, CoilWire = 0.012f;

        public FrontCorner Left, Right;
        public float RackY, RackZ, RackJointHalf, RackTravel, PinionX;
        public float ArbY, ArbZ, ArbHalf, TieBarY, TieBarZ;
        /// <summary>Front face of the cabin (bulkhead) behind the rack, measured across the rack's width.</summary>
        public float BulkheadZ;
        public float Wall, ArchTop;

        public FrontCorner Corner(bool left) => left ? Left : Right;

        public static FrontChassisGeometry Of(CarLayout l, InternalsProbe probe)
        {
            if (Cache.TryGetValue(l, out FrontChassisGeometry g)) return g;
            g = new FrontChassisGeometry();
            g.Compute(l, probe);
            Cache.Add(l, g);
            return g;
        }

        private void Compute(CarLayout l, InternalsProbe probe)
        {
            Wall = l.FrontWellWallX;
            ArchTop = l.FrontArchTopY;
            float barTop = l.AxleBarFront.size != Vector3.zero ? l.AxleBarFront.max.y : l.WheelCentreY + 0.1f;
            SteeringSlot(l, probe);
            RackJointHalf = Mathf.Min(0.36f, Wall - 0.2f);
            PinionX = Mathf.Clamp(l.Pedals.max.x + 0.11f, 0.1f, RackJointHalf - 0.08f);
            ArbHalf = Wall - 0.17f;
            Left = MakeCorner(l, probe, l.FrontLeft, barTop);
            Right = MakeCorner(l, probe, l.FrontRight, barTop);
            // Rack travel so that both tie rods have the same length in the steered pose.
            float lo = -0.2f, hi = 0.2f;
            for (int i = 0; i < 50; i++)
            {
                float mid = (lo + hi) * 0.5f;
                // The left rod grows as the rack moves right: shorten it by moving left, and the other way round.
                if (TieRodDifference(mid) > 0f) hi = mid; else lo = mid;
            }
            RackTravel = (lo + hi) * 0.5f;
        }

        // Rack, anti-roll bar and tie bar in the slot between the cabin's front face and the engine, measured across
        // the rack's width at their heights.
        private void SteeringSlot(CarLayout l, InternalsProbe probe)
        {
            RackY = l.FloorTopY + 0.125f;
            ArbY = RackY + 0.07f;
            float back = SlotBack(l, probe, RackY, 0.32f, 0.046f);
            BulkheadZ = back;
            float front = SlotFront(probe, RackY, 0.32f, 0.046f, back);
            // Room for the motor (radius 0.046) and its clearance; prefer the middle of a wide slot.
            RackZ = front - back > 0.13f ? (back + front) * 0.5f : back + 0.055f;
            float arbBack = SlotBack(l, probe, ArbY, Wall - 0.17f, 0.014f);
            float arbFront = SlotFront(probe, ArbY, Wall - 0.17f, 0.014f, arbBack);
            ArbZ = arbFront - arbBack > 0.035f ? (arbBack + arbFront) * 0.5f : arbBack + 0.016f;
            TieBarZ = RackZ;
            // Tie bar just above the floor under the rack (the bay floor stands higher than the cabin floor).
            float floor = l.FloorTopY;
            foreach (float x in new[] { -0.4f, 0f, 0.4f })
                if (probe.Down(new Vector3(x, RackY, RackZ), 0.3f, out float fy)) floor = Mathf.Max(floor, fy);
            TieBarY = floor + 0.022f;
        }

        private static readonly string[] Cabin = { "InteriorDash", "InteriorFloor", "CarConcept", "InteriorPedal", "InteriorMid" };

        // Front-most cabin surface over |x| <= half at heights y +- r.
        private static float SlotBack(CarLayout l, InternalsProbe probe, float y, float half, float r)
        {
            float best = l.FirewallZ;
            for (float x = -half; x <= half + 1e-4f; x += 0.04f)
                foreach (float dy in new[] { -r, 0f, r })
                    if (probe.Cast(new Vector3(x, y + dy, l.FrontAxleZ - 0.25f), Vector3.forward, 0.8f, out float d, Cabin))
                        best = Mathf.Max(best, l.FrontAxleZ - 0.25f + d);
            return best;
        }

        // Rear-most engine surface over |x| <= half at heights y +- r, looking forward from the cabin face.
        private static float SlotFront(InternalsProbe probe, float y, float half, float r, float from)
        {
            float best = from + 1f;
            for (float x = -half; x <= half + 1e-4f; x += 0.04f)
                foreach (float dy in new[] { -r, 0f, r })
                    if (probe.Cast(new Vector3(x, y + dy, from - 0.02f), Vector3.forward, 1f, out float d, "V6/"))
                        best = Mathf.Min(best, from - 0.02f + d);
            return best;
        }

        public Vector3 RackJoint(bool left) => new Vector3(RackTravel + (left ? -RackJointHalf : RackJointHalf), RackY, RackZ);

        private float TieRodDifference(float travel)
        {
            float a = Vector3.Distance(Left.Tre, new Vector3(travel - RackJointHalf, RackY, RackZ));
            float b = Vector3.Distance(Right.Tre, new Vector3(travel + RackJointHalf, RackY, RackZ));
            return a - b;
        }

        // |x| of the wheel-well wall at (y, z), cast inboard from inside the well.
        private float WallAt(InternalsProbe probe, WheelInfo w, float y, float z)
        {
            Vector3 from = new Vector3(w.Centre.x - w.Side * 0.12f, y, z);
            if (probe.Cast(from, new Vector3(-w.Side, 0f, 0f), 0.5f, out float d, "InteriorFloor", "CarConcept"))
                return Mathf.Abs(from.x) - d;
            return Wall;
        }

        // Underside of the wheel-arch roof above (x, z).
        private float RoofAt(InternalsProbe probe, float x, float z, float fromY)
        {
            if (probe.Cast(new Vector3(x, fromY, z), Vector3.up, 0.8f, out float d, "InteriorFloor", "CarConcept", "Body"))
                return fromY + d;
            return ArchTop;
        }

        private FrontCorner MakeCorner(CarLayout l, InternalsProbe probe, WheelInfo w, float barTop)
        {
            float s = w.Side;
            float cx = Mathf.Abs(w.Centre.x), y = w.Centre.y, z = w.Centre.z;
            var c = new FrontCorner
            {
                W = w,
                S = s,
                Side = w.Left ? "Left" : "Right",
                SideUz = w.Left ? "chap" : "o‘ng",
                Key = w.Left ? "front-left" : "front-right",
            };
            c.Lbj = new Vector3(s * (cx - 0.12f), y - 0.2f, z);
            c.Ubj = new Vector3(s * (cx - 0.17f), y + 0.22f, z);
            float lowY = y - 0.205f, upY = y + 0.2f;
            c.WallLcaFront = WallAt(probe, w, lowY, z + 0.17f);
            c.WallLcaRear = WallAt(probe, w, lowY, z - 0.17f);
            c.WallUcaFront = WallAt(probe, w, upY, z + 0.15f);
            c.WallUcaRear = WallAt(probe, w, upY, z - 0.03f);
            c.LcaFront = new Vector3(s * (c.WallLcaFront + 0.04f), lowY, z + 0.17f);
            c.LcaRear = new Vector3(s * (c.WallLcaRear + 0.04f), lowY, z - 0.17f);
            c.UcaFront = new Vector3(s * (c.WallUcaFront + 0.034f), upY, z + 0.15f);
            c.UcaRear = new Vector3(s * (c.WallUcaRear + 0.034f), upY, z - 0.03f);
            // Coilover on the rear leg of the lower arm, behind the drive shaft, nearly upright, under the arch roof.
            float coilX = WallAt(probe, w, y, z - 0.134f) + 0.075f;
            float t = Mathf.InverseLerp(Mathf.Abs(c.Lbj.x), Mathf.Abs(c.LcaRear.x), coilX);
            Vector3 onLeg = c.LcaLeg(false, t);
            c.CoilBottom = new Vector3(s * coilX, onLeg.y + 0.034f, onLeg.z);
            float topX = coilX;
            c.CoilTop = new Vector3(s * topX, RoofAt(probe, s * topX, onLeg.z, barTop + 0.05f) - 0.012f, onLeg.z);
            // Steering arm ahead of the axle in the steered wheel frame, inboard of the disc.
            c.Tre = w.Point(-0.10f, 0.15f, RackY - y);
            c.HubFace = w.DiscAxialMax + 0.002f;
            // Drop link on the front leg of the lower arm, up to the anti-roll bar lever.
            float linkX = Mathf.Max(c.WallLcaFront, Wall) + 0.055f;
            float tl = Mathf.InverseLerp(Mathf.Abs(c.Lbj.x), Mathf.Abs(c.LcaFront.x), linkX);
            Vector3 onFront = c.LcaLeg(true, tl);
            c.LinkLow = new Vector3(s * linkX, onFront.y + 0.032f, onFront.z);
            c.LinkHigh = new Vector3(s * linkX, ArbY, onFront.z);
            return c;
        }
    }
}
#endif
