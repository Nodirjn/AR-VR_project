#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace CarInternals
{
    /// <summary>
    /// Front suspension: two double-wishbone corners (arms with bushings and ball joints, knuckle, hub and bearing,
    /// coilover with top mount), the anti-roll bar with drop links and the subframe that carries the arm pivots.
    /// Reference subsystem for the next builders: every part is posed from FrontChassisGeometry, which comes from the
    /// measured layout.
    /// </summary>
    [CarInternalsSubsystem("Suspension", 10)]
    public sealed class FrontSuspension : ICarInternalsSubsystem
    {
        private InternalsContext ctx;
        private CarPalette m;
        private FrontChassisGeometry g;

        public void Build(InternalsContext context)
        {
            ctx = context;
            m = ctx.Mats;
            g = FrontChassisGeometry.Of(ctx.Layout, ctx.Probe);
            Transform system = ctx.System("Suspension");
            foreach (FrontCorner c in new[] { g.Left, g.Right })
                ctx.Note("front " + c.Side.ToLowerInvariant() + ": LBJ " + CarLayout.V(c.Lbj) + " UBJ " + CarLayout.V(c.Ubj) + " LCA pivots " + CarLayout.V(c.LcaFront) + " " + CarLayout.V(c.LcaRear) +
                         " UCA pivots " + CarLayout.V(c.UcaFront) + " " + CarLayout.V(c.UcaRear) + " coil " + CarLayout.V(c.CoilBottom) + ".." + CarLayout.V(c.CoilTop) + " TRE " + CarLayout.V(c.Tre));
            ctx.Note("front chassis: rack y " + CarLayout.F(g.RackY) + " z " + CarLayout.F(g.RackZ) + " travel " + CarLayout.F(g.RackTravel) + ", anti-roll bar y " + CarLayout.F(g.ArbY) + " z " + CarLayout.F(g.ArbZ) +
                     ", tie bar y " + CarLayout.F(g.TieBarY) + ", pinion x " + CarLayout.F(g.PinionX));
            Corner(system, g.Left);
            Corner(system, g.Right);
            AntiRollBar(system);
            Subframe(system);
            // Joints by design: the drive shaft (CarConcept/Axles) through the knuckle and hub, the caliper on its
            // carrier, brackets on the wheel-well walls, links through the walls.
            ctx.AllowContact("suspension.front-left.knuckle", "Axles");
            ctx.AllowContact("suspension.front-right.knuckle", "Axles");
            ctx.AllowContact("suspension.front-left.knuckle.caliper-carrier", "WheelFrontL/WheelFrontLBrakePad");
            ctx.AllowContact("suspension.front-right.knuckle.caliper-carrier", "WheelFrontR/WheelFrontRBrakePad");
            ctx.AllowContact("suspension.front-subframe", "InteriorFloor");
            ctx.AllowContact("suspension.front-subframe", "CarConcept");
            ctx.AllowContact("suspension.front-anti-roll-bar", "InteriorFloor");
            ctx.AllowContact("suspension.front-anti-roll-bar", "CarConcept");
            // The glTF's lower dash shell (InteriorDashMid / InteriorDashSides) reaches forward to the engine through
            // the bulkhead zone; the bar and the tie bar sit at the bulkhead inside it (never in the engine).
            ctx.AllowContact("suspension.front-anti-roll-bar", "InteriorDash");
            ctx.AllowContact("suspension.front-subframe.tie-bar", "InteriorDash");
            ctx.AllowContact("suspension.front-left.coilover.top-mount", "InteriorFloor");
            ctx.AllowContact("suspension.front-right.coilover.top-mount", "InteriorFloor");
            ctx.AllowContact("suspension.front-left.coilover.top-mount", "CarConcept");
            ctx.AllowContact("suspension.front-right.coilover.top-mount", "CarConcept");
            ctx.AllowContact("suspension.front-subframe", "suspension.front-left");
            ctx.AllowContact("suspension.front-subframe", "suspension.front-right");
            ctx.AllowContact("suspension.front-anti-roll-bar.link", "suspension.front-");
        }

        // ------------------------------------------------------------------------------------------------ corner

        private void Corner(Transform system, FrontCorner c)
        {
            string id = "suspension." + c.Key;
            string side = c.Side.ToLowerInvariant();
            Transform corner = ctx.Assembly(system, id, new Txt("Front " + c.Side + " Suspension Corner", "Old " + c.SideUz + " osma bloki",
                    "Double-wishbone corner: two control arms, knuckle, hub and coilover that locate and spring the front " + side + " wheel.",
                    "Ikki richagli osma: old " + c.SideUz + " g‘ildirakni ushlab turuvchi va amortizatsiya qiluvchi richaglar, burilish mushti, gupchak va prujinali amortizator."),
                "Steel and aluminium", new Vector3(c.S, 0f, 0f), 1.2f);
            LowerArm(corner, c, id);
            UpperArm(corner, c, id);
            Knuckle(corner, c, id);
            Coilover(corner, c, id);
        }

        private void LowerArm(Transform corner, FrontCorner c, string id)
        {
            Transform arm = ctx.SubAssembly(corner, id + ".lower-arm", new Txt("Lower Control Arm", "Pastki richag",
                    "Forged A-arm that carries the wheel's vertical and braking loads from the ball joint to two body bushings.",
                    "G‘ildirakning vertikal va tormozlash kuchlarini sharli tayanchdan ikki saylentblokka uzatuvchi bolg‘alangan A-simon richag."),
                "Forged aluminium", Vector3.down);
            Vector3 o = c.Lbj;
            Mesh mesh = ctx.Mesh("lower-arm-" + c.Key, k =>
            {
                k.Push(Matrix4x4.Translate(-o));
                foreach (bool front in new[] { true, false })
                {
                    Vector3 eye = front ? c.LcaFront : c.LcaRear;
                    Vector3 start = eye + (c.Lbj - eye).normalized * 0.034f;
                    Vector3 end = c.Lbj + (eye - c.Lbj).normalized * 0.03f;
                    Vector3 mid = Vector3.Lerp(start, end, 0.5f) + Vector3.up * 0.006f;
                    k.Tube(MeshKit.CatmullRom(new[] { start, mid, end }, 6), 0.019f, 12, true, true, Vector3.up, null, new Vector2(1.15f, 0.72f));
                    k.Push(eye, InternalsContext.AlongY(Vector3.forward));
                    k.Sleeve(0.036f, 0.031f, -0.028f, 0.028f, 18);
                    k.Pop();
                }
                // Web between the legs near the ball joint and the ball-joint boss.
                Vector3 wf = c.LcaLeg(true, 0.42f), wr = c.LcaLeg(false, 0.42f);
                k.PlanarPrism(new[] { c.Lbj + Vector3.up * 0.002f, wf + Vector3.up * 0.002f, wr + Vector3.up * 0.002f }, 0.009f);
                k.Push(c.Lbj, Quaternion.identity);
                k.Lathe(new[] { new Vector2(0.026f, -0.022f), new Vector2(0.035f, -0.022f), new Vector2(0.037f, -0.015f), new Vector2(0.037f, 0.016f), new Vector2(0.034f, 0.02f), new Vector2(0.026f, 0.02f), new Vector2(0.026f, -0.022f) }, 20);
                k.Pop();
                // Clevis for the coilover's lower eye on the rear leg.
                Vector3 cb = c.CoilBottom;
                foreach (float dz in new[] { -0.024f, 0.024f })
                {
                    float x0 = cb.x - c.S * 0.022f, x1 = cb.x + c.S * 0.022f;
                    float y0 = c.LcaLeg(false, Mathf.InverseLerp(Mathf.Abs(c.Lbj.x), Mathf.Abs(c.LcaRear.x), Mathf.Abs(cb.x))).y;
                    k.PlanarPrism(new[]
                    {
                        new Vector3(x0, y0, cb.z + dz), new Vector3(x1, y0, cb.z + dz), new Vector3(x1, cb.y + 0.006f, cb.z + dz),
                        new Vector3(cb.x, cb.y + 0.02f, cb.z + dz), new Vector3(x0, cb.y + 0.006f, cb.z + dz)
                    }, 0.006f);
                }
                // Tab for the anti-roll bar drop link on the front leg (stud along x).
                Vector3 ll = c.LinkLow + new Vector3(c.S * 0.014f, 0f, 0f);
                k.PlanarPrism(new[]
                {
                    new Vector3(ll.x, ll.y - 0.034f, ll.z - 0.02f), new Vector3(ll.x, ll.y - 0.034f, ll.z + 0.02f),
                    new Vector3(ll.x, ll.y + 0.012f, ll.z + 0.014f), new Vector3(ll.x, ll.y + 0.012f, ll.z - 0.014f)
                }, 0.006f);
                k.Pop();
            });
            ctx.Part(arm, id + ".lower-arm.arm", new Txt("Lower arm forging", "Pastki richag (bolg‘alangan)",
                    "I-section forged aluminium arm with the bushing eyes, ball-joint boss and coilover clevis.",
                    "Saylentblok ko‘zlari, sharli tayanch uyasi va amortizator kronshteyni bo‘lgan I-kesimli bolg‘alangan alyuminiy richag."),
                "Forged aluminium 6082-T6", mesh, new[] { m.CastAluminium }, o, Quaternion.identity);
            Bushing(arm, id + ".lower-arm.front-bushing", "Front", c.LcaFront, 0.031f, 0.056f, c.S);
            Bushing(arm, id + ".lower-arm.rear-bushing", "Rear", c.LcaRear, 0.031f, 0.056f, c.S);
            BallJoint(arm, id + ".lower-arm.ball-joint", "Lower Ball Joint", "Pastki sharli tayanch", c.Lbj, Vector3.up, 0.024f, 0.075f);
        }

        private void UpperArm(Transform corner, FrontCorner c, string id)
        {
            Transform arm = ctx.SubAssembly(corner, id + ".upper-arm", new Txt("Upper Control Arm", "Yuqori richag",
                    "Short upper wishbone that sets camber and caster; the coilover passes between its legs.",
                    "Razval va kasterni belgilovchi qisqa yuqori richag; prujinali amortizator uning oyoqlari orasidan o‘tadi."),
                "Forged steel", Vector3.up);
            Vector3 o = c.Ubj;
            Mesh mesh = ctx.Mesh("upper-arm-" + c.Key, k =>
            {
                k.Push(Matrix4x4.Translate(-o));
                foreach (bool front in new[] { true, false })
                {
                    Vector3 eye = front ? c.UcaFront : c.UcaRear;
                    Vector3 start = eye + (c.Ubj - eye).normalized * 0.03f;
                    Vector3 end = c.Ubj + (eye - c.Ubj).normalized * 0.026f;
                    Vector3 mid = Vector3.Lerp(start, end, 0.5f) + Vector3.up * 0.01f;
                    k.Tube(MeshKit.CatmullRom(new[] { start, mid, end }, 6), 0.0145f, 12, true, true, Vector3.up, null, new Vector2(1.1f, 0.85f));
                    k.Push(eye, InternalsContext.AlongY(Vector3.forward));
                    k.Sleeve(0.03f, 0.026f, -0.024f, 0.024f, 16);
                    k.Pop();
                }
                k.PlanarPrism(new[] { c.Ubj, Vector3.Lerp(c.Ubj, c.UcaFront, 0.35f), Vector3.Lerp(c.Ubj, c.UcaRear, 0.28f) }, 0.006f);
                k.Push(c.Ubj, Quaternion.identity);
                k.Lathe(new[] { new Vector2(0.022f, -0.018f), new Vector2(0.031f, -0.018f), new Vector2(0.032f, 0.016f), new Vector2(0.022f, 0.016f), new Vector2(0.022f, -0.018f) }, 18);
                k.Pop();
                k.Pop();
            });
            ctx.Part(arm, id + ".upper-arm.arm", new Txt("Upper arm", "Yuqori richag korpusi",
                    "Forged steel wishbone with pressed-in bushing eyes and the ball-joint boss.",
                    "Presslangan saylentblok ko‘zlari va sharli tayanch uyasi bo‘lgan bolg‘alangan po‘lat richag."),
                "Forged steel", mesh, new[] { m.BlackSteel }, o, Quaternion.identity);
            Bushing(arm, id + ".upper-arm.front-bushing", "Front", c.UcaFront, 0.026f, 0.046f, c.S);
            Bushing(arm, id + ".upper-arm.rear-bushing", "Rear", c.UcaRear, 0.026f, 0.046f, c.S);
            BallJoint(arm, id + ".upper-arm.ball-joint", "Upper Ball Joint", "Yuqori sharli tayanch", c.Ubj, Vector3.down, 0.021f, 0.07f);
        }

        // Rubber-metal bushing in an arm eye (axis along z) with its pivot bolt and nut.
        private void Bushing(Transform arm, string id, string which, Vector3 centre, float rOuter, float length, float s)
        {
            Transform b = ctx.SubAssembly(arm, id, new Txt(which + " Pivot Bushing", (which == "Front" ? "Old" : "Orqa") + " saylentblok",
                    "Rubber-metal bushing: lets the arm pivot while filtering road noise.",
                    "Rezina-metall sharnir: richagning burilishiga imkon beradi va yo‘l shovqinini so‘ndiradi."),
                "Steel and rubber", new Vector3(0f, 0f, which == "Front" ? 1f : -1f), 0.6f);
            Quaternion along = InternalsContext.AlongY(Vector3.forward);
            string size = Mathf.RoundToInt(rOuter * 1000f) + "x" + Mathf.RoundToInt(length * 1000f);
            Mesh shell = ctx.Mesh("bushing-shell-" + size, k => k.Sleeve(rOuter, rOuter - 0.0025f, -length * 0.5f, length * 0.5f, 18));
            Mesh rubber = ctx.Mesh("bushing-rubber-" + size, k =>
            {
                float ri = rOuter * 0.48f;
                k.Lathe(new[]
                {
                    new Vector2(ri, -length * 0.5f - 0.003f), new Vector2(rOuter - 0.0025f, -length * 0.5f - 0.003f), new Vector2(rOuter - 0.0025f, length * 0.5f + 0.003f),
                    new Vector2(ri, length * 0.5f + 0.003f), new Vector2(ri, -length * 0.5f - 0.003f)
                }, 18);
            });
            Mesh sleeve = ctx.Mesh("bushing-sleeve-" + size, k => k.Sleeve(rOuter * 0.48f, 0.0065f, -length * 0.5f - 0.006f, length * 0.5f + 0.006f, 14));
            ctx.Part(b, id + ".shell", new Txt("Outer shell", "Tashqi gilza", "Steel shell pressed into the arm eye.", "Richag ko‘ziga presslangan po‘lat gilza."),
                "Steel", shell, new[] { m.ZincSteel }, centre, along);
            ctx.Part(b, id + ".rubber", new Txt("Rubber element", "Rezina element", "Bonded natural-rubber element that twists as the arm moves.", "Richag harakatlanganda buraluvchi yopishtirilgan tabiiy kauchuk element."),
                "Natural rubber", rubber, new[] { m.Rubber }, centre, along, false, null, 0.5f);
            ctx.Part(b, id + ".sleeve", new Txt("Inner sleeve", "Ichki vtulka", "Steel sleeve clamped by the pivot bolt.", "Bolt bilan qisiladigan po‘lat vtulka."),
                "Steel", sleeve, new[] { m.ZincSteel }, centre, along);
            Fastener(b, id + ".bolt", centre + Vector3.forward * (length * 0.5f + 0.012f), Vector3.back, 0.012f, length + 0.05f, "M12");
        }

        // Bolt with nut on the far side: both fine detail.
        private void Fastener(Transform parent, string id, Vector3 head, Vector3 direction, float d, float length, string size)
        {
            Quaternion rot = InternalsContext.AlongY(-direction);
            Mesh bolt = ctx.Mesh("bolt-" + size + "x" + Mathf.RoundToInt(length * 1000f), k => Fasteners.HexBolt(k, d, length));
            Mesh nut = ctx.Mesh("nut-" + size, k => Fasteners.HexNut(k, d, true));
            ctx.Part(parent, id, new Txt("Bolt " + size, "Bolt " + size, "High-tensile flange bolt, property class 10.9.", "Yuqori mustahkamlikdagi flanetsli bolt, 10.9 sinf."),
                "Steel 10.9, zinc flake", bolt, new[] { m.ZincSteel }, head, rot, true);
            ctx.Part(parent, id + "-nut", new Txt("Nut " + size, "Gayka " + size, "Prevailing-torque flange nut.", "O‘z-o‘zidan bo‘shamaydigan flanetsli gayka."),
                "Steel, zinc flake", nut, new[] { m.ZincSteel }, head + direction.normalized * (length - d * 0.9f), InternalsContext.AlongY(direction), true);
        }

        // Ball joint: housing, stud pointing along studDir, boot, castle nut and split pin.
        private void BallJoint(Transform parent, string id, string en, string uz, Vector3 centre, Vector3 studDir, float r, float stud)
        {
            Transform j = ctx.SubAssembly(parent, id, new Txt(en, uz,
                    "Sealed ball-and-socket joint that lets the knuckle steer and move up and down.",
                    "Burilish mushtiga burilish va yuqoriga-pastga harakatlanishga imkon beruvchi zichlangan sharli bo‘g‘in."),
                "Steel", studDir, 0.7f);
            Quaternion rot = InternalsContext.AlongY(studDir);
            string size = Mathf.RoundToInt(r * 1000f) + "-" + Mathf.RoundToInt(stud * 1000f);
            Mesh housing = ctx.Mesh("ball-joint-housing-" + size, k => k.Lathe(new[]
            {
                new Vector2(0f, -r * 0.9f), new Vector2(r * 0.85f, -r * 0.9f), new Vector2(r, -r * 0.6f), new Vector2(r, r * 0.25f),
                new Vector2(r * 0.7f, r * 0.45f), new Vector2(0f, r * 0.45f)
            }, 18, 35f));
            Mesh boot = ctx.Mesh("ball-joint-boot-" + size, k => k.Lathe(new[]
            {
                new Vector2(r * 0.45f, r * 0.42f), new Vector2(r * 0.95f, r * 0.45f), new Vector2(r * 1.08f, r * 0.8f), new Vector2(r * 0.85f, r * 1.2f),
                new Vector2(r * 0.42f, r * 1.4f), new Vector2(r * 0.4f, r * 1.55f), new Vector2(r * 0.34f, r * 1.55f)
            }, 18, 50f));
            Mesh studMesh = ctx.Mesh("ball-joint-stud-" + size, k =>
            {
                k.Lathe(new[] { new Vector2(0f, -r * 0.55f), new Vector2(r * 0.5f, -r * 0.3f), new Vector2(r * 0.55f, 0f), new Vector2(r * 0.42f, r * 0.4f),
                    new Vector2(r * 0.38f, r * 1.4f), new Vector2(r * 0.3f, stud), new Vector2(r * 0.25f, stud + 0.004f), new Vector2(0f, stud + 0.004f) }, 14, 40f);
            });
            Mesh nut = ctx.Mesh("castle-nut-" + size, k =>
            {
                Fasteners.HexNut(k, r * 0.62f);
            });
            ctx.Part(j, id + ".housing", new Txt("Joint housing", "Tayanch korpusi", "Forged housing pressed into the arm; holds the ball seat.", "Richagga presslangan bolg‘alangan korpus; shar o‘rindig‘ini ushlaydi."),
                "Forged steel", housing, new[] { m.ForgedSteel }, centre, rot);
            ctx.Part(j, id + ".stud", new Txt("Ball stud", "Sharli barmoq", "Hardened ball with a tapered stud locked into the knuckle.", "Burilish mushtiga konus bilan mahkamlanadigan toblangan sharli barmoq."),
                "Case-hardened steel", studMesh, new[] { m.MachinedSteel }, centre, rot, false, studDir, 0.5f);
            ctx.Part(j, id + ".boot", new Txt("Dust boot", "Chang g‘ilofi", "Grease-filled rubber boot that keeps dirt out of the joint.", "Bo‘g‘inni changdan saqlovchi moy to‘ldirilgan rezina g‘ilof."),
                "Chloroprene rubber", boot, new[] { m.Rubber }, centre, rot);
            ctx.Part(j, id + ".nut", new Txt("Castle nut", "Tojsimon gayka", "Slotted nut on the stud, secured by a split pin.", "Barmoqdagi kesikli gayka, shplint bilan qulflanadi."),
                "Steel", nut, new[] { m.ZincSteel }, centre + studDir.normalized * (stud - 0.012f), rot, true);
            Mesh pin = ctx.Mesh("split-pin", k => k.Tube(MeshKit.Bends(new[] { new Vector3(-0.012f, 0f, 0f), new Vector3(0.012f, 0f, 0f), new Vector3(0.012f, -0.006f, 0f), new Vector3(0.004f, -0.006f, 0f) }, 0.003f), 0.0012f, 6));
            ctx.Part(j, id + ".split-pin", new Txt("Split pin", "Shplint", "Locks the castle nut.", "Tojsimon gaykani qulflaydi."),
                "Steel", pin, new[] { m.ZincSteel }, centre + studDir.normalized * (stud - 0.004f), rot, true);
        }

        // ------------------------------------------------------------------------------------------------ knuckle and hub

        private void Knuckle(Transform corner, FrontCorner c, string id)
        {
            WheelInfo w = c.W;
            Transform group = ctx.SubAssembly(corner, id + ".knuckle", new Txt("Steering Knuckle and Hub", "Burilish mushti va gupchak",
                    "Upright that carries the wheel bearing and hub, the brake caliper and the steering arm; turns about the ball joints.",
                    "G‘ildirak podshipnigi va gupchak, tormoz supporti va burilish richagini ko‘taruvchi tirgak; sharli tayanchlar atrofida buriladi."),
                "Cast iron", new Vector3(c.S, 0f, 0f), 1.1f);
            Vector3 C = w.Centre, ax = w.Outward, f = w.Rolling;
            float a = c.HubFace;
            Vector3 hubPoint = C + ax * (a - 0.058f);
            Mesh knuckle = ctx.Mesh("knuckle-" + c.Key, k =>
            {
                k.Push(Matrix4x4.Translate(-C));
                // Bearing boss.
                k.Push(C, InternalsContext.AlongY(ax));
                k.Lathe(new[] { new Vector2(0.044f, a - 0.088f), new Vector2(0.066f, a - 0.088f), new Vector2(0.071f, a - 0.078f), new Vector2(0.071f, a - 0.04f),
                    new Vector2(0.06f, a - 0.03f), new Vector2(0.044f, a - 0.03f), new Vector2(0.044f, a - 0.088f) }, 24);
                k.Pop();
                // Lower leg to the lower ball joint's eye.
                Vector3 lowEye = c.Lbj + Vector3.up * 0.042f;
                Vector3 lowStart = hubPoint - Vector3.up * 0.055f;
                Vector3 lowMid = Vector3.Lerp(lowStart, lowEye, 0.5f) - ax * 0.02f;
                k.Tube(MeshKit.CatmullRom(new[] { lowStart, lowMid, lowEye }, 6), 0.021f, 12, true, true, f, x => Mathf.Lerp(1.1f, 0.85f, x));
                k.Push(lowEye, Quaternion.identity);
                k.Lathe(new[] { new Vector2(0.008f, -0.012f), new Vector2(0.023f, -0.012f), new Vector2(0.025f, 0.014f), new Vector2(0.008f, 0.014f), new Vector2(0.008f, -0.012f) }, 16);
                k.Pop();
                // Gooseneck up and inboard to the upper ball joint's eye, clear of the caliper.
                Vector3 upEye = c.Ubj - Vector3.up * 0.04f;
                Vector3 upStart = C + ax * (a - 0.084f) + Vector3.up * 0.058f;
                Vector3 upMid = Vector3.Lerp(upStart, upEye, 0.45f) - ax * 0.05f;
                k.Tube(MeshKit.CatmullRom(new[] { upStart, upMid, upEye }, 8), 0.019f, 12, true, true, f, x => Mathf.Lerp(1.15f, 0.8f, x));
                k.Push(upEye, Quaternion.identity);
                k.Lathe(new[] { new Vector2(0.007f, -0.012f), new Vector2(0.021f, -0.012f), new Vector2(0.022f, 0.012f), new Vector2(0.007f, 0.012f), new Vector2(0.007f, -0.012f) }, 16);
                k.Pop();
                // Steering arm forward to the tie-rod end.
                Vector3 treEye = c.Tre + Vector3.up * 0.026f;
                Vector3 armStart = hubPoint + f * 0.045f - Vector3.up * 0.03f;
                k.Tube(MeshKit.CatmullRom(new[] { armStart, Vector3.Lerp(armStart, treEye, 0.5f) - ax * 0.006f, treEye }, 6), 0.016f, 10, true, true, Vector3.up, x => Mathf.Lerp(1.15f, 0.9f, x), new Vector2(1.3f, 0.8f));
                k.Push(treEye, Quaternion.identity);
                k.Lathe(new[] { new Vector2(0.006f, -0.01f), new Vector2(0.018f, -0.01f), new Vector2(0.019f, 0.01f), new Vector2(0.006f, 0.01f), new Vector2(0.006f, -0.01f) }, 14);
                k.Pop();
                k.Pop();
            });
            ctx.Part(group, id + ".knuckle.upright", new Txt("Steering knuckle", "Burilish mushti",
                    "Ductile-iron upright: bearing boss, ball-joint eyes and steering arm in one casting.",
                    "Egiluvchan cho‘yan tirgak: podshipnik uyasi, sharli tayanch ko‘zlari va burilish richagi bitta quymada."),
                "Ductile iron EN-GJS-500", knuckle, new[] { m.CastIron }, C, Quaternion.identity);
            // Caliper carrier: two arms from the bearing boss to the caliper's mounting bolts, inboard of the disc.
            Mesh carrier = ctx.Mesh("caliper-carrier-" + c.Key, k =>
            {
                k.Push(Matrix4x4.Translate(-C));
                float lugAxial = Mathf.Min(w.DiscAxialMin, a - 0.03f) - 0.022f;
                foreach (float da in new[] { -24f, 24f })
                {
                    float ang = (w.CaliperAngle + da) * Mathf.Deg2Rad;
                    Vector3 radial = f * Mathf.Cos(ang) + Vector3.up * Mathf.Sin(ang);
                    Vector3 p0 = C + ax * lugAxial + radial * 0.064f, p1 = C + ax * lugAxial + radial * Mathf.Max(0.1f, w.CaliperRadiusMin + 0.008f);
                    k.Tube(new[] { p0, p1 }, 0.011f, 8, true, true, ax, null, new Vector2(0.8f, 1.3f));
                    k.Push(p1, InternalsContext.AlongY(ax));
                    k.Cylinder(0.012f, -0.009f, 0.009f, 12);
                    k.Pop();
                }
                k.Pop();
            });
            ctx.Part(group, id + ".knuckle.caliper-carrier", new Txt("Caliper carrier", "Support kronshteyni",
                    "Bolted bracket that holds the brake caliper over the disc and takes the braking torque.",
                    "Tormoz supportini disk ustida ushlab turuvchi va tormoz momentini qabul qiluvchi boltli kronshteyn."),
                "Ductile iron", carrier, new[] { m.CastIron }, C, Quaternion.identity);

            Transform hub = ctx.SubAssembly(group, id + ".knuckle.hub", new Txt("Hub and Wheel Bearing", "Gupchak va g‘ildirak podshipnigi",
                    "Hub flange with five studs on a sealed double-row bearing unit; the drive shaft splines into it.",
                    "Zichlangan ikki qatorli podshipnikdagi beshta shpilkali gupchak flanetsi; uzatma vali unga shlitsa bilan kiradi."),
                "Steel", ax, 0.8f);
            Quaternion onAxle = InternalsContext.AlongY(ax);
            Vector3 face = C + ax * a;
            Mesh hubMesh = ctx.Mesh("hub-flange", k =>
            {
                k.Lathe(new[] { new Vector2(0f, -0.07f), new Vector2(0.027f, -0.07f), new Vector2(0.027f, -0.016f), new Vector2(0.05f, -0.014f), new Vector2(0.08f, -0.012f),
                    new Vector2(0.083f, -0.008f), new Vector2(0.083f, 0f), new Vector2(0.037f, 0f), new Vector2(0.036f, 0.013f), new Vector2(0.0f, 0.013f) }, 32, 35f);
            });
            ctx.Part(hub, id + ".knuckle.hub.flange", new Txt("Wheel hub", "G‘ildirak gupchagi", "Forged hub flange; the disc and the wheel bolt to it.", "Bolg‘alangan gupchak flanetsi; disk va g‘ildirak unga mahkamlanadi."),
                "Forged steel", hubMesh, new[] { m.MachinedSteel }, face, onAxle, false, ax, 0.6f);
            Mesh bearing = ctx.Mesh("wheel-bearing", k =>
            {
                k.Sleeve(0.044f, 0.028f, -0.078f, -0.02f, 28);
                k.Lathe(new[] { new Vector2(0.044f, -0.034f), new Vector2(0.058f, -0.034f), new Vector2(0.058f, -0.026f), new Vector2(0.044f, -0.026f) }, 28);
            });
            ctx.Part(hub, id + ".knuckle.hub.bearing", new Txt("Wheel bearing unit", "G‘ildirak podshipnigi bloki", "Sealed double-row angular-contact ball bearing, greased for life.", "Butun xizmat muddatiga moylangan zichlangan ikki qatorli radial-tirak sharikli podshipnik."),
                "Bearing steel 100Cr6", bearing, new[] { m.MachinedSteel }, face, onAxle);
            Mesh stud = ctx.Mesh("wheel-stud-M14", k =>
            {
                k.Lathe(new[] { new Vector2(0f, -0.016f), new Vector2(0.011f, -0.016f), new Vector2(0.011f, -0.012f), new Vector2(0.0085f, -0.012f), new Vector2(0.0085f, 0f),
                    new Vector2(0.007f, 0.001f), new Vector2(0.007f, 0.04f), new Vector2(0.0055f, 0.043f), new Vector2(0f, 0.043f) }, 10);
            });
            Vector3 r0 = Vector3.ProjectOnPlane(Vector3.up, ax).normalized, r1 = Vector3.Cross(ax, r0);
            for (int i = 0; i < 5; i++)
            {
                float ang = Mathf.PI * 2f * i / 5f;
                Vector3 p = face + (r0 * Mathf.Cos(ang) + r1 * Mathf.Sin(ang)) * 0.0572f;
                ctx.Part(hub, id + ".knuckle.hub.stud-" + (i + 1), new Txt("Wheel stud M14x1.5", "G‘ildirak shpilkasi M14x1.5", "Knurled stud pressed into the hub; carries a wheel nut.", "Gupchakka presslangan rifelli shpilka; g‘ildirak gaykasi buraladi."),
                    "Steel 10.9", stud, new[] { m.ZincSteel }, p, onAxle, true, ax, 0.4f);
            }
            Mesh axleNut = ctx.Mesh("nut-M24", k => Fasteners.HexNut(k, 0.024f, true));
            ctx.Part(hub, id + ".knuckle.hub.axle-nut", new Txt("Axle nut M24", "Val gaykasi M24", "Staked nut that preloads the bearing on the drive shaft.", "Podshipnikni uzatma valiga tortib turuvchi zarblangan gayka."),
                "Steel", axleNut, new[] { m.ZincSteel }, face + ax * 0.013f, onAxle, true);
            // Wheel-speed sensor on the knuckle, reading the tone ring behind the hub.
            Vector3 sensorAt = C + ax * (a - 0.06f) + (f * -0.7f + Vector3.up * -0.7f).normalized * 0.078f;
            Vector3 sensorDir = (C + ax * (a - 0.06f) - sensorAt).normalized;
            Mesh sensor = ctx.Mesh("abs-sensor", k =>
            {
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.007f, 0f), new Vector2(0.007f, 0.03f), new Vector2(0.011f, 0.03f), new Vector2(0.011f, 0.038f), new Vector2(0.006f, 0.05f), new Vector2(0f, 0.05f) }, 12);
                k.Box(new Vector3(0.012f, 0.034f, 0f), new Vector3(0.018f, 0.004f, 0.014f));
            });
            ctx.Part(group, id + ".knuckle.abs-sensor", new Txt("Wheel-speed sensor", "G‘ildirak tezligi datchigi", "Hall-effect sensor for ABS and stability control.", "ABS va barqarorlik tizimi uchun Xoll datchigi."),
                "Plastic and steel", sensor, new[] { m.BlackPlastic }, sensorAt, InternalsContext.AlongY(-sensorDir), true);
        }

        // ------------------------------------------------------------------------------------------------ coilover

        private void Coilover(Transform corner, FrontCorner c, string id)
        {
            Vector3 bottom = c.CoilBottom, top = c.CoilTop;
            Vector3 axis = top - bottom;
            float L = axis.magnitude;
            Quaternion rot = InternalsContext.AlongY(axis, Vector3.forward);
            Vector3 up = axis / L;
            // Heights along the axis: lower spring seat above the drive shaft, upper seat under the top mount.
            // Lower spring seat just clear of the drive shaft (the Axles bar, round, on the axle line).
            Bounds bar = ctx.Layout.AxleBarFront;
            float barRadius = bar.size != Vector3.zero ? bar.extents.y : 0.09f;
            float edge = Mathf.Abs(bottom.z - c.W.Centre.z) - FrontChassisGeometry.CoilRadius - FrontChassisGeometry.CoilWire * 0.5f - 0.004f;
            float clearY = edge < barRadius ? c.W.Centre.y + Mathf.Sqrt(barRadius * barRadius - edge * edge) + 0.012f : bottom.y + 0.1f;
            float seatLow = Mathf.Max(L * 0.3f, (clearY - bottom.y) / Mathf.Max(0.2f, up.y));
            float seatHigh = L - 0.05f;
            float body = seatLow + 0.07f;
            Transform coil = ctx.SubAssembly(corner, id + ".coilover", new Txt("Coilover", "Prujinali amortizator",
                    "Coil spring around a monotube damper: carries the car's weight and controls body and wheel motion.",
                    "Monotrubali amortizator atrofidagi prujina: avtomobil og‘irligini ko‘taradi va kuzov hamda g‘ildirak harakatini boshqaradi."),
                "Steel", Vector3.up, 1.3f);
            string key = "-" + Mathf.RoundToInt(L * 1000f);
            Transform damper = ctx.SubAssembly(coil, id + ".coilover.damper", new Txt("Damper", "Amortizator",
                    "Gas-charged monotube damper; the piston forces oil through valves to absorb motion.",
                    "Gaz to‘ldirilgan monotrubali amortizator; porshen moyni klapanlar orqali haydab harakatni so‘ndiradi."),
                "Steel", Vector3.down, 0.6f);
            Mesh bodyMesh = ctx.Mesh("damper-body" + key, k =>
            {
                k.Lathe(new[] { new Vector2(0f, 0.012f), new Vector2(0.018f, 0.012f), new Vector2(0.025f, 0.032f), new Vector2(0.025f, body - 0.012f), new Vector2(0.021f, body),
                    new Vector2(0.012f, body), new Vector2(0.012f, body + 0.006f), new Vector2(0f, body + 0.006f) }, 22, 35f);
                k.Sleeve(0.0275f, 0.025f, seatLow - 0.09f, seatLow + 0.01f, 22);
                k.Push(Vector3.zero, InternalsContext.AlongY(Vector3.forward));
                k.Sleeve(0.016f, 0.0065f, -0.017f, 0.017f, 16);
                k.Pop();
            });
            ctx.Part(damper, id + ".coilover.damper.body", new Txt("Damper body", "Amortizator korpusi", "Monotube with the piston, valving and the nitrogen chamber; threaded for ride-height adjustment.", "Porshen, klapanlar va azot kamerasi joylashgan monotruba; yo‘l tirqishini rostlash uchun rezbali."),
                "Steel tube, black", bodyMesh, new[] { m.BlackSteel }, bottom, rot);
            Mesh rod = ctx.Mesh("damper-rod" + key, k => k.Cylinder(0.011f, body - 0.01f, L - 0.006f, 14));
            ctx.Part(damper, id + ".coilover.damper.rod", new Txt("Piston rod", "Shtok", "Hard-chromed rod that carries the piston and passes through the top mount.", "Porshenni ko‘taruvchi va yuqori tayanchdan o‘tuvchi qattiq xromlangan shtok."),
                "Hard-chromed steel", rod, new[] { m.Chrome }, bottom, rot, false, Vector3.up, 0.5f);
            Mesh bump = ctx.Mesh("bump-stop", k =>
            {
                var p = new List<Vector2> { new Vector2(0.011f, 0f) };
                for (int i = 0; i <= 12; i++) p.Add(new Vector2(0.019f + 0.003f * Mathf.Cos(i * Mathf.PI * 0.5f), 0.005f + 0.05f * i / 12f));
                p.Add(new Vector2(0.011f, 0.06f));
                p.Add(new Vector2(0.011f, 0f));
                k.Lathe(p, 16, 55f);
            });
            ctx.Part(damper, id + ".coilover.damper.bump-stop", new Txt("Bump stop", "Siqilish buferi", "Microcellular polyurethane stop that cushions full compression.", "To‘liq siqilishni yumshatuvchi mikrog‘ovak poliuretan bufer."),
                "Polyurethane", bump, new[] { m.AnodisedYellow }, bottom + up * (body + 0.012f), rot);
            Mesh boot = ctx.Mesh("damper-boot" + key, k => k.Bellows(0.027f, 0.02f, 0.025f, 0.033f, seatHigh - 0.012f - (body - 0.03f), 8, 18));
            ctx.Part(damper, id + ".coilover.damper.boot", new Txt("Dust boot", "Chang g‘ilofi", "Bellows that keeps grit off the rod seal.", "Shtok salnigini qum va changdan saqlovchi gofra."),
                "Thermoplastic elastomer", boot, new[] { m.Rubber }, bottom + up * (body - 0.03f), rot);

            Mesh perch = ctx.Mesh("spring-perch", k => k.Lathe(new[] { new Vector2(0.0277f, -0.006f), new Vector2(0.058f, -0.006f), new Vector2(0.058f, 0f), new Vector2(0.046f, 0.006f), new Vector2(0.0277f, 0.006f), new Vector2(0.0277f, -0.006f) }, 28));
            ctx.Part(coil, id + ".coilover.lower-seat", new Txt("Lower spring seat", "Pastki prujina tayanchi", "Threaded perch and lock ring that set the ride height.", "Yo‘l tirqishini belgilovchi rezbali tayanch va qulflash halqasi."),
                "Anodised aluminium", perch, new[] { m.AnodisedRed }, bottom + up * seatLow, rot);
            float springLength = seatHigh - seatLow - 0.012f;
            Mesh spring = ctx.Mesh("coil-spring" + key, k => k.Tube(MeshKit.HelixPath(FrontChassisGeometry.CoilRadius, springLength, FrontChassisGeometry.CoilWire,
                Mathf.Clamp(springLength / 0.04f, 2.5f, 5f), 0.8f, 26), FrontChassisGeometry.CoilWire * 0.5f, 10, true, true, Vector3.up));
            ctx.Part(coil, id + ".coilover.spring", new Txt("Coil spring", "Spiral prujina", "Linear-rate spring of chrome-silicon wire; carries this corner's share of the car.", "Xrom-kremniy simdan chiziqli bikrlikdagi prujina; shu burchakka tushadigan og‘irlikni ko‘taradi."),
                "Chrome-silicon spring steel", spring, new[] { m.AnodisedYellow }, bottom + up * (seatLow + 0.006f), rot, false, new Vector3(c.S, 0f, 0f), 0.7f);
            Mesh upperSeat = ctx.Mesh("upper-spring-seat", k => k.Lathe(new[] { new Vector2(0.012f, -0.004f), new Vector2(0.056f, -0.004f), new Vector2(0.056f, 0.003f), new Vector2(0.04f, 0.012f), new Vector2(0.012f, 0.012f), new Vector2(0.012f, -0.004f) }, 28));
            ctx.Part(coil, id + ".coilover.upper-seat", new Txt("Upper spring seat", "Yuqori prujina tayanchi", "Pressed seat that centres the spring under the top mount.", "Prujinani yuqori tayanch ostida markazlashtiruvchi presslangan tayanch."),
                "Pressed steel", upperSeat, new[] { m.BlackSteel }, bottom + up * seatHigh, rot);

            Transform mount = ctx.SubAssembly(coil, id + ".coilover.top-mount", new Txt("Top Mount", "Yuqori tayanch",
                    "Rubber-isolated mount with a thrust bearing that bolts the coilover to the body.",
                    "Prujinali amortizatorni kuzovga mahkamlovchi, rezina bilan izolyatsiyalangan va tirak podshipnikli tayanch."),
                "Steel and rubber", Vector3.up, 0.7f);
            Mesh plate = ctx.Mesh("top-mount-plate", k =>
            {
                k.Lathe(new[] { new Vector2(0.012f, 0.008f), new Vector2(0.064f, 0.008f), new Vector2(0.064f, 0.012f), new Vector2(0.048f, 0.016f), new Vector2(0.03f, 0.03f), new Vector2(0.012f, 0.03f), new Vector2(0.012f, 0.008f) }, 30, 30f);
            });
            Vector3 mountBase = bottom + up * (L - 0.04f);
            ctx.Part(mount, id + ".coilover.top-mount.plate", new Txt("Mount plate", "Tayanch plastinasi", "Steel plate with three studs through the wheel-arch roof.", "G‘ildirak arkasi tomidan o‘tuvchi uchta shpilkali po‘lat plastina."),
                "Pressed steel", plate, new[] { m.BlackSteel }, mountBase, rot);
            Mesh isolator = ctx.Mesh("top-mount-isolator", k => k.Lathe(new[] { new Vector2(0.012f, 0.012f), new Vector2(0.047f, 0.012f), new Vector2(0.05f, 0.02f), new Vector2(0.046f, 0.028f), new Vector2(0.012f, 0.028f), new Vector2(0.012f, 0.012f) }, 24, 45f));
            ctx.Part(mount, id + ".coilover.top-mount.isolator", new Txt("Rubber isolator", "Rezina izolyator", "Bonded rubber that filters vibration from the rod.", "Shtokdan keladigan tebranishni so‘ndiruvchi yopishtirilgan rezina."),
                "Natural rubber", isolator, new[] { m.Rubber }, mountBase - up * 0.012f, rot);
            Mesh thrust = ctx.Mesh("top-mount-bearing", k => k.Sleeve(0.056f, 0.03f, 0f, 0.008f, 28));
            ctx.Part(mount, id + ".coilover.top-mount.bearing", new Txt("Thrust bearing", "Tirak podshipnigi", "Lets the spring seat turn freely under load.", "Prujina tayanchining yuk ostida erkin aylanishiga imkon beradi."),
                "Steel and polymer", thrust, new[] { m.GreyPlastic }, bottom + up * (seatHigh + 0.012f), rot);
            Mesh rodNut = ctx.Mesh("nut-M12", k => Fasteners.HexNut(k, 0.012f, true));
            ctx.Part(mount, id + ".coilover.top-mount.rod-nut", new Txt("Rod nut M12", "Shtok gaykasi M12", "Self-locking nut on the piston rod.", "Shtokdagi o‘z-o‘zidan qulflanadigan gayka."),
                "Steel", rodNut, new[] { m.ZincSteel }, mountBase + up * 0.02f, rot, true);
            Mesh mountStud = ctx.Mesh("stud-M8", k => k.Cylinder(0.004f, 0f, 0.03f, 8));
            Mesh mountNut = ctx.Mesh("nut-M8", k => Fasteners.HexNut(k, 0.008f, true));
            Vector3 s0 = Vector3.ProjectOnPlane(Vector3.forward, up).normalized, s1 = Vector3.Cross(up, s0);
            for (int i = 0; i < 3; i++)
            {
                float ang = Mathf.PI * 2f * i / 3f + Mathf.PI / 6f;
                Vector3 p = mountBase + up * 0.012f + (s0 * Mathf.Cos(ang) + s1 * Mathf.Sin(ang)) * 0.053f;
                ctx.Part(mount, id + ".coilover.top-mount.stud-" + (i + 1), new Txt("Mount stud M8", "Tayanch shpilkasi M8", "Stud through the arch roof.", "Arka tomidan o‘tuvchi shpilka."),
                    "Steel", mountStud, new[] { m.ZincSteel }, p, rot, true);
                ctx.Part(mount, id + ".coilover.top-mount.stud-nut-" + (i + 1), new Txt("Nut M8", "Gayka M8", "Flange nut on the mount stud.", "Tayanch shpilkasidagi flanetsli gayka."),
                    "Steel", mountNut, new[] { m.ZincSteel }, p + up * 0.016f, rot, true);
            }
            Fastener(coil, id + ".coilover.lower-bolt", bottom + Vector3.forward * 0.034f, Vector3.back, 0.012f, 0.075f, "M12");
        }

        // ------------------------------------------------------------------------------------------------ anti-roll bar

        private void AntiRollBar(Transform system)
        {
            string id = "suspension.front-anti-roll-bar";
            Transform arb = ctx.Assembly(system, id, new Txt("Front Anti-Roll Bar", "Old stabilizator",
                    "Torsion bar that links the two lower arms to reduce body roll in corners.",
                    "Burilishda kuzov og‘ishini kamaytirish uchun ikki pastki richagni bog‘lovchi buraluvchi sterjen."),
                "Spring steel", Vector3.forward, 1f);
            Vector3 l = g.Left.LinkHigh, r = g.Right.LinkHigh;
            Vector3 bl = new Vector3(-g.ArbHalf, g.ArbY, g.ArbZ), br = new Vector3(g.ArbHalf, g.ArbY, g.ArbZ);
            // Levers run outboard at the bar's height, then the eye sits on the drop link.
            Vector3 origin = new Vector3(0f, g.ArbY, g.ArbZ);
            Mesh bar = ctx.Mesh("front-anti-roll-bar", k =>
            {
                k.Push(Matrix4x4.Translate(-origin));
                List<Vector3> path = MeshKit.Bends(new[] { l, bl, br, r }, 0.05f, 8);
                k.Tube(path, 0.0115f, 14, true, true, Vector3.up);
                foreach (Vector3 end in new[] { l, r })
                {
                    k.Push(end + new Vector3(Mathf.Sign(end.x) * 0.014f, 0f, 0f), InternalsContext.AlongY(Vector3.right));
                    k.Lathe(new[] { new Vector2(0.006f, -0.006f), new Vector2(0.017f, -0.006f), new Vector2(0.017f, 0.006f), new Vector2(0.006f, 0.006f), new Vector2(0.006f, -0.006f) }, 16);
                    k.Pop();
                }
                k.Pop();
            });
            ctx.Part(arb, id + ".bar", new Txt("Anti-roll bar", "Stabilizator sterjeni", "Hollow spring-steel bar bent into a U; twists when one wheel rises more than the other.", "U-simon egilgan ichi bo‘sh prujina po‘lat sterjen; bir g‘ildirak ko‘proq ko‘tarilganda buraladi."),
                "Spring steel 50CrV4, black", bar, new[] { m.BlackSteel }, origin, Quaternion.identity);
            foreach (FrontCorner c in new[] { g.Left, g.Right })
            {
                string side = c.Side.ToLowerInvariant();
                Transform link = ctx.SubAssembly(arb, id + ".link-" + side, new Txt(c.Side + " Drop Link", (c.S < 0 ? "Chap" : "O‘ng") + " stabilizator tirgagi",
                        "Short rod with ball joints at both ends that ties the bar to the lower arm.",
                        "Stabilizatorni pastki richag bilan bog‘lovchi, ikki uchida sharli bo‘g‘inli qisqa tirgak."),
                    "Steel", new Vector3(c.S, 0f, 0f), 0.6f);
                float len = c.LinkHigh.y - c.LinkLow.y;
                Mesh body = ctx.Mesh("drop-link-" + Mathf.RoundToInt(len * 1000f), k =>
                {
                    k.Cylinder(0.0075f, 0.012f, len - 0.012f, 10);
                    foreach (float yy in new[] { 0.0f, len })
                    {
                        k.Push(new Vector3(0f, yy, 0f), InternalsContext.AlongY(Vector3.right));
                        k.Lathe(new[] { new Vector2(0f, -0.011f), new Vector2(0.012f, -0.011f), new Vector2(0.014f, 0f), new Vector2(0.012f, 0.011f), new Vector2(0f, 0.011f) }, 14);
                        k.Pop();
                    }
                });
                ctx.Part(link, id + ".link-" + side + ".body", new Txt("Link rod", "Tirgak sterjeni", "Rod with two moulded ball sockets.", "Ikki quyma sharli uyali sterjen."),
                    "Steel and glass-filled nylon", body, new[] { m.ZincSteel }, c.LinkLow, Quaternion.identity);
                Mesh stud = ctx.Mesh("link-stud", k => k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.006f, 0f), new Vector2(0.005f, 0.03f), new Vector2(0f, 0.03f) }, 10));
                Mesh nut = ctx.Mesh("nut-M10", k => Fasteners.HexNut(k, 0.01f, true));
                foreach (bool upper in new[] { false, true })
                {
                    Vector3 p = upper ? c.LinkHigh : c.LinkLow;
                    Quaternion inward = InternalsContext.AlongY(new Vector3(-c.S, 0f, 0f));
                    string u = upper ? "upper" : "lower";
                    ctx.Part(link, id + ".link-" + side + "." + u + "-stud", new Txt((upper ? "Upper" : "Lower") + " ball stud", (upper ? "Yuqori" : "Pastki") + " sharli barmoq", "Ball stud through the bar eye or the arm tab.", "Sterjen ko‘zi yoki richag qulog‘idan o‘tuvchi sharli barmoq."),
                        "Steel", stud, new[] { m.MachinedSteel }, p, InternalsContext.AlongY(new Vector3(c.S, 0f, 0f)), true);
                    ctx.Part(link, id + ".link-" + side + "." + u + "-nut", new Txt("Nut M10", "Gayka M10", "Self-locking nut.", "O‘z-o‘zidan qulflanadigan gayka."),
                        "Steel", nut, new[] { m.ZincSteel }, p + new Vector3(c.S * 0.022f, 0f, 0f), InternalsContext.AlongY(new Vector3(c.S, 0f, 0f)), true);
                }
            }
            Transform mounts = ctx.SubAssembly(arb, id + ".mounts", new Txt("Bar Mounts", "Sterjen tayanchlari",
                    "Split rubber bushings in steel clamps that hold the bar to the bulkhead.",
                    "Sterjenni bo‘lim devoriga mahkamlovchi po‘lat qisqichlardagi kesikli rezina vtulkalar."),
                "Rubber and steel", Vector3.back, 0.6f);
            Mesh bush = ctx.Mesh("arb-bushing", k => k.Sleeve(0.022f, 0.0115f, -0.018f, 0.018f, 16));
            float reach = Mathf.Max(0.03f, g.ArbZ - g.BulkheadZ);
            Mesh clamp = ctx.Mesh("arb-clamp-" + Mathf.RoundToInt(reach * 1000f), k =>
            {
                // U-strap round the front of the bushing, legs straight back to a flange on the bulkhead.
                var path = new List<Vector3>();
                for (int i = 0; i <= 12; i++)
                {
                    float t = Mathf.PI * i / 12f;
                    path.Add(new Vector3(0f, Mathf.Cos(t) * 0.0235f, Mathf.Sin(t) * 0.0235f));
                }
                k.Sweep(new[] { new Vector2(-0.0015f, -0.016f), new Vector2(0.0015f, -0.016f), new Vector2(0.0015f, 0.016f), new Vector2(-0.0015f, 0.016f) }, path, Vector3.right);
                foreach (float yy in new[] { 0.0235f, -0.0235f })
                    k.Box(new Vector3(0f, yy, -reach * 0.5f), new Vector3(0.032f, 0.003f, reach));
                k.Box(new Vector3(0f, 0f, -reach + 0.0015f), new Vector3(0.032f, 0.07f, 0.003f));
            });
            foreach (float sx in new[] { -1f, 1f })
            {
                // Between the assist motor and the pinion, clear of the rack below and the engine above.
                Vector3 p = new Vector3(sx < 0 ? -0.04f : Mathf.Min(0.12f, g.PinionX - 0.08f), g.ArbY, g.ArbZ);
                string side = sx < 0 ? "left" : "right";
                Quaternion alongX = InternalsContext.AlongY(Vector3.right);
                ctx.Part(mounts, id + ".mounts.bushing-" + side, new Txt((sx < 0 ? "Left" : "Right") + " bar bushing", (sx < 0 ? "Chap" : "O‘ng") + " sterjen vtulkasi", "Split rubber bushing that lets the bar twist.", "Sterjen buralishiga imkon beruvchi kesikli rezina vtulka."),
                    "Natural rubber", bush, new[] { m.Rubber }, p, alongX);
                Quaternion strap = Quaternion.identity;
                ctx.Part(mounts, id + ".mounts.clamp-" + side, new Txt((sx < 0 ? "Left" : "Right") + " bar clamp", (sx < 0 ? "Chap" : "O‘ng") + " sterjen qisqichi", "Pressed-steel strap bolted to the bulkhead.", "Bo‘lim devoriga boltlangan presslangan po‘lat xomut."),
                    "Pressed steel", clamp, new[] { m.BlackSteel }, p, strap);
                foreach (float dy in new[] { -0.03f, 0.03f })
                    Fastener(mounts, id + ".mounts.bolt-" + side + (dy < 0 ? "-lower" : "-upper"), p + new Vector3(0f, dy, -reach + 0.01f), Vector3.back, 0.008f, 0.03f, "M8");
            }
        }

        // ------------------------------------------------------------------------------------------------ subframe

        private void Subframe(Transform system)
        {
            string id = "suspension.front-subframe";
            Transform sub = ctx.Assembly(system, id, new Txt("Front Subframe", "Old yordamchi rama",
                    "Pickup brackets on the wheel-well walls and a lower tie bar: carry the arm pivots and the steering rack.",
                    "G‘ildirak arkasi devorlaridagi kronshteynlar va pastki bog‘lovchi to‘sin: richag o‘qlari va rul reykasini ko‘taradi."),
                "Pressed and tubular steel", Vector3.down, 1f);
            foreach (FrontCorner c in new[] { g.Left, g.Right })
            {
                string side = c.Side.ToLowerInvariant();
                Transform brackets = ctx.SubAssembly(sub, id + "." + side + "-brackets", new Txt(c.Side + " Pickup Brackets", (c.S < 0 ? "Chap" : "O‘ng") + " osma kronshteynlari",
                        "Pressed clevis brackets bolted to the wheel-well wall, one per arm pivot.",
                        "G‘ildirak arkasi devoriga boltlangan presslangan vilkasimon kronshteynlar, har bir richag o‘qi uchun bittadan."),
                    "Pressed steel", new Vector3(c.S, 0f, 0f), 0.8f);
                Bracket(brackets, id + "." + side + "-brackets.lower-front", "Lower front bracket", "Pastki old kronshteyn", c, c.LcaFront, c.WallLcaFront, 0.056f);
                Bracket(brackets, id + "." + side + "-brackets.lower-rear", "Lower rear bracket", "Pastki orqa kronshteyn", c, c.LcaRear, c.WallLcaRear, 0.056f);
                Bracket(brackets, id + "." + side + "-brackets.upper-front", "Upper front bracket", "Yuqori old kronshteyn", c, c.UcaFront, c.WallUcaFront, 0.046f);
                Bracket(brackets, id + "." + side + "-brackets.upper-rear", "Upper rear bracket", "Yuqori orqa kronshteyn", c, c.UcaRear, c.WallUcaRear, 0.046f);
            }
            Vector3 a = new Vector3(-g.Wall - 0.004f, g.TieBarY, g.TieBarZ), b = new Vector3(g.Wall + 0.004f, g.TieBarY, a.z);
            Mesh tie = ctx.Mesh("lower-tie-bar", k =>
            {
                k.Tube(new[] { a, b }, 0.019f, 16, false, false, Vector3.up);
                foreach (Vector3 end in new[] { a, b })
                {
                    k.Push(end, InternalsContext.AlongY(end.x < 0 ? Vector3.right : Vector3.left));
                    k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.04f, 0f), new Vector2(0.04f, 0.006f), new Vector2(0.019f, 0.012f), new Vector2(0f, 0.012f) }, 20);
                    k.Pop();
                }
            });
            ctx.Part(sub, id + ".tie-bar", new Txt("Lower tie bar", "Pastki bog‘lovchi to‘sin", "Tube that braces the two wheel wells and carries the steering-rack clamps.", "Ikki g‘ildirak arkasini bog‘lovchi va rul reykasi qisqichlarini ko‘taruvchi quvur."),
                "Steel tube", tie, new[] { m.BlackSteel }, Vector3.zero, Quaternion.identity);
        }

        // Clevis bracket for one arm pivot: a base plate on the wall behind the pivot, two ears for the pivot bolt
        // and two bolts into the wall.
        private void Bracket(Transform parent, string id, string en, string uz, FrontCorner c, Vector3 pivot, float wall, float bushingLength)
        {
            float wx = c.S * (wall + 0.003f);
            Vector3 origin = new Vector3(wx, pivot.y, pivot.z);
            float half = bushingLength * 0.5f + 0.012f;
            Mesh mesh = ctx.Mesh("pickup-bracket-" + Mathf.RoundToInt(bushingLength * 1000f) + "-" + Mathf.RoundToInt((Mathf.Abs(pivot.x) - wall) * 1000f) + (c.S < 0 ? "-l" : "-r"), k =>
            {
                k.RoundedBox(new Vector3(0f, 0f, 0f), new Vector3(0.006f, 0.07f, half * 2f + 0.03f), 0.003f, 2);
                float reach = Mathf.Abs(pivot.x) - wall;
                foreach (float dz in new[] { -half + 0.0025f, half - 0.0025f })
                {
                    Vector3 hole = new Vector3(c.S * (reach + 0.003f), 0f, dz);
                    k.PlanarPrism(new[]
                    {
                        new Vector3(0f, -0.032f, dz), new Vector3(c.S * reach * 0.5f, -0.026f, dz), hole + new Vector3(0f, -0.02f, 0f),
                        hole + new Vector3(c.S * 0.02f, 0f, 0f), hole + new Vector3(0f, 0.02f, 0f), new Vector3(c.S * reach * 0.5f, 0.026f, dz), new Vector3(0f, 0.032f, dz)
                    }, 0.005f);
                }
            });
            ctx.Part(parent, id, new Txt(en, uz, "Clevis that carries the arm's pivot bolt.", "Richag o‘qi boltini ko‘taruvchi vilka."),
                "Pressed steel, e-coated", mesh, new[] { m.BlackSteel }, origin, Quaternion.identity, false, new Vector3(c.S, 0f, 0f), 0.5f);
            Mesh bolt = ctx.Mesh("bolt-M10x30", k => Fasteners.HexBolt(k, 0.01f, 0.03f));
            foreach (float dy in new[] { -0.022f, 0.022f })
                ctx.Part(parent, id + "-bolt" + (dy < 0 ? "-lower" : "-upper"), new Txt("Bolt M10", "Bolt M10", "Bolts the bracket to the wall.", "Kronshteynni devorga mahkamlaydi."),
                    "Steel 10.9", bolt, new[] { m.ZincSteel }, origin + new Vector3(c.S * 0.003f, dy, half + 0.008f), InternalsContext.AlongY(new Vector3(c.S, 0f, 0f)), true);
        }
    }
}
#endif
