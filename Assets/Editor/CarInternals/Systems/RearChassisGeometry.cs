#if UNITY_EDITOR
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CarInternals
{
    /// <summary>
    /// Hardpoints of one rear corner, in car space. The corner is a five-link: an upper A-arm (two constraints), a
    /// lateral lower spring link, a toe link ahead of the axle and a trailing link to the front of the well (one
    /// constraint each). Wheel-side points are measured in the wheel frame (WheelInfo.Point) so the measured, slightly
    /// asymmetric rear wheels of the glTF are followed exactly.
    /// </summary>
    public sealed class RearCorner
    {
        public WheelInfo W;
        /// <summary>-1 on the left, +1 on the right.</summary>
        public float S;
        public string Side, SideUz, Key;
        /// <summary>Hub flange face, axial along Outward from the wheel centre (just outboard of the disc, as at the front).</summary>
        public float HubFace;

        /// <summary>Upper A-arm: front and rear pivot (bushing centres, axis along z) and ball-joint centre.</summary>
        public Vector3 UcaFront, UcaRear, Ubj;
        /// <summary>Lower spring link: inner pivot (on the subframe) with its bushing axis, and ball-joint centre.</summary>
        public Vector3 LcaInner, LcaAxis, Lbj;
        /// <summary>Toe link: inner joint (bushing axis along z, eccentric cam) and outer ball-joint centre.</summary>
        public Vector3 ToeInner, ToeOuter;
        /// <summary>Trailing link: rear bushing in the carrier clevis and front bushing in the body bracket (axes along x).</summary>
        public Vector3 TrailRear, TrailFront;
        /// <summary>Coilover: damper lower eye in the clevis on the spring link, and top-mount centre under the arch roof.</summary>
        public Vector3 CoilBottom, CoilTop;
        /// <summary>Spring pan centre on the spring link (the spring's lower seat).</summary>
        public Vector3 Pan;
        /// <summary>
        /// Strut tower between the top mount and the sloping arch roof: a pressed cone whose flange (square to the
        /// coilover axis) carries the mount plate and whose upper edge meets the roof, measured with vertical rays.
        /// Outer / inner wall rings at the flange (r 0.066 / 0.063) and on the roof (r 0.090 / 0.087).
        /// </summary>
        public Vector3[] TowerOuterBottom, TowerOuterTop, TowerInnerBottom, TowerInnerTop;
        /// <summary>Anti-roll bar drop link: lower ball on the spring-link tab, upper ball in the bar lever eye.</summary>
        public Vector3 LinkLow, LinkHigh;
        /// <summary>|x| of the wheel-well wall behind the wall brackets, and z of the well's (slanted) front wall at the
        /// inboard and outboard end of the trailing-link bracket (x = TrailFront.x -/+ S * 0.04).</summary>
        public float WallUcaFront, WallUcaRear, WallToe, WellFrontIn, WellFrontOut;

        // ---------------------------------------------------------------- interfaces for the other builders
        /// <summary>Caliper mounting lugs on the carrier (bolt hole centres) and the bolt axis (along Outward).</summary>
        public Vector3 CaliperLugA, CaliperLugB, CaliperBoltAxis;
        /// <summary>Drive shaft: outer CV joint centre at the hub's inboard spline end, and the shaft axis (unit, pointing inboard).</summary>
        public Vector3 DriveShaftOuter, DriveShaftAxis;
        /// <summary>Brake-hose bracket hole on the carrier (hose clip), with the bracket's face normal.</summary>
        public Vector3 HoseBracket, HoseBracketNormal;
        /// <summary>Parking-brake shoe carrier (backing plate) centre and its axis (Outward); the cable slot points at SlotDir.</summary>
        public Vector3 ParkBrakeCentre, ParkBrakeSlotDir;

        /// <summary>Point on the spring link's centre line at fraction t from the inner pivot toward the ball joint.</summary>
        public Vector3 LcaAt(float t) => Vector3.Lerp(LcaInner, Lbj, t);
    }

    /// <summary>
    /// Rear chassis hardpoints derived from the measured layout, shared by the rear suspension and published for the
    /// brakes, drivetrain and structure builders. The rear subframe is a welded box-section cradle in the open space
    /// under the raised rear floor (behind the floor step and the axle line, ahead of the rear diffuser panel); it
    /// carries the spring links, the anti-roll bar and four rubber body mounts into the rear floor. The upper arms,
    /// toe links and trailing links pick up on brackets bolted to the wheel-well walls, which are measured with rays.
    /// </summary>
    public sealed class RearChassisGeometry
    {
        private static readonly ConditionalWeakTable<CarLayout, RearChassisGeometry> Cache = new ConditionalWeakTable<CarLayout, RearChassisGeometry>();
        public const float CoilRadius = 0.043f, CoilWire = 0.0115f;

        public RearCorner Left, Right;

        // ---------------------------------------------------------------- subframe (cradle)
        /// <summary>Cross members: centre z, centre y, section depth (z) and height (y); half length in x.</summary>
        public float FrontCrossZ, RearCrossZ, CrossY, CrossDepth = 0.06f, CrossHeight = 0.07f, CrossHalf;
        /// <summary>Side members: centre |x| and section width (x).</summary>
        public float SideX, SideWidth = 0.06f;
        /// <summary>Underside of the raised rear floor above the front and rear cross members (where the body mounts bolt up).</summary>
        public float FloorFrontY, FloorRearY;
        /// <summary>Body-mount bolt axes: four points on the floor underside (front left, front right, rear left, rear right).</summary>
        public Vector3[] BodyMounts = new Vector3[4];
        /// <summary>Differential mount pads on the cross members (front centre left / right, rear centre), top faces.</summary>
        public Vector3[] DiffMounts = new Vector3[3];

        // ---------------------------------------------------------------- anti-roll bar
        public float ArbY, ArbZ, ArbHalf, ArbMountX;

        /// <summary>
        /// Volume the rear subframe and anti-roll bar occupy under the raised rear floor (a new reserved volume,
        /// "suspension.rear-subframe"): the exhaust and the differential pass below / bolt to it.
        /// </summary>
        public Bounds SubframeVolume;

        public RearCorner Corner(bool left) => left ? Left : Right;

        public static RearChassisGeometry Of(CarLayout l, InternalsProbe probe)
        {
            if (Cache.TryGetValue(l, out RearChassisGeometry g)) return g;
            g = new RearChassisGeometry();
            g.Compute(l, probe);
            Cache.Add(l, g);
            return g;
        }

        private static readonly string[] Shell = { "InteriorFloor", "CarConcept" };

        private void Compute(CarLayout l, InternalsProbe probe)
        {
            float wall = l.RearWellWallX;
            float axleZ = l.RearAxleZ;
            // Cradle under the raised rear floor: the floor steps up behind the axle; cross members just behind the
            // step and just ahead of the diffuser panel, side members under the wells' open rear ends.
            FrontCrossZ = axleZ - 0.231f;
            RearCrossZ = axleZ - 0.445f;
            CrossY = Mathf.Max(l.FloorTopY + 0.07f, 0.2f);
            SideX = wall + 0.021f;
            CrossHalf = SideX + SideWidth * 0.5f;
            FloorFrontY = FloorAbove(probe, l, FrontCrossZ);
            FloorRearY = FloorAbove(probe, l, RearCrossZ);
            float mountX = Mathf.Min(0.4f, wall - 0.16f);
            BodyMounts[0] = new Vector3(-mountX, FloorFrontY, FrontCrossZ);
            BodyMounts[1] = new Vector3(mountX, FloorFrontY, FrontCrossZ);
            BodyMounts[2] = new Vector3(-mountX, FloorRearY, RearCrossZ);
            BodyMounts[3] = new Vector3(mountX, FloorRearY, RearCrossZ);
            float top = CrossY + CrossHeight * 0.5f;
            DiffMounts[0] = new Vector3(-0.12f, top, FrontCrossZ);
            DiffMounts[1] = new Vector3(0.12f, top, FrontCrossZ);
            DiffMounts[2] = new Vector3(0f, top, RearCrossZ);
            // Anti-roll bar between the cross members, tucked under the rear floor.
            ArbZ = (FrontCrossZ + RearCrossZ) * 0.5f;
            ArbY = Mathf.Min(FloorFrontY, FloorRearY) - 0.033f;
            ArbHalf = wall + 0.0f;
            ArbMountX = mountX + 0.02f;
            Left = MakeCorner(l, probe, l.RearLeft, wall);
            Right = MakeCorner(l, probe, l.RearRight, wall);
            // Cradle, body mounts (bolt heads under it, bolt ends in the floor) and the bar between the cross members;
            // the bar's levers and drop links continue outboard into the corner volumes.
            SubframeVolume = new Bounds();
            SubframeVolume.SetMinMax(new Vector3(-(CrossHalf + 0.075f), CrossY - CrossHeight * 0.5f - 0.012f, RearCrossZ - CrossDepth * 0.5f - 0.005f),
                new Vector3(CrossHalf + 0.075f, Mathf.Max(FloorFrontY, FloorRearY) + 0.025f, FrontCrossZ + CrossDepth * 0.5f + 0.005f));
        }

        // Underside of the rear floor above the cradle's centre line at z (rays up between the wells).
        private static float FloorAbove(InternalsProbe probe, CarLayout l, float z)
        {
            float best = float.MaxValue;
            foreach (float x in new[] { -0.45f, -0.3f, 0f, 0.3f, 0.45f })
                if (probe.Up(new Vector3(x, 0.15f, z), 0.4f, out float y)) best = Mathf.Min(best, y);
            return best < float.MaxValue ? best : 0.3f;
        }

        // |x| of the wheel-well wall at (y, z), cast inboard from inside the well.
        private static float WallAt(InternalsProbe probe, WheelInfo w, float y, float z, float fallback)
        {
            Vector3 from = new Vector3(w.Centre.x - w.Side * 0.15f, y, z);
            if (probe.Cast(from, new Vector3(-w.Side, 0f, 0f), 0.5f, out float d, Shell))
                return Mathf.Abs(from.x) - d;
            return fallback;
        }

        // z of the well's front wall at |x| = ax and height y, cast forward from the axle line.
        private static float FrontWall(InternalsProbe probe, WheelInfo w, float ax, float y)
        {
            if (probe.Cast(new Vector3(w.Side * ax, y, w.Centre.z), Vector3.forward, 0.7f, out float d, Shell))
                return w.Centre.z + d;
            return w.Centre.z + 0.42f;
        }

        // Underside of the wheel-arch roof above (x, z).
        private static float RoofAt(InternalsProbe probe, float x, float z, float fromY, float fallback)
        {
            if (probe.Cast(new Vector3(x, fromY, z), Vector3.up, 0.8f, out float d, "InteriorFloor", "CarConcept", "Body"))
                return fromY + d;
            return fallback;
        }

        private RearCorner MakeCorner(CarLayout l, InternalsProbe probe, WheelInfo w, float wall)
        {
            float s = w.Side;
            var c = new RearCorner
            {
                W = w,
                S = s,
                Side = w.Left ? "Left" : "Right",
                SideUz = w.Left ? "chap" : "o‘ng",
                Key = w.Left ? "rear-left" : "rear-right",
                HubFace = w.DiscAxialMax + 0.002f,
            };
            // Carrier joints in the wheel frame (axial, rolling, up), clear of the caliper (inboard of it) and the rim.
            c.Ubj = w.Point(-0.150f, 0.000f, 0.205f);
            c.Lbj = w.Point(-0.125f, -0.140f, -0.170f);
            c.ToeOuter = w.Point(-0.135f, 0.165f, -0.050f);
            c.TrailRear = w.Point(-0.135f, 0.120f, -0.140f);

            // Upper A-arm on two wall brackets, ahead of and behind the axle, just below the arch roof.
            float uy = c.Ubj.y - 0.015f;
            float zf = w.Centre.z + 0.13f, zr = w.Centre.z - 0.10f;
            c.WallUcaFront = WallAt(probe, w, uy, zf, wall);
            c.WallUcaRear = WallAt(probe, w, uy, zr, wall);
            c.UcaFront = new Vector3(s * (c.WallUcaFront + 0.036f), uy, zf);
            c.UcaRear = new Vector3(s * (c.WallUcaRear + 0.036f), uy, zr);

            // Spring link: inner pivot in the clevis at the end of the cradle's front cross member.
            c.LcaInner = new Vector3(s * (CrossHalf + 0.033f), CrossY, FrontCrossZ);
            Vector3 dir = c.Lbj - c.LcaInner;
            dir.y = 0f;
            dir.Normalize();
            c.LcaAxis = Vector3.Cross(Vector3.up, dir).normalized;
            if (c.LcaAxis.z < 0f) c.LcaAxis = -c.LcaAxis;
            // Coilover on the spring link, behind the drive shaft (Axles stub), clear of it by the spring's radius.
            float panX = wall + 0.166f;
            float t = Mathf.InverseLerp(Mathf.Abs(c.LcaInner.x), Mathf.Abs(c.Lbj.x), panX);
            Vector3 onLink = c.LcaAt(t);
            // Pan floor on top of the link; the damper eye sits in the clevis ears that rise from the pan floor.
            c.Pan = new Vector3(s * panX, onLink.y + 0.018f, onLink.z);
            c.CoilBottom = new Vector3(s * panX, onLink.y + 0.040f, onLink.z);
            // Leaning forward and outboard at the top, toward the high, flat crown of the arch roof (the roof falls away
            // steeply at the back of the well); the lean stays clear of the drive shaft. The top mount (radius 0.064) is
            // seated under the roof with rays round its rim, so no part of it enters the roof.
            float barTop = l.AxleBarRear.size != Vector3.zero ? l.AxleBarRear.max.y : l.WheelCentreY + 0.1f;
            float topX = panX + 0.045f, topZ = onLink.z + 0.10f;
            float y0 = RoofAt(probe, s * topX, topZ, barTop + 0.05f, l.RearArchTopY);
            Vector3 top = new Vector3(s * topX, y0, topZ);
            Vector3 axis = (top - c.CoilBottom).normalized;
            Vector3 e0 = Vector3.ProjectOnPlane(Vector3.forward, axis).normalized, e1 = Vector3.Cross(axis, e0);
            float seat = y0;
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                Vector3 o = (e0 * Mathf.Cos(a) + e1 * Mathf.Sin(a)) * 0.067f;
                Vector3 q = top + o;
                seat = Mathf.Min(seat, RoofAt(probe, q.x, q.z, barTop + 0.05f, y0 + 0.2f) - o.y);
            }
            c.CoilTop = new Vector3(s * topX, seat - 0.003f, topZ);
            // Strut tower rings: the flange sits on the dished mount plate's rim (0.0155 up the axis from the mount base),
            // the cone's upper edge on the roof.
            Vector3 ax2 = (c.CoilTop - c.CoilBottom).normalized;
            Vector3 mountBase = c.CoilBottom + ax2 * (Vector3.Distance(c.CoilTop, c.CoilBottom) - 0.03f);
            Vector3 f0 = Vector3.ProjectOnPlane(Vector3.forward, ax2).normalized, f1 = Vector3.Cross(ax2, f0);
            const int towerN = 24;
            c.TowerOuterBottom = new Vector3[towerN]; c.TowerOuterTop = new Vector3[towerN];
            c.TowerInnerBottom = new Vector3[towerN]; c.TowerInnerTop = new Vector3[towerN];
            for (int i = 0; i < towerN; i++)
            {
                float a = i * Mathf.PI * 2f / towerN;
                Vector3 d = f0 * Mathf.Cos(a) + f1 * Mathf.Sin(a);
                Vector3 ob = mountBase + ax2 * 0.0155f + d * 0.066f, ib = mountBase + ax2 * 0.0185f + d * 0.063f;
                Vector3 ot = mountBase + ax2 * 0.0185f + d * 0.090f, it = mountBase + ax2 * 0.0185f + d * 0.087f;
                float ro = RoofAt(probe, ot.x, ot.z, barTop + 0.05f, ot.y), ri = RoofAt(probe, it.x, it.z, barTop + 0.05f, it.y);
                c.TowerOuterBottom[i] = ob;
                c.TowerInnerBottom[i] = ib;
                c.TowerOuterTop[i] = new Vector3(ot.x, Mathf.Max(ob.y + 0.004f, ro - 0.001f), ot.z);
                c.TowerInnerTop[i] = new Vector3(it.x, Mathf.Max(ib.y + 0.004f, ri - 0.001f), it.z);
            }

            // Toe link ahead of the axle: inner joint on a wall bracket with the eccentric adjuster.
            float ty = c.ToeOuter.y - 0.01f, tz = c.ToeOuter.z + 0.01f;
            c.WallToe = WallAt(probe, w, ty, tz, wall);
            c.ToeInner = new Vector3(s * (c.WallToe + 0.04f), ty, tz);

            // Trailing link forward to a bracket on the well's front wall, which is slanted in plan: measured at both
            // ends of the bracket, the pivot held 4 cm clear of its nearer end.
            float fx = Mathf.Abs(c.TrailRear.x) - 0.07f, fy = c.TrailRear.y - 0.01f;
            c.WellFrontIn = FrontWall(probe, w, fx - 0.04f, fy);
            c.WellFrontOut = FrontWall(probe, w, fx + 0.04f, fy);
            c.TrailFront = new Vector3(s * fx, fy, Mathf.Min(c.WellFrontIn, c.WellFrontOut) - 0.04f);

            // Drop link on a tab behind the spring link, near its outer end.
            float lx = Mathf.Abs(c.Lbj.x) - 0.045f;
            float tl = Mathf.InverseLerp(Mathf.Abs(c.LcaInner.x), Mathf.Abs(c.Lbj.x), lx);
            Vector3 onTab = c.LcaAt(tl);
            c.LinkLow = new Vector3(s * lx, onTab.y + 0.02f, onTab.z - 0.022f);
            c.LinkHigh = new Vector3(s * lx, ArbY + 0.016f, onTab.z - 0.022f);

            // Interfaces.
            float lugAxial = w.CaliperAxialMin - 0.008f;
            float lugR = w.CaliperRadiusMin + 0.006f;
            Vector3 Radial(float deg) => w.Rolling * Mathf.Cos(deg * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(deg * Mathf.Deg2Rad);
            c.CaliperLugA = w.Centre + w.Outward * lugAxial + Radial(w.CaliperAngle - 26f) * lugR;
            c.CaliperLugB = w.Centre + w.Outward * lugAxial + Radial(w.CaliperAngle + 26f) * lugR;
            c.CaliperBoltAxis = w.Outward;
            c.DriveShaftAxis = -w.Outward;
            c.DriveShaftOuter = w.Centre + w.Outward * (c.HubFace - 0.075f);
            // On the rear face of the carrier's upper arm (same line as the casting's arm).
            Vector3 upStart = w.Centre + w.Outward * (c.HubFace - 0.088f + 0.02f) + Vector3.up * 0.055f;
            Vector3 upEye = c.Ubj - Vector3.up * 0.04f;
            c.HoseBracket = Vector3.Lerp(upStart, upEye, 0.6f) - w.Rolling * 0.026f + Vector3.up * 0.012f;
            c.HoseBracketNormal = -w.Rolling;
            c.ParkBrakeCentre = w.Centre + w.Outward * (c.HubFace - 0.029f);
            c.ParkBrakeSlotDir = Radial(w.CaliperAngle);
            return c;
        }
    }
}
#endif
