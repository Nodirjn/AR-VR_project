#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace CarInternals
{
    /// <summary>
    /// Powertrain (clutch and flywheel, transaxle, engine and transmission mounts) and driveline (front half shafts,
    /// electric rear drive unit, rear half shafts, hybrid battery) of the hero car.
    ///
    /// Layout, from the measured car: the transverse V6 sits ahead of the front axle with its crank output at the right
    /// end (V6 node "cam001"). A dual-mass flywheel and a dry clutch (opened by a concentric slave cylinder: the car has
    /// no clutch pedal) drive an automated six-speed two-shaft gearbox coaxial with the crank. The gearbox output turns a
    /// bevel transfer shaft that runs back over the steering rack and the anti-roll bar to the hypoid final drive, which
    /// sits on the front axle line in the pocket between the footwell shell and the right wheel-well wall; the half
    /// shafts follow the axle line to the steered hubs (the left one passes under the steering pedestal). The cabin has
    /// no tunnel (the central seats stand on the floor from the footwell to the rear axle), so there is no propshaft:
    /// the rear wheels are driven by a coaxial electric drive unit on the rear axle line (through-the-road hybrid
    /// all-wheel drive), fed by a compact high-power battery beside it.
    /// </summary>
    [CarInternalsSubsystem("Powertrain", 50)]
    public sealed class Drivetrain : ICarInternalsSubsystem
    {
        private InternalsContext ctx;
        private CarPalette m;
        private CarLayout l;
        private Material friction;

        // Hardpoints, car space (see Geometry()).
        private float ay, az, fx;          // crank axis height and z, drive-flange face x
        private Vector3 B;                 // gearbox output shaft axis (x unused)
        private Vector3 C;                 // front final drive centre (axis along x)
        private float caseEnd;             // right end of the gear case
        private Vector3 T0, T1;            // transfer shaft: upper bevel pinion, lower (hypoid) pinion
        private Vector3 E;                 // rear drive unit axis (x unused)
        private float bayFloorLeft, bayFloorRight, rearFloor, rearStep;

        private static readonly Quaternion AlongX = InternalsContext.AlongY(Vector3.right, Vector3.up);
        private static readonly Quaternion AlongNegX = InternalsContext.AlongY(Vector3.left, Vector3.up);

        public void Build(InternalsContext context)
        {
            ctx = context;
            m = ctx.Mats;
            l = ctx.Layout;
            friction = m.Get("CI_Friction_Lining", new Color(0.17f, 0.15f, 0.13f), 0f, 0.22f);
            Geometry();

            Transform power = ctx.System("Powertrain");
            Clutch(power);
            Transaxle(power);
            Mounts(power);

            Transform drive = ctx.System("Driveline");
            FrontHalfShafts(drive);
            RearDriveUnit(drive);
            RearHalfShafts(drive);
            HybridBattery(drive);

            // The glTF's crude axle bar (wheel centre to wheel centre on both axles) stands where the real half shafts
            // now are and would hide them: its renderer is switched off (idempotent; the explorer skips disabled
            // renderers). Contacts with it are therefore not collisions.
            Transform axles = l.Car != null ? l.Car.Find("Axles") : null;
            Renderer bar = axles != null ? axles.GetComponent<Renderer>() : null;
            if (bar != null) bar.enabled = false;
            ctx.Note("drivetrain: CarConcept/Axles renderer " + (bar != null ? "disabled (replaced by the half shafts)" : "not found"));
            ctx.AllowContact("", "Axles");
            Contacts();
        }

        // ------------------------------------------------------------------------------------------------ geometry

        private void Geometry()
        {
            if (!l.V6Parts.TryGetValue("cam001", out Bounds flange))
                flange = l.V6Parts.TryGetValue("Crankshaft", out Bounds crank)
                    ? new Bounds(new Vector3(crank.max.x - 0.03f, crank.center.y + 0.012f, crank.center.z), new Vector3(0.02f, 0.13f, 0.13f))
                    : new Bounds(new Vector3(0.28f, l.WheelCentreY - 0.11f, l.FrontAxleZ + 0.48f), new Vector3(0.02f, 0.13f, 0.13f));
            ay = flange.center.y;
            az = flange.center.z;
            fx = flange.max.x;
            // Output shaft 85 mm from the input, above and behind it (toward the final drive), clear of the bay floor.
            B = new Vector3(0f, ay + 0.06f, az - 0.06f);
            float wall = l.FrontWellWallX;
            caseEnd = Mathf.Min(fx + 0.32f, wall + 0.02f);
            // Final drive on the axle line between the footwell shell and the right wheel-well wall: low enough for the
            // left shaft to pass under the steering pedestal, high enough for the transfer tube to clear the anti-roll
            // bar, a little ahead of the axle so the right inner joint stays under 22 degrees at full lock.
            C = new Vector3(wall - 0.075f, l.WheelCentreY - 0.054f, l.FrontAxleZ + 0.02f);
            // Transfer shaft: upper pinion behind the output shaft's bevel gear (between 2nd and 3rd), lower pinion on the
            // crown wheel's face.
            T0 = new Vector3(fx + 0.1815f, B.y, B.z - 0.045f);
            T1 = new Vector3(C.x - 0.017f, C.y + 0.042f, C.z + 0.045f);
            E = new Vector3(0f, (l.RearLeft.Centre.y + l.RearRight.Centre.y) * 0.5f - 0.034f, l.RearAxleZ + 0.02f);
            bayFloorLeft = Floor(new Vector3(-0.40f, 0.6f, az - 0.14f), l.FloorTopY + 0.03f);
            bayFloorRight = Floor(new Vector3(0.69f, 0.6f, az + 0.04f), l.FloorTopY + 0.03f);
            rearFloor = Floor(new Vector3(-0.07f, 0.6f, E.z + 0.115f), l.FloorTopY + 0.03f);
            rearStep = Floor(new Vector3(MotorX0 + 0.11f, 0.6f, E.z - 0.16f), rearFloor);
            ctx.Note("drivetrain: crank axis y " + CarLayout.F(ay) + " z " + CarLayout.F(az) + ", drive flange x " + CarLayout.F(fx) +
                     ", output shaft y " + CarLayout.F(B.y) + " z " + CarLayout.F(B.z) + ", front final drive " + CarLayout.V(C) +
                     ", rear drive axis y " + CarLayout.F(E.y) + " z " + CarLayout.F(E.z) + ", bay floor " + CarLayout.F(bayFloorLeft) + " / " + CarLayout.F(bayFloorRight) +
                     ", rear floor " + CarLayout.F(rearFloor) + " step " + CarLayout.F(rearStep));
        }

        private float Floor(Vector3 from, float fallback) => ctx.Probe.Down(from, 1f, out float y) ? y : fallback;

        private Vector3 A(float x) => new Vector3(x, ay, az);

        // ------------------------------------------------------------------------------------------------ helpers

        private static Txt T(string en, string uz, string dEn, string dUz) => new Txt(en, uz, dEn, dUz);

        private static Vector2[] Pr(params float[] v)
        {
            var p = new Vector2[v.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = new Vector2(v[2 * i], v[2 * i + 1]);
            return p;
        }

        private Transform Pt(Transform parent, string id, Txt t, string material, Mesh mesh, Material mat, Vector3 pos, Quaternion rot,
            bool fine = false, Vector3? explode = null, float dist = 1f)
            => ctx.Part(parent, id, t, material, mesh, new[] { mat }, pos, rot, fine, explode, dist);

        // Hex bolt with its head at `head` and the shank running along `into` (into the joint): a light mesh (about 120
        // triangles) because the drivetrain carries some fifty of them.
        private void Bolt(Transform parent, string id, Vector3 head, Vector3 into, float d, float len, string dEn, string dUz)
        {
            int dm = Mathf.RoundToInt(d * 1000f);
            Mesh mesh = ctx.Mesh("dt-bolt-M" + dm + "x" + Mathf.RoundToInt(len * 1000f), k => DriveKit.LightBolt(k, d, len));
            ctx.Part(parent, id, T("Bolt M" + dm, "Bolt M" + dm, dEn, dUz), "Steel 10.9, zinc flake", mesh, new[] { m.ZincSteel },
                head, InternalsContext.AlongY(-into), true, -into, 0.4f);
        }

        private void Clamp(Transform parent, string id, float r, Vector3 at, Vector3 axis, bool large)
        {
            Mesh mesh = ctx.Mesh("dt-boot-clamp-" + Mathf.RoundToInt(r * 1000f), k =>
            {
                k.Sleeve(r + 0.002f, r, -0.004f, 0.004f, 10);
                k.Box(new Vector3(r + 0.004f, 0f, 0f), new Vector3(0.005f, 0.008f, 0.006f));
            });
            ctx.Part(parent, id, T(large ? "Boot clamp, large" : "Boot clamp, small", large ? "G‘ilof xomuti, katta" : "G‘ilof xomuti, kichik",
                    "Stainless ear clamp that seals the boot on its seat.", "G‘ilofni o‘rindig‘iga zich qisuvchi zanglamas quloqli xomut."),
                "Stainless steel", mesh, new[] { m.ZincSteel }, at, InternalsContext.AlongY(axis), true, axis, 0.3f);
        }

        // ------------------------------------------------------------------------------------------------ clutch

        private void Clutch(Transform system)
        {
            string id = "powertrain.clutch";
            Transform asm = ctx.Assembly(system, id, T("Clutch and Flywheel", "Ilashish muftasi va maxovik",
                    "Dual-mass flywheel and a single dry clutch on the crank's output end: couples the V6 to the transaxle and opens for each gear change.",
                    "Tirsakli val chiqish uchidagi ikki massali maxovik va bitta quruq ilashish muftasi: V6 ni transakslga ulaydi va har bir uzatma almashishida ajraladi."),
                "Cast iron and steel", new Vector3(0.25f, 1f, 0f), 1f);

            Transform fw = ctx.SubAssembly(asm, id + ".flywheel", T("Dual-Mass Flywheel", "Ikki massali maxovik",
                    "Primary and secondary masses joined by arc springs: store the crank's energy and filter its torsional vibration.",
                    "Yoysimon prujinalar bilan bog‘langan birlamchi va ikkilamchi massalar: tirsakli val energiyasini saqlaydi va buralish tebranishlarini so‘ndiradi."),
                "Cast iron and steel", Vector3.left, 0.5f);
            Mesh primary = ctx.Mesh("dt-flywheel-primary", k => k.Lathe(Pr(0.027f, 0.002f, 0.113f, 0.002f, 0.113f, 0.024f, 0.106f, 0.024f, 0.106f, 0.008f, 0.027f, 0.008f, 0.027f, 0.002f), 28, 40f));
            Pt(fw, id + ".flywheel.primary-mass", T("Primary mass", "Birlamchi massa",
                    "Dished steel plate bolted to the crank flange; its rim carries the starter ring gear and encloses the arc springs.",
                    "Tirsakli val flanetsiga boltlangan botiq po‘lat disk; gardishi starter tishli gardishini ko‘taradi va yoysimon prujinalarni o‘rab turadi."),
                "Steel", primary, m.CastIron, A(fx), AlongX, false, Vector3.left, 0.4f);
            Mesh ring = ctx.Mesh("dt-ring-gear", k => DriveKit.Gear(k, 50, 0.1130f, 0.1200f, 0.004f, 0.016f, 0.1125f, 25));
            Pt(fw, id + ".flywheel.ring-gear", T("Starter ring gear", "Starter tishli gardishi",
                    "Hardened ring gear shrunk onto the primary mass; the starter pinion engages it to crank the engine.",
                    "Birlamchi massaga qizdirib o‘rnatilgan toblangan tishli gardish; starter shesternyasi dvigatelni aylantirish uchun unga ilashadi."),
                "Induction-hardened steel", ring, m.MachinedSteel, A(fx), AlongX, false, Vector3.up, 0.4f);
            Mesh secondary = ctx.Mesh("dt-flywheel-secondary", k => k.Lathe(Pr(0.055f, 0.010f, 0.104f, 0.010f, 0.104f, 0.033f, 0.101f, 0.036f, 0.058f, 0.036f, 0.055f, 0.033f, 0.055f, 0.010f), 28, 40f));
            Pt(fw, id + ".flywheel.secondary-mass", T("Secondary mass", "Ikkilamchi massa",
                    "Cast-iron flywheel with the ground friction face for the clutch disc; turns on a bearing against the arc springs.",
                    "Ilashish diski uchun silliqlangan ishqalanish yuzali cho‘yan maxovik; yoysimon prujinalarga qarshi podshipnikda aylanadi."),
                "Grey cast iron", secondary, m.CastIron, A(fx), AlongX, false, Vector3.right, 0.4f);
            for (int i = 0; i < 6; i++)
            {
                float a = (30f + 60f * i) * Mathf.Deg2Rad;
                Vector3 p = A(fx + 0.008f) + new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a)) * 0.040f;
                Bolt(fw, id + ".flywheel.bolt-" + (i + 1), p, Vector3.left, 0.010f, 0.020f,
                    "Stretch bolt that clamps the flywheel to the crank flange.", "Maxovikni tirsakli val flanetsiga qisuvchi cho‘ziluvchan bolt.");
            }

            Transform disc = ctx.SubAssembly(asm, id + ".disc", T("Clutch Disc", "Ilashish diski",
                    "Friction disc splined to the gearbox input shaft, with a spring damper in its hub.",
                    "Uzatmalar qutisi birlamchi valiga shlitsa bilan kiygizilgan, gupchagida prujinali dempferi bor friksion disk."),
                "Steel and friction material", Vector3.right, 0.3f);
            Mesh lining = ctx.Mesh("dt-clutch-lining", k => k.Lathe(Pr(0.064f, 0.037f, 0.101f, 0.037f, 0.101f, 0.047f, 0.064f, 0.047f, 0.064f, 0.037f), 28, 40f));
            Pt(disc, id + ".disc.lining", T("Friction linings", "Friksion qoplamalar",
                    "Two organic friction rings riveted to cushion segments; they grip the flywheel and the pressure plate.",
                    "Yumshatuvchi segmentlarga parchinlangan ikki organik friksion halqa; maxovik va bosim diskini mahkam ushlaydi."),
                "Organic friction compound", lining, friction, A(fx), AlongX, false, Vector3.right, 0.3f);
            Mesh hub = ctx.Mesh("dt-clutch-hub", k =>
            {
                k.Lathe(Pr(0.0135f, 0.030f, 0.028f, 0.030f, 0.028f, 0.066f, 0.0135f, 0.066f, 0.0135f, 0.030f), 20, 40f);
                foreach (float y in new[] { 0.0385f, 0.0455f })
                    k.Lathe(Pr(0.028f, y - 0.001f, 0.066f, y - 0.001f, 0.066f, y + 0.001f, 0.028f, y + 0.001f, 0.028f, y - 0.001f), 20, 40f);
            });
            Pt(disc, id + ".disc.hub", T("Splined hub and side plates", "Shlitsali gupchak va yon plastinalar",
                    "Hub splined to the input shaft between two side plates that carry the linings.",
                    "Qoplamalarni ko‘taruvchi ikki yon plastina orasidagi, birlamchi valga shlitsali gupchak."),
                "Steel", hub, m.MachinedSteel, A(fx), AlongX, false, Vector3.right, 0.5f);
            Mesh springs = ctx.Mesh("dt-clutch-damper-springs", k =>
            {
                for (int i = 0; i < 6; i++)
                {
                    float a = (60f * i) * Mathf.Deg2Rad;
                    Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                    k.Push(radial * 0.047f + Vector3.up * 0.042f, InternalsContext.AlongY(tangent, Vector3.up));
                    var p = new List<Vector2> { new Vector2(0f, -0.013f) };
                    for (int c = 0; c < 3; c++)
                    {
                        float y = -0.013f + 0.0087f * c;
                        p.Add(new Vector2(0.0062f, y)); p.Add(new Vector2(0.0062f, y + 0.0065f)); p.Add(new Vector2(0.0048f, y + 0.0076f));
                    }
                    p.Add(new Vector2(0.0062f, 0.013f));
                    p.Add(new Vector2(0f, 0.013f));
                    k.Lathe(p, 6, 50f);
                    k.Pop();
                }
            });
            Pt(disc, id + ".disc.damper-springs", T("Torsion damper springs", "Buralish dempferi prujinalari",
                    "Six coil springs in the hub windows that soften torque shocks and gear rattle.",
                    "Gupchak oynalaridagi oltita prujina: moment zarbalarini va shesternya taqillashini yumshatadi."),
                "Spring steel", springs, m.SpringBlue, A(fx), AlongX, false, Vector3.up, 0.3f);

            Transform pp = ctx.SubAssembly(asm, id + ".pressure-plate", T("Clutch Cover and Pressure Plate", "Mufta korpusi va bosim diski",
                    "Pressed cover bolted to the flywheel; its diaphragm spring clamps the pressure plate onto the disc.",
                    "Maxovikka boltlangan presslangan korpus; diafragmali prujinasi bosim diskini ilashish diskiga qisadi."),
                "Steel and cast iron", Vector3.right, 0.6f);
            Mesh plate = ctx.Mesh("dt-pressure-plate", k => k.Lathe(Pr(0.064f, 0.047f, 0.101f, 0.047f, 0.101f, 0.058f, 0.098f, 0.060f, 0.067f, 0.060f, 0.064f, 0.058f, 0.064f, 0.047f), 28, 40f));
            Pt(pp, id + ".pressure-plate.plate", T("Pressure plate", "Bosim diski",
                    "Cast-iron ring that presses the disc against the flywheel and soaks up the slip heat.",
                    "Diskni maxovikka bosuvchi va sirpanish issiqligini yutuvchi cho‘yan halqa."),
                "Grey cast iron", plate, m.CastIron, A(fx), AlongX, false, Vector3.right, 0.3f);
            Mesh cover = ctx.Mesh("dt-clutch-cover", k => k.Lathe(Pr(0.106f, 0.036f, 0.113f, 0.036f, 0.113f, 0.0385f, 0.1085f, 0.0385f, 0.1085f, 0.065f, 0.1015f, 0.0725f,
                0.068f, 0.0725f, 0.068f, 0.070f, 0.100f, 0.070f, 0.106f, 0.064f, 0.106f, 0.036f), 28, 35f));
            Pt(pp, id + ".pressure-plate.cover", T("Clutch cover", "Mufta korpusi",
                    "Pressed-steel cover that carries the diaphragm spring's fulcrum rings.",
                    "Diafragmali prujina tayanch halqalarini ko‘taruvchi presslangan po‘lat korpus."),
                "Pressed steel", cover, m.BlackSteel, A(fx), AlongX, false, Vector3.right, 0.5f);
            Mesh diaphragm = ctx.Mesh("dt-diaphragm-spring", k =>
            {
                k.Lathe(Pr(0.071f, 0.0655f, 0.092f, 0.0655f, 0.092f, 0.0675f, 0.071f, 0.0675f, 0.071f, 0.0655f), 32, 40f);
                for (int i = 0; i < 18; i++)
                {
                    float deg = 20f * i;
                    k.Push(Vector3.zero, Quaternion.Euler(0f, -deg, 0f));
                    k.Push(new Vector3(0.049f, 0.0705f, 0f), Quaternion.Euler(0f, 0f, 12f));
                    k.Box(Vector3.zero, new Vector3(0.046f, 0.0018f, 0.011f));
                    k.Pop();
                    k.Pop();
                }
            });
            Pt(pp, id + ".pressure-plate.diaphragm", T("Diaphragm spring", "Diafragmali prujina",
                    "Slotted Belleville spring: its fingers are the release levers, its rim clamps the plate.",
                    "Kesikli tarelkasimon prujina: barmoqlari ajratish richaglari, gardishi esa diskni qisadi."),
                "Spring steel", diaphragm, m.ZincSteel, A(fx), AlongX, false, Vector3.right, 0.7f);
            for (int i = 0; i < 6; i++)
            {
                float a = (60f * i) * Mathf.Deg2Rad;
                Vector3 p = A(fx + 0.0385f) + new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a)) * 0.1095f;
                Bolt(pp, id + ".pressure-plate.bolt-" + (i + 1), p, Vector3.left, 0.008f, 0.016f,
                    "Bolts the clutch cover to the flywheel rim.", "Mufta korpusini maxovik gardishiga mahkamlaydi.");
            }

            Transform rel = ctx.SubAssembly(asm, id + ".release", T("Release System", "Ajratish tizimi",
                    "Electro-hydraulic actuator and concentric slave cylinder that open the clutch for the automated gearbox.",
                    "Avtomatlashtirilgan uzatmalar qutisi uchun muftani ajratuvchi elektrogidravlik aktuator va konsentrik ishchi silindr."),
                "Aluminium and steel", Vector3.right, 0.9f);
            Mesh bearing = ctx.Mesh("dt-release-bearing", k => k.Lathe(Pr(0.021f, 0.071f, 0.034f, 0.071f, 0.036f, 0.074f, 0.036f, 0.079f, 0.021f, 0.079f, 0.021f, 0.071f), 24, 40f));
            Pt(rel, id + ".release.bearing", T("Release bearing", "Ajratish podshipnigi",
                    "Angular-contact ball bearing that pushes on the spinning diaphragm fingers.",
                    "Aylanayotgan diafragma barmoqlarini bosuvchi radial-tirak sharikli podshipnik."),
                "Bearing steel", bearing, m.MachinedSteel, A(fx), AlongX, false, Vector3.right, 0.4f);
            Mesh csc = ctx.Mesh("dt-slave-cylinder", k =>
            {
                k.Lathe(Pr(0.020f, 0.079f, 0.030f, 0.079f, 0.030f, 0.086f, 0.046f, 0.088f, 0.048f, 0.104f, 0.020f, 0.104f, 0.020f, 0.079f), 28, 40f);
                k.Push(new Vector3(0f, 0.096f, 0.046f), Quaternion.identity);
                k.Box(new Vector3(0f, 0f, 0.01f), new Vector3(0.016f, 0.014f, 0.022f));
                k.Pop();
            });
            Pt(rel, id + ".release.slave-cylinder", T("Concentric slave cylinder", "Konsentrik ishchi silindr",
                    "Annular hydraulic piston around the input shaft that slides the release bearing.",
                    "Birlamchi val atrofidagi halqasimon gidravlik porshen; ajratish podshipnigini suradi."),
                "Aluminium and glass-filled nylon", csc, m.CastAluminium, A(fx), AlongX, false, Vector3.right, 0.6f);
            // Actuator on top of the bell housing; the pressure line drops straight into the slave cylinder's port.
            Vector3 actAt = new Vector3(fx + 0.075f, ay + 0.152f, az - 0.03f);
            Mesh actuator = ctx.Mesh("dt-clutch-actuator", k =>
            {
                k.Push(Vector3.zero, AlongX);
                k.Lathe(Pr(0f, -0.040f, 0.020f, -0.040f, 0.022f, -0.036f, 0.022f, 0.010f, 0.016f, 0.012f, 0.016f, 0.046f, 0.012f, 0.050f, 0f, 0.050f), 18, 40f);
                k.Pop();
                k.RoundedBox(new Vector3(0f, -0.012f, 0.03f), new Vector3(0.05f, 0.03f, 0.03f), 0.004f, 1);
                k.Box(new Vector3(0f, -0.026f, 0f), new Vector3(0.09f, 0.004f, 0.05f));
            });
            Pt(rel, id + ".release.actuator", T("Clutch actuator", "Mufta aktuatori",
                    "Electric motor and master cylinder that build the release pressure on the controller's command.",
                    "Boshqaruv bloki buyrug‘i bilan ajratish bosimini hosil qiluvchi elektr motor va bosh silindr."),
                "Aluminium, steel and copper", actuator, m.CastAluminium, actAt, Quaternion.identity, false, Vector3.up, 0.8f);
            Mesh plug = ctx.Mesh("dt-connector", k => k.RoundedBox(Vector3.zero, new Vector3(0.022f, 0.016f, 0.026f), 0.003f, 1));
            Pt(rel, id + ".release.connector", T("Actuator connector", "Aktuator ulagichi", "Sealed power and sensor connector.", "Quvvat va datchik uchun zichlangan ulagich."),
                "Glass-filled nylon", plug, m.BlackPlastic, actAt + new Vector3(-0.052f, 0f, 0f), Quaternion.identity, true, Vector3.left, 0.4f);
            Vector3 port = A(fx + 0.096f) + new Vector3(0f, 0.056f, 0f);
            Mesh line = ctx.Mesh("dt-clutch-line", k =>
            {
                Vector3 a = port - actAt, b = actAt + new Vector3(0.042f, -0.028f, -0.01f) - actAt;
                k.Tube(MeshKit.CatmullRom(new[] { a, a + new Vector3(0f, 0.04f, 0f), (a + b) * 0.5f + new Vector3(0f, 0.03f, -0.012f), b }, 6), 0.004f, 8);
            });
            Pt(rel, id + ".release.line", T("Pressure line", "Bosim quvuri",
                    "Braided hose from the actuator through the bell-housing grommet to the slave cylinder.",
                    "Aktuatordan mufta karteri vtulkasi orqali ishchi silindrga boradigan o‘rama shlang."),
                "PTFE hose, steel braid", line, m.BraidedHose, actAt, Quaternion.identity, true, Vector3.up, 0.5f);
        }

        // ------------------------------------------------------------------------------------------------ transaxle

        private void Transaxle(Transform system)
        {
            string id = "powertrain.transaxle";
            Transform asm = ctx.Assembly(system, id, T("Transaxle", "Transaksl",
                    "Automated six-speed gearbox on the crank axis with a bevel transfer to the front final drive and differential on the axle line.",
                    "Tirsakli val o‘qidagi avtomatlashtirilgan olti pog‘onali uzatmalar qutisi, o‘q chizig‘idagi old bosh uzatma va differensialga konussimon uzatuvchi val bilan."),
                "Cast aluminium and steel", new Vector3(1f, 0.35f, 0f), 1.2f);
            Case(asm, id);
            GearTrain(asm, id);
            FinalDrive(asm, id);
            ShiftActuator(asm, id);
        }

        // Section of the gear case: hull of the input-shaft and output-shaft circles, as (−dz, dy) about the crank axis.
        private List<Vector2> CaseHull(float inset)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i < 28; i++)
            {
                float a = Mathf.PI * 2f * i / 28f;
                pts.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (0.104f - inset));
                pts.Add(new Vector2(-(B.z - az), B.y - ay) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (0.094f - inset));
            }
            return DriveKit.ConvexHull(pts);
        }

        private void Case(Transform asm, string id)
        {
            Transform cs = ctx.SubAssembly(asm, id + ".case", T("Transaxle Case", "Transaksl karteri",
                    "Die-cast aluminium bell housing, gear case and end cover that hold the shafts' bearings and the oil.",
                    "Vallar podshipniklari va moyni ushlab turuvchi, bosim ostida quyilgan alyuminiy mufta karteri, uzatmalar karteri va yon qopqoq."),
                "Die-cast aluminium AlSi9Cu3", new Vector3(0f, 1f, 0.25f), 0.9f);
            float bellEnd = fx + 0.112f;
            Mesh bell = ctx.Mesh("dt-bell-housing", k =>
            {
                k.Lathe(Pr(0.118f, 0.000f, 0.135f, 0.000f, 0.135f, 0.008f, 0.128f, 0.012f, 0.128f, 0.112f, 0.034f, 0.112f, 0.034f, 0.104f, 0.122f, 0.104f, 0.122f, 0.008f, 0.118f, 0.008f, 0.118f, 0.000f), 28, 35f);
                // Lug for the torque rod at the bottom rear.
                k.Push(new Vector3(0f, 0.056f, 0f), Quaternion.identity);
                k.RoundedBox(new Vector3(-0.088f, 0f, -0.098f), new Vector3(0.05f, 0.03f, 0.036f), 0.004f, 1);
                k.Pop();
            });
            // Lathe axis +Y = car +x; local z = car up, local x = car +z (AlongX).
            Pt(cs, id + ".case.bell-housing", T("Bell housing", "Ilashish muftasi karteri",
                    "Front half of the case: bolts to the block and encloses the flywheel and clutch.",
                    "Karterning old yarmi: blokka boltlanadi va maxovik hamda muftani o‘rab turadi."),
                "Die-cast aluminium", bell, m.CastAluminium, A(fx), AlongX, false, Vector3.left, 0.5f);
            List<Vector2> outer = CaseHull(0f), inner = CaseHull(0.006f);
            Mesh gearCase = ctx.Mesh("dt-gear-case", k =>
            {
                var path = new[] { new Vector3(bellEnd - 0.004f, 0f, 0f), new Vector3(caseEnd, 0f, 0f) };
                k.Sweep(outer, path, Vector3.up, false, false, 25f);
                k.Sweep(inner, path, Vector3.up, false, false, 25f, true);
                DriveKit.RingFace(k, outer, inner, path[0].x, false);
                DriveKit.RingFace(k, outer, inner, path[1].x, true);
                // Ribs across the top.
                foreach (float x in new[] { 0.45f, 0.52f, 0.58f })
                    k.Box(new Vector3(x + (fx - 0.294f), B.y - ay + 0.094f, B.z - az), new Vector3(0.006f, 0.012f, 0.05f));
            });
            Pt(cs, id + ".case.gear-case", T("Gear case", "Uzatmalar karteri",
                    "Middle section with ribs: carries the input and output shaft bearings and the shift rails.",
                    "Qovurg‘ali o‘rta qism: birlamchi va ikkilamchi val podshipniklarini hamda almashtirish shtoklarini ko‘taradi."),
                "Die-cast aluminium", gearCase, m.CastAluminium, new Vector3(0f, ay, az), Quaternion.identity, false, Vector3.up, 0.6f);
            Mesh endCover = ctx.Mesh("dt-end-cover", k =>
            {
                List<Vector2> lip = CaseHull(0.004f);
                k.Sweep(lip, new[] { new Vector3(caseEnd, 0f, 0f), new Vector3(caseEnd + 0.011f, 0f, 0f) }, Vector3.up, true, true, 25f);
                k.Push(new Vector3(caseEnd + 0.011f, 0f, 0f), AlongX);
                k.Lathe(Pr(0f, 0f, 0.03f, 0f, 0.027f, 0.008f, 0f, 0.008f), 20, 40f);
                k.Pop();
            });
            Pt(cs, id + ".case.end-cover", T("End cover", "Yon qopqoq",
                    "Bolted cover over the shafts' outer bearings, sealed with liquid gasket.",
                    "Vallarning tashqi podshipniklari ustidagi, suyuq qistirma bilan zichlangan boltli qopqoq."),
                "Die-cast aluminium", endCover, m.CastAluminium, new Vector3(0f, ay, az), Quaternion.identity, false, Vector3.right, 0.6f);
            for (int i = 0; i < 6; i++)
            {
                float a = (30f + 60f * i) * Mathf.Deg2Rad;
                Vector3 p = A(fx + 0.008f) + new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a)) * 0.1265f;
                Bolt(cs, id + ".case.bell-bolt-" + (i + 1), p, Vector3.left, 0.010f, 0.035f,
                    "Bolts the bell housing to the cylinder block.", "Mufta karterini silindrlar blokiga mahkamlaydi.");
            }
            List<Vector2> boltRing = CaseHull(0.013f);
            for (int i = 0; i < 6; i++)
            {
                Vector2 q = boltRing[(i * boltRing.Count) / 6];
                Vector3 p = new Vector3(caseEnd + 0.011f, ay + q.y, az - q.x);
                Bolt(cs, id + ".case.cover-bolt-" + (i + 1), p, Vector3.left, 0.008f, 0.025f,
                    "Bolts the end cover to the gear case.", "Yon qopqoqni uzatmalar karteriga mahkamlaydi.");
            }
            Mesh breather = ctx.Mesh("dt-breather", k =>
            {
                k.Lathe(Pr(0f, 0f, 0.006f, 0f, 0.006f, 0.014f, 0.011f, 0.016f, 0.011f, 0.022f, 0.008f, 0.025f, 0f, 0.025f), 12, 40f);
            });
            Pt(cs, id + ".case.breather", T("Breather", "Sapun",
                    "Vent cap that lets the case breathe as the oil warms, without letting water in.",
                    "Moy qizishida karterga havo almashtirishga imkon beruvchi, suv kiritmaydigan sapun qopqog‘i."),
                "Glass-filled nylon", breather, m.BlackPlastic, new Vector3(fx + 0.27f, B.y + 0.092f, B.z), Quaternion.identity, true, Vector3.up, 0.4f);
            Mesh drain = ctx.Mesh("dt-plug-M18", k =>
            {
                k.Lathe(Pr(0f, 0f, 0.013f, 0f, 0.013f, 0.003f, 0f, 0.003f), 16, 40f);
                k.Lathe(Pr(0f, 0.003f, 0.0105f, 0.003f, 0.0105f, 0.011f, 0.009f, 0.0125f, 0f, 0.0125f), 6, 40f, 30f, 360f, true);
                k.Cylinder(0.009f, -0.014f, 0.001f, 12);
            });
            Pt(cs, id + ".case.drain-plug", T("Drain plug M18", "To‘kish tiqini M18",
                    "Magnetic plug at the low point of the case; catches wear particles.",
                    "Karterning eng past nuqtasidagi magnitli tiqin; yeyilish zarralarini ushlaydi."),
                "Steel", drain, m.ZincSteel, A(fx + 0.27f) + Vector3.down * 0.104f, InternalsContext.AlongY(Vector3.down), true, Vector3.down, 0.4f);
            Pt(cs, id + ".case.fill-plug", T("Fill plug M18", "Quyish tiqini M18",
                    "Oil level and fill plug on the front face of the case.", "Karterning old yuzasidagi moy sathi va quyish tiqini."),
                "Steel", drain, m.ZincSteel, A(fx + 0.25f) + Vector3.forward * 0.104f, InternalsContext.AlongY(Vector3.forward), true, Vector3.forward, 0.4f);
        }

        private void GearTrain(Transform asm, string id)
        {
            Transform gt = ctx.SubAssembly(asm, id + ".gear-train", T("Gear Train", "Tishli uzatmalar to‘plami",
                    "Two-shaft constant-mesh gearset: six forward pairs, three synchronisers and the bevel drive to the final drive.",
                    "Doimiy ilashuvli ikki valli tishli to‘plam: oltita oldinga yurish jufti, uchta sinxronizator va bosh uzatmaga konussimon uzatma."),
                "Case-hardened steel 20MnCr5", new Vector3(1f, 0f, 0f), 0.7f);
            // Module 4.25 mm, 40 teeth per pair, centre distance 85 mm. Pairs 1..6 along x from the bell housing.
            int[] inTeeth = { 10, 13, 17, 19, 21, 23 };
            float[] xs = { 0.132f, 0.165f, 0.1985f, 0.2315f, 0.248f, 0.281f };
            float[] sync = { 0.1485f, 0.215f, 0.2645f };
            const float mod = 0.00425f, w = 0.013f;
            float x0 = fx;
            Mesh input = ctx.Mesh("dt-input-shaft", k =>
            {
                k.Lathe(Pr(0f, 0.034f, 0.0115f, 0.034f, 0.0125f, 0.036f, 0.0125f, 0.105f, 0.017f, 0.107f, 0.017f, 0.312f, 0.012f, 0.316f, 0.012f, 0.326f, 0f, 0.326f), 16, 40f);
                for (int i = 0; i < 6; i++)
                {
                    float pr = inTeeth[i] * mod * 0.5f;
                    DriveKit.Gear(k, inTeeth[i], pr - 1.25f * mod, pr + mod, xs[i] - w * 0.5f, xs[i] + w * 0.5f, 0.016f, 0);
                }
                DriveKit.Gear(k, 13, 0.016f, 0.023f, 0.300f, 0.309f, 0.016f, 0);
            });
            Pt(gt, id + ".gear-train.input-shaft", T("Input shaft", "Birlamchi val",
                    "Splined into the clutch disc; carries the six drive gears cut from the shaft.",
                    "Ilashish diskiga shlitsa bilan kiradi; valning o‘zida kesilgan oltita yetakchi shesternyani ko‘taradi."),
                "Case-hardened steel 20MnCr5", input, m.MachinedSteel, A(x0), AlongX, false, Vector3.right, 0.5f);
            Mesh output = ctx.Mesh("dt-output-shaft", k =>
            {
                k.Lathe(Pr(0f, 0.115f, 0.012f, 0.115f, 0.014f, 0.118f, 0.014f, 0.316f, 0.012f, 0.319f, 0f, 0.319f), 16, 40f);
                for (int i = 0; i < 6; i++)
                {
                    int z = 40 - inTeeth[i];
                    float pr = z * mod * 0.5f;
                    DriveKit.Gear(k, z, pr - 1.25f * mod, pr + mod, xs[i] - w * 0.5f, xs[i] + w * 0.5f, 0.014f, 0);
                }
                foreach (float s in sync)
                {
                    // Synchroniser sleeve with the fork groove.
                    k.Lathe(Pr(0.026f, s - 0.008f, 0.043f, s - 0.008f, 0.043f, s - 0.0035f, 0.039f, s - 0.0035f, 0.039f, s + 0.0035f, 0.043f, s + 0.0035f,
                        0.043f, s + 0.008f, 0.026f, s + 0.008f, 0.026f, s - 0.008f), 20, 40f);
                    k.Lathe(Pr(0.014f, s - 0.006f, 0.026f, s - 0.006f, 0.026f, s + 0.006f, 0.014f, s + 0.006f, 0.014f, s - 0.006f), 16, 40f);
                }
                // Bevel gear that drives the transfer shaft (teeth toward the bell housing).
                k.Push(new Vector3(0f, 0.1745f, 0f), Quaternion.identity);
                DriveKit.BevelGear(k, 22, 0.042f, 0.030f, 0.014f, 0.014f);
                k.Pop();
            });
            Pt(gt, id + ".gear-train.output-shaft", T("Output shaft", "Ikkilamchi val",
                    "Carries the six free-running driven gears (30/27/23/21/19/17 teeth), the synchroniser hubs and the bevel gear of the transfer drive.",
                    "Erkin aylanuvchi oltita yetaklanuvchi shesternya, sinxronizator gupchaklari va uzatuvchi valning konussimon shesternyasini ko‘taradi."),
                "Case-hardened steel 20MnCr5", output, m.MachinedSteel, new Vector3(x0, B.y, B.z), AlongX, false, Vector3.up, 0.5f);
            // Reverse idler between the shafts.
            Vector3 idler = new Vector3(x0, ay - 0.004f, az - 0.06f);
            Mesh rev = ctx.Mesh("dt-reverse-idler", k =>
            {
                k.Cylinder(0.009f, 0.29f, 0.326f, 12);
                DriveKit.Gear(k, 15, 0.0215f, 0.0285f, 0.300f, 0.309f, 0.009f, 0);
            });
            Pt(gt, id + ".gear-train.reverse-idler", T("Reverse idler", "Orqa yurish oraliq shesternyasi",
                    "Idler gear on its own pin that reverses the output shaft's rotation in reverse.",
                    "Orqaga yurishda ikkilamchi val aylanishini teskari qiluvchi alohida o‘qdagi oraliq shesternya."),
                "Case-hardened steel", rev, m.MachinedSteel, idler, AlongX, false, Vector3.down, 0.5f);
            // Shift rail above the output shaft with three forks in the synchroniser grooves.
            Vector3 railAt = new Vector3(x0, B.y + 0.062f, B.z + 0.004f);
            Mesh forks = ctx.Mesh("dt-shift-forks", k =>
            {
                k.Push(Vector3.zero, AlongX);
                k.Cylinder(0.0065f, 0.125f, 0.318f, 12);
                k.Pop();
                foreach (float s in sync)
                {
                    var arc = new List<Vector3>();
                    for (int i = 0; i <= 12; i++)
                    {
                        float a = Mathf.PI * (0.15f + 0.7f * i / 12f);
                        arc.Add(new Vector3(s, -0.062f + Mathf.Sin(a) * 0.041f, -0.004f + Mathf.Cos(a) * 0.041f));
                    }
                    k.Tube(arc, 0.0032f, 8, true, true, Vector3.right, null, new Vector2(1f, 1.6f));
                    k.Box(new Vector3(s, -0.012f, 0f), new Vector3(0.012f, 0.03f, 0.016f));
                    k.Push(new Vector3(s, 0f, 0f), AlongX);
                    k.Cylinder(0.011f, -0.008f, 0.008f, 12);
                    k.Pop();
                }
            });
            Pt(gt, id + ".gear-train.shift-forks", T("Shift rail and forks", "Almashtirish shtoki va vilkalari",
                    "Forged forks on a rail that slide the synchroniser sleeves to engage each gear.",
                    "Har bir uzatmani ulash uchun sinxronizator muftalarini suruvchi shtokdagi bolg‘alangan vilkalar."),
                "Forged steel", forks, m.ForgedSteel, railAt, Quaternion.identity, false, Vector3.up, 0.6f);
        }

        private void FinalDrive(Transform asm, string id)
        {
            Transform fd = ctx.SubAssembly(asm, id + ".final-drive", T("Final Drive and Differential", "Bosh uzatma va differensial",
                    "Bevel transfer shaft, hypoid gear set and open differential on the front axle line.",
                    "Konussimon uzatuvchi val, gipoid tishli juftlik va old o‘q chizig‘idagi ochiq differensial."),
                "Aluminium and steel", new Vector3(0f, -0.25f, -1f), 0.8f);
            Vector3 td = (T1 - T0).normalized;
            float tLen = Vector3.Distance(T0, T1);
            Quaternion onC = AlongX;
            Vector3 c = C;
            // Housing: left half with the transfer tube, right half (cover) with the bolts.
            Mesh housing = ctx.Mesh("dt-final-drive-housing", k =>
            {
                k.Push(Vector3.zero, onC);
                k.Lathe(Pr(0.034f, -0.060f, 0.044f, -0.060f, 0.044f, -0.045f, 0.062f, -0.035f, 0.086f, -0.012f, 0.092f, 0.012f, 0.092f, 0.019f, 0.101f, 0.019f, 0.101f, 0.027f,
                    0.086f, 0.027f, 0.086f, 0.012f, 0.080f, -0.008f, 0.056f, -0.030f, 0.034f, -0.040f, 0.034f, -0.060f), 20, 35f);
                k.Pop();
                // Transfer tube from the housing's upper front to the gear case's rear wall, flanged at the top.
                Vector3 low = T1 - c + td * 0.03f, high = T0 - c + td * 0.05f;
                k.Tube(new[] { low, high }, 0.030f, 18, true, false, Vector3.right);
                k.Push(high, InternalsContext.AlongY(-td, Vector3.right));
                k.Lathe(Pr(0.030f, -0.010f, 0.046f, -0.010f, 0.046f, 0f, 0.030f, 0f), 18, 40f);
                k.Pop();
            });
            Pt(fd, id + ".final-drive.housing", T("Final-drive housing", "Bosh uzatma karteri",
                    "Cast housing of the differential with the tube that carries the transfer shaft up to the gear case.",
                    "Differensialning quyma karteri, uzatuvchi valni uzatmalar karterigacha olib boruvchi quvuri bilan."),
                "Die-cast aluminium", housing, m.CastAluminium, c, Quaternion.identity, false, Vector3.left, 0.5f);
            Mesh fdCover = ctx.Mesh("dt-final-drive-cover", k => k.Lathe(Pr(0.086f, 0.027f, 0.101f, 0.027f, 0.101f, 0.035f, 0.090f, 0.040f, 0.066f, 0.050f, 0.044f, 0.053f,
                0.044f, 0.058f, 0.034f, 0.058f, 0.034f, 0.050f, 0.060f, 0.046f, 0.082f, 0.036f, 0.086f, 0.034f, 0.086f, 0.027f), 20, 35f));
            Pt(fd, id + ".final-drive.cover", T("Differential cover", "Differensial qopqog‘i",
                    "Right half of the housing; carries the right carrier bearing and output seal.",
                    "Karterning o‘ng yarmi; o‘ng differensial podshipnigi va chiqish salnigini ko‘taradi."),
                "Die-cast aluminium", fdCover, m.CastAluminium, c, onC, false, Vector3.right, 0.6f);
            for (int i = 0; i < 6; i++)
            {
                float a = (30f + 60f * i) * Mathf.Deg2Rad;
                Vector3 p = c + new Vector3(0.035f, Mathf.Sin(a) * 0.0965f, Mathf.Cos(a) * 0.0965f);
                Bolt(fd, id + ".final-drive.cover-bolt-" + (i + 1), p, Vector3.left, 0.008f, 0.020f,
                    "Bolts the differential cover to the housing.", "Differensial qopqog‘ini karterga mahkamlaydi.");
            }
            Mesh shaft = ctx.Mesh("dt-transfer-shaft", k =>
            {
                k.Push(Vector3.zero, InternalsContext.AlongY(td, Vector3.right));
                k.Cylinder(0.012f, 0.006f, tLen - 0.006f, 14);
                // Upper bevel pinion (driven by the output shaft) and lower hypoid pinion (drives the crown wheel).
                k.Lathe(Pr(0f, -0.002f, 0.016f, -0.002f, 0.020f, 0.014f, 0.012f, 0.020f, 0f, 0.020f), 14, 40f);
                k.Push(new Vector3(0f, -0.002f, 0f), Quaternion.Euler(180f, 0f, 0f));
                DriveKit.BevelGear(k, 11, 0.020f, 0.014f, 0.012f, 0.008f);
                k.Pop();
                k.Push(new Vector3(0f, tLen, 0f), Quaternion.identity);
                DriveKit.BevelGear(k, 10, 0.021f, 0.014f, 0.016f, 0.008f);
                k.Pop();
                k.Pop();
            });
            Pt(fd, id + ".final-drive.transfer-shaft", T("Transfer shaft", "Uzatuvchi val",
                    "Shaft with a bevel pinion at each end: takes the drive from the gearbox down to the crown wheel.",
                    "Ikki uchida konussimon shesternyali val: harakatni uzatmalar qutisidan yetaklanuvchi tishli g‘ildirakka uzatadi."),
                "Case-hardened steel", shaft, m.MachinedSteel, T0, Quaternion.identity, false, Vector3.up, 0.6f);
            ctx.Part(fd, id + ".final-drive.seal-left", T("Left output seal", "Chap chiqish salnigi", "Lip seal around the left inner-joint stem.", "Chap ichki sharnir dumi atrofidagi labli salnik."),
                "FKM rubber", ctx.Mesh("dt-output-seal", k => k.Lathe(Pr(0.017f, 0f, 0.034f, 0f, 0.034f, 0.008f, 0.017f, 0.008f, 0.017f, 0f), 20, 40f)), new[] { m.Rubber },
                c + new Vector3(-0.068f, 0f, 0f), AlongX, true, Vector3.left, 0.4f);
            ctx.Part(fd, id + ".final-drive.seal-right", T("Right output seal", "O‘ng chiqish salnigi", "Lip seal around the right inner-joint stem.", "O‘ng ichki sharnir dumi atrofidagi labli salnik."),
                "FKM rubber", ctx.Mesh("dt-output-seal", null), new[] { m.Rubber }, c + new Vector3(0.049f, 0f, 0f), AlongX, true, Vector3.right, 0.4f);

            Transform diff = ctx.SubAssembly(fd, id + ".final-drive.differential", T("Differential", "Differensial",
                    "Crown wheel on an open bevel-gear differential: lets the front wheels turn at different speeds in a corner.",
                    "Ochiq konussimon differensialdagi yetaklanuvchi tishli g‘ildirak: burilishda old g‘ildiraklarning turli tezlikda aylanishiga imkon beradi."),
                "Steel and ductile iron", Vector3.right, 0.7f);
            Mesh crown = ctx.Mesh("dt-crown-wheel-front", k => DriveKit.CrownWheel(k, 37, 0.044f, 0.071f, 0.010f, 0.022f, 0.007f));
            Pt(diff, id + ".final-drive.differential.crown-wheel", T("Crown wheel", "Yetaklanuvchi tishli g‘ildirak",
                    "Hypoid ring gear, 37 teeth, bolted to the carrier flange; final-drive ratio 3.7:1 with the 10-tooth pinion.",
                    "Differensial korpusi flanetsiga boltlangan 37 tishli gipoid tishli gardish; 10 tishli shesternya bilan bosh uzatma soni 3,7:1."),
                "Case-hardened steel", crown, m.MachinedSteel, c, onC, false, Vector3.right, 0.5f);
            Mesh carrier = ctx.Mesh("dt-diff-carrier", k => DriveKit.DiffCarrier(k));
            Pt(diff, id + ".final-drive.differential.carrier", T("Differential carrier", "Differensial korpusi",
                    "Ductile-iron cage that turns with the crown wheel and holds the cross pin and the four bevel gears.",
                    "Yetaklanuvchi g‘ildirak bilan aylanuvchi, krestovina o‘qi va to‘rtta konussimon shesternyani ushlovchi egiluvchan cho‘yan qafas."),
                "Ductile iron EN-GJS-600", carrier, m.CastIron, c, onC, false, Vector3.down, 0.4f);
            Mesh sideGears = ctx.Mesh("dt-diff-side-gears", k => DriveKit.SideGears(k));
            Pt(diff, id + ".final-drive.differential.side-gears", T("Side gears", "Yarim o‘q shesternyalari",
                    "Two bevel gears splined to the half-shaft stems.", "Yarim o‘q dumlariga shlitsa bilan kiygizilgan ikki konussimon shesternya."),
                "Case-hardened steel", sideGears, m.MachinedSteel, c, onC, false, Vector3.left, 0.3f);
            Mesh pinions = ctx.Mesh("dt-diff-pinions", k => DriveKit.SpiderGears(k));
            Pt(diff, id + ".final-drive.differential.pinion-gears", T("Pinion gears and cross pin", "Satellitlar va o‘q",
                    "Two planet bevel gears on the cross pin that share the torque between the side gears.",
                    "Momentni yarim o‘q shesternyalari o‘rtasida taqsimlovchi o‘qdagi ikki satellit."),
                "Case-hardened steel", pinions, m.ForgedSteel, c, onC, false, Vector3.up, 0.3f);
            Mesh bearing = ctx.Mesh("dt-carrier-bearing", k => k.Lathe(Pr(0.022f, -0.008f, 0.034f, -0.008f, 0.034f, 0.008f, 0.022f, 0.008f, 0.022f, -0.008f), 24, 40f));
            Pt(diff, id + ".final-drive.differential.bearing-left", T("Left carrier bearing", "Chap differensial podshipnigi",
                    "Tapered roller bearing that locates the carrier in the housing.", "Differensial korpusini karterda joylashtiruvchi konussimon rolikli podshipnik."),
                "Bearing steel", bearing, m.MachinedSteel, c + new Vector3(-0.047f, 0f, 0f), AlongX, false, Vector3.left, 0.4f);
            Pt(diff, id + ".final-drive.differential.bearing-right", T("Right carrier bearing", "O‘ng differensial podshipnigi",
                    "Tapered roller bearing in the differential cover.", "Differensial qopqog‘idagi konussimon rolikli podshipnik."),
                "Bearing steel", bearing, m.MachinedSteel, c + new Vector3(0.045f, 0f, 0f), AlongX, false, Vector3.right, 0.4f);
            Mesh crownBolts = ctx.Mesh("dt-crown-bolts", k =>
            {
                for (int i = 0; i < 8; i++)
                {
                    float a = Mathf.PI * 2f * i / 8f;
                    k.Push(new Vector3(Mathf.Cos(a) * 0.052f, 0.022f, Mathf.Sin(a) * 0.052f), Quaternion.identity);
                    DriveKit.LightBolt(k, 0.008f, 0.02f);
                    k.Pop();
                }
            });
            Pt(diff, id + ".final-drive.differential.crown-bolts", T("Crown-wheel bolts M8 (8)", "Tishli g‘ildirak boltlari M8 (8)",
                    "Fine-thread bolts that clamp the crown wheel to the carrier flange.", "Tishli g‘ildirakni korpus flanetsiga qisuvchi mayda rezbali boltlar."),
                "Steel 10.9", crownBolts, m.ZincSteel, c, onC, true, Vector3.right, 0.3f);
        }

        private void ShiftActuator(Transform asm, string id)
        {
            Transform sa = ctx.SubAssembly(asm, id + ".shift-actuator", T("Electro-Hydraulic Shift Actuator", "Elektrogidravlik almashtirish aktuatori",
                    "Valve block, pump and accumulator on top of the case that select and engage the gears in about 100 ms.",
                    "Karter ustidagi klapan bloki, nasos va akkumulyator: uzatmalarni taxminan 100 ms da tanlaydi va ulaydi."),
                "Aluminium and steel", Vector3.up, 1.2f);
            float top = B.y + 0.094f;
            Vector3 at = new Vector3(fx + 0.185f, top + 0.034f, B.z + 0.035f);
            Mesh block = ctx.Mesh("dt-shift-valve-block", k =>
            {
                k.RoundedBox(Vector3.zero, new Vector3(0.092f, 0.046f, 0.076f), 0.006f, 1);
                k.Box(new Vector3(0f, -0.026f, 0f), new Vector3(0.11f, 0.006f, 0.09f));
                for (int i = 0; i < 3; i++)
                {
                    k.Push(new Vector3(-0.03f + 0.03f * i, 0.023f, 0.018f), Quaternion.identity);
                    k.Lathe(Pr(0f, 0f, 0.009f, 0f, 0.009f, 0.012f, 0f, 0.012f), 12, 40f);
                    k.Pop();
                }
            });
            Pt(sa, id + ".shift-actuator.valve-block", T("Valve block", "Klapan bloki",
                    "Aluminium block with solenoid valves that steer oil to the select and shift pistons.",
                    "Tanlash va almashtirish porshenlariga moy yo‘naltiruvchi solenoid klapanli alyuminiy blok."),
                "Anodised aluminium", block, m.MachinedAluminium, at, Quaternion.identity, false, Vector3.up, 0.5f);
            Mesh pump = ctx.Mesh("dt-shift-pump", k => k.Lathe(Pr(0f, 0f, 0.021f, 0f, 0.022f, 0.004f, 0.022f, 0.052f, 0.016f, 0.056f, 0f, 0.056f), 18, 40f));
            Pt(sa, id + ".shift-actuator.pump", T("Pump motor", "Nasos motori",
                    "Electric motor and gear pump that keep the accumulator charged.", "Akkumulyatorni zaryadlangan holda ushlab turuvchi elektr motor va tishli nasos."),
                "Steel and aluminium", pump, m.BlackSteel, at + new Vector3(0.046f, -0.004f, -0.02f), AlongX, false, Vector3.right, 0.5f);
            Mesh acc = ctx.Mesh("dt-shift-accumulator", k => k.Lathe(Pr(0f, 0f, 0.016f, 0f, 0.018f, 0.004f, 0.018f, 0.06f, 0.012f, 0.066f, 0f, 0.066f), 18, 40f));
            Pt(sa, id + ".shift-actuator.accumulator", T("Pressure accumulator", "Bosim akkumulyatori",
                    "Gas-charged accumulator that stores oil pressure for a fast shift.", "Tez almashtirish uchun moy bosimini saqlovchi gaz to‘ldirilgan akkumulyator."),
                "Steel", acc, m.ZincSteel, at + new Vector3(0.046f, -0.006f, 0.024f), AlongX, false, Vector3.right, 0.7f);
            Pt(sa, id + ".shift-actuator.connector", T("Actuator connector", "Aktuator ulagichi", "Sealed connector to the transmission controller.", "Transmissiya boshqaruv blokiga ulanadigan zichlangan ulagich."),
                "Glass-filled nylon", ctx.Mesh("dt-connector", null), m.BlackPlastic, at + new Vector3(0f, 0.004f, 0.05f), Quaternion.identity, true, Vector3.forward, 0.4f);
            Mesh selector = ctx.Mesh("dt-selector-shaft", k => k.Cylinder(0.008f, -0.05f, 0.0f, 12));
            Pt(sa, id + ".shift-actuator.selector-shaft", T("Selector shaft", "Tanlash vali",
                    "Shaft that the actuator turns and slides to pick and move the shift rail.", "Aktuator burib va surib almashtirish shtokini tanlaydigan val."),
                "Steel", selector, m.MachinedSteel, at + new Vector3(0f, -0.023f, 0f), Quaternion.identity, false, Vector3.up, 0.3f);
        }

        // ------------------------------------------------------------------------------------------------ mounts

        /// <summary>False in the combined car: the timing-end mount's place is taken (see Mounts).</summary>
        public static readonly bool BuildTimingEndMount = false;

        private void Mounts(Transform system)
        {
            string id = "powertrain.mounts";
            Transform asm = ctx.Assembly(system, id, T("Engine and Transmission Mounts", "Dvigatel va transmissiya tayanchlari",
                    "Pendulum mounting: hydraulic mounts carry the weight on the engine's axis of inertia, a torque rod takes the drive torque.",
                    "Mayatnikli osish: gidravlik tayanchlar og‘irlikni inersiya o‘qida ko‘taradi, reaktiv tortqi esa uzatma momentini qabul qiladi."),
                "Aluminium, steel and rubber", new Vector3(0f, 0.45f, 1f), 1f);
            Mesh hydro = ctx.Mesh("dt-hydromount", k =>
            {
                k.Lathe(Pr(0f, 0f, 0.048f, 0f, 0.048f, 0.006f, 0.040f, 0.008f, 0.040f, 0.030f, 0.034f, 0.046f, 0.024f, 0.054f, 0.024f, 0.062f, 0.006f, 0.062f, 0.006f, 0.080f, 0f, 0.080f), 18, 35f);
            });
            Mesh hydroRubber = ctx.Mesh("dt-hydromount-rubber", k => k.Lathe(Pr(0.020f, 0.028f, 0.041f, 0.028f, 0.036f, 0.044f, 0.020f, 0.050f, 0.020f, 0.028f), 18, 50f));

            // Left: hydraulic mount on a floor pedestal under the timing end, arm bolted to the timing cover.
            // Integration (combined car): that spot is taken by the accessory drive (water pump, alternator) and the
            // ABS unit, so the timing-end mount stays out until it can hang from a body rail (see BuildTimingEndMount).
            if (BuildTimingEndMount) {
            Vector3 lTop = new Vector3(-0.40f, Mathf.Max(bayFloorLeft + 0.135f, ay - 0.012f), az - 0.14f);
            Vector3 lBase = lTop - Vector3.up * 0.062f;
            Transform em = ctx.SubAssembly(asm, id + ".engine", T("Engine Mount", "Dvigatel tayanchi",
                    "Hydraulic mount at the timing end with its arm on the engine and its pedestal on the bay floor.",
                    "Taqsimlash tomonidagi gidravlik tayanch: kronshteyni dvigatelda, ustuni bo‘linma polida."),
                "Aluminium, steel and rubber", Vector3.left, 0.7f);
            Pt(em, id + ".engine.hydromount", T("Hydraulic mount", "Gidravlik tayanch",
                    "Rubber spring with a glycol-filled chamber and inertia track: soft at idle, damped over bumps.",
                    "Glikol to‘ldirilgan kamerali va inersion kanalli rezina prujina: salt yurishda yumshoq, notekisliklarda so‘ndiruvchi."),
                "Aluminium and natural rubber", hydro, m.CastAluminium, lBase, Quaternion.identity, false, Vector3.up, 0.4f);
            Pt(em, id + ".engine.rubber", T("Mount rubber spring", "Tayanch rezina prujinasi", "Bonded rubber spring of the mount.", "Tayanchning yopishtirilgan rezina prujinasi."),
                "Natural rubber", hydroRubber, m.Rubber, lBase, Quaternion.identity, false, Vector3.forward, 0.3f);
            float coverX = -0.272f;
            if (l.V6Parts.TryGetValue("Engine side cover", out Bounds side)) coverX = side.min.x - 0.002f;
            Mesh lArm = ctx.Mesh("dt-engine-mount-arm", k =>
            {
                Vector3 a = new Vector3(coverX - lTop.x, 0.004f, 0f), b = new Vector3(0f, 0.016f, 0f);
                k.Tube(new[] { a + new Vector3(-0.012f, 0f, 0f), b }, 0.016f, 12, true, true, Vector3.up, x => Mathf.Lerp(1.25f, 0.9f, x), new Vector2(1.4f, 0.75f));
                k.RoundedBox(a + new Vector3(-0.006f, 0f, 0f), new Vector3(0.012f, 0.07f, 0.07f), 0.003f, 1);
                k.Push(b, Quaternion.identity);
                k.Lathe(Pr(0.007f, -0.008f, 0.028f, -0.008f, 0.028f, 0.008f, 0.007f, 0.008f, 0.007f, -0.008f), 18, 40f);
                k.Pop();
            });
            Pt(em, id + ".engine.arm", T("Engine-side arm", "Dvigatel tomondagi kronshteyn",
                    "Cast-aluminium arm bolted to the timing cover; sits on the mount's stud.",
                    "Taqsimlash qopqog‘iga boltlangan quyma alyuminiy kronshteyn; tayanch shpilkasiga o‘rnatiladi."),
                "Cast aluminium", lArm, m.CastAluminium, lTop, Quaternion.identity, false, Vector3.right, 0.5f);
            float lH = lBase.y - bayFloorLeft;
            Mesh pedestal = ctx.Mesh("dt-mount-pedestal-" + Mathf.RoundToInt(lH * 1000f), k => DriveKit.Pedestal(k, lH));
            Pt(em, id + ".engine.pedestal", T("Floor pedestal", "Pol ustuni",
                    "Pressed-steel box pedestal welded to a base plate that bolts to the bay floor.",
                    "Bo‘linma poliga boltlanadigan asos plitasiga payvandlangan presslangan po‘lat qutisimon ustun."),
                "Pressed steel", pedestal, m.BlackSteel, new Vector3(lBase.x, bayFloorLeft, lBase.z), Quaternion.identity, false, Vector3.left, 0.5f);
            foreach (float dz in new[] { -0.042f, 0.042f })
                Bolt(em, id + ".engine.floor-bolt-" + (dz < 0 ? "rear" : "front"), new Vector3(lBase.x, bayFloorLeft + 0.006f, lBase.z + dz), Vector3.down, 0.010f, 0.025f,
                    "Bolts the pedestal to the bay floor.", "Ustunni bo‘linma poliga mahkamlaydi.");
            foreach (float dz in new[] { -0.022f, 0.022f })
                Bolt(em, id + ".engine.arm-bolt-" + (dz < 0 ? "rear" : "front"), new Vector3(coverX - 0.012f, lTop.y + 0.004f, lTop.z + dz), Vector3.right, 0.010f, 0.035f,
                    "Bolts the arm to the timing cover.", "Kronshteynni taqsimlash qopqog‘iga mahkamlaydi.");
            }
            else ctx.Note("drivetrain: timing-end engine mount not built in the combined car (accessory drive and ABS unit fill its place)");

            // Right: mount at the end cover, on a tower from the bay floor ahead of the right wheel well.
            Vector3 rTop = new Vector3(caseEnd + 0.075f, ay + 0.085f, az + 0.04f);
            Vector3 rBase = rTop - Vector3.up * 0.062f;
            Transform tm = ctx.SubAssembly(asm, id + ".transmission", T("Transmission Mount", "Transmissiya tayanchi",
                    "Hydraulic mount at the transaxle's end, on a steel tower from the bay floor.",
                    "Transaksl uchidagi gidravlik tayanch, bo‘linma polidan ko‘tarilgan po‘lat ustunda."),
                "Aluminium, steel and rubber", Vector3.right, 0.7f);
            Pt(tm, id + ".transmission.hydromount", T("Hydraulic mount", "Gidravlik tayanch",
                    "Same hydraulic mount as the engine side, tuned for the gearbox's mass.", "Dvigatel tomondagi kabi gidravlik tayanch, uzatmalar qutisi massasiga moslangan."),
                "Aluminium and natural rubber", hydro, m.CastAluminium, rBase, Quaternion.identity, false, Vector3.up, 0.4f);
            Pt(tm, id + ".transmission.rubber", T("Mount rubber spring", "Tayanch rezina prujinasi", "Bonded rubber spring of the mount.", "Tayanchning yopishtirilgan rezina prujinasi."),
                "Natural rubber", hydroRubber, m.Rubber, rBase, Quaternion.identity, false, Vector3.forward, 0.3f);
            Mesh rArm = ctx.Mesh("dt-transmission-mount-arm", k =>
            {
                Vector3 a = new Vector3(caseEnd + 0.012f - rTop.x, -0.02f, 0f), b = new Vector3(0f, 0.016f, 0f);
                k.Tube(new[] { a, b }, 0.015f, 12, true, true, Vector3.up, x => Mathf.Lerp(1.2f, 0.9f, x), new Vector2(1.3f, 0.8f));
                k.RoundedBox(a + new Vector3(0.006f, 0f, 0f), new Vector3(0.012f, 0.06f, 0.06f), 0.003f, 1);
                k.Push(b, Quaternion.identity);
                k.Lathe(Pr(0.007f, -0.008f, 0.028f, -0.008f, 0.028f, 0.008f, 0.007f, 0.008f, 0.007f, -0.008f), 18, 40f);
                k.Pop();
            });
            Pt(tm, id + ".transmission.arm", T("Case-side arm", "Karter tomondagi kronshteyn",
                    "Forged arm bolted to the end cover's bosses.", "Yon qopqoq bobishkalariga boltlangan bolg‘alangan kronshteyn."),
                "Forged aluminium", rArm, m.CastAluminium, rTop, Quaternion.identity, false, Vector3.left, 0.5f);
            float rH = rBase.y - bayFloorRight;
            Mesh tower = ctx.Mesh("dt-mount-pedestal-" + Mathf.RoundToInt(rH * 1000f), k => DriveKit.Pedestal(k, rH));
            Pt(tm, id + ".transmission.tower", T("Mount tower", "Tayanch ustuni",
                    "Box-section steel tower that bolts to the bay floor ahead of the wheel well.", "G‘ildirak arkasi oldida bo‘linma poliga boltlanadigan qutisimon po‘lat ustun."),
                "Pressed steel", tower, m.BlackSteel, new Vector3(rBase.x, bayFloorRight, rBase.z), Quaternion.identity, false, Vector3.right, 0.5f);
            foreach (float dz in new[] { -0.042f, 0.042f })
                Bolt(tm, id + ".transmission.floor-bolt-" + (dz < 0 ? "rear" : "front"), new Vector3(rBase.x, bayFloorRight + 0.006f, rBase.z + dz), Vector3.down, 0.010f, 0.025f,
                    "Bolts the tower to the bay floor.", "Ustunni bo‘linma poliga mahkamlaydi.");

            // Torque rod: bell-housing lug to a bracket clamped on the subframe's tie bar.
            Transform tr = ctx.SubAssembly(asm, id + ".torque-rod", T("Torque Rod", "Reaktiv tortqi",
                    "Dog-bone link that stops the engine rocking on its mounts under drive and braking torque.",
                    "Tortish va tormozlash momentida dvigatelning tayanchlarida chayqalishini to‘xtatuvchi suyaksimon tortqi."),
                "Forged steel and rubber", new Vector3(0f, -0.5f, -1f), 0.7f);
            var fcg = FrontChassisGeometry.Of(l, ctx.Probe);
            float rodX = fx + 0.056f;
            Vector3 front = new Vector3(rodX, ay - 0.106f, az - 0.094f);
            Vector3 rear = new Vector3(rodX, fcg.TieBarY + 0.012f, fcg.TieBarZ + 0.052f);
            float len = Vector3.Distance(front, rear);
            Vector3 dir = (rear - front).normalized;
            Mesh rod = ctx.Mesh("dt-torque-rod-" + Mathf.RoundToInt(len * 1000f), k =>
            {
                k.Push(Vector3.zero, AlongX);
                k.Sleeve(0.028f, 0.021f, -0.018f, 0.018f, 20);
                k.Pop();
                k.Push(new Vector3(0f, 0f, len), AlongX);
                k.Sleeve(0.020f, 0.0145f, -0.014f, 0.014f, 16);
                k.Pop();
                k.Tube(new[] { new Vector3(0f, 0f, 0.026f), new Vector3(0f, 0f, len - 0.018f) }, 0.011f, 10, true, true, Vector3.up, null, new Vector2(0.75f, 1.35f));
            });
            Quaternion rodRot = Quaternion.LookRotation(dir, Vector3.up);
            Pt(tr, id + ".torque-rod.rod", T("Rod", "Tortqi tanasi",
                    "Forged dog-bone with a large and a small bushing eye.", "Katta va kichik vtulka ko‘zli bolg‘alangan suyaksimon tortqi."),
                "Forged steel", rod, m.ForgedSteel, front, rodRot, false, Vector3.down, 0.4f);
            Mesh bigBush = ctx.Mesh("dt-torque-rod-bush-big", k => k.Sleeve(0.021f, 0.008f, -0.017f, 0.017f, 18));
            Mesh smallBush = ctx.Mesh("dt-torque-rod-bush-small", k => k.Sleeve(0.0145f, 0.006f, -0.013f, 0.013f, 16));
            Pt(tr, id + ".torque-rod.bushing-front", T("Large bushing", "Katta vtulka", "Soft rubber bushing at the engine end.", "Dvigatel uchidagi yumshoq rezina vtulka."),
                "Natural rubber", bigBush, m.Rubber, front, AlongX, false, Vector3.right, 0.3f);
            Pt(tr, id + ".torque-rod.bushing-rear", T("Small bushing", "Kichik vtulka", "Stiff rubber bushing at the subframe end.", "Yordamchi rama uchidagi qattiq rezina vtulka."),
                "Natural rubber", smallBush, m.Rubber, rear, AlongX, false, Vector3.right, 0.3f);
            Mesh br = ctx.Mesh("dt-torque-rod-bracket", k =>
            {
                // Strap round the tie bar and two ears forward to the rod's rear eye.
                var arc = new List<Vector3>();
                for (int i = 0; i <= 14; i++)
                {
                    float a = Mathf.PI * (-0.15f + 1.3f * i / 14f);
                    arc.Add(new Vector3(0f, -0.012f + Mathf.Sin(a) * 0.0215f, -0.052f + Mathf.Cos(a) * 0.0215f));
                }
                k.Sweep(new[] { new Vector2(-0.0015f, -0.018f), new Vector2(0.0015f, -0.018f), new Vector2(0.0015f, 0.018f), new Vector2(-0.0015f, 0.018f) }, arc, Vector3.right);
                foreach (float sx in new[] { -0.0165f, 0.0165f })
                    k.PlanarPrism(new[] { new Vector3(sx, -0.006f, -0.034f), new Vector3(sx, 0.010f, -0.034f), new Vector3(sx, 0.016f, 0.0f), new Vector3(sx, -0.016f, 0.0f) }, 0.003f);
            });
            Pt(tr, id + ".torque-rod.bracket", T("Tie-bar bracket", "To‘sin kronshteyni",
                    "Pressed clevis clamped round the subframe tie bar.", "Yordamchi rama to‘sini atrofiga qisilgan presslangan vilka."),
                "Pressed steel", br, m.BlackSteel, rear, Quaternion.identity, false, Vector3.back, 0.5f);
            Bolt(tr, id + ".torque-rod.bolt-front", front + new Vector3(0.024f, 0f, 0f), Vector3.left, 0.012f, 0.05f,
                "Pivot bolt through the large bushing into the bell-housing lug.", "Katta vtulka orqali mufta karteri qulog‘iga kiruvchi o‘q bolt.");
            Bolt(tr, id + ".torque-rod.bolt-rear", rear + new Vector3(0.022f, 0f, 0f), Vector3.left, 0.010f, 0.045f,
                "Pivot bolt through the small bushing and the bracket ears.", "Kichik vtulka va kronshteyn quloqlari orqali o‘tuvchi o‘q bolt.");
        }

        // ------------------------------------------------------------------------------------------------ half shafts

        private sealed class Shaft
        {
            public string Id, Side, SideUz;
            public float S;
            public Vector3 Ji, Jo, Axle, InnerAxis;
            public float HubFace;
            public bool Tubular, Damper;
        }

        private void FrontHalfShafts(Transform system)
        {
            string id = "driveline.front-half-shafts";
            Transform asm = ctx.Assembly(system, id, T("Front Half Shafts", "Old yarim o‘qlar",
                    "Two constant-velocity drive shafts from the final drive to the steered hubs: plunging tripod joints inboard, Rzeppa joints outboard.",
                    "Bosh uzatmadan buriluvchi gupchaklarga boruvchi ikki teng burchak tezlikli uzatma vali: ichkarida siljuvchi tripod, tashqarida Rzeppa sharnirlari."),
                "Steel and rubber", new Vector3(0f, 1f, -0.6f), 1f);
            foreach (WheelInfo w in new[] { l.FrontLeft, l.FrontRight })
            {
                float a = w.DiscAxialMax + 0.002f;
                var s = new Shaft
                {
                    Id = id + "." + (w.Left ? "left" : "right"),
                    Side = w.Left ? "Left" : "Right",
                    SideUz = w.Left ? "chap" : "o‘ng",
                    S = w.Side,
                    Ji = new Vector3(w.Left ? C.x - 0.105f : C.x + 0.11f, C.y, C.z),
                    Jo = w.Centre + w.Outward * (a - 0.128f),
                    Axle = w.Outward,
                    InnerAxis = new Vector3(w.Side, 0f, 0f),
                    HubFace = a,
                    Tubular = w.Left,
                    Damper = w.Left,
                };
                HalfShaft(asm, s, true, w);
            }
        }

        private void RearHalfShafts(Transform system)
        {
            string id = "driveline.rear-half-shafts";
            Transform asm = ctx.Assembly(system, id, T("Rear Half Shafts", "Orqa yarim o‘qlar",
                    "Constant-velocity shafts from the electric drive unit to the rear hubs.",
                    "Elektr yuritma blokidan orqa gupchaklarga boruvchi teng burchak tezlikli vallar."),
                "Steel and rubber", new Vector3(0f, 0.5f, -1f), 1f);
            foreach (WheelInfo w in new[] { l.RearLeft, l.RearRight })
            {
                var s = new Shaft
                {
                    Id = id + "." + (w.Left ? "left" : "right"),
                    Side = w.Left ? "Left" : "Right",
                    SideUz = w.Left ? "chap" : "o‘ng",
                    S = w.Side,
                    Ji = new Vector3(w.Left ? RearLeftOutputX() - 0.045f : RearRightOutputX() + 0.045f, E.y, E.z),
                    Jo = w.Centre + w.Outward * -0.16f,
                    Axle = w.Outward,
                    InnerAxis = new Vector3(w.Side, 0f, 0f),
                    HubFace = -0.032f,
                    Tubular = false,
                    Damper = false,
                };
                HalfShaft(asm, s, false, w);
            }
        }

        private void HalfShaft(Transform asm, Shaft s, bool front, WheelInfo w)
        {
            string axle = front ? "Old" : "Orqa";
            string en = (front ? "Front " : "Rear ") + s.Side + " Half Shaft";
            Transform hs = ctx.SubAssembly(asm, s.Id, T(en, axle + " " + s.SideUz + " yarim o‘q",
                    (s.Tubular ? "Long tubular shaft with a tuned damper" : "Solid shaft") + " between a plunging tripod joint and a fixed ball joint.",
                    (s.Tubular ? "Sozlangan dempferli uzun quvursimon val" : "Yaxlit val") + ", siljuvchi tripod sharnir va qo‘zg‘almas sharli sharnir orasida."),
                "Steel and rubber", s.InnerAxis, 0.9f);
            Vector3 shaftDir = (s.Jo - s.Ji).normalized;
            float shaftLen = Vector3.Distance(s.Ji, s.Jo);
            string key = (front ? "f" : "r") + (s.S < 0 ? "l" : "r");

            // Inner tripod joint: tulip on the output axis, spider on the shaft.
            Transform ij = ctx.SubAssembly(hs, s.Id + ".inner-joint", T("Inner Tripod Joint", "Ichki tripod sharnir",
                    "Plunging joint: three needle-roller trunnions slide in the tulip housing so the shaft can change length as the wheel moves.",
                    "Siljuvchi sharnir: uchta ignasimon rolikli barmoq lola korpusida sirpanadi, g‘ildirak harakatlanganda val uzunligi o‘zgarishi mumkin."),
                "Steel and thermoplastic", -s.InnerAxis, 0.5f);
            Mesh tulip = ctx.Mesh("dt-tripod-housing", k =>
            {
                k.Lathe(Pr(0f, -0.068f, 0.0165f, -0.068f, 0.0165f, -0.040f, 0.024f, -0.036f, 0.044f, -0.030f, 0.047f, -0.020f, 0.047f, 0.034f, 0.041f, 0.036f, 0.041f, -0.022f, 0f, -0.022f), 16, 35f);
                for (int i = 0; i < 3; i++)
                {
                    float deg = 60f + 120f * i;
                    k.Push(Vector3.zero, Quaternion.Euler(0f, deg, 0f));
                    k.Box(new Vector3(0.046f, 0.007f, 0f), new Vector3(0.006f, 0.05f, 0.022f));
                    k.Pop();
                }
            });
            Pt(ij, s.Id + ".inner-joint.housing", T("Tripod housing", "Tripod korpusi",
                    "Forged tulip with three roller tracks; its stem splines into the differential side gear.",
                    "Uchta rolik yo‘lakchali bolg‘alangan lola korpus; dumi differensial shesternyasiga shlitsa bilan kiradi."),
                "Forged steel C45", tulip, m.ForgedSteel, s.Ji, InternalsContext.AlongY(s.InnerAxis, Vector3.up), false, s.InnerAxis, 0.4f);
            Mesh spider = ctx.Mesh("dt-tripod-spider", k =>
            {
                k.Sleeve(0.022f, 0.012f, -0.012f, 0.012f, 14);
                for (int i = 0; i < 3; i++)
                {
                    float a = Mathf.PI * 2f * i / 3f;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    k.Push(Vector3.zero, InternalsContext.AlongY(d, Vector3.up));
                    k.Cylinder(0.008f, 0.018f, 0.025f, 8);
                    k.Lathe(Pr(0.008f, 0.024f, 0.016f, 0.024f, 0.0175f, 0.030f, 0.016f, 0.036f, 0.008f, 0.036f, 0.008f, 0.024f), 10, 40f);
                    k.Pop();
                }
            });
            Pt(ij, s.Id + ".inner-joint.spider", T("Tripod spider", "Tripod krestovinasi",
                    "Three trunnions with spherical rollers on needle bearings, splined to the shaft.",
                    "Valga shlitsa bilan kiygizilgan, ignasimon podshipnikli sferik rolikli uchta barmoq."),
                "Case-hardened steel", spider, m.MachinedSteel, s.Ji, InternalsContext.AlongY(shaftDir, Vector3.up), false, -s.InnerAxis, 0.5f);
            Vector3 iBootFrom = s.Ji + s.InnerAxis * 0.030f;
            Vector3 iBootTo = s.Ji + shaftDir * 0.115f;
            Vector3 iBootDir = (iBootTo - iBootFrom).normalized;
            float iBootLen = Vector3.Distance(iBootFrom, iBootTo);
            Mesh iBoot = ctx.Mesh("dt-boot-inner-" + Mathf.RoundToInt(iBootLen * 1000f), k => DriveKit.Boot(k, 0.0475f, 0.0165f, 0.026f, 0.044f, iBootLen, 5, 12, 0.012f));
            Pt(ij, s.Id + ".inner-joint.boot", T("Inner boot", "Ichki g‘ilof",
                    "Thermoplastic bellows that keeps the grease in and dirt out while the joint plunges.",
                    "Sharnir siljiganda moyni ichkarida, kirni tashqarida ushlab turuvchi termoplastik gofra."),
                "Thermoplastic elastomer", iBoot, m.Rubber, iBootFrom, InternalsContext.AlongY(iBootDir, Vector3.up), false, iBootDir, 0.4f);
            Clamp(ij, s.Id + ".inner-joint.clamp-large", 0.0475f, iBootFrom + iBootDir * 0.006f, iBootDir, true);
            Clamp(ij, s.Id + ".inner-joint.clamp-small", 0.0165f, iBootTo - iBootDir * 0.006f, iBootDir, false);

            // Shaft (tubular on the long side, with a tuned mass damper).
            float r = s.Tubular ? 0.0185f : 0.0135f;
            Mesh bar = ctx.Mesh("dt-half-shaft-" + key + "-" + Mathf.RoundToInt(shaftLen * 1000f), k =>
            {
                float end0 = 0.0f, end1 = shaftLen;
                if (s.Tubular)
                {
                    k.Lathe(Pr(0f, end0, 0.0125f, end0, 0.0125f, 0.10f, r, 0.14f, r, end1 - 0.14f, 0.0125f, end1 - 0.10f, 0.0125f, end1, 0f, end1), 16, 30f);
                }
                else
                {
                    k.Lathe(Pr(0f, end0, 0.0125f, end0, 0.0125f, 0.045f, r, 0.06f, r, end1 - 0.06f, 0.0125f, end1 - 0.045f, 0.0125f, end1, 0f, end1), 14, 30f);
                }
            });
            Pt(hs, s.Id + ".shaft", T(s.Tubular ? "Tubular shaft" : "Solid shaft", s.Tubular ? "Quvursimon val" : "Yaxlit val",
                    s.Tubular ? "Friction-welded steel tube with splined ends; the larger diameter keeps it stiff over its length."
                              : "Induction-hardened bar with rolled splines at both ends.",
                    s.Tubular ? "Ishqalanib payvandlangan, uchlari shlitsali po‘lat quvur; katta diametri uzun valni bikr qiladi."
                              : "Ikki uchida prokat shlitsali induksion toblangan sterjen."),
                s.Tubular ? "Steel tube 25MnB5" : "Steel 42CrMo4", bar, m.MachinedSteel, s.Ji, InternalsContext.AlongY(shaftDir, Vector3.up), false, Vector3.down, 0.3f);
            if (s.Damper)
            {
                Mesh damper = ctx.Mesh("dt-shaft-damper", k =>
                {
                    k.Lathe(Pr(r, -0.036f, 0.024f, -0.036f, 0.024f, -0.028f, 0.033f, -0.024f, 0.033f, 0.024f, 0.024f, 0.028f, 0.024f, 0.036f, r, 0.036f, r, -0.036f), 16, 35f);
                });
                Pt(hs, s.Id + ".damper", T("Dynamic damper", "Dinamik so‘ndirgich",
                        "Steel mass bonded in rubber, tuned to cancel the long shaft's bending resonance.",
                        "Uzun valning egilish rezonansini so‘ndirishga sozlangan, rezinaga yopishtirilgan po‘lat massa."),
                    "Steel and rubber", damper, m.BlackSteel, s.Ji + shaftDir * (shaftLen * 0.45f), InternalsContext.AlongY(shaftDir, Vector3.up), false, Vector3.down, 0.4f);
            }

            // Outer fixed ball joint on the wheel axle, its stub through the hub bearing.
            Transform oj = ctx.SubAssembly(hs, s.Id + ".outer-joint", T("Outer CV Joint", "Tashqi teng burchak tezlikli sharnir",
                    "Fixed Rzeppa ball joint: six balls in a cage transmit torque at up to 47 degrees for steering and suspension travel.",
                    "Qo‘zg‘almas Rzeppa sharli sharniri: separatordagi oltita shar rul va osma harakati uchun 47 darajagacha burchakda momentni uzatadi."),
                "Steel and thermoplastic", s.Axle, 0.6f);
            float stub = s.HubFace + 0.022f - (Vector3.Dot(s.Jo - w.Centre, s.Axle));
            Mesh bell = ctx.Mesh("dt-cv-bell-" + (front ? "front" : "rear") + "-" + Mathf.RoundToInt(stub * 1000f), k =>
            {
                k.Lathe(Pr(0f, 0.000f, 0.030f, 0.000f, 0.040f, -0.010f, 0.040f, -0.042f, 0.046f, -0.042f, 0.049f, -0.024f, 0.049f, 0.010f, 0.043f, 0.024f, 0.030f, 0.030f, 0f, 0.030f), 18, 35f);
                k.Lathe(Pr(0f, 0.029f, 0.026f, 0.029f, 0.026f, 0.046f, 0.0195f, 0.050f, 0.0195f, stub - 0.03f, 0.0125f, stub - 0.026f, 0.0125f, stub, 0f, stub), 14, 35f);
            });
            Pt(oj, s.Id + ".outer-joint.housing", T("Outer joint housing", "Tashqi sharnir korpusi",
                    "Forged bell with six ball tracks and the splined, threaded stub that drives the hub.",
                    "Oltita shar yo‘lakchali bolg‘alangan qo‘ng‘iroq va gupchakni aylantiruvchi shlitsali, rezbali dum."),
                "Forged steel C55, induction-hardened tracks", bell, m.ForgedSteel, s.Jo, InternalsContext.AlongY(s.Axle, Vector3.up), false, s.Axle, 0.5f);
            Mesh cage = ctx.Mesh("dt-cv-cage", k =>
            {
                k.Lathe(Pr(0.012f, -0.013f, 0.027f, -0.013f, 0.031f, -0.006f, 0.031f, 0.006f, 0.027f, 0.013f, 0.012f, 0.013f, 0.012f, -0.013f), 12, 30f);
                k.Lathe(Pr(0.035f, -0.009f, 0.039f, -0.008f, 0.040f, 0f, 0.039f, 0.008f, 0.035f, 0.009f, 0.035f, -0.009f), 14, 30f);
                for (int i = 0; i < 6; i++)
                {
                    float a = Mathf.PI * 2f * i / 6f;
                    k.Push(new Vector3(Mathf.Cos(a) * 0.036f, 0f, Mathf.Sin(a) * 0.036f), Quaternion.identity);
                    k.Lathe(Pr(0f, -0.0085f, 0.006f, -0.006f, 0.0085f, 0f, 0.006f, 0.006f, 0f, 0.0085f), 5, 60f);
                    k.Pop();
                }
            });
            Pt(oj, s.Id + ".outer-joint.cage", T("Inner race, cage and balls", "Ichki obyma, separator va sharlar",
                    "Inner race splined to the shaft, steel cage and six hardened balls that keep the joint homokinetic.",
                    "Valga shlitsali ichki obyma, po‘lat separator va sharnirni teng tezlikli saqlovchi oltita toblangan shar."),
                "Bearing steel 100Cr6", cage, m.MachinedSteel, s.Jo, InternalsContext.AlongY(shaftDir, Vector3.up), false, -s.Axle, 0.4f);
                        Vector3 oBootFrom = s.Jo + (-s.Axle * 0.040f);
            Vector3 oBootTo = s.Jo - shaftDir * 0.13f;
            Vector3 oBootDir = (oBootTo - oBootFrom).normalized;
            float oBootLen = Vector3.Distance(oBootFrom, oBootTo);
            Mesh oBoot = ctx.Mesh("dt-boot-outer-" + Mathf.RoundToInt(oBootLen * 1000f), k => DriveKit.Boot(k, 0.044f, 0.0165f, 0.024f, 0.042f, oBootLen, 6, 12, 0.012f));
            Pt(oj, s.Id + ".outer-joint.boot", T("Outer boot", "Tashqi g‘ilof",
                    "Thermoplastic bellows that lets the joint articulate while sealing its grease.",
                    "Sharnirning egilishiga imkon berib, uning moyini zichlovchi termoplastik gofra."),
                "Thermoplastic elastomer", oBoot, m.Rubber, oBootFrom, InternalsContext.AlongY(oBootDir, Vector3.up), false, oBootDir, 0.4f);
            Clamp(oj, s.Id + ".outer-joint.clamp-large", 0.044f, oBootFrom + oBootDir * 0.006f, oBootDir, true);
            Clamp(oj, s.Id + ".outer-joint.clamp-small", 0.0165f, oBootTo - oBootDir * 0.006f, oBootDir, false);
        }

        // ------------------------------------------------------------------------------------------------ rear drive

        private const float MotorX0 = -0.300f, MotorX1 = -0.075f, GearX1 = 0.130f;
        private float RearLeftOutputX() => MotorX0 - 0.030f;
        private float RearRightOutputX() => GearX1;

        private void RearDriveUnit(Transform system)
        {
            string id = "driveline.rear-drive-unit";
            Transform asm = ctx.Assembly(system, id, T("Electric Rear Drive Unit", "Orqa elektr yuritma bloki",
                    "Coaxial e-axle on the rear axle line: permanent-magnet motor, planetary reduction and open differential with the inverter on top; drives the rear wheels for all-wheel drive.",
                    "Orqa o‘q chizig‘idagi koaksial elektr o‘q: doimiy magnitli motor, planetar reduktor va ochiq differensial, ustida invertor; to‘liq yuritma uchun orqa g‘ildiraklarni aylantiradi."),
                "Aluminium, steel and copper", new Vector3(0f, 1f, -0.5f), 1.1f);
            Vector3 e(float x) => new Vector3(x, E.y, E.z);

            Transform motor = ctx.SubAssembly(asm, id + ".motor", T("Traction Motor", "Tortuvchi elektr motor",
                    "Oil-cooled permanent-magnet synchronous motor, about 90 kW, around a hollow rotor shaft.",
                    "Moy bilan sovutiladigan doimiy magnitli sinxron motor, taxminan 90 kVt, ichi bo‘sh rotor vali atrofida."),
                "Aluminium, electrical steel and copper", Vector3.left, 0.7f);
            float ml = MotorX1 - MotorX0;
            Mesh stator = ctx.Mesh("dt-motor-housing", k =>
            {
                var p = new List<Vector2> { new Vector2(0.080f, 0f), new Vector2(0.088f, 0f) };
                for (int i = 0; i < 4; i++)
                {
                    float y = 0.035f + i * 0.048f;
                    p.Add(new Vector2(0.088f, y)); p.Add(new Vector2(0.094f, y + 0.003f)); p.Add(new Vector2(0.094f, y + 0.009f)); p.Add(new Vector2(0.088f, y + 0.012f));
                }
                p.Add(new Vector2(0.088f, ml)); p.Add(new Vector2(0.080f, ml)); p.Add(new Vector2(0.080f, 0f));
                k.Lathe(p, 24, 40f);
                // Ears for the front-left and the rear mount (local x = car z, local z = car y).
                foreach (float dy in new[] { -0.024f, 0.024f })
                {
                    k.Box(new Vector3(0.10f, 0.11f + dy, -0.032f), new Vector3(0.05f, 0.006f, 0.03f));
                    k.Box(new Vector3(-0.125f, 0.11f + dy, 0f), new Vector3(0.07f, 0.006f, 0.03f));
                }
            });
            Pt(motor, id + ".motor.housing", T("Stator housing", "Stator korpusi",
                    "Finned cast-aluminium housing with the oil jacket around the stator.", "Stator atrofida moy ko‘ylagi bo‘lgan qovurg‘ali quyma alyuminiy korpus."),
                "Cast aluminium", stator, m.CastAluminium, e(MotorX0), AlongX, false, Vector3.up, 0.4f);
            Mesh statorCore = ctx.Mesh("dt-motor-stator", k =>
            {
                k.Lathe(Pr(0.060f, 0.012f, 0.080f, 0.012f, 0.080f, ml - 0.012f, 0.060f, ml - 0.012f, 0.060f, 0.012f), 24, 40f);
                foreach (float y in new[] { 0.004f, ml - 0.004f })
                    k.Lathe(Pr(0.058f, y - 0.008f, 0.074f, y - 0.008f, 0.076f, y, 0.074f, y + 0.008f, 0.058f, y + 0.008f, 0.058f, y - 0.008f), 16, 45f);
            });
            Pt(motor, id + ".motor.stator", T("Stator and windings", "Stator va chulg‘amlar",
                    "Laminated electrical-steel core with hairpin copper windings.", "Shpilkasimon mis chulg‘amli qatlamli elektrotexnik po‘lat o‘zak."),
                "Electrical steel and copper", statorCore, m.Copper, e(MotorX0), AlongX, false, Vector3.forward, 0.5f);
            Mesh rotor = ctx.Mesh("dt-motor-rotor", k =>
            {
                k.Lathe(Pr(0.020f, 0.018f, 0.057f, 0.018f, 0.057f, ml - 0.018f, 0.020f, ml - 0.018f, 0.020f, 0.018f), 20, 40f);
                k.Sleeve(0.020f, 0.012f, -0.02f, ml + 0.03f, 16);
            });
            Pt(motor, id + ".motor.rotor", T("Rotor", "Rotor",
                    "Interior permanent-magnet rotor on a hollow shaft; the left half shaft runs through it.",
                    "Ichi bo‘sh valdagi ichki doimiy magnitli rotor; chap yarim o‘q uning ichidan o‘tadi."),
                "Electrical steel and NdFeB magnets", rotor, m.MachinedSteel, e(MotorX0), AlongX, false, Vector3.right, 0.6f);
            Mesh endBell = ctx.Mesh("dt-motor-end-bell", k => k.Lathe(Pr(0.020f, -0.030f, 0.040f, -0.030f, 0.042f, -0.022f, 0.060f, -0.016f, 0.088f, -0.008f, 0.088f, 0f,
                0.080f, 0f, 0.056f, -0.008f, 0.020f, -0.012f, 0.020f, -0.030f), 24, 35f));
            Pt(motor, id + ".motor.end-bell", T("End bell", "Yon qopqoq",
                    "Cast end shield with the rotor bearing, the resolver and the left output seal.", "Rotor podshipnigi, rezolver va chap chiqish salnigi joylashgan quyma yon qalqon."),
                "Cast aluminium", endBell, m.CastAluminium, e(MotorX0), AlongX, false, Vector3.left, 0.4f);

            Transform gb = ctx.SubAssembly(asm, id + ".gearbox", T("Reduction Gearbox and Differential", "Reduktor va differensial",
                    "Two-stage planetary reduction (about 10:1) and an open differential in one housing on the motor axis.",
                    "Motor o‘qidagi bitta korpusda ikki pog‘onali planetar reduktor (taxminan 10:1) va ochiq differensial."),
                "Aluminium and steel", Vector3.right, 0.7f);
            float gl = GearX1 - MotorX1;
            Mesh gHousing = ctx.Mesh("dt-edu-gear-housing", k =>
            {
                k.Lathe(Pr(0.082f, 0f, 0.096f, 0f, 0.096f, 0.004f, 0.090f, 0.006f, 0.090f, 0.066f, 0.098f, 0.072f, 0.098f, gl - 0.062f, 0.066f, gl - 0.030f, 0.042f, gl - 0.016f, 0.040f, gl,
                    0.034f, gl, 0.034f, gl - 0.020f, 0.060f, gl - 0.032f, 0.090f, gl - 0.062f, 0.090f, 0.075f, 0.082f, 0.060f, 0.082f, 0f), 24, 35f);
                // Ears for the front-right mount (local x = car z, local z = car y).
                foreach (float dy in new[] { -0.024f, 0.024f })
                    k.Box(new Vector3(0.10f, 0.12f + dy, -0.032f), new Vector3(0.05f, 0.006f, 0.03f));
            });
            Pt(gb, id + ".gearbox.housing", T("Gearbox housing", "Reduktor korpusi",
                    "Cast housing of the reduction stages and differential, with the mount feet and the oil sump.",
                    "Reduktor pog‘onalari va differensialning quyma korpusi, tayanch oyoqlari va moy karteri bilan."),
                "Cast aluminium", gHousing, m.CastAluminium, e(MotorX1), AlongX, false, Vector3.right, 0.4f);
            Mesh planet = ctx.Mesh("dt-planetary-set", k =>
            {
                DriveKit.Gear(k, 14, 0.0155f, 0.0205f, 0.008f, 0.030f, 0.012f, 0);
                for (int i = 0; i < 3; i++)
                {
                    float a = Mathf.PI * 2f * i / 3f;
                    k.Push(new Vector3(Mathf.Cos(a) * 0.038f, 0f, Mathf.Sin(a) * 0.038f), Quaternion.identity);
                    DriveKit.Gear(k, 16, 0.0145f, 0.0195f, 0.010f, 0.028f, 0.006f, 0);
                    k.Pop();
                }
                k.Sleeve(0.068f, 0.059f, 0.008f, 0.030f, 32);
                k.Lathe(Pr(0.014f, 0.032f, 0.050f, 0.032f, 0.050f, 0.037f, 0.014f, 0.037f, 0.014f, 0.032f), 24, 40f);
            });
            Pt(gb, id + ".gearbox.planetary", T("Planetary reduction set", "Planetar reduktor to‘plami",
                    "Sun gear on the rotor shaft, three planets on the carrier and the fixed ring gear.",
                    "Rotor validagi quyosh shesternyasi, tashuvchidagi uchta satellit va qo‘zg‘almas toj shesternya."),
                "Case-hardened steel", planet, m.MachinedSteel, e(MotorX1 + 0.002f), AlongX, false, Vector3.up, 0.5f);
            Mesh carrier = ctx.Mesh("dt-diff-carrier", null);
            Pt(gb, id + ".gearbox.differential", T("Differential", "Differensial",
                    "Open bevel-gear differential driven by the planet carrier.", "Satellit tashuvchisi aylantiradigan ochiq konussimon differensial."),
                "Ductile iron", carrier, m.CastIron, e(MotorX1 + 0.105f), AlongX, false, Vector3.up, 0.4f);
            Pt(gb, id + ".gearbox.side-gears", T("Side gears", "Yarim o‘q shesternyalari", "Bevel gears splined to the half-shaft stems.", "Yarim o‘q dumlariga shlitsali konussimon shesternyalar."),
                "Case-hardened steel", ctx.Mesh("dt-diff-side-gears", null), m.MachinedSteel, e(MotorX1 + 0.105f), AlongX, false, Vector3.left, 0.3f);
            Pt(gb, id + ".gearbox.pinion-gears", T("Pinion gears and cross pin", "Satellitlar va o‘q", "Planet bevel gears on the cross pin.", "O‘qdagi konussimon satellitlar."),
                "Case-hardened steel", ctx.Mesh("dt-diff-pinions", null), m.ForgedSteel, e(MotorX1 + 0.105f), AlongX, false, Vector3.forward, 0.3f);
            Mesh seal = ctx.Mesh("dt-output-seal", null);
            Pt(gb, id + ".gearbox.seal-right", T("Right output seal", "O‘ng chiqish salnigi", "Lip seal on the right output.", "O‘ng chiqishdagi labli salnik."),
                "FKM rubber", seal, m.Rubber, e(GearX1 - 0.006f), AlongX, true, Vector3.right, 0.4f);
            Pt(gb, id + ".gearbox.seal-left", T("Left output seal", "Chap chiqish salnigi", "Lip seal in the motor's end bell.", "Motor yon qopqog‘idagi labli salnik."),
                "FKM rubber", seal, m.Rubber, e(MotorX0 - 0.030f), AlongX, true, Vector3.left, 0.4f);

            // Inverter on top, across the motor and the gearbox.
            Transform inv = ctx.SubAssembly(asm, id + ".inverter", T("Power Inverter", "Quvvat invertori",
                    "Silicon-carbide inverter that turns the battery's direct current into three-phase current for the motor and back when braking.",
                    "Akkumulyator o‘zgarmas tokini motor uchun uch fazali tokka, tormozlashda esa teskari aylantiruvchi kremniy karbidli invertor."),
                "Aluminium and electronics", Vector3.up, 0.9f);
            Vector3 invC = new Vector3(-0.115f, E.y + 0.122f, E.z - 0.008f);
            Vector3 invSize = new Vector3(0.27f, 0.052f, 0.14f);
            Mesh invBox = ctx.Mesh("dt-inverter-housing", k =>
            {
                k.RoundedBox(Vector3.zero, invSize, 0.008f, 1);
                k.Box(new Vector3(0f, -invSize.y * 0.5f - 0.006f, 0f), new Vector3(0.05f, 0.014f, 0.05f));
            });
            Pt(inv, id + ".inverter.housing", T("Inverter housing", "Invertor korpusi",
                    "Cast housing with the power module on a pin-fin cold plate, the DC-link capacitor and the driver board.",
                    "Ignasimon qovurg‘ali sovutish plitasidagi quvvat moduli, o‘zgarmas tok kondensatori va boshqaruv platasi joylashgan quyma korpus."),
                "Cast aluminium", invBox, m.CastAluminium, invC, Quaternion.identity, false, Vector3.up, 0.4f);
            Mesh lid = ctx.Mesh("dt-inverter-lid", k =>
            {
                k.RoundedBox(Vector3.zero, new Vector3(invSize.x - 0.01f, 0.006f, invSize.z - 0.01f), 0.0025f, 1);
                for (int i = 0; i < 7; i++)
                    k.Box(new Vector3(-0.105f + i * 0.035f, 0.007f, 0f), new Vector3(0.004f, 0.008f, invSize.z - 0.03f));
            });
            Pt(inv, id + ".inverter.lid", T("Ribbed lid", "Qovurg‘ali qopqoq", "Stiffened lid sealed with a formed-in-place gasket.", "Joyida shakllangan qistirma bilan zichlangan qovurg‘ali qopqoq."),
                "Cast aluminium", lid, m.MachinedAluminium, invC + Vector3.up * (invSize.y * 0.5f + 0.003f), Quaternion.identity, false, Vector3.up, 0.6f);
            Mesh hv = ctx.Mesh("dt-hv-connector", k =>
            {
                k.RoundedBox(Vector3.zero, new Vector3(0.03f, 0.034f, 0.05f), 0.004f, 1);
                foreach (float dz in new[] { -0.012f, 0.012f })
                {
                    k.Push(new Vector3(-0.015f, 0f, dz), AlongNegX);
                    k.Lathe(Pr(0f, 0f, 0.010f, 0f, 0.010f, 0.016f, 0f, 0.016f), 14, 40f);
                    k.Pop();
                }
            });
            Vector3 hvAt = invC + new Vector3(-invSize.x * 0.5f - 0.015f, 0f, -0.025f);
            Pt(inv, id + ".inverter.dc-connector", T("High-voltage DC connector", "Yuqori kuchlanishli o‘zgarmas tok ulagichi",
                    "Shielded, touch-safe two-pole connector with an interlock loop.", "Blokirovka zanjirli, ekranlangan va tegishdan himoyalangan ikki qutbli ulagich."),
                "Glass-filled PA, orange", hv, m.OrangeHighVoltage, hvAt, Quaternion.identity, false, Vector3.left, 0.4f);
            Mesh busbar = ctx.Mesh("dt-phase-busbar-cover", k => k.RoundedBox(Vector3.zero, new Vector3(0.06f, 0.03f, 0.05f), 0.005f, 1));
            Pt(inv, id + ".inverter.phase-cover", T("Phase busbar cover", "Faza shinalari qopqog‘i",
                    "Cover over the three copper busbars that feed the stator.", "Statorni ta’minlovchi uchta mis shina ustidagi qopqoq."),
                "Glass-filled PA, orange", busbar, m.OrangeHighVoltage, new Vector3(MotorX1 - 0.04f, E.y + 0.086f, E.z - 0.03f), Quaternion.identity, false, Vector3.down, 0.3f);
            Pt(inv, id + ".inverter.signal-connector", T("Signal connector", "Signal ulagichi", "CAN and resolver connector.", "CAN va rezolver ulagichi."),
                "Glass-filled nylon", ctx.Mesh("dt-connector", null), m.BlackPlastic, invC + new Vector3(0.07f, 0f, invSize.z * 0.5f + 0.012f), Quaternion.identity, true, Vector3.forward, 0.4f);

            // Three rubber mounts: two in front on the floor, one behind on the raised floor.
            Transform mounts = ctx.SubAssembly(asm, id + ".mounts", T("Drive-Unit Mounts", "Yuritma bloki tayanchlari",
                    "Three rubber-bushed brackets that hold the unit to the floor and take its reaction torque.",
                    "Blokni polga mahkamlovchi va reaktiv momentini qabul qiluvchi uchta rezina vtulkali kronshteyn."),
                "Steel and rubber", Vector3.forward, 0.6f);
            Mesh bush = ctx.Mesh("dt-edu-mount-bush", k => k.Sleeve(0.024f, 0.008f, -0.02f, 0.02f, 18));
            var points = new[]
            {
                ("front-left", "Front left", "Old chap", new Vector3(MotorX0 + 0.11f, E.y - 0.04f, E.z + 0.118f), l.FloorTopY, rearFloor),
                ("front-right", "Front right", "Old o‘ng", new Vector3(MotorX1 + 0.12f, E.y - 0.04f, E.z + 0.118f), l.FloorTopY, rearFloor),
                ("rear", "Rear", "Orqa", new Vector3(MotorX0 + 0.11f, E.y, E.z - 0.16f), l.FloorTopY, rearStep),
            };
            foreach (var (key, en, uz, at, _, floor) in points)
            {
                Pt(mounts, id + ".mounts." + key + "-bushing", T(en + " mount bushing", uz + " tayanch vtulkasi", "Bonded rubber bushing that isolates motor whine from the floor.", "Motor shovqinini poldan ajratuvchi yopishtirilgan rezina vtulka."),
                    "Natural rubber and steel", bush, m.Rubber, at, AlongX, false, Vector3.right, 0.3f);
                float h = at.y - floor;
                Mesh brk = ctx.Mesh("dt-edu-mount-bracket-" + Mathf.RoundToInt(h * 1000f), k =>
                {
                    foreach (float sx in new[] { -0.024f, 0.024f })
                        k.PlanarPrism(new[] { new Vector3(sx, 0.022f, -0.02f), new Vector3(sx, 0.022f, 0.02f), new Vector3(sx, -h + 0.004f, 0.034f), new Vector3(sx, -h + 0.004f, -0.034f) }, 0.004f);
                    k.Box(new Vector3(0f, -h + 0.002f, 0f), new Vector3(0.08f, 0.004f, 0.07f));
                });
                Pt(mounts, id + ".mounts." + key + "-bracket", T(en + " mount bracket", uz + " tayanch kronshteyni", "Pressed clevis bolted to the floor.", "Polga boltlangan presslangan vilka."),
                    "Pressed steel", brk, m.BlackSteel, at, Quaternion.identity, false, Vector3.forward, 0.4f);
                Bolt(mounts, id + ".mounts." + key + "-bolt", at + new Vector3(0.03f, 0f, 0f), Vector3.left, 0.010f, 0.06f,
                    "Through-bolt of the bushing.", "Vtulkaning o‘tuvchi bolti.");
            }
        }

        // ------------------------------------------------------------------------------------------------ battery

        private void HybridBattery(Transform system)
        {
            string id = "driveline.hybrid-battery";
            Transform asm = ctx.Assembly(system, id, T("Hybrid Battery Module", "Gibrid akkumulyator moduli",
                    "Compact 400 V lithium-ion pack (about 1.5 kWh, high power) beside the rear drive unit; recovers braking energy and powers the rear motor.",
                    "Orqa yuritma bloki yonidagi ixcham 400 V litiy-ion batareya (taxminan 1,5 kVt·soat, yuqori quvvatli); tormozlash energiyasini qaytaradi va orqa motorni ta’minlaydi."),
                "Aluminium and lithium-ion cells", new Vector3(-0.6f, 1f, 0.3f), 1f);
            float y0 = Floor(new Vector3(-0.42f, 0.6f, E.z + 0.21f), l.FloorTopY + 0.03f);
            Vector3 size = new Vector3(0.19f, 0.24f, 0.26f);
            Vector3 c = new Vector3(-0.405f, y0 + 0.012f + size.y * 0.5f, E.z + 0.215f);
            Transform pack = ctx.SubAssembly(asm, id + ".pack", T("Battery Pack", "Batareya bloki",
                    "Sealed case with 96 pouch cells, the management electronics and the service disconnect.",
                    "96 ta paket elementli, boshqaruv elektronikasi va xizmat uzgichli zichlangan korpus."),
                "Aluminium and lithium-ion cells", Vector3.up, 0.6f);
            Mesh box = ctx.Mesh("dt-battery-case", k =>
            {
                k.RoundedBox(Vector3.zero, size, 0.012f, 1);
                for (int i = 0; i < 4; i++)
                    k.Box(new Vector3(size.x * 0.5f + 0.002f, -0.08f + i * 0.053f, 0f), new Vector3(0.004f, 0.008f, size.z - 0.04f));
            });
            Pt(pack, id + ".pack.case", T("Battery case", "Batareya korpusi",
                    "Extruded-aluminium case with a liquid-cooled base plate; crash-rated and sealed to IP67.",
                    "Suyuqlik bilan sovutiladigan taglik plitali presslangan alyuminiy korpus; to‘qnashuvga chidamli, IP67 bo‘yicha zichlangan."),
                "Aluminium and glass-fibre composite", box, m.BatteryCase, c, Quaternion.identity, false, Vector3.left, 0.3f);
            Mesh lid = ctx.Mesh("dt-battery-lid", k => k.RoundedBox(Vector3.zero, new Vector3(size.x - 0.006f, 0.008f, size.z - 0.006f), 0.003f, 1));
            Pt(pack, id + ".pack.lid", T("Battery lid", "Batareya qopqog‘i", "Bolted lid over the cell stack.", "Elementlar to‘plami ustidagi boltli qopqoq."),
                "Aluminium", lid, m.GreyPlastic, c + Vector3.up * (size.y * 0.5f + 0.003f), Quaternion.identity, false, Vector3.up, 0.5f);
            Mesh disconnect = ctx.Mesh("dt-service-disconnect", k =>
            {
                k.RoundedBox(new Vector3(0f, 0.01f, 0f), new Vector3(0.05f, 0.02f, 0.03f), 0.004f, 1);
                k.Box(new Vector3(0f, 0.026f, 0f), new Vector3(0.04f, 0.012f, 0.008f));
            });
            Pt(pack, id + ".pack.service-disconnect", T("Service disconnect", "Xizmat uzgichi",
                    "Pull-out plug with a fuse that splits the pack for safe servicing.", "Xavfsiz xizmat uchun batareyani ikkiga ajratuvchi saqlagichli tortib olinadigan ulagich."),
                "Glass-filled PA, orange", disconnect, m.OrangeHighVoltage, c + new Vector3(-0.04f, size.y * 0.5f + 0.007f, 0.04f), Quaternion.identity, false, Vector3.up, 0.6f);
            Vector3 bHv = c + new Vector3(0.02f, size.y * 0.5f + 0.017f, -0.07f);
            Pt(pack, id + ".pack.hv-connector", T("High-voltage connector", "Yuqori kuchlanishli ulagich",
                    "Two-pole DC outlet to the inverter, with interlock.", "Invertorga boradigan blokirovkali ikki qutbli o‘zgarmas tok chiqishi."),
                "Glass-filled PA, orange", ctx.Mesh("dt-hv-connector", null), m.OrangeHighVoltage, bHv, Quaternion.Euler(0f, 0f, 90f), false, Vector3.up, 0.4f);
            Mesh bms = ctx.Mesh("dt-bms", k => k.RoundedBox(Vector3.zero, new Vector3(0.012f, 0.08f, 0.12f), 0.003f, 1));
            Pt(pack, id + ".pack.management-unit", T("Battery management unit", "Batareya boshqaruv bloki",
                    "Monitors every cell's voltage and temperature and balances the cells.", "Har bir element kuchlanishi va haroratini kuzatadi hamda ularni muvozanatlaydi."),
                "Electronics in aluminium", bms, m.BlackSteel, c + new Vector3(size.x * 0.5f + 0.012f, 0.03f, 0f), Quaternion.identity, false, Vector3.right, 0.4f);
            Mesh port = ctx.Mesh("dt-coolant-port", k => k.Lathe(Pr(0f, 0f, 0.008f, 0f, 0.008f, 0.02f, 0.0095f, 0.022f, 0.0095f, 0.026f, 0f, 0.026f), 12, 40f));
            foreach (float dy in new[] { -0.05f, 0.0f })
                Pt(pack, id + ".pack.coolant-port-" + (dy < 0 ? "inlet" : "outlet"), T(dy < 0 ? "Coolant inlet" : "Coolant outlet", dy < 0 ? "Sovutish suyuqligi kirishi" : "Sovutish suyuqligi chiqishi",
                        "Quick-connect port of the cold plate's glycol circuit.", "Sovutish plitasi glikol konturining tez ulanadigan patrubkasi."),
                    "Aluminium", port, m.MachinedAluminium, c + new Vector3(0.05f, dy - 0.04f, size.z * 0.5f), InternalsContext.AlongY(Vector3.forward), true, Vector3.forward, 0.4f);

            Transform mount = ctx.SubAssembly(asm, id + ".mounting", T("Mounting", "Mahkamlash",
                    "Two steel brackets that bolt the pack to the floor beside the drive unit.", "Batareyani yuritma bloki yonida polga mahkamlovchi ikki po‘lat kronshteyn."),
                "Steel", Vector3.forward, 0.5f);
            Mesh foot = ctx.Mesh("dt-battery-foot", k =>
            {
                k.Box(new Vector3(0f, 0f, 0f), new Vector3(size.x + 0.03f, 0.004f, 0.03f));
                foreach (float sx in new[] { -1f, 1f })
                    k.Box(new Vector3(sx * (size.x * 0.5f + 0.002f), 0.03f, 0f), new Vector3(0.004f, 0.06f, 0.03f));
            });
            foreach (float dz in new[] { -0.08f, 0.08f })
            {
                string side = dz < 0 ? "rear" : "front";
                Vector3 at = new Vector3(c.x, y0 + 0.002f, c.z + dz);
                Pt(mount, id + ".mounting.bracket-" + side, T(dz < 0 ? "Rear bracket" : "Front bracket", dz < 0 ? "Orqa kronshteyn" : "Old kronshteyn",
                        "U-bracket under the case, bolted to the floor.", "Korpus ostidagi, polga boltlangan U-simon kronshteyn."),
                    "Pressed steel", foot, m.BlackSteel, at, Quaternion.identity, false, Vector3.forward, 0.4f);
                foreach (float sx in new[] { -1f, 1f })
                    Bolt(mount, id + ".mounting.bolt-" + side + (sx < 0 ? "-left" : "-right"), at + new Vector3(sx * (size.x * 0.5f + 0.008f), 0.002f, 0f), Vector3.down, 0.008f, 0.02f,
                        "Bolts the bracket to the floor.", "Kronshteynni polga mahkamlaydi.");
            }

            // DC cables from the pack to the inverter.
            Vector3 invHv = new Vector3(-0.115f - 0.135f - 0.03f, E.y + 0.122f, E.z - 0.008f - 0.025f);
            Mesh cables = ctx.Mesh("dt-hv-cables", k =>
            {
                foreach (float dz in new[] { -0.012f, 0.012f })
                {
                    Vector3 a = bHv + new Vector3(0f, 0.016f, dz), b = invHv + new Vector3(0f, 0f, dz);
                    var pts = new[] { a, a + new Vector3(0f, 0.04f, 0f), Vector3.Lerp(a, b, 0.5f) + new Vector3(0f, 0.07f, 0f), b + new Vector3(-0.05f, 0.0f, 0f), b };
                    k.Tube(MeshKit.CatmullRom(pts, 6), 0.0085f, 10);
                }
            });
            Pt(asm, id + ".hv-cables", T("High-voltage cables", "Yuqori kuchlanishli kabellar",
                    "Two shielded 35 mm² orange cables, DC plus and minus, from the pack to the inverter.",
                    "Batareyadan invertorga boradigan ikki ekranlangan 35 mm² to‘q sariq kabel, musbat va manfiy."),
                "Copper, silicone jacket", cables, m.OrangeHighVoltage, Vector3.zero, Quaternion.identity, false, Vector3.up, 0.6f);
        }

        // ------------------------------------------------------------------------------------------------ contacts

        private void Contacts()
        {
            // Bolted to the engine: flywheel on the crank flange (its pilot enters the flywheel bore), bell housing on
            // the block and the oil pan, the engine mount's arm on the timing cover.
            ctx.AllowContact("powertrain.clutch.flywheel", "V6/");
            ctx.AllowContact("powertrain.transaxle.case.bell", "V6/");
            ctx.AllowContact("powertrain.mounts.engine.arm", "V6/");
            ctx.AllowContact("powertrain.mounts.engine.arm-bolt", "V6/");
            // The clutch sits in the bell housing; its pressure line passes the housing's grommet, the actuator stands on it.
            ctx.AllowContact("powertrain.clutch", "powertrain.transaxle.case");
            ctx.AllowContact("powertrain.clutch", "powertrain.transaxle.gear-train.input-shaft");
            // Mounts: arms on the case, pedestals and towers bolted to the bay floor, torque rod on the bell-housing lug
            // and on the subframe tie bar.
            ctx.AllowContact("powertrain.mounts.transmission.arm", "powertrain.transaxle.case");
            ctx.AllowContact("powertrain.mounts", "InteriorFloor");
            ctx.AllowContact("powertrain.mounts", "CarConcept");
            ctx.AllowContact("powertrain.mounts.torque-rod", "powertrain.transaxle.case.bell-housing");
            ctx.AllowContact("powertrain.mounts.torque-rod", "suspension.front-subframe.tie-bar");
            // Half shafts: inner joints splined into the final drive, shafts through the wheel-well walls, outer joints
            // in the knuckles and hubs; the left shaft crosses the footwell side shells of the concept's cabin.
            ctx.AllowContact("driveline.front-half-shafts", "powertrain.transaxle.final-drive");
            ctx.AllowContact("driveline.front-half-shafts", "InteriorFloor");
            ctx.AllowContact("driveline.front-half-shafts", "CarConcept");
            ctx.AllowContact("driveline.front-half-shafts.left", "InteriorDash");
            ctx.AllowContact("driveline.front-half-shafts.right", "InteriorDash");
            ctx.AllowContact("driveline.front-half-shafts.left.outer-joint", "suspension.front-left.knuckle");
            ctx.AllowContact("driveline.front-half-shafts.right.outer-joint", "suspension.front-right.knuckle");
            // Rear: unit and battery bolted to the floor, shafts through the rear wells into the hub carriers of the rear
            // suspension (agent A's ids are assumed to be suspension.rear-<side>.hub-carrier).
            ctx.AllowContact("driveline.rear-drive-unit.mounts", "InteriorFloor");
            ctx.AllowContact("driveline.rear-drive-unit.mounts", "CarConcept");
            ctx.AllowContact("driveline.hybrid-battery.mounting", "InteriorFloor");
            ctx.AllowContact("driveline.hybrid-battery.mounting", "CarConcept");
            ctx.AllowContact("driveline.rear-half-shafts", "driveline.rear-drive-unit");
            ctx.AllowContact("driveline.rear-half-shafts", "InteriorFloor");
            ctx.AllowContact("driveline.rear-half-shafts", "CarConcept");
            ctx.AllowContact("driveline.rear-half-shafts.left.outer-joint", "suspension.rear-left.hub-carrier");
            ctx.AllowContact("driveline.rear-half-shafts.right.outer-joint", "suspension.rear-right.hub-carrier");
            ctx.AllowContact("driveline.hybrid-battery.hv-cables", "driveline.rear-drive-unit.inverter");
        }
    }

    /// <summary>
    /// Geometry helpers for the drivetrain (gears, bevel gears, a differential carrier, case sections). Candidates for
    /// the shared MeshKit: Gear, BevelGear, CrownWheel, ConvexHull, RingFace.
    /// </summary>
    internal static class DriveKit
    {
        private static Vector2 Polar(float r, float a) => new Vector2(r * Mathf.Cos(a), r * Mathf.Sin(a));

        /// <summary>Spur gear along +Y from y0 to y1: a body from the bore to the root circle and trapezoid teeth.</summary>
        public static void Gear(MeshKit k, int teeth, float rRoot, float rTip, float y0, float y1, float rBore, int bodySegments)
        {
            int seg = bodySegments > 0 ? bodySegments : Mathf.Max(12, teeth / 2);
            k.Lathe(new[] { new Vector2(rBore, y0), new Vector2(rRoot, y0), new Vector2(rRoot, y1), new Vector2(rBore, y1), new Vector2(rBore, y0) }, seg, 40f);
            float pitch = Mathf.PI * 2f / teeth;
            float r0 = rRoot - 0.0003f;
            for (int i = 0; i < teeth; i++)
            {
                float a = pitch * i;
                Prism(k, new[] { Polar(r0, a - pitch * 0.28f), Polar(rTip, a - pitch * 0.14f), Polar(rTip, a + pitch * 0.14f), Polar(r0, a + pitch * 0.28f) }, y0, y1);
            }
        }

        /// <summary>Prism of a convex polygon in the XZ plane between y0 and y1, flat shaded.</summary>
        public static void Prism(MeshKit k, Vector2[] poly, float y0, float y1)
        {
            int n = poly.Length;
            Vector2 c = Vector2.zero;
            foreach (Vector2 p in poly) c += p;
            c /= n;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % n];
                Vector2 e = b - a;
                Vector2 nn = new Vector2(e.y, -e.x).normalized;
                if (Vector2.Dot(nn, (a + b) * 0.5f - c) < 0f) nn = -nn;
                var N = new Vector3(nn.x, 0f, nn.y);
                int v0 = k.Vertex(new Vector3(a.x, y0, a.y), N, new Vector2(0f, y0));
                int v1 = k.Vertex(new Vector3(b.x, y0, b.y), N, new Vector2(e.magnitude, y0));
                int v2 = k.Vertex(new Vector3(b.x, y1, b.y), N, new Vector2(e.magnitude, y1));
                int v3 = k.Vertex(new Vector3(a.x, y1, a.y), N, new Vector2(0f, y1));
                k.Quad(v0, v1, v2, v3);
            }
            foreach (float y in new[] { y0, y1 })
            {
                Vector3 N = y == y0 ? Vector3.down : Vector3.up;
                int first = -1;
                for (int i = 0; i < n; i++)
                {
                    int v = k.Vertex(new Vector3(poly[i].x, y, poly[i].y), N, poly[i]);
                    if (first < 0) first = v;
                }
                for (int i = 1; i < n - 1; i++) k.Tri(first, first + i, first + i + 1);
            }
        }

        /// <summary>Straight bevel gear along +Y: cone from rBig at y = 0 to rSmall at y = h, teeth along the slant.</summary>
        public static void BevelGear(MeshKit k, int teeth, float rBig, float rSmall, float h, float rBore)
        {
            k.Lathe(new[] { new Vector2(rBore, 0f), new Vector2(rBig - 0.002f, 0f), new Vector2(rSmall - 0.002f, h), new Vector2(rBore, h), new Vector2(rBore, 0f) }, Mathf.Max(12, teeth), 35f);
            Vector2 s2 = new Vector2(rSmall - rBig, h);
            float slant = s2.magnitude;
            s2 /= slant;
            Vector2 n2 = new Vector2(s2.y, -s2.x);
            float pitch = Mathf.PI * 2f / teeth;
            for (int i = 0; i < teeth; i++)
            {
                float a = pitch * i;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 along = radial * s2.x + Vector3.up * s2.y;
                Vector3 normal = radial * n2.x + Vector3.up * n2.y;
                float rm = (rBig + rSmall) * 0.5f;
                Vector3 centre = radial * (rm - 0.002f) + Vector3.up * (h * 0.5f) + normal * 0.002f;
                k.Push(centre, Quaternion.LookRotation(tangent, along));
                k.Box(Vector3.zero, new Vector3(0.0035f, slant * 0.9f, rm * pitch * 0.42f));
                k.Pop();
            }
        }

        /// <summary>Crown (ring) wheel along +Y: body ring from y0 to y1 between rIn and rOut, radial teeth on the y0 face.</summary>
        public static void CrownWheel(MeshKit k, int teeth, float rIn, float rOut, float y0, float y1, float toothH)
        {
            k.Lathe(new[] { new Vector2(rIn, y0), new Vector2(rOut, y0), new Vector2(rOut + 0.002f, y0 + 0.003f), new Vector2(rOut + 0.002f, y1), new Vector2(rIn, y1), new Vector2(rIn, y0) }, 40, 40f);
            float pitch = Mathf.PI * 2f / teeth;
            float len = rOut - rIn - 0.004f, rm = (rIn + rOut) * 0.5f;
            for (int i = 0; i < teeth; i++)
            {
                float deg = Mathf.Rad2Deg * pitch * i;
                k.Push(Vector3.zero, Quaternion.Euler(0f, -deg, 0f));
                k.Push(new Vector3(rm, y0 - toothH * 0.5f, 0f), Quaternion.Euler(0f, 18f, 0f));
                k.Box(Vector3.zero, new Vector3(len, toothH, rm * pitch * 0.45f));
                k.Pop();
                k.Pop();
            }
        }

        /// <summary>Open differential carrier along +Y (centre at 0): end bells, a windowed middle band and the crown flange.</summary>
        public static void DiffCarrier(MeshKit k)
        {
            k.Lathe(new[] { new Vector2(0.015f, -0.056f), new Vector2(0.022f, -0.056f), new Vector2(0.022f, -0.040f), new Vector2(0.040f, -0.032f), new Vector2(0.050f, -0.024f), new Vector2(0.046f, -0.024f), new Vector2(0.036f, -0.030f), new Vector2(0.015f, -0.036f), new Vector2(0.015f, -0.056f) }, 16, 35f);
            k.Lathe(new[] { new Vector2(0.015f, 0.036f), new Vector2(0.036f, 0.030f), new Vector2(0.046f, 0.024f), new Vector2(0.050f, 0.024f),
                new Vector2(0.040f, 0.032f), new Vector2(0.022f, 0.040f), new Vector2(0.022f, 0.056f), new Vector2(0.015f, 0.056f), new Vector2(0.015f, 0.036f) }, 16, 35f);
            k.Lathe(new[] { new Vector2(0.046f, 0.010f), new Vector2(0.060f, 0.010f), new Vector2(0.060f, 0.022f), new Vector2(0.046f, 0.022f), new Vector2(0.046f, 0.010f) }, 16, 40f);
            foreach (float start in new[] { 40f, 220f })
                k.Lathe(new[] { new Vector2(0.046f, -0.024f), new Vector2(0.050f, -0.024f), new Vector2(0.050f, 0.024f), new Vector2(0.046f, 0.024f), new Vector2(0.046f, -0.024f) }, 8, 40f, start, 100f);
        }

        /// <summary>Two side gears facing each other on the carrier axis (+Y).</summary>
        public static void SideGears(MeshKit k)
        {
            foreach (float sgn in new[] { -1f, 1f })
            {
                k.Push(new Vector3(0f, sgn * 0.024f, 0f), sgn > 0 ? Quaternion.Euler(180f, 0f, 0f) : Quaternion.identity);
                BevelGear(k, 12, 0.026f, 0.016f, 0.012f, 0.011f);
                k.Pop();
            }
        }

        /// <summary>Cross pin across the carrier (local X) with two planet bevel gears on it.</summary>
        public static void SpiderGears(MeshKit k)
        {
            k.Push(Vector3.zero, Quaternion.Euler(0f, 0f, -90f));
            k.Cylinder(0.0065f, -0.046f, 0.046f, 12);
            k.Pop();
            foreach (float sgn in new[] { -1f, 1f })
            {
                k.Push(new Vector3(sgn * 0.026f, 0f, 0f), Quaternion.Euler(0f, 0f, sgn > 0 ? 90f : -90f));
                BevelGear(k, 8, 0.017f, 0.010f, 0.010f, 0.0065f);
                k.Pop();
            }
        }

        /// <summary>Convex hull (counter-clockwise) of 2D points: monotone chain.</summary>
        public static List<Vector2> ConvexHull(List<Vector2> pts)
        {
            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var hull = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector2 p = pass == 0 ? pts[i] : pts[pts.Count - 1 - i];
                    while (hull.Count >= start + 2)
                    {
                        Vector2 a = hull[hull.Count - 2], b = hull[hull.Count - 1];
                        if ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x) > 1e-9f) break;
                        hull.RemoveAt(hull.Count - 1);
                    }
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        /// <summary>
        /// Flat ring between two section outlines (same point count, matching order) at x, facing ±x, in the frame of
        /// a Sweep along +x with up +y (outline x = car −z, outline y = car +y).
        /// </summary>
        public static void RingFace(MeshKit k, List<Vector2> outer, List<Vector2> inner, float x, bool facePlusX)
        {
            Vector3 n = facePlusX ? Vector3.right : Vector3.left;
            int count = Mathf.Min(outer.Count, inner.Count);
            for (int i = 0; i < count; i++)
            {
                Vector2 a = outer[i], b = outer[(i + 1) % count], c = inner[(i + 1) % count], d = inner[i];
                int i0 = k.Vertex(new Vector3(x, a.y, -a.x), n, a), i1 = k.Vertex(new Vector3(x, b.y, -b.x), n, b);
                int i2 = k.Vertex(new Vector3(x, c.y, -c.x), n, c), i3 = k.Vertex(new Vector3(x, d.y, -d.x), n, d);
                k.Quad(i0, i1, i2, i3);
            }
        }

        /// <summary>Light hex bolt along local -Y (head above y = 0): washer face, six-sided head, eight-sided shank.</summary>
        public static void LightBolt(MeshKit k, float d, float len)
        {
            float s = d * 1.55f, R = s / (2f * Mathf.Cos(30f * Mathf.Deg2Rad)), head = d * 0.65f, w = d * 0.12f;
            k.Lathe(new[] { new Vector2(R * 1.06f, 0f), new Vector2(R * 1.06f, w), new Vector2(R * 0.9f, w) }, 12, 40f);
            k.Lathe(new[] { new Vector2(R, w), new Vector2(R, w + head * 0.85f), new Vector2(R * 0.86f, w + head), new Vector2(0f, w + head) }, 6, 40f, 30f, 360f, true);
            k.Lathe(new[] { new Vector2(0f, -len), new Vector2(d * 0.5f, -len), new Vector2(d * 0.5f, 0.001f) }, 8, 40f);
        }

        /// <summary>
        /// Light convoluted boot along +Y, open at both ends: collar r0 at y = 0, collar r1 at the top, folds tapering
        /// from the large end to the small one (three samples per fold).
        /// </summary>
        public static void Boot(MeshKit k, float r0, float r1, float rMin, float rMax, float length, int folds, int segments, float collar)
        {
            var p = new List<Vector2> { new Vector2(r0, 0f), new Vector2(r0, collar) };
            int samples = folds * 3;
            float span = length - 2f * collar;
            for (int i = 1; i < samples; i++)
            {
                float t = (float)i / samples;
                float hi = Mathf.Lerp(rMax, r1 + 0.012f, t), lo = Mathf.Lerp(rMin, r1 + 0.002f, t);
                float r = (hi + lo) * 0.5f + (hi - lo) * 0.5f * Mathf.Cos(t * folds * Mathf.PI * 2f);
                p.Add(new Vector2(r, collar + span * t));
            }
            p.Add(new Vector2(r1, length - collar));
            p.Add(new Vector2(r1, length));
            k.Lathe(p, segments, 55f);
        }

        /// <summary>Box-section pedestal of height h from y = 0 (floor) up, with a base plate and a top plate.</summary>
        public static void Pedestal(MeshKit k, float h)
        {
            k.Box(new Vector3(0f, 0.003f, 0f), new Vector3(0.07f, 0.006f, 0.11f));
            k.BoxTube(new[] { new Vector3(0f, 0.006f, 0f), new Vector3(0f, h - 0.006f, 0f) }, 0.06f, 0.06f, 0.003f, Vector3.forward);
            k.Box(new Vector3(0f, h - 0.003f, 0f), new Vector3(0.075f, 0.006f, 0.075f));
            foreach (float sz in new[] { -1f, 1f })
                k.PlanarPrism(new[] { new Vector3(0f, 0.006f, sz * 0.03f), new Vector3(0f, 0.006f, sz * 0.054f), new Vector3(0f, Mathf.Min(0.05f, h * 0.5f), sz * 0.03f) }, 0.004f);
        }
    }
}
#endif
