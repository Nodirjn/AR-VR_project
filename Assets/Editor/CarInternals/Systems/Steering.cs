#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace CarInternals
{
    /// <summary>
    /// Steering: rack-and-pinion gear with a concentric electric assist motor, tie rods with outer ends on the
    /// knuckles, and the column with two universal joints and an intermediate shaft up to the existing steering wheel.
    /// The rack stands displaced by the steering angle the front wheels have in the scene.
    /// </summary>
    [CarInternalsSubsystem("Steering", 20)]
    public sealed class Steering : ICarInternalsSubsystem
    {
        private InternalsContext ctx;
        private CarPalette m;
        private FrontChassisGeometry g;

        public void Build(InternalsContext context)
        {
            ctx = context;
            m = ctx.Mats;
            g = FrontChassisGeometry.Of(ctx.Layout, ctx.Probe);
            Transform system = ctx.System("Steering");
            Rack(system);
            Column(system);
            ctx.AllowContact("steering.rack.tie-rod-left.outer-end", "suspension.front-left.knuckle");
            ctx.AllowContact("steering.rack.tie-rod-right.outer-end", "suspension.front-right.knuckle");
            ctx.AllowContact("steering.rack.tie-rod", "InteriorFloor");
            ctx.AllowContact("steering.rack.tie-rod", "CarConcept");
            ctx.AllowContact("steering.rack.housing.clamp", "suspension.front-subframe");
            ctx.AllowContact("steering.column", "InteriorSteering");
            ctx.AllowContact("steering.column", "InteriorDash");
            ctx.AllowContact("steering.column.seal", "InteriorFloor");
            ctx.AllowContact("steering.column.seal", "CarConcept");
            // The pinion reaches through the bulkhead to the lower universal joint, sealed by the grommet.
            ctx.AllowContact("steering.rack.pinion", "InteriorDash");
            ctx.AllowContact("steering.rack.housing.pinion-housing", "InteriorDash");
            ctx.AllowContact("steering.rack.pinion", "steering.column.lower-joint");
            ctx.AllowContact("steering.rack.housing.pinion-housing", "steering.column");
            ctx.AllowContact("steering.rack.housing.tube", "steering.column.seal");
            // The rack sits at the bulkhead, inside the glTF's lower dash shell, which reaches forward to the engine.
            ctx.AllowContact("steering.rack", "InteriorDash");
        }

        // ------------------------------------------------------------------------------------------------ rack

        private void Rack(Transform system)
        {
            string id = "steering.rack";
            Transform rack = ctx.Assembly(system, id, new Txt("Steering Rack and Pinion", "Rul reykasi va shesternyasi",
                    "Turns the steering wheel's rotation into the side-to-side push that steers the front wheels.",
                    "Rul chambaragining aylanishini old g‘ildiraklarni buruvchi ko‘ndalang harakatga aylantiradi."),
                "Aluminium and steel", Vector3.forward, 1f);
            float y = g.RackY, z = g.RackZ;
            float housingHalf = g.RackJointHalf - 0.075f;
            float motor0 = -housingHalf + 0.065f, motor1 = motor0 + 0.16f;
            Vector3 origin = new Vector3(0f, y, z);
            Quaternion alongX = InternalsContext.AlongY(Vector3.right, Vector3.forward);

            Transform housing = ctx.SubAssembly(rack, id + ".housing", new Txt("Rack Housing", "Reyka korpusi",
                    "Cast-aluminium tube that guides the rack, with the pinion and valve housing and two mounting clamps.",
                    "Reykani yo‘naltiruvchi quyma alyuminiy quvur, shesternya korpusi va ikki mahkamlash qisqichi bilan."),
                "Cast aluminium", Vector3.up, 0.6f);
            Mesh tube = ctx.Mesh("rack-housing", k =>
            {
                var p = new List<Vector2>
                {
                    new Vector2(0.013f, -housingHalf), new Vector2(0.03f, -housingHalf), new Vector2(0.03f, -housingHalf + 0.02f), new Vector2(0.026f, -housingHalf + 0.026f),
                    new Vector2(0.026f, g.PinionX - 0.05f), new Vector2(0.033f, g.PinionX - 0.035f), new Vector2(0.033f, g.PinionX + 0.035f), new Vector2(0.026f, g.PinionX + 0.05f),
                    new Vector2(0.026f, housingHalf - 0.026f), new Vector2(0.03f, housingHalf - 0.02f), new Vector2(0.03f, housingHalf), new Vector2(0.013f, housingHalf)
                };
                k.Lathe(p, 24, 35f);
            });
            ctx.Part(housing, id + ".housing.tube", new Txt("Rack housing", "Reyka korpusi", "Die-cast tube with the rack bushes and seals.", "Reyka vtulkalari va salniklari joylashgan quyma quvur."),
                "Die-cast aluminium", tube, new[] { m.CastAluminium }, origin, alongX);
            Vector3 pinBase = new Vector3(g.PinionX, y, z);
            Vector3 pinAxis = PinionAxis(pinBase);
            Mesh pinionHousing = ctx.Mesh("pinion-housing", k =>
            {
                k.Lathe(new[] { new Vector2(0f, -0.02f), new Vector2(0.03f, -0.02f), new Vector2(0.031f, 0.05f), new Vector2(0.024f, 0.058f), new Vector2(0.017f, 0.058f), new Vector2(0.017f, 0.066f), new Vector2(0f, 0.066f) }, 20, 35f);
            });
            ctx.Part(housing, id + ".housing.pinion-housing", new Txt("Pinion housing", "Shesternya korpusi", "Holds the pinion's bearings and the torque sensor that commands the assist motor.", "Shesternya podshipniklari va kuchaytirgichni boshqaruvchi moment datchigini ushlaydi."),
                "Die-cast aluminium", pinionHousing, new[] { m.CastAluminium }, pinBase, InternalsContext.AlongY(pinAxis, Vector3.forward));
            Mesh clamp = ctx.Mesh("rack-clamp", k =>
            {
                var path = new List<Vector3>();
                for (int i = 0; i <= 14; i++)
                {
                    float t = Mathf.PI * i / 14f;
                    path.Add(new Vector3(0f, Mathf.Sin(t) * 0.034f - 0.0f, Mathf.Cos(t) * 0.034f));
                }
                k.Sweep(new[] { new Vector2(-0.002f, -0.014f), new Vector2(0.002f, -0.014f), new Vector2(0.002f, 0.014f), new Vector2(-0.002f, 0.014f) }, path, Vector3.right);
                float drop = y - g.TieBarY - 0.019f;
                foreach (float zz in new[] { -0.034f, 0.034f })
                    k.Box(new Vector3(0f, -drop * 0.5f, zz), new Vector3(0.028f, drop, 0.004f));
            });
            Mesh clampBush = ctx.Mesh("rack-clamp-bushing", k => k.Sleeve(0.033f, 0.026f, -0.014f, 0.014f, 20));
            Mesh boltShort = ctx.Mesh("bolt-M10x30", k => Fasteners.HexBolt(k, 0.01f, 0.03f));
            foreach (float sx in new[] { -1f, 1f })
            {
                float cx = sx < 0 ? Mathf.Min(motor1 + 0.045f, g.PinionX - 0.09f) : housingHalf - 0.06f;
                string side = sx < 0 ? "left" : "right";
                Vector3 p = new Vector3(cx, y, z);
                ctx.Part(housing, id + ".housing.clamp-" + side, new Txt((sx < 0 ? "Left" : "Right") + " mounting clamp", (sx < 0 ? "Chap" : "O‘ng") + " mahkamlash qisqichi", "Steel clamp that bolts the rack to the subframe tie bar.", "Reykani yordamchi rama to‘siniga mahkamlovchi po‘lat qisqich."),
                    "Pressed steel", clamp, new[] { m.BlackSteel }, p, Quaternion.identity);
                ctx.Part(housing, id + ".housing.clamp-bushing-" + side, new Txt("Clamp bushing", "Qisqich vtulkasi", "Rubber bushing between the housing and the clamp.", "Korpus va qisqich orasidagi rezina vtulka."),
                    "Rubber", clampBush, new[] { m.Rubber }, p, alongX);
                foreach (float zz in new[] { -0.034f, 0.034f })
                    ctx.Part(housing, id + ".housing.clamp-bolt-" + side + (zz < 0 ? "-rear" : "-front"), new Txt("Bolt M10", "Bolt M10", "Clamps the rack onto the tie bar.", "Reykani to‘singa qisadi."),
                        "Steel 10.9", boltShort, new[] { m.ZincSteel }, new Vector3(cx, g.TieBarY + 0.021f, z + zz), Quaternion.identity, true);
            }

            Mesh rackBar = ctx.Mesh("rack-bar", k =>
            {
                float half = g.RackJointHalf - 0.012f;
                k.Cylinder(0.0125f, -half, half, 16);
                // tooth zone: flats along the pinion section
                k.Box(new Vector3(0f, g.PinionX - g.RackTravel, 0.011f), new Vector3(0.016f, 0.11f, 0.006f));
            });
            ctx.Part(rack, id + ".bar", new Txt("Toothed rack", "Tishli reyka", "Induction-hardened bar with straight teeth meshing with the pinion; slides left and right.", "Shesternya bilan tishlashuvchi to‘g‘ri tishli, induksion toblangan sterjen; chapga-o‘ngga suriladi."),
                "Induction-hardened steel", rackBar, new[] { m.MachinedSteel }, new Vector3(g.RackTravel, y, z), alongX, false, Vector3.right, 0.6f);
            Mesh pinion = ctx.Mesh("pinion-shaft", k =>
            {
                k.Cylinder(0.009f, -0.01f, 0.1f, 14);
                k.Lathe(new[] { new Vector2(0f, -0.012f), new Vector2(0.014f, -0.012f), new Vector2(0.014f, 0.03f), new Vector2(0f, 0.03f) }, 14, 40f, 0f, 360f, true);
            });
            ctx.Part(rack, id + ".pinion", new Txt("Pinion shaft", "Shesternya vali", "Helical pinion and input spline driven by the column.", "Kolonka aylantiradigan qiya tishli shesternya va kirish shlitsasi."),
                "Case-hardened steel", pinion, new[] { m.MachinedSteel }, pinBase, InternalsContext.AlongY(pinAxis, Vector3.forward), false, pinAxis, 0.6f);

            Transform eps = ctx.SubAssembly(rack, id + ".eps", new Txt("Electric Power-Steering Unit", "Elektr rul kuchaytirgichi",
                    "Brushless motor around the rack that adds assist force through a belt and ball-nut drive.",
                    "Reyka atrofidagi cho‘tkasiz motor; tasma va sharikli gayka uzatmasi orqali qo‘shimcha kuch beradi."),
                "Aluminium and copper", Vector3.down, 0.7f);
            Mesh motorMesh = ctx.Mesh("eps-motor", k =>
            {
                var p = new List<Vector2> { new Vector2(0.03f, 0f), new Vector2(0.04f, 0f), new Vector2(0.043f, 0.006f) };
                for (int i = 0; i < 9; i++)
                {
                    float y0 = 0.014f + i * 0.012f;
                    p.Add(new Vector2(0.043f, y0)); p.Add(new Vector2(0.046f, y0 + 0.002f)); p.Add(new Vector2(0.046f, y0 + 0.006f)); p.Add(new Vector2(0.043f, y0 + 0.008f));
                }
                p.Add(new Vector2(0.043f, motor1 - motor0 - 0.05f));
                p.Add(new Vector2(0.03f, motor1 - motor0 - 0.05f));
                k.Lathe(p, 28, 40f);
            });
            ctx.Part(eps, id + ".eps.motor", new Txt("Assist motor", "Kuchaytirgich motori", "Brushless permanent-magnet motor, about 80 A peak.", "Doimiy magnitli cho‘tkasiz motor, cho‘qqi toki taxminan 80 A."),
                "Aluminium housing, copper windings", motorMesh, new[] { m.CastAluminium }, new Vector3(motor0, y, z), alongX);
            Mesh drive = ctx.Mesh("eps-ball-nut", k => k.Lathe(new[] { new Vector2(0.026f, 0f), new Vector2(0.04f, 0f), new Vector2(0.04f, 0.05f), new Vector2(0.03f, 0.056f), new Vector2(0.026f, 0.056f) }, 28, 40f));
            ctx.Part(eps, id + ".eps.ball-nut", new Txt("Ball-nut drive", "Sharikli gayka uzatmasi", "Recirculating-ball nut that turns motor torque into rack force.", "Motor momentini reyka kuchiga aylantiruvchi aylanma sharikli gayka."),
                "Steel in aluminium housing", drive, new[] { m.MachinedAluminium }, new Vector3(motor1 - 0.05f, y, z), alongX);
            // Controller as a finned cap on the motor's outer end, connector toward the bulkhead.
            Mesh ecu = ctx.Mesh("eps-ecu", k =>
            {
                var p = new List<Vector2> { new Vector2(0f, 0f), new Vector2(0.04f, 0f) };
                for (int i = 0; i < 3; i++)
                {
                    float y0 = 0.004f + i * 0.008f;
                    p.Add(new Vector2(0.04f, y0)); p.Add(new Vector2(0.045f, y0 + 0.001f)); p.Add(new Vector2(0.045f, y0 + 0.004f)); p.Add(new Vector2(0.04f, y0 + 0.005f));
                }
                p.Add(new Vector2(0.04f, 0.03f));
                p.Add(new Vector2(0.026f, 0.034f));
                p.Add(new Vector2(0f, 0.034f));
                k.Lathe(p, 28, 40f);
            });
            Quaternion outward = InternalsContext.AlongY(Vector3.left, Vector3.forward);
            Vector3 ecuAt = new Vector3(motor0, y, z);
            ctx.Part(eps, id + ".eps.ecu", new Txt("Steering control unit", "Rul boshqaruv bloki", "Finned controller on the motor end; reads the torque sensor and drives the motor.", "Motor uchidagi qovurg‘ali boshqaruv bloki; moment datchigini o‘qib motorni boshqaradi."),
                "Die-cast aluminium", ecu, new[] { m.CastAluminium }, ecuAt, outward);
            Mesh connector = ctx.Mesh("connector-small", k => k.RoundedBox(Vector3.zero, new Vector3(0.02f, 0.016f, 0.024f), 0.003f, 1));
            Vector3 plug = ecuAt + new Vector3(-0.016f, 0f, -0.052f);
            ctx.Part(eps, id + ".eps.connector", new Txt("Power connector", "Quvvat ulagichi", "Sealed connector for power and the vehicle network.", "Quvvat va avtomobil tarmog‘i uchun zichlangan ulagich."),
                "Glass-filled nylon", connector, new[] { m.BlackPlastic }, plug, Quaternion.identity, true);
            // Straight back from the connector into the bulkhead.
            float toBulkhead = Mathf.Max(0.04f, z - 0.052f - ctx.Layout.FirewallZ + 0.03f);
            Mesh harness = ctx.Mesh("eps-harness", k =>
            {
                Vector3 a = new Vector3(0f, 0f, -0.012f);
                Vector3 b = new Vector3(-0.004f, -0.006f, -toBulkhead * 0.5f);
                Vector3 c = new Vector3(-0.016f, -0.012f, -toBulkhead);
                k.Tube(MeshKit.CatmullRom(new[] { a, b, c }, 8), 0.0045f, 8);
            });
            ctx.Part(eps, id + ".eps.harness", new Txt("Assist harness", "Kuchaytirgich simlari", "Power and CAN cable to the bulkhead.", "Bo‘lim devorigacha boradigan quvvat va CAN kabeli."),
                "Copper, PVC jacket", harness, new[] { m.WireBlack }, plug, Quaternion.identity, true);

            TieRod(rack, id, g.Left, housingHalf);
            TieRod(rack, id, g.Right, housingHalf);
        }

        private Vector3 PinionAxis(Vector3 pinBase)
        {
            // Low and mostly backward, under the anti-roll bar, through the bulkhead toward the column.
            Vector3 toColumn = (UpperJoint() - pinBase).normalized;
            return (Vector3.back + toColumn).normalized;
        }

        private void TieRod(Transform rack, string id, FrontCorner c, float housingHalf)
        {
            string side = c.Side.ToLowerInvariant();
            Vector3 joint = g.RackJoint(c.S < 0f);
            Vector3 dir = (c.Tre - joint).normalized;
            float total = Vector3.Distance(joint, c.Tre);
            Transform tie = ctx.SubAssembly(rack, id + ".tie-rod-" + side, new Txt(c.Side + " Tie Rod", (c.S < 0 ? "Chap" : "O‘ng") + " rul tortqisi",
                    "Inner rod, boot and outer end that push and pull the steering arm; the threads set the toe.",
                    "Burilish richagini itaruvchi va tortuvchi ichki tortqi, g‘ilof va tashqi uch; rezba orqali yaqinlashuv rostlanadi."),
                "Steel", new Vector3(c.S, 0f, 0f), 0.9f);
            Quaternion along = InternalsContext.AlongY(dir, Vector3.forward);
            float innerLen = total - 0.092f;
            Mesh inner = ctx.Mesh("inner-tie-rod-" + side, k =>
            {
                k.Lathe(new[] { new Vector2(0f, -0.018f), new Vector2(0.012f, -0.016f), new Vector2(0.017f, -0.006f), new Vector2(0.017f, 0.006f), new Vector2(0.012f, 0.016f), new Vector2(0.008f, 0.022f), new Vector2(0f, 0.022f) }, 16, 50f);
                k.Cylinder(0.0085f, 0.02f, innerLen, 12);
            });
            ctx.Part(tie, id + ".tie-rod-" + side + ".inner", new Txt("Inner tie rod", "Ichki rul tortqisi", "Rod with an axial ball joint screwed into the rack end.", "Reyka uchiga buralgan o‘q bo‘ylab sharli bo‘g‘inli tortqi."),
                "Forged steel", inner, new[] { m.ForgedSteel }, joint, along);
            // Boot from the housing end to the rod, past the inner joint.
            Vector3 bootFrom = new Vector3(c.S * housingHalf, g.RackY, g.RackZ);
            Vector3 bootTo = joint + dir * 0.1f;
            Vector3 bootDir = (bootTo - bootFrom).normalized;
            float bootLength = Vector3.Distance(bootFrom, bootTo);
            Quaternion bootRot = InternalsContext.AlongY(bootDir, Vector3.forward);
            Mesh boot = ctx.Mesh("rack-boot-" + side, k => k.Bellows(0.029f, 0.011f, 0.022f, 0.033f, bootLength, Mathf.Max(5, Mathf.RoundToInt(bootLength / 0.018f)), 20));
            ctx.Part(tie, id + ".tie-rod-" + side + ".boot", new Txt("Rack boot", "Reyka g‘ilofi", "Rubber bellows that seals the rack end and the inner joint.", "Reyka uchi va ichki bo‘g‘inni zichlovchi rezina gofra."),
                "Chloroprene rubber", boot, new[] { m.Rubber }, bootFrom, bootRot);
            Mesh clip = ctx.Mesh("boot-clip", k => k.Sleeve(0.031f, 0.028f, -0.003f, 0.003f, 18));
            Mesh clipSmall = ctx.Mesh("boot-clip-small", k => k.Sleeve(0.013f, 0.0105f, -0.003f, 0.003f, 14));
            ctx.Part(tie, id + ".tie-rod-" + side + ".clip-inner", new Txt("Boot clip", "G‘ilof xomuti", "Spring clip on the housing end.", "Korpus uchidagi prujinali xomut."),
                "Spring steel", clip, new[] { m.ZincSteel }, bootFrom + bootDir * 0.007f, bootRot, true);
            ctx.Part(tie, id + ".tie-rod-" + side + ".clip-outer", new Txt("Boot clip, small", "G‘ilof xomuti, kichik", "Clip on the tie rod.", "Tortqidagi xomut."),
                "Spring steel", clipSmall, new[] { m.ZincSteel }, bootTo - bootDir * 0.007f, bootRot, true);
            Mesh jam = ctx.Mesh("nut-M16-jam", k => Fasteners.HexNut(k, 0.016f));
            ctx.Part(tie, id + ".tie-rod-" + side + ".jam-nut", new Txt("Jam nut M16", "Kontrgayka M16", "Locks the outer end after the toe is set.", "Yaqinlashuv o‘rnatilgandan so‘ng tashqi uchni qulflaydi."),
                "Steel", jam, new[] { m.ZincSteel }, c.Tre - dir * 0.09f, along, true);

            Transform end = ctx.SubAssembly(tie, id + ".tie-rod-" + side + ".outer-end", new Txt("Outer Tie-Rod End", "Tortqi tashqi uchi",
                    "Ball joint that connects the tie rod to the knuckle's steering arm.",
                    "Tortqini burilish mushtining richagiga ulovchi sharli bo‘g‘in."),
                "Forged steel", Vector3.up, 0.6f);
            Mesh body = ctx.Mesh("tie-rod-end-" + side, k =>
            {
                k.Push(Matrix4x4.Translate(-c.Tre));
                k.Push(c.Tre, Quaternion.identity);
                k.Lathe(new[] { new Vector2(0f, -0.022f), new Vector2(0.016f, -0.022f), new Vector2(0.021f, -0.012f), new Vector2(0.021f, 0.006f), new Vector2(0.015f, 0.012f), new Vector2(0f, 0.012f) }, 18, 35f);
                k.Pop();
                Vector3 shankEnd = c.Tre - dir * 0.086f;
                k.Tube(new[] { c.Tre - dir * 0.012f + Vector3.down * 0.004f, shankEnd }, 0.0105f, 12, false, true, Vector3.up, x => Mathf.Lerp(1.25f, 0.95f, x));
                k.Pop();
            });
            ctx.Part(end, id + ".tie-rod-" + side + ".outer-end.housing", new Txt("Tie-rod end housing", "Tortqi uchi korpusi", "Forged eye with the ball seat and the threaded shank.", "Shar o‘rindig‘i va rezbali dumli bolg‘alangan ko‘z."),
                "Forged steel", body, new[] { m.ForgedSteel }, c.Tre, Quaternion.identity);
            Mesh stud = ctx.Mesh("tie-rod-end-stud", k => k.Lathe(new[] { new Vector2(0f, -0.012f), new Vector2(0.011f, -0.006f), new Vector2(0.011f, 0.004f), new Vector2(0.0085f, 0.012f), new Vector2(0.007f, 0.05f), new Vector2(0.0055f, 0.054f), new Vector2(0f, 0.054f) }, 12, 40f));
            ctx.Part(end, id + ".tie-rod-" + side + ".outer-end.stud", new Txt("Ball stud", "Sharli barmoq", "Tapered stud locked in the steering arm.", "Burilish richagiga konus bilan mahkamlangan sharli barmoq."),
                "Case-hardened steel", stud, new[] { m.MachinedSteel }, c.Tre, Quaternion.identity, false, Vector3.up, 0.5f);
            Mesh tBoot = ctx.Mesh("tie-rod-end-boot", k => k.Lathe(new[] { new Vector2(0.007f, 0.004f), new Vector2(0.018f, 0.006f), new Vector2(0.02f, 0.012f), new Vector2(0.012f, 0.02f), new Vector2(0.008f, 0.021f) }, 16, 50f));
            ctx.Part(end, id + ".tie-rod-" + side + ".outer-end.boot", new Txt("Joint boot", "Bo‘g‘in g‘ilofi", "Rubber boot over the ball.", "Shar ustidagi rezina g‘ilof."),
                "Rubber", tBoot, new[] { m.Rubber }, c.Tre, Quaternion.identity);
            Mesh castle = ctx.Mesh("nut-M12-castle", k => Fasteners.HexNut(k, 0.012f));
            ctx.Part(end, id + ".tie-rod-" + side + ".outer-end.nut", new Txt("Castle nut M12", "Tojsimon gayka M12", "Slotted nut on top of the steering arm.", "Burilish richagi ustidagi kesikli gayka."),
                "Steel", castle, new[] { m.ZincSteel }, c.Tre + Vector3.up * 0.038f, Quaternion.identity, true);
        }

        // ------------------------------------------------------------------------------------------------ column

        private Vector3 ColumnStart() => ctx.Layout.SteeringWheelCentre + ctx.Layout.SteeringAxis * 0.035f;
        private Vector3 UpperJoint() => ColumnStart() + ctx.Layout.SteeringAxis * 0.4f;

        private void Column(Transform system)
        {
            string id = "steering.column";
            CarLayout l = ctx.Layout;
            Vector3 axis = l.SteeringAxis;
            Vector3 start = ColumnStart(), uj1 = UpperJoint();
            Vector3 pinBase = new Vector3(g.PinionX, g.RackY, g.RackZ);
            Vector3 pinAxis = PinionAxis(pinBase);
            Vector3 uj2 = pinBase + pinAxis * 0.1f;
            Vector3 shaftDir = (uj2 - uj1).normalized;
            Transform column = ctx.Assembly(system, id, new Txt("Steering Column", "Rul kolonkasi",
                    "Collapsible column from the steering wheel to the rack: jacket, shafts and two universal joints.",
                    "Rul chambaragidan reykagacha bo‘lgan yig‘iluvchan kolonka: g‘ilof, vallar va ikki kardan sharnir."),
                "Steel", Vector3.up, 1f);
            Quaternion onAxis = InternalsContext.AlongY(axis, Vector3.up);
            Transform jacket = ctx.SubAssembly(column, id + ".jacket", new Txt("Column Jacket", "Kolonka g‘ilofi",
                    "Outer tube with bearings, the tilt mechanism and the bracket under the dashboard; it collapses in a crash.",
                    "Podshipnikli tashqi quvur, egilish mexanizmi va panel ostidagi kronshteyn; to‘qnashuvda qisqaradi."),
                "Steel and aluminium", Vector3.up, 0.6f);
            Mesh tube = ctx.Mesh("column-jacket", k => k.Lathe(new[]
            {
                new Vector2(0.014f, 0.05f), new Vector2(0.032f, 0.05f), new Vector2(0.032f, 0.09f), new Vector2(0.027f, 0.1f), new Vector2(0.027f, 0.2f),
                new Vector2(0.03f, 0.205f), new Vector2(0.03f, 0.26f), new Vector2(0.024f, 0.27f), new Vector2(0.024f, 0.36f), new Vector2(0.014f, 0.36f)
            }, 22, 35f));
            ctx.Part(jacket, id + ".jacket.tube", new Txt("Column jacket", "Kolonka quvuri", "Telescopic steel tube that collapses to absorb energy in a crash.", "To‘qnashuvda energiyani yutish uchun qisqaradigan teleskopik po‘lat quvur."),
                "Steel tube", tube, new[] { m.BlackSteel }, start, onAxis);
            Vector3 side = Vector3.Cross(axis, Vector3.up).normalized;
            Vector3 brUp = Vector3.Cross(side, axis).normalized;
            Vector3 b0 = start + axis * 0.2f;
            Mesh bracket = ctx.Mesh("column-bracket", k =>
            {
                k.PlanarPrism(new[] { b0 + side * 0.07f + brUp * 0.045f, b0 - side * 0.07f + brUp * 0.045f, b0 - side * 0.035f + brUp * 0.02f, b0 + side * 0.035f + brUp * 0.02f }, 0.005f);
                foreach (float sgn in new[] { -1f, 1f })
                    k.PlanarPrism(new[] { b0 + side * sgn * 0.034f + brUp * 0.045f + axis * 0.03f, b0 + side * sgn * 0.034f + brUp * 0.045f - axis * 0.03f,
                        b0 + side * sgn * 0.034f - brUp * 0.01f - axis * 0.02f, b0 + side * sgn * 0.034f - brUp * 0.01f + axis * 0.02f }, 0.004f);
            });
            ctx.Part(jacket, id + ".jacket.bracket", new Txt("Mounting bracket", "Mahkamlash kronshteyni", "Bracket that hangs the column from the cross-car beam.", "Kolonkani ko‘ndalang to‘singa osib qo‘yuvchi kronshteyn."),
                "Pressed steel", bracket, new[] { m.BlackSteel }, Vector3.zero, Quaternion.identity);
            Mesh lever = ctx.Mesh("tilt-lever", k => k.Tube(new[] { Vector3.zero, new Vector3(0.0f, -0.02f, -0.06f), new Vector3(0.0f, -0.03f, -0.1f) }, 0.006f, 8, true, true, Vector3.right));
            ctx.Part(jacket, id + ".jacket.lever", new Txt("Tilt and reach lever", "Egilish va uzunlik dastagi", "Unlocks the column to adjust tilt and reach.", "Kolonkani egish va uzaytirish uchun qulfdan chiqaradi."),
                "Plastic over steel", lever, new[] { m.BlackPlastic }, start + axis * 0.16f - brUp * 0.03f + side * 0.034f, Quaternion.LookRotation(axis, brUp), true);
            Mesh bolt = ctx.Mesh("bolt-M8x25", k => Fasteners.HexBolt(k, 0.008f, 0.025f));
            foreach (float sgn in new[] { -1f, 1f })
                ctx.Part(jacket, id + ".jacket.bolt-" + (sgn < 0 ? "left" : "right"), new Txt("Bolt M8", "Bolt M8", "Bolts the bracket to the beam.", "Kronshteynni to‘singa mahkamlaydi."),
                    "Steel 8.8", bolt, new[] { m.ZincSteel }, b0 + side * sgn * 0.055f + brUp * 0.048f, InternalsContext.AlongY(brUp), true);

            float upperLen = Vector3.Distance(start, uj1) - 0.02f;
            Mesh upper = ctx.Mesh("column-upper-shaft", k =>
            {
                k.Cylinder(0.0105f, -0.03f, upperLen, 14);
                k.Lathe(new[] { new Vector2(0f, -0.04f), new Vector2(0.014f, -0.04f), new Vector2(0.014f, -0.02f), new Vector2(0f, -0.02f) }, 16, 40f, 0f, 360f, true);
            });
            ctx.Part(column, id + ".upper-shaft", new Txt("Upper shaft", "Yuqori val", "Splined shaft that carries the steering wheel hub.", "Rul chambaragi gupchagini ko‘taruvchi shlitsali val."),
                "Steel", upper, new[] { m.MachinedSteel }, start, onAxis, false, -axis, 0.5f);
            UJoint(column, id + ".upper-joint", "Upper Universal Joint", "Yuqori kardan sharnir", uj1, -axis, shaftDir);
            Transform inter = ctx.SubAssembly(column, id + ".intermediate", new Txt("Intermediate Shaft", "Oraliq val",
                    "Telescopic shaft that slides to absorb body flex and crash intrusion.",
                    "Kuzov egilishi va to‘qnashuvdagi siljishni yutuvchi teleskopik val."),
                "Steel", Vector3.down, 0.6f);
            float iLen = Vector3.Distance(uj1, uj2);
            Quaternion onShaft = InternalsContext.AlongY(shaftDir, Vector3.up);
            Mesh iTube = ctx.Mesh("intermediate-tube", k => k.Sleeve(0.0135f, 0.0105f, 0.03f, iLen * 0.6f, 14));
            ctx.Part(inter, id + ".intermediate.tube", new Txt("Telescopic tube", "Teleskopik quvur", "Splined tube over the sliding shaft.", "Sirpanuvchi val ustidagi shlitsali quvur."),
                "Steel tube", iTube, new[] { m.ZincSteel }, uj1, onShaft);
            Mesh iBar = ctx.Mesh("intermediate-bar", k => k.Cylinder(0.0098f, iLen * 0.4f, iLen - 0.03f, 12));
            ctx.Part(inter, id + ".intermediate.bar", new Txt("Splined shaft", "Shlitsali val", "Solid shaft with a coated spline sliding in the tube.", "Quvur ichida sirpanuvchi qoplamali shlitsali yaxlit val."),
                "Steel", iBar, new[] { m.MachinedSteel }, uj1, onShaft);
            UJoint(column, id + ".lower-joint", "Lower Universal Joint", "Pastki kardan sharnir", uj2, -shaftDir, -pinAxis);

            // Bulkhead seal where the pinion shaft passes the bulkhead.
            float fz = l.FirewallZ;
            float along = Mathf.Abs(pinAxis.z) > 1e-3f ? Mathf.Clamp((fz - pinBase.z) / pinAxis.z, 0.03f, 0.09f) : 0.06f;
            Vector3 sealAt = pinBase + pinAxis * along;
            Mesh seal = ctx.Mesh("bulkhead-seal", k => k.Lathe(new[] { new Vector2(0.0145f, -0.02f), new Vector2(0.02f, -0.02f), new Vector2(0.034f, -0.004f), new Vector2(0.034f, 0.004f), new Vector2(0.02f, 0.012f), new Vector2(0.0145f, 0.012f), new Vector2(0.0145f, -0.02f) }, 20, 40f));
            ctx.Part(column, id + ".seal", new Txt("Bulkhead seal", "Bo‘lim devori zichlagichi", "Rubber grommet with a bearing that seals the shaft passage.", "Val o‘tish joyini zichlovchi podshipnikli rezina vtulka."),
                "EPDM rubber", seal, new[] { m.Rubber }, sealAt, InternalsContext.AlongY(pinAxis, Vector3.up), false, Vector3.back, 0.4f);
        }

        // Universal joint at a centre between two shafts (directions pointing away from the joint).
        private void UJoint(Transform parent, string id, string en, string uz, Vector3 centre, Vector3 dirA, Vector3 dirB)
        {
            Transform j = ctx.SubAssembly(parent, id, new Txt(en, uz,
                    "Cardan joint: two yokes and a needle-bearing cross let the shaft turn through an angle.",
                    "Kardan sharnir: ikki vilka va ignasimon podshipnikli krestovina valni burchak ostida aylantiradi."),
                "Steel", Vector3.zero, 0.5f);
            Mesh yoke = ctx.Mesh("u-joint-yoke", k =>
            {
                // Hub along +Y (away from the joint), ears toward the joint centre (y = 0) on +-X.
                k.Lathe(new[] { new Vector2(0.0105f, 0.022f), new Vector2(0.017f, 0.022f), new Vector2(0.017f, 0.05f), new Vector2(0.0105f, 0.05f), new Vector2(0.0105f, 0.022f) }, 16);
                foreach (float sx in new[] { -1f, 1f })
                {
                    k.Box(new Vector3(sx * 0.016f, 0.012f, 0f), new Vector3(0.006f, 0.024f, 0.02f));
                    k.Push(new Vector3(sx * 0.016f, 0f, 0f), InternalsContext.AlongY(Vector3.right * sx));
                    k.Cylinder(0.01f, -0.003f, 0.004f, 14);
                    k.Pop();
                }
            });
            Mesh spider = ctx.Mesh("u-joint-spider", k =>
            {
                k.RoundedBox(Vector3.zero, Vector3.one * 0.014f, 0.003f, 1);
                foreach (Vector3 d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                {
                    k.Push(Vector3.zero, InternalsContext.AlongY(d));
                    k.Cylinder(0.005f, 0.004f, 0.017f, 10);
                    k.Lathe(new[] { new Vector2(0f, 0.01f), new Vector2(0.0075f, 0.01f), new Vector2(0.0075f, 0.019f), new Vector2(0f, 0.019f) }, 12);
                    k.Pop();
                }
            });
            Quaternion ra = InternalsContext.AlongY(dirA, Vector3.up);
            Vector3 earA = ra * Vector3.right;
            Vector3 earB = Vector3.ProjectOnPlane(Vector3.Cross(dirB, earA), dirB).normalized;
            if (earB.sqrMagnitude < 1e-6f) earB = Vector3.ProjectOnPlane(Vector3.up, dirB).normalized;
            Quaternion rb = Quaternion.LookRotation(Vector3.Cross(earB, dirB.normalized), dirB.normalized);
            ctx.Part(j, id + ".yoke-input", new Txt("Input yoke", "Kirish vilkasi", "Yoke clamped on the upstream shaft.", "Yuqoridagi valga qisilgan vilka."),
                "Forged steel", yoke, new[] { m.ForgedSteel }, centre, ra);
            ctx.Part(j, id + ".yoke-output", new Txt("Output yoke", "Chiqish vilkasi", "Yoke clamped on the downstream shaft.", "Pastdagi valga qisilgan vilka."),
                "Forged steel", yoke, new[] { m.ForgedSteel }, centre, rb);
            ctx.Part(j, id + ".spider", new Txt("Cross spider", "Krestovina", "Cross with four needle-bearing caps.", "To‘rtta ignasimon podshipnik qopqoqli krestovina."),
                "Case-hardened steel", spider, new[] { m.MachinedSteel }, centre, Quaternion.LookRotation(earB, Vector3.Cross(earB, earA).normalized) , true);
        }
    }
}
#endif
