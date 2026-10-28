#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace CarInternals
{
    /// <summary>
    /// Rear suspension: two five-link corners (upper A-arm, lower spring link with spring pan and damper clevis, toe
    /// link with eccentric adjuster, trailing link, cast hub carrier with hub, bearing and parking-brake shoe carrier,
    /// coilover with top mount), the rear anti-roll bar with drop links, and the rear subframe: a welded box-section
    /// cradle under the raised rear floor on four rubber body mounts, plus the pickup brackets on the wheel-well walls.
    /// Every point comes from RearChassisGeometry (measured layout and rays); the interfaces for the brakes, drivetrain
    /// and structure builders are published there.
    /// </summary>
    [CarInternalsSubsystem("Suspension", 30)]
    public sealed class RearSuspension : ICarInternalsSubsystem
    {
        private InternalsContext ctx;
        private CarPalette m;
        private RearChassisGeometry g;

        public void Build(InternalsContext context)
        {
            ctx = context;
            m = ctx.Mats;
            g = RearChassisGeometry.Of(ctx.Layout, ctx.Probe);
            Transform system = ctx.System("Suspension");
            foreach (RearCorner c in new[] { g.Left, g.Right })
            {
                ctx.Note("rear " + c.Side.ToLowerInvariant() + ": UBJ " + CarLayout.V(c.Ubj) + " UCA pivots " + CarLayout.V(c.UcaFront) + " " + CarLayout.V(c.UcaRear) +
                         " LBJ " + CarLayout.V(c.Lbj) + " spring link pivot " + CarLayout.V(c.LcaInner) + " toe " + CarLayout.V(c.ToeInner) + ".." + CarLayout.V(c.ToeOuter) +
                         " trailing " + CarLayout.V(c.TrailFront) + ".." + CarLayout.V(c.TrailRear) + " coil " + CarLayout.V(c.CoilBottom) + ".." + CarLayout.V(c.CoilTop) +
                         " hub face " + CarLayout.F(c.HubFace));
                ctx.Note("rear " + c.Side.ToLowerInvariant() + " interfaces: caliper lugs " + CarLayout.V(c.CaliperLugA) + " " + CarLayout.V(c.CaliperLugB) + " axis " + CarLayout.V(c.CaliperBoltAxis) +
                         ", drive shaft outer joint " + CarLayout.V(c.DriveShaftOuter) + " axis " + CarLayout.V(c.DriveShaftAxis) + ", hose bracket " + CarLayout.V(c.HoseBracket) +
                         ", parking-brake shoe carrier " + CarLayout.V(c.ParkBrakeCentre));
            }
            ctx.Note("rear subframe: cross members z " + CarLayout.F(g.FrontCrossZ) + " / " + CarLayout.F(g.RearCrossZ) + " y " + CarLayout.F(g.CrossY) + " half " + CarLayout.F(g.CrossHalf) +
                     ", side members |x| " + CarLayout.F(g.SideX) + ", floor above y " + CarLayout.F(g.FloorFrontY) + " / " + CarLayout.F(g.FloorRearY) +
                     ", body mounts " + CarLayout.V(g.BodyMounts[0]) + " " + CarLayout.V(g.BodyMounts[1]) + " " + CarLayout.V(g.BodyMounts[2]) + " " + CarLayout.V(g.BodyMounts[3]) +
                     ", diff pads " + CarLayout.V(g.DiffMounts[0]) + " " + CarLayout.V(g.DiffMounts[1]) + " " + CarLayout.V(g.DiffMounts[2]) +
                     ", anti-roll bar y " + CarLayout.F(g.ArbY) + " z " + CarLayout.F(g.ArbZ) + ", volume suspension.rear-subframe " + CarLayout.B(g.SubframeVolume));
            Corner(system, g.Left);
            Corner(system, g.Right);
            AntiRollBar(system);
            Subframe(system);

            // Joints by design: the drive-shaft stand-in (CarConcept/Axles) through the carrier and hub, the caliper on
            // its lugs, the wheel studs through the wheel, brackets and mounts on the floor tub, the top mounts under
            // the arch roof, bushings in the subframe's clevises and the drop links on the spring-link tabs.
            foreach (RearCorner c in new[] { g.Left, g.Right })
            {
                string id = "suspension." + c.Key;
                string wheel = c.W.Node;
                ctx.AllowContact(id + ".hub-carrier", "Axles");
                ctx.AllowContact(id + ".hub-carrier.upright", wheel + "/" + wheel + "BrakePad");
                ctx.AllowContact(id + ".hub-carrier.hub", wheel + "/" + wheel + "Rim");
                ctx.AllowContact(id + ".hub-carrier.hub", wheel + "/" + wheel + "BrakeDisc");
                ctx.AllowContact(id + ".coilover.top-mount", "InteriorFloor");
                ctx.AllowContact(id + ".coilover.top-mount", "CarConcept");
            }
            ctx.AllowContact("suspension.rear-subframe", "InteriorFloor");
            ctx.AllowContact("suspension.rear-subframe", "CarConcept");
            ctx.AllowContact("suspension.rear-subframe", "suspension.rear-left");
            ctx.AllowContact("suspension.rear-subframe", "suspension.rear-right");
            ctx.AllowContact("suspension.rear-anti-roll-bar.mounts", "InteriorFloor");
            ctx.AllowContact("suspension.rear-anti-roll-bar.mounts", "CarConcept");
            ctx.AllowContact("suspension.rear-anti-roll-bar.link", "suspension.rear-");
        }

        // ------------------------------------------------------------------------------------------------ corner

        private void Corner(Transform system, RearCorner c)
        {
            string id = "suspension." + c.Key;
            string side = c.Side.ToLowerInvariant();
            Transform corner = ctx.Assembly(system, id, new Txt("Rear " + c.Side + " Suspension Corner", "Orqa " + c.SideUz + " osma bloki",
                    "Five-link corner: upper A-arm, spring link, toe link and trailing link locate the rear " + side + " hub carrier; the coilover springs and damps it.",
                    "Besh richagli osma: yuqori A-richag, prujina richagi, yaqinlashuv tortqisi va bo‘ylama richag orqa " + c.SideUz + " gupchak tayanchini ushlaydi; prujinali amortizator uni ressorlaydi."),
                "Steel, cast iron and aluminium", new Vector3(c.S, 0f, 0f), 1.2f);
            UpperArm(corner, c, id);
            LowerArm(corner, c, id);
            ToeLink(corner, c, id);
            TrailingLink(corner, c, id);
            HubCarrier(corner, c, id);
            Coilover(corner, c, id);
        }

        private void UpperArm(Transform corner, RearCorner c, string id)
        {
            Transform arm = ctx.SubAssembly(corner, id + ".upper-arm", new Txt("Upper Control Arm", "Yuqori richag",
                    "Forged A-arm between two wall brackets and the top of the hub carrier: sets camber and takes the brake reaction at the top.",
                    "Ikki devor kronshteyni va gupchak tayanchi tepasi orasidagi bolg‘alangan A-richag: razvalni belgilaydi va tormoz reaksiyasini tepada qabul qiladi."),
                "Forged aluminium", new Vector3(0f, 1f, 0.45f), 0.9f);
            Vector3 o = c.Ubj;
            Mesh mesh = ctx.Mesh("rs-upper-arm-" + c.Key, k =>
            {
                k.Push(Matrix4x4.Translate(-o));
                foreach (bool front in new[] { true, false })
                {
                    Vector3 eye = front ? c.UcaFront : c.UcaRear;
                    Vector3 start = eye + (c.Ubj - eye).normalized * 0.028f;
                    Vector3 end = c.Ubj + (eye - c.Ubj).normalized * 0.024f;
                    Vector3 mid = Vector3.Lerp(start, end, 0.5f) + Vector3.up * 0.008f;
                    k.Tube(MeshKit.CatmullRom(new[] { start, mid, end }, 5), 0.0135f, 10, true, true, Vector3.up, x => Mathf.Lerp(1.05f, 0.9f, x), new Vector2(1.2f, 0.8f));
                    k.Push(eye, InternalsContext.AlongY(Vector3.forward));
                    k.Sleeve(0.03f, 0.026f, -0.023f, 0.023f, 16);
                    k.Pop();
                }
                k.PlanarPrism(new[] { c.Ubj, Vector3.Lerp(c.Ubj, c.UcaFront, 0.4f), Vector3.Lerp(c.Ubj, c.UcaRear, 0.4f) }, 0.006f);
                k.Push(c.Ubj, Quaternion.identity);
                k.Lathe(new[] { new Vector2(0.021f, -0.017f), new Vector2(0.03f, -0.017f), new Vector2(0.031f, 0.015f), new Vector2(0.021f, 0.015f), new Vector2(0.021f, -0.017f) }, 18);
                k.Pop();
                k.Pop();
            });
            ctx.Part(arm, id + ".upper-arm.arm", new Txt("Upper arm forging", "Yuqori richag (bolg‘alangan)",
                    "Forged aluminium wishbone with two pressed bushing eyes and the ball-joint boss.",
                    "Ikki presslangan saylentblok ko‘zi va sharli tayanch uyasi bo‘lgan bolg‘alangan alyuminiy richag."),
                "Forged aluminium 6082-T6", mesh, new[] { m.CastAluminium }, o, Quaternion.identity);
            Bushing(arm, id + ".upper-arm.front-bushing", "Front Pivot Bushing", "Old saylentblok", c.UcaFront, Vector3.forward, 0.026f, 0.046f, new Vector3(0f, 0f, 1f));
            Bushing(arm, id + ".upper-arm.rear-bushing", "Rear Pivot Bushing", "Orqa saylentblok", c.UcaRear, Vector3.forward, 0.026f, 0.046f, new Vector3(0f, 0f, -1f));
            BallJoint(arm, id + ".upper-arm.ball-joint", "Upper Ball Joint", "Yuqori sharli tayanch", c.Ubj, Vector3.down, 0.02f, 0.066f);
        }

        private void LowerArm(Transform corner, RearCorner c, string id)
        {
            Transform arm = ctx.SubAssembly(corner, id + ".lower-arm", new Txt("Lower Control Arm", "Pastki richag",
                    "Lateral spring link from the subframe to the bottom of the hub carrier: carries the coilover on its pan and most of the corner's vertical load.",
                    "Yordamchi ramadan gupchak tayanchi pastigacha bo‘lgan ko‘ndalang prujina richagi: tovasida prujinali amortizatorni va burchakning asosiy vertikal yukini ko‘taradi."),
                "Pressed steel", Vector3.down, 0.9f);
            Vector3 o = c.Lbj;
            Vector3 along = (c.Lbj - c.LcaInner);
            along.y = 0f;
            along.Normalize();
            Mesh mesh = ctx.Mesh("rs-spring-link-" + c.Key, k =>
            {
                k.Push(Matrix4x4.Translate(-o));
                // Flat pressed box beam from the inner eye to the ball-joint boss.
                Vector3 start = c.LcaInner + along * 0.036f;
                Vector3 end = c.Lbj - along * 0.026f;
                k.Tube(new[] { start, Vector3.Lerp(start, end, 0.5f), end }, 0.017f, 14, true, true, Vector3.up, null, new Vector2(0.75f, 1.6f));
                k.Push(c.LcaInner, InternalsContext.AlongY(c.LcaAxis));
                k.Sleeve(0.034f, 0.0295f, -0.026f, 0.026f, 14);
                k.Pop();
                k.Push(c.Lbj, Quaternion.identity);
                k.Lathe(new[] { new Vector2(0.024f, -0.02f), new Vector2(0.033f, -0.02f), new Vector2(0.035f, -0.013f), new Vector2(0.035f, 0.014f), new Vector2(0.032f, 0.018f), new Vector2(0.024f, 0.018f), new Vector2(0.024f, -0.02f) }, 14);
                k.Pop();
                // Drop-link tab behind the link near its outer end (stud along x).
                Vector3 ll = c.LinkLow - new Vector3(c.S * 0.016f, 0f, 0f);
                k.PlanarPrism(new[]
                {
                    new Vector3(ll.x, ll.y - 0.026f, ll.z + 0.024f), new Vector3(ll.x, ll.y - 0.026f, ll.z - 0.012f),
                    new Vector3(ll.x, ll.y + 0.012f, ll.z - 0.012f), new Vector3(ll.x, ll.y + 0.012f, ll.z + 0.012f)
                }, 0.005f);
                k.Pop();
            });
            ctx.Part(arm, id + ".lower-arm.arm", new Txt("Spring link", "Prujina richagi",
                    "Pressed-steel box link, flat and wide in plan; the inner eye takes a bushing, the outer boss a ball joint, the welded pan the spring.",
                    "Rejada yassi va keng presslangan po‘lat qutisimon richag; ichki ko‘zida saylentblok, tashqi uyasida sharli tayanch, payvandlangan tovasida prujina."),
                "Pressed steel S420MC, e-coated", mesh, new[] { m.BlackSteel }, o, Quaternion.identity);
            Mesh pan = ctx.Mesh("rs-spring-pan", k =>
            {
                k.Lathe(new[] { new Vector2(0.028f, -0.003f), new Vector2(0.05f, -0.003f), new Vector2(0.06f, 0.006f), new Vector2(0.064f, 0.017f), new Vector2(0.06f, 0.019f),
                    new Vector2(0.056f, 0.008f), new Vector2(0.049f, 0.001f), new Vector2(0.028f, 0.001f), new Vector2(0.028f, -0.003f) }, 18, 35f);
            });
            ctx.Part(arm, id + ".lower-arm.spring-pan", new Txt("Spring pan", "Prujina tovasi",
                    "Pressed cup welded to the link: the coil spring's lower seat, with a drain hole and the damper passing through.",
                    "Richagga payvandlangan presslangan kosacha: spiral prujinaning pastki tayanchi, suv oqish teshigi va undan amortizator o‘tadi."),
                "Pressed steel, e-coated", pan, new[] { m.BlackSteel }, c.Pan, Quaternion.identity, false, Vector3.up, 0.5f);
            // Damper clevis: two ears either side of the damper eye, bolt along the link's pivot axis.
            Mesh clevis = ctx.Mesh("rs-damper-clevis", k =>
            {
                foreach (float sgn in new[] { -1f, 1f })
                {
                    k.PlanarPrism(new[]
                    {
                        new Vector3(-0.024f, -0.022f, sgn * 0.0235f), new Vector3(0.024f, -0.022f, sgn * 0.0235f), new Vector3(0.017f, 0.006f, sgn * 0.0235f),
                        new Vector3(0.0f, 0.016f, sgn * 0.0235f), new Vector3(-0.017f, 0.006f, sgn * 0.0235f)
                    }, 0.005f);
                }
                k.Box(new Vector3(0f, -0.0235f, 0f), new Vector3(0.05f, 0.004f, 0.052f));
            });
            Quaternion clevisRot = Quaternion.LookRotation(c.LcaAxis, Vector3.up);
            ctx.Part(arm, id + ".lower-arm.damper-clevis", new Txt("Damper clevis", "Amortizator vilkasi",
                    "Forged fork inside the spring pan that holds the damper's lower eye bolt.",
                    "Prujina tovasi ichidagi, amortizatorning pastki ko‘z boltini ushlovchi bolg‘alangan vilka."),
                "Forged steel", clevis, new[] { m.ForgedSteel }, c.CoilBottom, clevisRot, false, Vector3.up, 0.4f);
            Bushing(arm, id + ".lower-arm.inner-bushing", "Inner Pivot Bushing", "Ichki saylentblok", c.LcaInner, c.LcaAxis, 0.0295f, 0.052f, -along);
            BallJoint(arm, id + ".lower-arm.ball-joint", "Lower Ball Joint", "Pastki sharli tayanch", c.Lbj, Vector3.up, 0.023f, 0.07f);
        }

        private void ToeLink(Transform corner, RearCorner c, string id)
        {
            Transform toe = ctx.SubAssembly(corner, id + ".toe-link", new Txt("Toe Link", "Yaqinlashuv tortqisi",
                    "Adjustable tie rod ahead of the axle that holds the carrier's steer angle; the eccentric cam at the inner joint sets the toe.",
                    "O‘q oldidagi rostlanadigan tortqi gupchak tayanchining burilish burchagini ushlab turadi; ichki bo‘g‘indagi eksentrik yaqinlashuvni rostlaydi."),
                "Forged steel", new Vector3(-c.S * 0.6f, 0f, 0.8f), 0.9f);
            Vector3 a = c.ToeInner, b = c.ToeOuter;
            Vector3 dir = (b - a).normalized;
            Quaternion along = InternalsContext.AlongY(dir, Vector3.forward);
            float len = Vector3.Distance(a, b);
            // Rod: inner eye (bushing axis along z), rod body, female thread for the outer joint's shank.
            Mesh rod = ctx.Mesh("rs-toe-rod-" + Mathf.RoundToInt(len * 1000f), k =>
            {
                k.Push(Vector3.zero, InternalsContext.AlongY(Vector3.forward));
                k.Sleeve(0.03f, 0.025f, -0.022f, 0.022f, 16);
                k.Pop();
                k.Cylinder(0.0095f, 0.026f, len - 0.072f, 12);
                k.Lathe(new[] { new Vector2(0f, len - 0.074f), new Vector2(0.012f, len - 0.074f), new Vector2(0.012f, len - 0.05f), new Vector2(0.0105f, len - 0.047f), new Vector2(0f, len - 0.047f) }, 12);
            });
            ctx.Part(toe, id + ".toe-link.rod", new Txt("Toe rod", "Tortqi sterjeni",
                    "Forged rod with the inner bushing eye and a threaded socket for the outer joint.",
                    "Ichki saylentblok ko‘zi va tashqi bo‘g‘in uchun rezbali uyasi bo‘lgan bolg‘alangan sterjen."),
                "Forged steel 42CrMo4", rod, new[] { m.ForgedSteel }, a, along);
            Mesh eye = ctx.Mesh("rs-toe-eye", k =>
            {
                k.Sleeve(0.025f, 0.0205f, -0.02f, 0.02f, 12);
                k.Sub(1);
                k.Sleeve(0.0205f, 0.009f, -0.022f, 0.022f, 12);
            }, 2);
            Quaternion alongZ = InternalsContext.AlongY(Vector3.forward);
            Transform inner = ctx.SubAssembly(toe, id + ".toe-link.inner-joint", new Txt("Inner Joint and Adjuster", "Ichki bo‘g‘in va rostlagich",
                    "Rubber bushing in the rod eye, clamped by a bolt whose eccentric cams move the rod in or out to set the toe.",
                    "Sterjen ko‘zidagi rezina vtulka; uni qisuvchi boltning eksentrik shaybalari tortqini ichkariga yoki tashqariga surib yaqinlashuvni rostlaydi."),
                "Steel and rubber", new Vector3(-c.S, 0f, 0f), 0.6f);
            ctx.Part(inner, id + ".toe-link.inner-joint.bushing", new Txt("Eye bushing", "Ko‘z vtulkasi", "Steel shell with a bonded rubber core: filters noise and lets the rod swing.", "Yopishtirilgan rezina o‘zakli po‘lat gilza: shovqinni so‘ndiradi va tortqining tebranishiga imkon beradi."),
                "Steel and natural rubber", eye, new[] { m.ZincSteel, m.Rubber }, a, alongZ);
            Mesh camBolt = BoltMesh(0.012f, 0.08f);
            ctx.Part(inner, id + ".toe-link.inner-joint.cam-bolt", new Txt("Cam bolt M12", "Eksentrik bolt M12", "Bolt through the bracket slots; turning it turns the cams.", "Kronshteyn tirqishlaridan o‘tuvchi bolt; uni burash eksentriklarni buraydi."),
                "Steel 10.9, zinc flake", camBolt, new[] { m.ZincSteel }, a + Vector3.forward * 0.036f, InternalsContext.AlongY(Vector3.forward), true);
            Mesh cam = ctx.Mesh("rs-adjuster-cam", k =>
            {
                k.Push(new Vector3(0.006f, 0f, 0f), Quaternion.identity);
                k.Lathe(new[] { new Vector2(0.0065f, 0f), new Vector2(0.024f, 0f), new Vector2(0.024f, 0.004f), new Vector2(0.0065f, 0.004f), new Vector2(0.0065f, 0f) }, 18);
                k.Pop();
                k.Box(new Vector3(0.028f, 0.002f, 0f), new Vector3(0.012f, 0.004f, 0.006f));
            });
            ctx.Part(inner, id + ".toe-link.inner-joint.adjuster-cam", new Txt("Adjuster cam", "Rostlash eksentrigi", "Eccentric washer keyed to the bolt; its lobe bears on the bracket's guide.", "Boltga shponkalangan eksentrik shayba; uning bo‘rtig‘i kronshteyn yo‘naltiruvchisiga tiraladi."),
                "Steel", cam, new[] { m.ZincSteel }, a + Vector3.forward * 0.031f, InternalsContext.AlongY(Vector3.forward), true);
            Mesh camNut = NutMesh(0.012f, true);
            ctx.Part(inner, id + ".toe-link.inner-joint.nut", new Txt("Nut M12", "Gayka M12", "Prevailing-torque nut that locks the cam setting.", "Eksentrik holatini qulflovchi o‘z-o‘zidan bo‘shamaydigan gayka."),
                "Steel, zinc flake", camNut, new[] { m.ZincSteel }, a - Vector3.forward * 0.036f, InternalsContext.AlongY(Vector3.back), true);
            // Outer ball joint: housing with a threaded shank into the rod, stud up into the carrier.
            Transform outer = ctx.SubAssembly(toe, id + ".toe-link.outer-joint", new Txt("Outer Ball Joint", "Tashqi sharli bo‘g‘in",
                    "Sealed ball joint screwed into the rod; its stud locks into the hub carrier's front arm.",
                    "Sterjenga buralgan zichlangan sharli bo‘g‘in; barmog‘i gupchak tayanchining old yelkasiga mahkamlanadi."),
                "Forged steel", Vector3.up, 0.6f);
            Mesh housing = ctx.Mesh("rs-toe-joint-housing-" + c.Key, k =>
            {
                k.Lathe(new[] { new Vector2(0f, -0.019f), new Vector2(0.015f, -0.019f), new Vector2(0.019f, -0.01f), new Vector2(0.019f, 0.006f), new Vector2(0.014f, 0.011f), new Vector2(0f, 0.011f) }, 12, 35f);
                Vector3 shankDir = -dir;
                k.Tube(new[] { shankDir * 0.014f + Vector3.down * 0.003f, shankDir * 0.056f }, 0.0082f, 10, false, true, Vector3.up);
                k.Sub(1);
                k.Lathe(new[] { new Vector2(0.007f, 0.011f), new Vector2(0.017f, 0.012f), new Vector2(0.019f, 0.018f), new Vector2(0.011f, 0.025f), new Vector2(0.008f, 0.026f) }, 12, 50f);
            }, 2);
            ctx.Part(outer, id + ".toe-link.outer-joint.housing", new Txt("Joint housing and boot", "Bo‘g‘in korpusi va g‘ilofi", "Forged housing with a threaded shank, sealed by a rubber boot.", "Rezbali dumli bolg‘alangan korpus, rezina g‘ilof bilan zichlangan."),
                "Forged steel, chloroprene boot", housing, new[] { m.ForgedSteel, m.Rubber }, b, Quaternion.identity);
            Mesh stud = ctx.Mesh("tie-rod-end-stud", k => k.Lathe(new[] { new Vector2(0f, -0.012f), new Vector2(0.011f, -0.006f), new Vector2(0.011f, 0.004f), new Vector2(0.0085f, 0.012f), new Vector2(0.007f, 0.05f), new Vector2(0.0055f, 0.054f), new Vector2(0f, 0.054f) }, 12, 40f));
            ctx.Part(outer, id + ".toe-link.outer-joint.stud", new Txt("Ball stud", "Sharli barmoq", "Tapered stud locked in the carrier's front arm.", "Gupchak tayanchining old yelkasiga konus bilan mahkamlangan sharli barmoq."),
                "Case-hardened steel", stud, new[] { m.MachinedSteel }, b, Quaternion.identity, false, Vector3.up, 0.5f);
            Mesh studNut = NutMesh(0.012f, false);
            ctx.Part(outer, id + ".toe-link.outer-joint.nut", new Txt("Nut M12", "Gayka M12", "Self-locking nut on top of the carrier arm.", "Tayanch yelkasi ustidagi o‘z-o‘zidan qulflanadigan gayka."),
                "Steel", studNut, new[] { m.ZincSteel }, b + Vector3.up * 0.04f, Quaternion.identity, true);
            Mesh jam = NutMesh(0.016f, false);
            ctx.Part(toe, id + ".toe-link.lock-nut", new Txt("Lock nut M16", "Kontrgayka M16", "Jam nut that locks the outer joint's thread in the rod.", "Tashqi bo‘g‘in rezbasini sterjenda qulflovchi kontrgayka."),
                "Steel", jam, new[] { m.ZincSteel }, b - dir * 0.066f, InternalsContext.AlongY(dir, Vector3.forward), true);
        }

        private void TrailingLink(Transform corner, RearCorner c, string id)
        {
            Transform link = ctx.SubAssembly(corner, id + ".trailing-link", new Txt("Trailing Link", "Bo‘ylama richag",
                    "Forged link from the hub carrier forward to the well's front wall: takes the drive and brake thrust at the bottom of the carrier.",
                    "Gupchak tayanchidan arka old devorigacha bo‘lgan bolg‘alangan richag: tayanch pastida tortish va tormozlash kuchlarini qabul qiladi."),
                "Forged steel", new Vector3(0f, -0.5f, 1f), 1f);
            Vector3 a = c.TrailFront, b = c.TrailRear;
            Vector3 o = Vector3.Lerp(a, b, 0.5f);
            Vector3 dir = (b - a).normalized;
            Mesh mesh = ctx.Mesh("rs-trailing-link-" + c.Key, k =>
            {
                k.Push(Matrix4x4.Translate(-o));
                Vector3 s0 = a + dir * 0.03f, s1 = b - dir * 0.03f;
                Vector3 mid = Vector3.Lerp(s0, s1, 0.5f) + Vector3.down * 0.008f;
                k.Tube(MeshKit.CatmullRom(new[] { s0, mid, s1 }, 5), 0.0125f, 10, true, true, Vector3.right, x => 1f + 0.18f * Mathf.Sin(x * Mathf.PI), new Vector2(0.75f, 1.45f));
                foreach (Vector3 e in new[] { a, b })
                {
                    k.Push(e, InternalsContext.AlongY(Vector3.right));
                    k.Sleeve(0.029f, 0.0245f, -0.022f, 0.022f, 14);
                    k.Pop();
                }
                k.Pop();
            });
            ctx.Part(link, id + ".trailing-link.link", new Txt("Trailing link forging", "Bo‘ylama richag (bolg‘alangan)",
                    "Forged steel link, deep in section against buckling, with a pressed bushing eye at each end.",
                    "Bo‘ylama egilishga qarshi baland kesimli, har bir uchida presslangan saylentblok ko‘zi bo‘lgan bolg‘alangan po‘lat richag."),
                "Forged steel 38MnVS6", mesh, new[] { m.ForgedSteel }, o, Quaternion.identity);
            Bushing(link, id + ".trailing-link.front-bushing", "Front Bushing", "Old saylentblok", a, c.W.Outward, 0.0245f, 0.044f, Vector3.forward);
            Bushing(link, id + ".trailing-link.rear-bushing", "Rear Bushing", "Orqa saylentblok", b, c.W.Outward, 0.0245f, 0.044f, Vector3.back);
        }

        // ------------------------------------------------------------------------------------------------ hub carrier

        private void HubCarrier(Transform corner, RearCorner c, string id)
        {
            WheelInfo w = c.W;
            Transform group = ctx.SubAssembly(corner, id + ".hub-carrier", new Txt("Hub Carrier", "Gupchak tayanchi",
                    "Cast upright that holds the wheel bearing and hub, the brake caliper and the parking-brake shoes, with an eye or clevis for each link.",
                    "G‘ildirak podshipnigi va gupchak, tormoz supporti va to‘xtash tormozi kolodkalarini ushlovchi, har bir richag uchun ko‘z yoki vilkasi bo‘lgan quyma tirgak."),
                "Cast iron", new Vector3(c.S, 0f, 0f), 1.1f);
            Vector3 C = w.Centre, ax = w.Outward, f = w.Rolling;
            float a = c.HubFace;
            float bossIn = a - 0.088f, bossOut = a - 0.03f;
            Mesh upright = ctx.Mesh("rs-carrier-" + c.Key, k =>
            {
                k.Push(Matrix4x4.Translate(-C));
                // Bearing boss.
                k.Push(C, InternalsContext.AlongY(ax));
                k.Lathe(new[] { new Vector2(0.044f, bossIn), new Vector2(0.062f, bossIn), new Vector2(0.068f, bossIn + 0.008f), new Vector2(0.068f, bossOut - 0.008f),
                    new Vector2(0.06f, bossOut), new Vector2(0.044f, bossOut), new Vector2(0.044f, bossIn) }, 18);
                k.Pop();
                // Upper arm to the upper ball joint's eye (the stud comes down into it).
                Vector3 upEye = c.Ubj - Vector3.up * 0.04f;
                Vector3 upStart = C + ax * (bossIn + 0.02f) + Vector3.up * 0.055f;
                k.Tube(MeshKit.CatmullRom(new[] { upStart, Vector3.Lerp(upStart, upEye, 0.5f) - ax * 0.01f, upEye }, 5), 0.019f, 8, true, true, f, x => Mathf.Lerp(1.15f, 0.85f, x), new Vector2(1.25f, 0.85f));
                Eye(k, upEye, 0.021f, 0.0075f, 0.012f);
                // Lower rear arm to the spring link's ball joint (stud up into the eye).
                Vector3 lowEye = c.Lbj + Vector3.up * 0.042f;
                Vector3 lowStart = C + ax * (bossIn + 0.016f) + (-f * 0.6f - Vector3.up * 0.8f).normalized * 0.05f;
                k.Tube(MeshKit.CatmullRom(new[] { lowStart, Vector3.Lerp(lowStart, lowEye, 0.5f) - ax * 0.012f, lowEye }, 5), 0.02f, 8, true, true, f, x => Mathf.Lerp(1.15f, 0.85f, x), new Vector2(1.2f, 0.9f));
                Eye(k, lowEye, 0.024f, 0.008f, 0.013f);
                // Front arm to the toe link's ball joint.
                Vector3 toeEye = c.ToeOuter + Vector3.up * 0.03f;
                Vector3 toeStart = C + ax * (bossIn + 0.016f) + (f * 0.95f - Vector3.up * 0.2f).normalized * 0.055f;
                k.Tube(MeshKit.CatmullRom(new[] { toeStart, Vector3.Lerp(toeStart, toeEye, 0.5f) - ax * 0.008f, toeEye }, 5), 0.016f, 8, true, true, Vector3.up, x => Mathf.Lerp(1.1f, 0.9f, x), new Vector2(1.3f, 0.85f));
                Eye(k, toeEye, 0.02f, 0.007f, 0.011f);
                // Front-lower clevis for the trailing link's rear bushing (axis along x).
                Vector3 tr = c.TrailRear;
                Vector3 trStart = C + ax * (bossIn + 0.02f) + (f * 0.6f - Vector3.up * 0.8f).normalized * 0.055f;
                Vector3 trRoot = tr + Vector3.up * 0.034f + f * 0.006f;
                k.Tube(MeshKit.CatmullRom(new[] { trStart, Vector3.Lerp(trStart, trRoot, 0.5f) - ax * 0.01f, trRoot }, 5), 0.017f, 8, true, true, f, x => Mathf.Lerp(1.15f, 0.95f, x), new Vector2(1.4f, 0.9f));
                foreach (float dx in new[] { -0.031f, 0.031f })
                {
                    Vector3 ear = tr + Vector3.right * dx;
                    k.PlanarPrism(new[]
                    {
                        ear + new Vector3(0f, 0.04f, -0.03f), ear + new Vector3(0f, 0.04f, 0.03f), ear + new Vector3(0f, -0.005f, 0.03f),
                        ear + new Vector3(0f, -0.03f, 0.012f), ear + new Vector3(0f, -0.03f, -0.012f), ear + new Vector3(0f, -0.005f, -0.03f)
                    }, 0.006f);
                }
                k.Box(trRoot + Vector3.up * 0.004f, new Vector3(0.067f, 0.008f, 0.05f));
                // Caliper lugs at the measured caliper position, each on a web from the boss; bolts along the axle.
                foreach (Vector3 lug in new[] { c.CaliperLugA, c.CaliperLugB })
                {
                    Vector3 radial = Vector3.ProjectOnPlane(lug - C, ax).normalized;
                    float lugAxial = Vector3.Dot(lug - C, ax);
                    Vector3 root = C + ax * Mathf.Clamp(lugAxial, bossIn + 0.012f, bossOut - 0.012f) + radial * 0.062f;
                    k.Tube(new[] { root, lug - radial * 0.008f }, 0.0105f, 10, true, true, ax, null, new Vector2(1.35f, 0.8f));
                    k.Push(lug, InternalsContext.AlongY(ax));
                    k.Lathe(new[] { new Vector2(0.0055f, -0.008f), new Vector2(0.0125f, -0.008f), new Vector2(0.0135f, -0.006f), new Vector2(0.0135f, 0.006f),
                        new Vector2(0.0125f, 0.008f), new Vector2(0.0055f, 0.008f), new Vector2(0.0055f, -0.008f) }, 10);
                    k.Pop();
                }
                // Wheel-speed sensor boss (rear-top of the bearing boss) and the hose-bracket pad.
                Vector3 sensorDir = (-f * 0.7f + Vector3.up * 0.7f).normalized;
                k.Push(C + ax * (bossIn + 0.03f) + sensorDir * 0.066f, InternalsContext.AlongY(sensorDir));
                k.Cylinder(0.011f, 0f, 0.012f, 12);
                k.Pop();
                k.Pop();
            });
            ctx.Part(group, id + ".hub-carrier.upright", new Txt("Hub carrier casting", "Gupchak tayanchi (quyma)",
                    "Ductile-iron upright: bearing boss, three ball-joint eyes, the trailing-link clevis and the caliper lugs in one casting.",
                    "Egiluvchan cho‘yan tirgak: podshipnik uyasi, uchta sharli tayanch ko‘zi, bo‘ylama richag vilkasi va support quloqlari bitta quymada."),
                "Ductile iron EN-GJS-500", upright, new[] { m.CastIron }, C, Quaternion.identity);

            // Parking-brake shoe carrier: backing plate inside the disc's hat with the shoe-expander slot at the caliper side.
            Mesh plate = ctx.Mesh("rs-park-brake-plate", k =>
            {
                const int n = 20;
                var outline = new List<Vector2>();
                for (int i = 0; i <= n; i++)
                {
                    float t = Mathf.Lerp(16f, 344f, i / (float)n) * Mathf.Deg2Rad;
                    outline.Add(new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * 0.084f);
                }
                for (int i = n; i >= 0; i--)
                {
                    float t = Mathf.Lerp(28f, 332f, i / (float)n) * Mathf.Deg2Rad;
                    outline.Add(new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * 0.047f);
                }
                k.Push(Vector3.zero, Quaternion.Euler(90f, 0f, 0f));
                k.Extrude(outline, 0.0025f);
                k.Pop();
                k.Sleeve(0.06f, 0.046f, -0.004f, 0.002f, 16);
            });
            Vector3 slotDir = Vector3.ProjectOnPlane(c.ParkBrakeSlotDir, ax).normalized;
            Quaternion plateRot = AlignXY(slotDir, ax);
            ctx.Part(group, id + ".hub-carrier.park-brake-carrier", new Txt("Parking-brake shoe carrier", "To‘xtash tormozi kolodka tayanchi",
                    "Pressed backing plate bolted to the carrier inside the disc's hat; the slot takes the shoe expander and cable of the drum-in-hat parking brake.",
                    "Disk qalpoqchasi ichida tayanchga boltlangan presslangan plastina; tirqishiga qalpoqchadagi to‘xtash tormozining kolodka ochgichi va trosi kiradi."),
                "Pressed steel, zinc plated", plate, new[] { m.ZincSteel }, c.ParkBrakeCentre, plateRot, false, ax, 0.5f);

            Transform hub = ctx.SubAssembly(group, id + ".hub-carrier.hub", new Txt("Hub and Wheel Bearing", "Gupchak va g‘ildirak podshipnigi",
                    "Driven hub flange with five studs on a sealed double-row bearing; the drive shaft's outer joint splines into it.",
                    "Zichlangan ikki qatorli podshipnikdagi beshta shpilkali yetaklovchi gupchak flanetsi; uzatma valining tashqi sharniri unga shlitsa bilan kiradi."),
                "Steel", ax, 0.8f);
            Quaternion onAxle = InternalsContext.AlongY(ax);
            Vector3 face = C + ax * a;
            Mesh hubMesh = ctx.Mesh("rs-hub-flange", k =>
            {
                k.Lathe(new[] { new Vector2(0f, -0.07f), new Vector2(0.027f, -0.07f), new Vector2(0.027f, -0.016f), new Vector2(0.05f, -0.014f), new Vector2(0.08f, -0.012f),
                    new Vector2(0.083f, -0.008f), new Vector2(0.083f, 0f), new Vector2(0.037f, 0f), new Vector2(0.036f, 0.013f), new Vector2(0.0f, 0.013f) }, 20, 35f);
            });
            ctx.Part(hub, id + ".hub-carrier.hub.flange", new Txt("Wheel hub", "G‘ildirak gupchagi", "Forged hub flange with a splined bore for the drive shaft; the disc and the wheel bolt to it.", "Uzatma vali uchun shlitsali teshikli bolg‘alangan gupchak flanetsi; disk va g‘ildirak unga mahkamlanadi."),
                "Forged steel", hubMesh, new[] { m.MachinedSteel }, face, onAxle, false, ax, 0.6f);
            Mesh bearing = ctx.Mesh("rs-wheel-bearing", k =>
            {
                k.Sleeve(0.044f, 0.028f, -0.078f, -0.02f, 20);
                k.Lathe(new[] { new Vector2(0.044f, -0.034f), new Vector2(0.058f, -0.034f), new Vector2(0.058f, -0.026f), new Vector2(0.044f, -0.026f) }, 20);
            });
            ctx.Part(hub, id + ".hub-carrier.hub.bearing", new Txt("Wheel bearing unit", "G‘ildirak podshipnigi bloki", "Sealed double-row angular-contact ball bearing with the ABS tone ring, greased for life.", "ABS tishli halqali, butun xizmat muddatiga moylangan zichlangan ikki qatorli radial-tirak sharikli podshipnik."),
                "Bearing steel 100Cr6", bearing, new[] { m.MachinedSteel }, face, onAxle);
            Mesh stud = ctx.Mesh("rs-wheel-stud-M14", k =>
            {
                k.Lathe(new[] { new Vector2(0f, -0.016f), new Vector2(0.011f, -0.016f), new Vector2(0.011f, -0.012f), new Vector2(0.007f, -0.011f),
                    new Vector2(0.007f, 0.04f), new Vector2(0.0055f, 0.043f), new Vector2(0f, 0.043f) }, 6);
            });
            Vector3 r0 = Vector3.ProjectOnPlane(Vector3.up, ax).normalized, r1 = Vector3.Cross(ax, r0);
            for (int i = 0; i < 5; i++)
            {
                float ang = Mathf.PI * 2f * i / 5f;
                Vector3 p = face + (r0 * Mathf.Cos(ang) + r1 * Mathf.Sin(ang)) * 0.0572f;
                ctx.Part(hub, id + ".hub-carrier.hub.stud-" + (i + 1), new Txt("Wheel stud M14x1.5", "G‘ildirak shpilkasi M14x1.5", "Knurled stud pressed into the hub; carries a wheel nut.", "Gupchakka presslangan rifelli shpilka; g‘ildirak gaykasi buraladi."),
                    "Steel 10.9", stud, new[] { m.ZincSteel }, p, onAxle, true, ax, 0.4f);
            }
            Mesh axleNut = NutMesh(0.024f, true);
            ctx.Part(hub, id + ".hub-carrier.hub.axle-nut", new Txt("Drive-shaft nut M24", "Val gaykasi M24", "Staked nut on the drive shaft's spline end that preloads the bearing.", "Uzatma valining shlitsali uchidagi, podshipnikni tortib turuvchi zarblangan gayka."),
                "Steel", axleNut, new[] { m.ZincSteel }, face + ax * 0.013f, onAxle, true);

            // Wheel-speed sensor in its boss, reading the tone ring.
            Vector3 sDir = (-f * 0.7f + Vector3.up * 0.7f).normalized;
            Vector3 sensorAt = C + ax * (bossIn + 0.03f) + sDir * 0.078f;
            Mesh sensor = ctx.Mesh("abs-sensor", k =>
            {
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.007f, 0f), new Vector2(0.007f, 0.03f), new Vector2(0.011f, 0.03f), new Vector2(0.011f, 0.038f), new Vector2(0.006f, 0.05f), new Vector2(0f, 0.05f) }, 12);
                k.Box(new Vector3(0.012f, 0.034f, 0f), new Vector3(0.018f, 0.004f, 0.014f));
            });
            ctx.Part(group, id + ".hub-carrier.abs-sensor", new Txt("Wheel-speed sensor", "G‘ildirak tezligi datchigi", "Active Hall-effect sensor for ABS, stability control and the parking-brake logic.", "ABS, barqarorlik tizimi va to‘xtash tormozi mantiqi uchun faol Xoll datchigi."),
                "Plastic and steel", sensor, new[] { m.BlackPlastic }, sensorAt - sDir * 0.012f, InternalsContext.AlongY(sDir), true);

            // Brake-hose bracket: tab bolted to the upper arm, hole for the hose's crimp fitting and clip.
            Mesh tab = ctx.Mesh("rs-hose-bracket", k =>
            {
                List<Vector2> outline = MeshKit.Slot(new Vector2(0f, -0.016f), new Vector2(0f, 0.012f), 0.011f, 6);
                k.Extrude(outline, 0.0025f);
                k.Push(new Vector3(0f, 0.012f, 0f), InternalsContext.AlongY(Vector3.forward));
                k.Sleeve(0.0075f, 0.005f, -0.0045f, 0.0045f, 12);
                k.Pop();
            });
            ctx.Part(group, id + ".hub-carrier.hose-bracket", new Txt("Brake-hose bracket", "Tormoz shlangi kronshteyni", "Pressed tab on the carrier that holds the flexible brake hose's fitting and clip.", "Egiluvchan tormoz shlangi uchligi va qisqichini ushlovchi tayanchdagi presslangan quloq."),
                "Pressed steel, zinc plated", tab, new[] { m.ZincSteel }, c.HoseBracket - Vector3.up * 0.012f, Quaternion.LookRotation(c.HoseBracketNormal, Vector3.up), true);
        }

        // Rotation whose local +X is xDir and +Y is yDir (orthonormalised).
        private static Quaternion AlignXY(Vector3 xDir, Vector3 yDir)
        {
            Vector3 y = yDir.normalized;
            Vector3 x = Vector3.ProjectOnPlane(xDir, y).normalized;
            Vector3 z = Vector3.Cross(x, y);
            return Quaternion.LookRotation(z, y);
        }

        // Ring-shaped eye with a vertical bore (ball-stud taper seat).
        private static void Eye(MeshKit k, Vector3 centre, float rOuter, float rInner, float halfHeight)
        {
            k.Push(centre, Quaternion.identity);
            k.Lathe(new[] { new Vector2(rInner, -halfHeight), new Vector2(rOuter, -halfHeight), new Vector2(rOuter + 0.001f, halfHeight), new Vector2(rInner, halfHeight), new Vector2(rInner, -halfHeight) }, 12);
            k.Pop();
        }

        // ------------------------------------------------------------------------------------------------ coilover

        private void Coilover(Transform corner, RearCorner c, string id)
        {
            Vector3 bottom = c.CoilBottom, top = c.CoilTop;
            Vector3 axis = top - bottom;
            float L = axis.magnitude;
            Vector3 up = axis / L;
            Quaternion rot = InternalsContext.AlongY(axis, c.LcaAxis);
            float panY = Vector3.Dot(c.Pan - bottom, up) + 0.004f;
            float seatHigh = L - 0.042f;
            float body = Mathf.Min(0.26f, L * 0.52f);
            Transform coil = ctx.SubAssembly(corner, id + ".coilover", new Txt("Coilover", "Prujinali amortizator",
                    "Coil spring around a monotube damper, standing on the spring link's pan: carries the rear of the car and controls wheel motion.",
                    "Prujina richagi tovasida turgan, monotrubali amortizator atrofidagi spiral prujina: avtomobil orqasini ko‘taradi va g‘ildirak harakatini boshqaradi."),
                "Steel", new Vector3(0f, 1f, -0.5f), 1.3f);
            string key = "-" + Mathf.RoundToInt(L * 1000f);
            Transform damper = ctx.SubAssembly(coil, id + ".coilover.damper", new Txt("Damper", "Amortizator",
                    "Gas-charged monotube damper; the piston forces oil through shim valves to damp the wheel's motion.",
                    "Gaz to‘ldirilgan monotrubali amortizator; porshen moyni plastinali klapanlar orqali haydab g‘ildirak harakatini so‘ndiradi."),
                "Steel", Vector3.down, 0.6f);
            Mesh bodyMesh = ctx.Mesh("rs-damper-body" + key, k =>
            {
                k.Lathe(new[] { new Vector2(0f, 0.012f), new Vector2(0.016f, 0.012f), new Vector2(0.0235f, 0.03f), new Vector2(0.0235f, body - 0.012f), new Vector2(0.02f, body),
                    new Vector2(0.011f, body), new Vector2(0.011f, body + 0.006f), new Vector2(0f, body + 0.006f) }, 14, 35f);
                k.Push(Vector3.zero, InternalsContext.AlongY(Vector3.forward));
                k.Sleeve(0.015f, 0.0065f, -0.0185f, 0.0185f, 16);
                k.Pop();
            });
            ctx.Part(damper, id + ".coilover.damper.body", new Txt("Damper body", "Amortizator korpusi", "Monotube with the piston, shim valving and the nitrogen chamber; a bonded bushing in its lower eye.", "Porshen, plastinali klapanlar va azot kamerasi joylashgan monotruba; pastki ko‘zida yopishtirilgan vtulka."),
                "Steel tube, black", bodyMesh, new[] { m.BlackSteel }, bottom, rot);
            Mesh rod = ctx.Mesh("rs-damper-rod" + key, k => k.Cylinder(0.0105f, body - 0.01f, L - 0.006f, 14));
            ctx.Part(damper, id + ".coilover.damper.rod", new Txt("Piston rod", "Shtok", "Hard-chromed rod that carries the piston and passes through the top mount.", "Porshenni ko‘taruvchi va yuqori tayanchdan o‘tuvchi qattiq xromlangan shtok."),
                "Hard-chromed steel", rod, new[] { m.Chrome }, bottom, rot, false, Vector3.up, 0.5f);
            Mesh bump = ctx.Mesh("rs-bump-stop", k =>
            {
                var p = new List<Vector2> { new Vector2(0.011f, 0f) };
                for (int i = 0; i <= 6; i++) p.Add(new Vector2(0.019f + 0.003f * Mathf.Cos(i * Mathf.PI * 0.5f), 0.005f + 0.05f * i / 6f));
                p.Add(new Vector2(0.011f, 0.06f));
                p.Add(new Vector2(0.011f, 0f));
                k.Lathe(p, 10, 55f);
            });
            ctx.Part(damper, id + ".coilover.damper.bump-stop", new Txt("Bump stop", "Siqilish buferi", "Microcellular polyurethane stop that cushions full compression.", "To‘liq siqilishni yumshatuvchi mikrog‘ovak poliuretan bufer."),
                "Polyurethane", bump, new[] { m.AnodisedYellow }, bottom + up * (body + 0.012f), rot);
            float bootLen = Mathf.Max(0.06f, seatHigh - 0.012f - (body - 0.03f));
            Mesh boot = ctx.Mesh("rs-damper-boot" + key, k => k.Bellows(0.026f, 0.02f, 0.024f, 0.031f, bootLen, 5, 10));
            ctx.Part(damper, id + ".coilover.damper.boot", new Txt("Dust boot", "Chang g‘ilofi", "Bellows that keeps grit off the rod seal.", "Shtok salnigini qum va changdan saqlovchi gofra."),
                "Thermoplastic elastomer", boot, new[] { m.Rubber }, bottom + up * (body - 0.03f), rot);

            Mesh pad = ctx.Mesh("rs-spring-pad", k => k.Lathe(new[] { new Vector2(0.03f, 0f), new Vector2(0.055f, 0f), new Vector2(0.055f, 0.005f), new Vector2(0.03f, 0.005f), new Vector2(0.03f, 0f) }, 16));
            ctx.Part(coil, id + ".coilover.lower-pad", new Txt("Lower spring pad", "Pastki prujina qistirmasi", "Rubber pad between the spring's end coil and the pan: stops noise and wear.", "Prujina oxirgi o‘rami va tova orasidagi rezina qistirma: shovqin va yeyilishning oldini oladi."),
                "EPDM rubber", pad, new[] { m.Rubber }, bottom + up * (panY - 0.004f), rot);
            float springLength = seatHigh - panY - 0.006f;
            float wire = RearChassisGeometry.CoilWire;
            Mesh spring = ctx.Mesh("rs-coil-spring" + key, k => k.Tube(MeshKit.HelixPath(RearChassisGeometry.CoilRadius, springLength, wire,
                Mathf.Clamp(springLength / 0.09f, 3f, 5.5f), 0.8f, 16), wire * 0.5f, 7, true, true, Vector3.up));
            ctx.Part(coil, id + ".coilover.spring", new Txt("Coil spring", "Spiral prujina", "Linear-rate spring of chrome-silicon wire with closed, ground ends.", "Uchlari yopiq va silliqlangan, xrom-kremniy simdan chiziqli bikrlikdagi prujina."),
                "Chrome-silicon spring steel", spring, new[] { m.AnodisedYellow }, bottom + up * (panY + 0.001f), rot, false, new Vector3(c.S, 0f, 0f), 0.7f);
            Mesh upperSeat = ctx.Mesh("rs-upper-spring-seat", k => k.Lathe(new[] { new Vector2(0.012f, -0.004f), new Vector2(0.056f, -0.004f), new Vector2(0.056f, 0.003f), new Vector2(0.04f, 0.012f), new Vector2(0.012f, 0.012f), new Vector2(0.012f, -0.004f) }, 18));
            ctx.Part(coil, id + ".coilover.upper-seat", new Txt("Upper spring seat", "Yuqori prujina tayanchi", "Pressed seat with a rubber insulator that centres the spring under the top mount.", "Prujinani yuqori tayanch ostida markazlashtiruvchi rezina izolyatorli presslangan tayanch."),
                "Pressed steel and rubber", upperSeat, new[] { m.BlackSteel }, bottom + up * seatHigh, rot);

            Transform mount = ctx.SubAssembly(coil, id + ".coilover.top-mount", new Txt("Top Mount", "Yuqori tayanch",
                    "Rubber-isolated mount that bolts the coilover to the wheel-arch roof.",
                    "Prujinali amortizatorni g‘ildirak arkasi tomiga mahkamlovchi, rezina bilan izolyatsiyalangan tayanch."),
                "Steel and rubber", Vector3.up, 0.7f);
            Mesh plate = ctx.Mesh("rs-top-mount-plate", k =>
            {
                k.Lathe(new[] { new Vector2(0.012f, 0.008f), new Vector2(0.064f, 0.008f), new Vector2(0.064f, 0.012f), new Vector2(0.048f, 0.016f), new Vector2(0.03f, 0.03f), new Vector2(0.012f, 0.03f), new Vector2(0.012f, 0.008f) }, 18, 30f);
            });
            Vector3 mountBase = bottom + up * (L - 0.03f);
            ctx.Part(mount, id + ".coilover.top-mount.plate", new Txt("Mount plate", "Tayanch plastinasi", "Dished steel plate with three studs that bolts under the strut-tower flange.", "Minora gardishi ostiga boltlanadigan uchta shpilkali botiq po‘lat plastina."),
                "Pressed steel", plate, new[] { m.BlackSteel }, mountBase, rot);
            Mesh isolator = ctx.Mesh("rs-top-mount-isolator", k => k.Lathe(new[] { new Vector2(0.012f, 0.012f), new Vector2(0.047f, 0.012f), new Vector2(0.05f, 0.02f), new Vector2(0.046f, 0.028f), new Vector2(0.012f, 0.028f), new Vector2(0.012f, 0.012f) }, 14, 45f));
            ctx.Part(mount, id + ".coilover.top-mount.isolator", new Txt("Rubber isolator", "Rezina izolyator", "Bonded rubber that filters vibration from the rod.", "Shtokdan keladigan tebranishni so‘ndiruvchi yopishtirilgan rezina."),
                "Natural rubber", isolator, new[] { m.Rubber }, mountBase - up * 0.012f, rot);
            // Strut tower: pressed cone welded into the sloping arch roof; its flange, square to the coilover, carries the
            // mount plate (studs through the flange, nuts on top).
            Mesh tower = ctx.Mesh("rs-strut-tower-" + c.Key, k =>
            {
                int n = c.TowerOuterBottom.Length;
                k.Push(Matrix4x4.Translate(-mountBase));
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    Vector3 r0 = Vector3.ProjectOnPlane(c.TowerOuterBottom[i] - mountBase, up).normalized, r1 = Vector3.ProjectOnPlane(c.TowerOuterBottom[j] - mountBase, up).normalized;
                    Vector3 n0 = (r0 + up * 0.35f).normalized, n1 = (r1 + up * 0.35f).normalized;
                    k.Quad(k.Vertex(c.TowerOuterBottom[i], n0, Vector2.zero), k.Vertex(c.TowerOuterBottom[j], n1, Vector2.right), k.Vertex(c.TowerOuterTop[j], n1, Vector2.one), k.Vertex(c.TowerOuterTop[i], n0, Vector2.up));
                    k.Quad(k.Vertex(c.TowerInnerBottom[i], -n0, Vector2.zero), k.Vertex(c.TowerInnerTop[i], -n0, Vector2.up), k.Vertex(c.TowerInnerTop[j], -n1, Vector2.one), k.Vertex(c.TowerInnerBottom[j], -n1, Vector2.right));
                    k.Quad(k.Vertex(c.TowerOuterTop[i], Vector3.up, Vector2.zero), k.Vertex(c.TowerOuterTop[j], Vector3.up, Vector2.right), k.Vertex(c.TowerInnerTop[j], Vector3.up, Vector2.one), k.Vertex(c.TowerInnerTop[i], Vector3.up, Vector2.up));
                }
                k.Pop();
                // Flange square to the coilover axis: the mount plate bolts to its underside.
                k.Push(Vector3.zero, InternalsContext.AlongY(up, c.LcaAxis));
                k.Lathe(new[] { new Vector2(0.042f, 0.0155f), new Vector2(0.066f, 0.0155f), new Vector2(0.066f, 0.0185f), new Vector2(0.042f, 0.0185f), new Vector2(0.042f, 0.0155f) }, 24);
                k.Pop();
            });
            ctx.Part(mount, id + ".coilover.top-mount.strut-tower", new Txt("Strut tower", "Amortizator minorasi",
                    "Pressed-steel cone welded into the sloping arch roof: its flange gives the top mount a flat seat square to the coilover and spreads the spring load into the body.",
                    "Qiya arka tomiga payvandlangan presslangan po‘lat konus: gardishi yuqori tayanchga prujinali amortizatorga tik tekis o‘rindiq beradi va prujina yukini kuzovga tarqatadi."),
                "Pressed steel, e-coated", tower, new[] { m.BlackSteel }, mountBase, Quaternion.identity, false, Vector3.up, 0.5f);
            Mesh rodNut = NutMesh(0.012f, true);
            ctx.Part(mount, id + ".coilover.top-mount.rod-nut", new Txt("Rod nut M12", "Shtok gaykasi M12", "Self-locking nut on the piston rod.", "Shtokdagi o‘z-o‘zidan qulflanadigan gayka."),
                "Steel", rodNut, new[] { m.ZincSteel }, mountBase + up * 0.03f, rot, true);
            Mesh studNut = ctx.Mesh("rs-stud-M8-nut", k =>
            {
                k.Cylinder(0.004f, 0f, 0.024f, 6);
                k.Push(new Vector3(0f, 0.0065f, 0f), Quaternion.identity);
                Nut(k, 0.008f, false);
                k.Pop();
            });
            Vector3 s0 = Vector3.ProjectOnPlane(Vector3.forward, up).normalized, s1 = Vector3.Cross(up, s0);
            for (int i = 0; i < 3; i++)
            {
                float ang = Mathf.PI * 2f * i / 3f + Mathf.PI / 6f;
                Vector3 p = mountBase + up * 0.012f + (s0 * Mathf.Cos(ang) + s1 * Mathf.Sin(ang)) * 0.053f;
                ctx.Part(mount, id + ".coilover.top-mount.stud-" + (i + 1), new Txt("Mount stud M8 with nut", "Tayanch shpilkasi M8 gaykasi bilan", "Stud pressed into the mount plate, through the strut-tower flange, held by a nut on top.", "Tayanch plastinasiga presslangan, minora gardishidan o‘tuvchi, ustidan gayka bilan tortilgan shpilka."),
                    "Steel 8.8, zinc plated", studNut, new[] { m.ZincSteel }, p, rot, true);
            }
            // Lower eye bolt through the clevis, along the spring link's pivot axis.
            Vector3 boltDir = -c.LcaAxis;
            Fastener(coil, id + ".coilover.lower-bolt", bottom - boltDir * 0.03f, boltDir, 0.012f, 0.066f, "M12", "Damper eye bolt M12", "Amortizator ko‘z bolti M12",
                "Bolt through the clevis and the damper's lower eye bushing.", "Vilka va amortizator pastki ko‘z vtulkasidan o‘tuvchi bolt.");
        }

        // ------------------------------------------------------------------------------------------------ anti-roll bar

        private void AntiRollBar(Transform system)
        {
            string id = "suspension.rear-anti-roll-bar";
            Transform arb = ctx.Assembly(system, id, new Txt("Rear Anti-Roll Bar", "Orqa stabilizator",
                    "Torsion bar across the subframe, linked to both spring links: resists body roll and trims the car's balance in corners.",
                    "Yordamchi rama bo‘ylab o‘tgan, ikki prujina richagiga ulangan buraluvchi sterjen: kuzov og‘ishiga qarshilik qiladi va burilishda muvozanatni sozlaydi."),
                "Spring steel", Vector3.back, 1f);
            Vector3 origin = new Vector3(0f, g.ArbY, g.ArbZ);
            var ends = new List<(Vector3 eye, Vector3 knee, Vector3 bend)>();
            foreach (RearCorner c in new[] { g.Left, g.Right })
            {
                Vector3 eye = c.LinkHigh + Vector3.up * 0.018f;
                Vector3 bend = new Vector3(c.S * g.ArbHalf, g.ArbY, g.ArbZ);
                Vector3 knee = new Vector3(c.S * (Mathf.Abs(eye.x) - 0.015f), Mathf.Lerp(g.ArbY, eye.y, 0.4f), g.ArbZ);
                ends.Add((eye, knee, bend));
            }
            Mesh bar = ctx.Mesh("rs-anti-roll-bar", k =>
            {
                k.Push(Matrix4x4.Translate(-origin));
                var corners = new List<Vector3> { ends[0].eye, ends[0].knee, ends[0].bend, ends[1].bend, ends[1].knee, ends[1].eye };
                k.Tube(MeshKit.Bends(corners, 0.045f, 6), 0.0115f, 10, true, true, Vector3.up);
                foreach (var e in ends)
                {
                    k.Push(e.eye, Quaternion.identity);
                    k.Lathe(new[] { new Vector2(0.0055f, -0.006f), new Vector2(0.016f, -0.006f), new Vector2(0.016f, 0.006f), new Vector2(0.0055f, 0.006f), new Vector2(0.0055f, -0.006f) }, 16);
                    k.Pop();
                }
                k.Pop();
            });
            ctx.Part(arb, id + ".bar", new Txt("Anti-roll bar", "Stabilizator sterjeni", "Hollow spring-steel bar bent into a U; its levers end in flattened eyes for the drop links.", "U-simon egilgan ichi bo‘sh prujina po‘lat sterjen; richaglari tirgaklar uchun yassilangan ko‘zlar bilan tugaydi."),
                "Spring steel 50CrV4, black", bar, new[] { m.BlackSteel }, origin, Quaternion.identity);
            foreach (RearCorner c in new[] { g.Left, g.Right })
            {
                string side = c.Side.ToLowerInvariant();
                Transform link = ctx.SubAssembly(arb, id + ".link-" + side, new Txt(c.Side + " Drop Link", (c.S < 0 ? "Chap" : "O‘ng") + " stabilizator tirgagi",
                        "Short rod with a ball joint at each end that ties the bar lever to the spring link.",
                        "Stabilizator richagini prujina richagiga bog‘lovchi, ikki uchida sharli bo‘g‘inli qisqa tirgak."),
                    "Steel", new Vector3(c.S, 0f, 0f), 0.6f);
                float len = c.LinkHigh.y - c.LinkLow.y;
                Mesh body = ctx.Mesh("rs-drop-link-" + Mathf.RoundToInt(len * 1000f), k =>
                {
                    k.Cylinder(0.007f, 0.011f, len - 0.011f, 10);
                    foreach (float yy in new[] { 0.0f, len })
                    {
                        k.Push(new Vector3(0f, yy, 0f), InternalsContext.AlongY(Vector3.right));
                        k.Lathe(new[] { new Vector2(0f, -0.01f), new Vector2(0.011f, -0.01f), new Vector2(0.013f, 0f), new Vector2(0.011f, 0.01f), new Vector2(0f, 0.01f) }, 10);
                        k.Pop();
                    }
                });
                ctx.Part(link, id + ".link-" + side + ".body", new Txt("Link rod", "Tirgak sterjeni", "Rod with two moulded ball sockets.", "Ikki quyma sharli uyali sterjen."),
                    "Steel and glass-filled nylon", body, new[] { m.ZincSteel }, c.LinkLow, Quaternion.identity);
                Mesh stud = ctx.Mesh("rs-link-stud-nut", k =>
                {
                    k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.006f, 0f), new Vector2(0.005f, 0.03f), new Vector2(0f, 0.03f) }, 10);
                    k.Push(new Vector3(0f, 0.018f, 0f), Quaternion.identity);
                    Nut(k, 0.01f, false);
                    k.Pop();
                });
                foreach (bool upper in new[] { false, true })
                {
                    Vector3 p = upper ? c.LinkHigh : c.LinkLow;
                    Vector3 outward = new Vector3(c.S, 0f, 0f);
                    string u = upper ? "upper" : "lower";
                    ctx.Part(link, id + ".link-" + side + "." + u + "-stud", new Txt((upper ? "Upper" : "Lower") + " ball stud and nut", (upper ? "Yuqori" : "Pastki") + " sharli barmoq va gayka", "Ball stud through the bar eye or the link tab, held by a self-locking M10 nut.", "Sterjen ko‘zi yoki richag qulog‘idan o‘tuvchi, M10 o‘z-o‘zidan qulflanadigan gayka bilan tortilgan sharli barmoq."),
                        "Steel", stud, new[] { m.MachinedSteel }, p, InternalsContext.AlongY(upper ? Vector3.up : -outward), true);
                }
            }
            Transform mounts = ctx.SubAssembly(arb, id + ".mounts", new Txt("Bar Mounts", "Sterjen tayanchlari",
                    "Split rubber bushings in steel clamps that hold the bar under the rear floor.",
                    "Sterjenni orqa pol ostida ushlab turuvchi po‘lat qisqichlardagi kesikli rezina vtulkalar."),
                "Rubber and steel", Vector3.down, 0.6f);
            Mesh bush = ctx.Mesh("rs-arb-bushing", k => k.Sleeve(0.021f, 0.0115f, -0.017f, 0.017f, 12));
            float reach = Mathf.Max(0.012f, Mathf.Min(g.FloorFrontY, g.FloorRearY) - g.ArbY);
            Mesh clamp = ctx.Mesh("rs-arb-clamp-" + Mathf.RoundToInt(reach * 1000f), k =>
            {
                // U-strap under the bushing, flanges up against the floor.
                var path = new List<Vector3>();
                for (int i = 0; i <= 12; i++)
                {
                    float t = Mathf.PI * i / 12f;
                    path.Add(new Vector3(0f, -Mathf.Sin(t) * 0.0225f, Mathf.Cos(t) * 0.0225f));
                }
                k.Sweep(new[] { new Vector2(-0.0015f, -0.015f), new Vector2(0.0015f, -0.015f), new Vector2(0.0015f, 0.015f), new Vector2(-0.0015f, 0.015f) }, path, Vector3.right);
                foreach (float zz in new[] { 0.0225f, -0.0225f })
                    k.Box(new Vector3(0f, reach * 0.5f - 0.0015f, zz), new Vector3(0.03f, reach - 0.003f, 0.003f));
                foreach (float zz in new[] { 0.034f, -0.034f })
                    k.Box(new Vector3(0f, reach - 0.0015f, zz), new Vector3(0.03f, 0.003f, 0.026f));
            });
            Quaternion alongX = InternalsContext.AlongY(Vector3.right);
            Mesh bolt = BoltMesh(0.008f, 0.02f);
            foreach (float sx in new[] { -1f, 1f })
            {
                Vector3 p = new Vector3(sx * g.ArbMountX, g.ArbY, g.ArbZ);
                string side = sx < 0 ? "left" : "right";
                ctx.Part(mounts, id + ".mounts.bushing-" + side, new Txt((sx < 0 ? "Left" : "Right") + " bar bushing", (sx < 0 ? "Chap" : "O‘ng") + " sterjen vtulkasi", "Split rubber bushing that lets the bar twist.", "Sterjen buralishiga imkon beruvchi kesikli rezina vtulka."),
                    "Natural rubber", bush, new[] { m.Rubber }, p, alongX);
                ctx.Part(mounts, id + ".mounts.clamp-" + side, new Txt((sx < 0 ? "Left" : "Right") + " bar clamp", (sx < 0 ? "Chap" : "O‘ng") + " sterjen qisqichi", "Pressed-steel strap bolted up into the rear floor.", "Orqa polga pastdan boltlangan presslangan po‘lat xomut."),
                    "Pressed steel", clamp, new[] { m.BlackSteel }, p, Quaternion.identity);
                foreach (float dz in new[] { -0.034f, 0.034f })
                    ctx.Part(mounts, id + ".mounts.bolt-" + side + (dz < 0 ? "-rear" : "-front"), new Txt("Bolt M8", "Bolt M8", "Bolts the clamp to a weld nut in the floor.", "Qisqichni poldagi payvand gaykaga mahkamlaydi."),
                        "Steel 8.8, zinc flake", bolt, new[] { m.ZincSteel }, p + new Vector3(0f, reach - 0.003f, dz), InternalsContext.AlongY(Vector3.down), true);
            }
        }

        // ------------------------------------------------------------------------------------------------ subframe

        private void Subframe(Transform system)
        {
            string id = "suspension.rear-subframe";
            Transform sub = ctx.Assembly(system, id, new Txt("Rear Subframe", "Orqa yordamchi rama",
                    "Welded box-section cradle under the raised rear floor on four rubber mounts, with the pickup brackets on the wheel-well walls: carries the spring links, the anti-roll bar and the differential.",
                    "Orqa ko‘tarilgan pol ostidagi to‘rtta rezina tayanchli payvandlangan quti kesimli rama va g‘ildirak arkasi devorlaridagi kronshteynlar: prujina richaglari, stabilizator va differensialni ko‘taradi."),
                "Pressed and welded steel", Vector3.down, 1f);
            Transform cradle = ctx.SubAssembly(sub, id + ".cradle", new Txt("Cradle", "Rama",
                    "Two cross members and two side members welded into a rigid frame; clevises for the spring links and pads for the differential.",
                    "Ikki ko‘ndalang va ikki bo‘ylama to‘sin payvandlangan qattiq rama; prujina richaglari uchun vilkalar va differensial uchun tayanch maydonchalari."),
                "Steel S355, e-coated", Vector3.down, 0.8f);
            float hw = g.CrossDepth, hh = g.CrossHeight;
            foreach (bool front in new[] { true, false })
            {
                float z = front ? g.FrontCrossZ : g.RearCrossZ;
                Vector3 o = new Vector3(0f, g.CrossY, z);
                float half = g.CrossHalf - g.SideWidth;
                Mesh cross = ctx.Mesh("rs-cross-member-" + (front ? "front" : "rear"), k =>
                {
                    k.BoxTube(new[] { new Vector3(-half, 0f, 0f), new Vector3(half, 0f, 0f) }, hw, hh, 0.003f, Vector3.up);
                    // Mount cups for the body-mount bushings and pads for the differential.
                    foreach (Vector3 bm in g.BodyMounts)
                        if (Mathf.Abs(bm.z - z) < 0.01f)
                        {
                            k.Push(new Vector3(bm.x, hh * 0.5f, 0f), Quaternion.identity);
                            k.Sleeve(0.034f, 0.031f, -0.012f, 0.012f, 14);
                            k.Pop();
                        }
                    foreach (Vector3 dm in g.DiffMounts)
                        if (Mathf.Abs(dm.z - z) < 0.01f)
                        {
                            k.Box(new Vector3(dm.x, hh * 0.5f + 0.003f, 0f), new Vector3(0.05f, 0.006f, hw + 0.004f));
                            k.Push(new Vector3(dm.x, hh * 0.5f + 0.006f, 0f), Quaternion.identity);
                            k.Sleeve(0.011f, 0.006f, 0f, 0.008f, 12);
                            k.Pop();
                        }
                    // Corner gussets on top, tying each end into its side member.
                    float zEdge = front ? -hw * 0.5f : hw * 0.5f, zDir = front ? -1f : 1f;
                    foreach (float sx in new[] { -1f, 1f })
                        k.PlanarPrism(new[]
                        {
                            new Vector3(sx * half, hh * 0.5f + 0.002f, zEdge), new Vector3(sx * (half - 0.075f), hh * 0.5f + 0.002f, zEdge),
                            new Vector3(sx * half, hh * 0.5f + 0.002f, zEdge + zDir * 0.075f)
                        }, 0.004f);
                    if (front)
                    {
                        // Clevises for the spring links' inner bushings at both ends.
                        foreach (RearCorner c in new[] { g.Left, g.Right })
                        {
                            Vector3 pivot = c.LcaInner - o;
                            Vector3 axis = c.LcaAxis;
                            Vector3 outward = Vector3.Cross(axis, Vector3.up).normalized * (Vector3.Dot(Vector3.Cross(axis, Vector3.up), new Vector3(c.S, 0f, 0f)) > 0f ? 1f : -1f);
                            float toSide = (Mathf.Abs(c.LcaInner.x) - g.SideX) / Mathf.Max(0.3f, Mathf.Abs(outward.x));
                            foreach (float sgn in new[] { -1f, 1f })
                            {
                                Vector3 e = pivot + axis * sgn * 0.0345f;
                                Vector3 back = -outward * toSide;
                                k.PlanarPrism(new[]
                                {
                                    e + Vector3.up * 0.032f + back, e - Vector3.up * 0.032f + back, e - Vector3.up * 0.022f + outward * 0.01f,
                                    e + outward * 0.03f, e + Vector3.up * 0.022f + outward * 0.01f
                                }, 0.005f);
                            }
                        }
                    }
                });
                ctx.Part(cradle, id + ".cradle." + (front ? "front" : "rear") + "-cross-member", new Txt((front ? "Front" : "Rear") + " cross member", (front ? "Old" : "Orqa") + " ko‘ndalang to‘sin",
                        front ? "Box-section beam behind the floor step; carries the spring-link clevises, two body mounts and the differential's front pads."
                              : "Box-section beam ahead of the diffuser panel; carries two body mounts and the differential's rear pad.",
                        front ? "Pol pog‘onasi ortidagi quti kesimli to‘sin; prujina richagi vilkalari, ikki kuzov tayanchi va differensialning old tayanchlarini ko‘taradi."
                              : "Diffuzor paneli oldidagi quti kesimli to‘sin; ikki kuzov tayanchi va differensialning orqa tayanchini ko‘taradi."),
                    "Steel S355, 3 mm, e-coated", cross, new[] { m.BlackSteel }, o, Quaternion.identity, false, front ? Vector3.forward : Vector3.back, 0.6f);
            }
            foreach (float sx in new[] { -1f, 1f })
            {
                Vector3 a = new Vector3(sx * g.SideX, g.CrossY, g.FrontCrossZ + g.CrossDepth * 0.5f);
                Vector3 b = new Vector3(sx * g.SideX, g.CrossY, g.RearCrossZ - g.CrossDepth * 0.5f);
                Vector3 o = Vector3.Lerp(a, b, 0.5f);
                Mesh side = ctx.Mesh("rs-side-member", k =>
                {
                    Vector3 h = (a - b) * 0.5f;
                    h.x = 0f;
                    k.Sweep(Profiles.Hat(g.SideWidth - 0.008f, hh - 0.004f, 0.004f, 0.003f), new[] { -h, h }, Vector3.up);
                    k.Box(new Vector3(0f, -hh * 0.5f + 0.0015f, 0f), new Vector3(g.SideWidth + 0.004f, 0.003f, Mathf.Abs(h.z) * 2f));
                });
                string s = sx < 0 ? "left" : "right";
                ctx.Part(cradle, id + ".cradle." + s + "-side-member", new Txt((sx < 0 ? "Left" : "Right") + " side member", (sx < 0 ? "Chap" : "O‘ng") + " bo‘ylama to‘sin",
                        "Top-hat section closed by a bottom plate, welded between the cross members under the well's open rear end.",
                        "Pastki plastina bilan yopilgan shlyapasimon kesim, arkaning ochiq orqa qismi ostida ko‘ndalang to‘sinlar orasiga payvandlangan."),
                    "Steel S355, 3 mm, e-coated", side, new[] { m.BlackSteel }, o, Quaternion.identity, false, new Vector3(sx, 0f, 0f), 0.6f);
            }

            // Four body mounts: vertical rubber bushings in the cups, bolted up into the rear floor.
            Transform mounts = ctx.SubAssembly(sub, id + ".body-mounts", new Txt("Subframe Mounts", "Rama tayanchlari",
                    "Four rubber-bushed mounts that bolt the cradle to the rear floor and isolate road noise from the cabin.",
                    "Ramani orqa polga mahkamlovchi va yo‘l shovqinini salondan ajratuvchi to‘rtta rezina vtulkali tayanch."),
                "Rubber and steel", Vector3.up, 0.6f);
            string[] names = { "front-left", "front-right", "rear-left", "rear-right" };
            string[] en = { "Front Left", "Front Right", "Rear Left", "Rear Right" };
            string[] uz = { "Old chap", "Old o‘ng", "Orqa chap", "Orqa o‘ng" };
            for (int i = 0; i < 4; i++)
            {
                Vector3 top = g.BodyMounts[i];
                float h = Mathf.Max(0.03f, top.y - (g.CrossY + hh * 0.5f - 0.012f));
                Vector3 baseP = new Vector3(top.x, top.y - h, top.z);
                Transform mt = ctx.SubAssembly(mounts, id + ".body-mounts." + names[i], new Txt(en[i] + " Body Mount", uz[i] + " kuzov tayanchi",
                        "Bonded rubber bushing with a steel sleeve, clamped to the floor by one bolt.",
                        "Po‘lat vtulkali yopishtirilgan rezina tayanch, polga bitta bolt bilan tortilgan."),
                    "Rubber and steel", Vector3.up, 0.5f);
                string hk = Mathf.RoundToInt(h * 1000f).ToString();
                Mesh rubber = ctx.Mesh("rs-body-mount-rubber-" + hk, k => k.Lathe(new[]
                {
                    new Vector2(0.0125f, 0f), new Vector2(0.03f, 0f), new Vector2(0.03f, h - 0.014f), new Vector2(0.037f, h - 0.011f), new Vector2(0.037f, h - 0.003f),
                    new Vector2(0.0125f, h - 0.003f), new Vector2(0.0125f, 0f)
                }, 12));
                ctx.Part(mt, id + ".body-mounts." + names[i] + ".bushing", new Txt("Rubber bushing", "Rezina vtulka", "Natural-rubber element with a collar that seats against the floor.", "Polga tiraladigan yoqali tabiiy kauchuk element."),
                    "Natural rubber", rubber, new[] { m.Rubber }, baseP, Quaternion.identity);
                Mesh sleeve = ctx.Mesh("rs-body-mount-sleeve-" + hk, k => k.Sleeve(0.0125f, 0.0065f, -0.004f, h, 10));
                ctx.Part(mt, id + ".body-mounts." + names[i] + ".sleeve", new Txt("Inner sleeve", "Ichki vtulka", "Steel sleeve that takes the bolt's clamp load.", "Bolt qisish kuchini qabul qiluvchi po‘lat vtulka."),
                    "Steel", sleeve, new[] { m.ZincSteel }, baseP, Quaternion.identity);
                float headY = g.CrossY - hh * 0.5f - 0.001f;
                float boltLen = top.y + 0.022f - headY;
                Mesh bolt = BoltMesh(0.012f, boltLen);
                ctx.Part(mt, id + ".body-mounts." + names[i] + ".bolt", new Txt("Bolt M12", "Bolt M12", "Bolt from under the cross member up through the sleeve into a weld nut in the floor.", "Ko‘ndalang to‘sin ostidan vtulka orqali poldagi payvand gaykaga kiruvchi bolt."),
                    "Steel 10.9, zinc flake", bolt, new[] { m.ZincSteel }, new Vector3(top.x, headY, top.z), InternalsContext.AlongY(Vector3.down), true, Vector3.down, 0.5f);
            }

            // Pickup brackets on the wheel-well walls.
            foreach (RearCorner c in new[] { g.Left, g.Right })
            {
                string side = c.Side.ToLowerInvariant();
                Transform br = ctx.SubAssembly(sub, id + "." + side + "-brackets", new Txt(c.Side + " Pickup Brackets", (c.S < 0 ? "Chap" : "O‘ng") + " osma kronshteynlari",
                        "Pressed clevis brackets bolted to the wheel-well wall and front wall: upper arm, toe link and trailing link pivots.",
                        "G‘ildirak arkasi devori va old devoriga boltlangan presslangan vilkasimon kronshteynlar: yuqori richag, yaqinlashuv tortqisi va bo‘ylama richag o‘qlari."),
                    "Pressed steel", new Vector3(c.S, 0f, 0f), 0.8f);
                UpperBracket(br, id + "." + side + "-brackets.upper-arm", c);
                WallClevis(br, id + "." + side + "-brackets.toe-link", "Toe-link bracket", "Yaqinlashuv tortqisi kronshteyni",
                    "Clevis with slotted ears and cam guides: the eccentric cams slide the bolt to set the toe.",
                    "Tirqishli quloqlar va eksentrik yo‘naltiruvchili vilka: eksentriklar boltni surib yaqinlashuvni rostlaydi.",
                    c, c.ToeInner, c.WallToe, 0.044f, true);
                TrailBracket(br, id + "." + side + "-brackets.trailing-link", c);
            }
        }

        // One pressed bracket on the wall carrying both upper-arm pivots (axis along z).
        private void UpperBracket(Transform parent, string id, RearCorner c)
        {
            float wall = Mathf.Min(c.WallUcaFront, c.WallUcaRear);
            float wx = c.S * (wall + 0.003f);
            Vector3 origin = new Vector3(wx, c.UcaFront.y, (c.UcaFront.z + c.UcaRear.z) * 0.5f);
            float span = c.UcaFront.z - c.UcaRear.z;
            float reachF = Mathf.Abs(c.UcaFront.x) - wall, reachR = Mathf.Abs(c.UcaRear.x) - wall;
            Mesh mesh = ctx.Mesh("rs-upper-bracket-" + c.Key, k =>
            {
                k.RoundedBox(Vector3.zero, new Vector3(0.006f, 0.064f, span + 0.11f), 0.003f, 1);
                foreach (bool front in new[] { true, false })
                {
                    float zc = front ? span * 0.5f : -span * 0.5f;
                    float reach = front ? reachF : reachR;
                    foreach (float dz in new[] { -0.0315f, 0.0315f })
                    {
                        Vector3 hole = new Vector3(c.S * (reach + 0.003f), 0f, zc + dz);
                        k.PlanarPrism(new[]
                        {
                            new Vector3(0f, -0.03f, zc + dz), new Vector3(c.S * reach * 0.5f, -0.026f, zc + dz), hole + new Vector3(0f, -0.019f, 0f),
                            hole + new Vector3(c.S * 0.019f, 0f, 0f), hole + new Vector3(0f, 0.019f, 0f), new Vector3(c.S * reach * 0.5f, 0.026f, zc + dz), new Vector3(0f, 0.03f, zc + dz)
                        }, 0.005f);
                    }
                }
            });
            ctx.Part(parent, id, new Txt("Upper-arm bracket", "Yuqori richag kronshteyni", "Pressed plate on the wall with two clevises for the upper arm's pivot bolts.", "Devordagi yuqori richag o‘q boltlari uchun ikki vilkali presslangan plastina."),
                "Pressed steel, e-coated", mesh, new[] { m.BlackSteel }, origin, Quaternion.identity, false, new Vector3(c.S, 0f, 0f), 0.5f);
            Mesh bolt = BoltMesh(0.01f, 0.03f);
            float[] zs = { span * 0.5f + 0.042f, 0f, -span * 0.5f - 0.042f };
            for (int i = 0; i < zs.Length; i++)
                ctx.Part(parent, id + "-bolt-" + (i + 1), new Txt("Bolt M10", "Bolt M10", "Bolts the bracket to a weld nut in the wall.", "Kronshteynni devordagi payvand gaykaga mahkamlaydi."),
                    "Steel 10.9, zinc flake", bolt, new[] { m.ZincSteel }, origin + new Vector3(c.S * 0.003f, 0f, zs[i]), InternalsContext.AlongY(new Vector3(c.S, 0f, 0f)), true);
        }

        // Clevis bracket on the wall for one pivot (axis along z); with cam guides when it carries the toe adjuster.
        private void WallClevis(Transform parent, string id, string en, string uz, string descEn, string descUz, RearCorner c, Vector3 pivot, float wall, float bushingLength, bool cams)
        {
            float wx = c.S * (wall + 0.003f);
            Vector3 origin = new Vector3(wx, pivot.y, pivot.z);
            float half = bushingLength * 0.5f + 0.008f;
            float reach = Mathf.Abs(pivot.x) - wall;
            Mesh mesh = ctx.Mesh("rs-wall-clevis-" + Mathf.RoundToInt(bushingLength * 1000f) + "-" + Mathf.RoundToInt(reach * 1000f) + (cams ? "-cam" : "") + (c.S < 0 ? "-l" : "-r"), k =>
            {
                k.RoundedBox(Vector3.zero, new Vector3(0.006f, 0.066f, half * 2f + 0.03f), 0.003f, 1);
                foreach (float dz in new[] { -half + 0.0025f, half - 0.0025f })
                {
                    Vector3 hole = new Vector3(c.S * (reach + 0.003f), 0f, dz);
                    k.PlanarPrism(new[]
                    {
                        new Vector3(0f, -0.031f, dz), new Vector3(c.S * reach * 0.5f, -0.027f, dz), hole + new Vector3(0f, -0.022f, 0f),
                        hole + new Vector3(c.S * 0.022f, 0f, 0f), hole + new Vector3(0f, 0.022f, 0f), new Vector3(c.S * reach * 0.5f, 0.027f, dz), new Vector3(0f, 0.031f, dz)
                    }, 0.005f);
                    if (cams)
                    {
                        float outz = dz + Mathf.Sign(dz) * 0.004f;
                        foreach (float dy in new[] { -0.027f, 0.027f })
                            k.Box(hole + new Vector3(-c.S * 0.002f, dy, outz - dz), new Vector3(0.036f, 0.003f, 0.004f));
                    }
                }
            });
            ctx.Part(parent, id, new Txt(en, uz, descEn, descUz), "Pressed steel, e-coated", mesh, new[] { m.BlackSteel }, origin, Quaternion.identity, false, new Vector3(c.S, 0f, 0f), 0.5f);
            Mesh bolt = BoltMesh(0.01f, 0.03f);
            foreach (float dy in new[] { -0.022f, 0.022f })
                ctx.Part(parent, id + "-bolt" + (dy < 0 ? "-lower" : "-upper"), new Txt("Bolt M10", "Bolt M10", "Bolts the bracket to a weld nut in the wall.", "Kronshteynni devordagi payvand gaykaga mahkamlaydi."),
                    "Steel 10.9, zinc flake", bolt, new[] { m.ZincSteel }, origin + new Vector3(c.S * 0.003f, dy, half + 0.01f), InternalsContext.AlongY(new Vector3(c.S, 0f, 0f)), true);
        }

        // Clevis on the well's front wall for the trailing link's front bushing (axis along x). The wall is slanted in
        // plan, so the base plate follows the two measured wall points and each ear reaches the wall at its own depth.
        private void TrailBracket(Transform parent, string id, RearCorner c)
        {
            Vector3 p = c.TrailFront;
            float xIn = p.x - c.S * 0.04f, xOut = p.x + c.S * 0.04f;
            float zIn = c.WellFrontIn - 0.003f, zOut = c.WellFrontOut - 0.003f;
            float WallZ(float x) => Mathf.Lerp(zIn, zOut, Mathf.InverseLerp(xIn, xOut, x));
            Mesh mesh = ctx.Mesh("rs-trail-bracket-" + c.Key, k =>
            {
                k.Push(Matrix4x4.Translate(-p));
                k.PlanarPrism(new[]
                {
                    new Vector3(xIn, p.y - 0.033f, zIn), new Vector3(xOut, p.y - 0.033f, zOut), new Vector3(xOut, p.y + 0.033f, zOut), new Vector3(xIn, p.y + 0.033f, zIn)
                }, 0.006f);
                foreach (float dx in new[] { -0.031f, 0.031f })
                {
                    float ex = p.x + dx;
                    float wz = WallZ(ex) - 0.002f;
                    Vector3 hole = new Vector3(ex, p.y, p.z);
                    float mid = (wz + p.z) * 0.5f;
                    k.PlanarPrism(new[]
                    {
                        new Vector3(ex, p.y - 0.031f, wz), new Vector3(ex, p.y - 0.027f, mid), hole + new Vector3(0f, -0.022f, 0f),
                        hole + new Vector3(0f, 0f, -0.022f), hole + new Vector3(0f, 0.022f, 0f), new Vector3(ex, p.y + 0.027f, mid), new Vector3(ex, p.y + 0.031f, wz)
                    }, 0.005f);
                }
                k.Pop();
            });
            ctx.Part(parent, id, new Txt("Trailing-link bracket", "Bo‘ylama richag kronshteyni", "Pressed clevis on the well's front wall that takes the trailing link's thrust into the floor structure.", "Arka old devoridagi, bo‘ylama richag kuchini pol karkasiga uzatuvchi presslangan vilka."),
                "Pressed steel, e-coated", mesh, new[] { m.BlackSteel }, p, Quaternion.identity, false, Vector3.forward, 0.5f);
            Mesh bolt = BoltMesh(0.01f, 0.012f);
            Vector3 wallDir = new Vector3(xOut - xIn, 0f, zOut - zIn).normalized;
            Vector3 normal = Vector3.Cross(Vector3.up, wallDir);
            if (normal.z > 0f) normal = -normal;
            foreach (float dx in new[] { -0.036f, 0.036f })
            {
                float bx = p.x + dx;
                ctx.Part(parent, id + "-bolt" + (dx * c.S < 0 ? "-inner" : "-outer"), new Txt("Bolt M10", "Bolt M10", "Bolts the bracket to a weld nut in the front wall.", "Kronshteynni old devordagi payvand gaykaga mahkamlaydi."),
                    "Steel 10.9, zinc flake", bolt, new[] { m.ZincSteel }, new Vector3(bx, p.y, WallZ(bx) - 0.006f), InternalsContext.AlongY(normal), true);
            }
        }

        // ------------------------------------------------------------------------------------------------ shared parts

        // Rubber-metal bushing (shell, rubber, sleeve) with its pivot bolt along the axis.
        private void Bushing(Transform parent, string id, string en, string uz, Vector3 centre, Vector3 axis, float rOuter, float length, Vector3 explode)
        {
            Transform b = ctx.SubAssembly(parent, id, new Txt(en, uz,
                    "Rubber-metal bushing: lets the link pivot while filtering road noise and setting its compliance.",
                    "Rezina-metall sharnir: richagning burilishiga imkon beradi, yo‘l shovqinini so‘ndiradi va uning elastikligini belgilaydi."),
                "Steel and rubber", explode, 0.6f);
            Quaternion along = InternalsContext.AlongY(axis);
            string size = Mathf.RoundToInt(rOuter * 1000f) + "x" + Mathf.RoundToInt(length * 1000f);
            Mesh shell = ctx.Mesh("rs-bushing-shell-" + size, k => k.Sleeve(rOuter, rOuter - 0.0025f, -length * 0.5f, length * 0.5f, 16));
            Mesh rubber = ctx.Mesh("rs-bushing-rubber-" + size, k =>
            {
                float ri = rOuter * 0.48f;
                k.Lathe(new[]
                {
                    new Vector2(ri, -length * 0.5f - 0.003f), new Vector2(rOuter - 0.0025f, -length * 0.5f - 0.003f), new Vector2(rOuter - 0.0025f, length * 0.5f + 0.003f),
                    new Vector2(ri, length * 0.5f + 0.003f), new Vector2(ri, -length * 0.5f - 0.003f)
                }, 12);
            });
            Mesh sleeve = ctx.Mesh("rs-bushing-sleeve-" + size, k => k.Sleeve(rOuter * 0.48f, 0.0065f, -length * 0.5f - 0.006f, length * 0.5f + 0.006f, 10));
            ctx.Part(b, id + ".shell", new Txt("Outer shell", "Tashqi gilza", "Steel shell pressed into the link eye.", "Richag ko‘ziga presslangan po‘lat gilza."),
                "Steel", shell, new[] { m.ZincSteel }, centre, along);
            ctx.Part(b, id + ".rubber", new Txt("Rubber element", "Rezina element", "Bonded natural-rubber element that twists as the link moves.", "Richag harakatlanganda buraluvchi yopishtirilgan tabiiy kauchuk element."),
                "Natural rubber", rubber, new[] { m.Rubber }, centre, along, false, axis, 0.5f);
            ctx.Part(b, id + ".sleeve", new Txt("Inner sleeve", "Ichki vtulka", "Steel sleeve clamped by the pivot bolt.", "O‘q bolti bilan qisiladigan po‘lat vtulka."),
                "Steel", sleeve, new[] { m.ZincSteel }, centre, along);
            float boltLen = length + 0.04f;
            Mesh bolt = BoltMesh(0.012f, boltLen);
            ctx.Part(b, id + ".bolt", new Txt("Pivot bolt M12", "O‘q bolti M12", "High-tensile flange bolt, property class 10.9, into a weld nut.", "Payvand gaykaga kiruvchi yuqori mustahkamlikdagi flanetsli bolt, 10.9 sinf."),
                "Steel 10.9, zinc flake", bolt, new[] { m.ZincSteel }, centre + axis.normalized * (length * 0.5f + 0.011f), InternalsContext.AlongY(axis), true);
        }

        // Hex flange bolt like Fasteners.HexBolt with fewer segments (fine detail): head above y = 0, shank to -length.
        private static void Bolt(MeshKit k, float d, float length)
        {
            float sf = d * 1.55f, R = sf / (2f * Mathf.Cos(30f * Mathf.Deg2Rad)), head = d * 0.65f, y0 = d * 0.14f, r = d * 0.5f;
            k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(R * 1.08f, 0f), new Vector2(R * 1.08f, y0 * 0.6f), new Vector2(R * 0.9f, y0), new Vector2(0f, y0) }, 10);
            k.Lathe(new[] { new Vector2(0f, y0), new Vector2(R, y0), new Vector2(R, y0 + head * 0.82f), new Vector2(R * 0.86f, y0 + head), new Vector2(0f, y0 + head) }, 6, 40f, 30f, 360f, true);
            k.Lathe(new[] { new Vector2(0f, -length), new Vector2(r * 0.82f, -length), new Vector2(r, -length + d * 0.12f), new Vector2(r, 0.001f), new Vector2(0f, 0.001f) }, 6);
        }

        // Hex nut like Fasteners.HexNut with fewer segments, seated on y = 0.
        private static void Nut(MeshKit k, float d, bool flange)
        {
            float sf = d * 1.55f, R = sf / (2f * Mathf.Cos(30f * Mathf.Deg2Rad)), h = d * 0.8f, ri = d * 0.5f, y0 = 0f;
            if (flange)
            {
                y0 = d * 0.14f;
                k.Lathe(new[] { new Vector2(ri, 0f), new Vector2(R * 1.1f, 0f), new Vector2(R * 1.1f, y0 * 0.6f), new Vector2(R * 0.9f, y0), new Vector2(ri, y0) }, 10);
            }
            k.Lathe(new[] { new Vector2(ri, y0), new Vector2(R, y0), new Vector2(R, y0 + h * 0.85f), new Vector2(R * 0.86f, y0 + h), new Vector2(ri, y0 + h) }, 6, 40f, 30f, 360f, true);
            k.Lathe(new[] { new Vector2(ri, y0 + h), new Vector2(ri, y0) }, 6);
        }

        private Mesh BoltMesh(float d, float length) => ctx.Mesh("rs-bolt-M" + Mathf.RoundToInt(d * 1000f) + "x" + Mathf.RoundToInt(length * 1000f), k => Bolt(k, d, length));
        private Mesh NutMesh(float d, bool flange) => ctx.Mesh("rs-nut-M" + Mathf.RoundToInt(d * 1000f) + (flange ? "-flange" : ""), k => Nut(k, d, flange));

        // Bolt with nut on the far side: both fine detail.
        private void Fastener(Transform parent, string id, Vector3 head, Vector3 direction, float d, float length, string size, string en, string uz, string descEn, string descUz)
        {
            Mesh bolt = BoltMesh(d, length);
            Mesh nut = NutMesh(d, true);
            ctx.Part(parent, id, new Txt(en, uz, descEn, descUz), "Steel 10.9, zinc flake", bolt, new[] { m.ZincSteel }, head, InternalsContext.AlongY(-direction), true);
            ctx.Part(parent, id + "-nut", new Txt("Nut " + size, "Gayka " + size, "Prevailing-torque flange nut.", "O‘z-o‘zidan bo‘shamaydigan flanetsli gayka."),
                "Steel, zinc flake", nut, new[] { m.ZincSteel }, head + direction.normalized * (length - d * 0.9f), InternalsContext.AlongY(direction), true);
        }

        // Ball joint: housing, stud pointing along studDir, boot and self-locking nut.
        private void BallJoint(Transform parent, string id, string en, string uz, Vector3 centre, Vector3 studDir, float r, float stud)
        {
            Transform j = ctx.SubAssembly(parent, id, new Txt(en, uz,
                    "Sealed ball-and-socket joint that lets the hub carrier move up and down and rotate under load.",
                    "Gupchak tayanchiga yuk ostida yuqoriga-pastga harakatlanish va burilishga imkon beruvchi zichlangan sharli bo‘g‘in."),
                "Steel", studDir, 0.7f);
            Quaternion rot = InternalsContext.AlongY(studDir);
            string size = Mathf.RoundToInt(r * 1000f) + "-" + Mathf.RoundToInt(stud * 1000f);
            Mesh housing = ctx.Mesh("rs-ball-joint-housing-" + size, k => k.Lathe(new[]
            {
                new Vector2(0f, -r * 0.9f), new Vector2(r * 0.85f, -r * 0.9f), new Vector2(r, -r * 0.6f), new Vector2(r, r * 0.25f),
                new Vector2(r * 0.7f, r * 0.45f), new Vector2(0f, r * 0.45f)
            }, 12, 35f));
            Mesh boot = ctx.Mesh("rs-ball-joint-boot-" + size, k => k.Lathe(new[]
            {
                new Vector2(r * 0.45f, r * 0.42f), new Vector2(r * 0.95f, r * 0.45f), new Vector2(r * 1.08f, r * 0.8f), new Vector2(r * 0.85f, r * 1.2f),
                new Vector2(r * 0.42f, r * 1.4f), new Vector2(r * 0.4f, r * 1.55f), new Vector2(r * 0.34f, r * 1.55f)
            }, 12, 50f));
            Mesh studMesh = ctx.Mesh("rs-ball-joint-stud-" + size, k =>
            {
                k.Lathe(new[] { new Vector2(0f, -r * 0.55f), new Vector2(r * 0.5f, -r * 0.3f), new Vector2(r * 0.55f, 0f), new Vector2(r * 0.42f, r * 0.4f),
                    new Vector2(r * 0.38f, r * 1.4f), new Vector2(r * 0.3f, stud), new Vector2(r * 0.25f, stud + 0.004f), new Vector2(0f, stud + 0.004f) }, 8, 40f);
            });
            Mesh nut = ctx.Mesh("rs-joint-nut-" + size, k => Nut(k, r * 0.62f, false));
            ctx.Part(j, id + ".housing", new Txt("Joint housing", "Tayanch korpusi", "Forged housing pressed into the link; holds the ball seat.", "Richagga presslangan bolg‘alangan korpus; shar o‘rindig‘ini ushlaydi."),
                "Forged steel", housing, new[] { m.ForgedSteel }, centre, rot);
            ctx.Part(j, id + ".stud", new Txt("Ball stud", "Sharli barmoq", "Hardened ball with a tapered stud locked into the hub carrier.", "Gupchak tayanchiga konus bilan mahkamlanadigan toblangan sharli barmoq."),
                "Case-hardened steel", studMesh, new[] { m.MachinedSteel }, centre, rot, false, studDir, 0.5f);
            ctx.Part(j, id + ".boot", new Txt("Dust boot", "Chang g‘ilofi", "Grease-filled rubber boot that keeps dirt out of the joint.", "Bo‘g‘inni changdan saqlovchi moy to‘ldirilgan rezina g‘ilof."),
                "Chloroprene rubber", boot, new[] { m.Rubber }, centre, rot);
            ctx.Part(j, id + ".nut", new Txt("Lock nut", "Qulflovchi gayka", "Self-locking nut on the stud's thread.", "Barmoq rezbasidagi o‘z-o‘zidan qulflanadigan gayka."),
                "Steel", nut, new[] { m.ZincSteel }, centre + studDir.normalized * (stud - 0.012f), rot, true);
        }
    }
}
#endif
