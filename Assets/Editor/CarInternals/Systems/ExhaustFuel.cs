#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace CarInternals
{
    /// <summary>
    /// Exhaust and fuel systems of the hero car (brief section 11 D).
    ///
    /// Exhaust: the CAD V6 has both manifold collectors pointing to the right-hand (transaxle) end, the front bank's
    /// at the nose and the rear bank's at the bulkhead. Each bank gets a V-band joint, a close-coupled catalyst with
    /// upstream and downstream lambda sensors; the front bank's pipe crosses over the transaxle (there is no room under
    /// the engine: the sump sits 2 cm above the bay floor) and both meet in a Y-junction in the pocket under the right
    /// dash shell. A short decoupler and the bulkhead pipe take the gas through the toe board into the right footwell
    /// channel, where the gasoline particulate filter sits; the centre pipe then runs rearward on the floor of the tub
    /// along the only free corridor between the seat pans and the floor mats (measured with a 5 mm height field: the
    /// seat shells come down to 4-6 cm above the floor, so the centre pipe is necked to 38 mm), through the rear floor
    /// step and into a three-chamber silencer that hangs under the rear deck, with two tailpipes under the diffuser.
    ///
    /// Fuel: a blow-moulded tank shaped to the free rear-left floor bay (between the left middle seat, the rear seat
    /// and the side trim), two straps, the pump and level-sender module, rollover vent valve, EVAP canister, filler neck
    /// to a pocket in the left side trim, supply and purge lines along the left floor corridor, through the toe board
    /// and over the rear valve cover to two port-injection rails on the intake runners with six injectors, a crossover
    /// tube and the rail pressure sensor.
    /// </summary>
    [CarInternalsSubsystem("Exhaust", 60)]
    public sealed class ExhaustFuel : ICarInternalsSubsystem
    {
        private InternalsContext ctx;
        private CarPalette m;
        private CarLayout l;

        // Pipe sizes (radius, m).
        private const float BankR = 0.0254f;      // 2.0 in down pipes
        private const float MergedR = 0.0285f;    // 57 mm after the Y-junction
        private const float CentreR = 0.019f;     // 38 mm centre pipe (tub clearance)
        private const float TailR = 0.022f;

        /// <summary>False in the combined car until the exhaust is re-planned (see Build).</summary>
        public static readonly bool BuildExhaust = false;

        public void Build(InternalsContext context)
        {
            ctx = context;
            m = ctx.Mats;
            l = ctx.Layout;
            // Integration (combined car): the exhaust as routed here cannot coexist with the other systems. Its
            // drops from both banks and the rear-bank catalyst pass through the transaxle's front final drive and the
            // left inner tripod joint, the centre pipe shares the seat-track corridors, and the silencer box stands in
            // the rear subframe's cradle. It stays out of the build until it is re-planned (tunnel in the floor tub,
            // a route clear of the final-drive pocket, a silencer place clear of the rear subframe).
            if (BuildExhaust)
            {
                Transform exhaust = ctx.System("Exhaust");
                ExhaustSystem(exhaust);
                ExhaustContacts();
            }
            else ctx.Note("exhaust: not built in the combined car (route clashes with the transaxle final drive, seat tracks and rear subframe; needs a re-plan)");
            Transform fuel = ctx.System("Fuel");
            FuelTank(fuel);
            FuelLines(fuel);
            FuelContacts();
            if (BuildExhaust) ctx.Note("exhaust: front outlet " + CarLayout.V(OutletFront) + " rear outlet " + CarLayout.V(OutletRear) +
                     ", centre pipe 38 mm along the right floor corridor, silencer under the rear deck z " + CarLayout.F(SilencerZ0) + ".." + CarLayout.F(SilencerZ1));
            ctx.Note("fuel: tank " + CarLayout.B(TankBounds) + ", rails y/z rear " + CarLayout.F(RailRearY) + "/" + CarLayout.F(RailRearZ) +
                     " front " + CarLayout.F(RailFrontY) + "/" + CarLayout.F(RailFrontZ));
        }

        // ================================================================================================ layout

        private Bounds V6(string renderer, Vector3 fallbackMin, Vector3 fallbackMax)
        {
            if (l.V6Parts.TryGetValue(renderer, out Bounds b)) return b;
            ctx.Warn("ExhaustFuel: V6 part '" + renderer + "' not measured, using the reference numbers");
            var f = new Bounds();
            f.SetMinMax(fallbackMin, fallbackMax);
            return f;
        }

        // Manifold collector outlets: the end of each collector pipe (its centre is 31 mm above the AABB bottom and
        // 6 mm outboard of the AABB centre, measured from the CAD mesh in the lab), pipe end pointing to +x.
        private Vector3 OutletFront
        {
            get
            {
                Bounds b = V6("Exhaust manifold 1", new Vector3(-0.109f, 0.337f, 2.135f), new Vector3(0.466f, 0.458f, 2.212f));
                return new Vector3(b.max.x, b.min.y + 0.0314f, b.center.z + 0.0062f);
            }
        }

        private Vector3 OutletRear
        {
            get
            {
                Bounds b = V6("Exhaust manifold 2", new Vector3(-0.195f, 0.337f, 1.547f), new Vector3(0.380f, 0.458f, 1.623f));
                return new Vector3(b.max.x, b.min.y + 0.0314f, b.center.z - 0.0064f);
            }
        }

        private Bounds FlangeRear => V6("Intake manifold mirored001", new Vector3(-0.095f, 0.530f, 1.695f), new Vector3(0.304f, 0.582f, 1.747f));
        private Bounds FlangeFront => V6("Intake manifold mirored", new Vector3(-0.188f, 0.530f, 2.011f), new Vector3(0.211f, 0.582f, 2.063f));

        private float RailRearY => FlangeRear.max.y + 0.033f;
        private float RailRearZ => FlangeRear.max.z + 0.018f;
        private float RailFrontY => FlangeFront.max.y + 0.023f;
        private float RailFrontZ => FlangeFront.min.z - 0.018f;

        // Runner ends on the head flanges as fractions of the flange length (measured from the runner meshes).
        private static readonly float[] RunnersRear = { 0.150f, 0.481f, 0.817f };
        private static readonly float[] RunnersFront = { 0.188f, 0.521f, 0.844f };

        private float Floor => l.FloorTopY;
        private const float SilencerZ0 = -1.535f, SilencerZ1 = -1.885f;
        private static Bounds TankBounds
        {
            get
            {
                var b = new Bounds();
                b.SetMinMax(new Vector3(-0.533f, 0.136f, -0.8825f), new Vector3(-0.297f, 0.286f, -0.4275f));
                return b;
            }
        }

        // ================================================================================================ helpers

        private static Txt T(string en, string uz, string de, string du) => new Txt(en, uz, de, du);

        private Transform Part(Transform parent, string id, Txt t, string material, Mesh mesh, Material mat, Vector3 pos, Quaternion rot,
            bool fine = false, Vector3? explode = null, float distance = 1f)
            => ctx.Part(parent, id, t, material, mesh, new[] { mat }, pos, rot, fine, explode, distance);

        private static Vector3 Centroid(IList<Vector3> pts)
        {
            Vector3 c = Vector3.zero;
            foreach (Vector3 p in pts) c += p;
            return c / Mathf.Max(1, pts.Count);
        }

        /// <summary>A pipe or line along a car-space path; the part sits at the path's centroid.</summary>
        private Transform Pipe(Transform parent, string id, Txt t, string material, List<Vector3> path, float r, int sides, Material mat,
            bool fine = false, Vector3? explode = null, float distance = 1f, System.Func<float, float> scale = null)
        {
            Vector3 c = Centroid(path);
            var local = new List<Vector3>(path.Count);
            foreach (Vector3 p in path) local.Add(p - c);
            Mesh mesh = ctx.Mesh(id, k => k.Tube(local, r, sides, true, true, Vector3.up, scale));
            return Part(parent, id, t, material, mesh, mat, c, Quaternion.identity, fine, explode, distance);
        }

        /// <summary>A lathe part whose +Y runs along <paramref name="axis"/> from <paramref name="origin"/>.</summary>
        private Transform Lathe(Transform parent, string id, Txt t, string material, string key, IList<Vector2> profile, int segments,
            Vector3 origin, Vector3 axis, Material mat, bool fine = false, Vector3? explode = null, float distance = 1f, float hard = 40f)
        {
            Mesh mesh = ctx.Mesh(key, k => k.Lathe(profile, segments, hard));
            return Part(parent, id, t, material, mesh, mat, origin, InternalsContext.AlongY(axis), fine, explode, distance);
        }

        private static List<Vector3> Bent(float radius, params Vector3[] corners) => MeshKit.Bends(corners, radius, 8);

        private static List<Vector3> Smooth(int samples, params Vector3[] pts) => MeshKit.CatmullRom(pts, samples);

        // ================================================================================================ exhaust

        private void ExhaustSystem(Transform system)
        {
            const string id = "exhaust.system";
            Transform asm = ctx.Assembly(system, id, T("Exhaust System", "Chiqarish tizimi",
                    "Twin-bank exhaust: close-coupled catalysts, Y-junction, particulate filter, centre pipe and rear silencer with two tailpipes.",
                    "Ikki qatorli chiqarish tizimi: dvigatelga yaqin katalizatorlar, Y-birlashma, zarrachalar filtri, o‘rta quvur va ikki chiqish quvurli orqa so‘ndirgich."),
                "Stainless steel 1.4512 and 1.4301", Vector3.down, 1f);

            Vector3 o1 = OutletFront, o2 = OutletRear;
            // Front bank: elbow up and back from the outlet, catalyst on the rising leg, crossover over the transaxle,
            // down behind the engine into the Y-junction.
            Vector3 d1 = new Vector3(0f, 0.70f, -0.714f).normalized;
            Vector3 b1 = new Vector3(o1.x + 0.049f, o1.y, o1.z);
            Vector3 s1 = b1 + d1 * 0.07f;
            Vector3 e1 = s1 + d1 * 0.18f;
            var cross = Bent(0.06f, e1, e1 + d1 * 0.03f, new Vector3(0.522f, 0.627f, 1.93f), new Vector3(0.528f, 0.636f, 1.71f),
                new Vector3(0.533f, 0.585f, 1.615f), new Vector3(0.535f, 0.40f, 1.525f), new Vector3(0.532f, 0.275f, 1.445f), new Vector3(0.515f, 0.214f, 1.362f));
            Bank(asm, id + ".front-bank", true, o1, b1, d1, s1, cross,
                T("Front-Bank Down Pipe and Catalyst", "Old qator tushuvchi quvuri va katalizatori",
                    "Pipe from the front-bank manifold with its close-coupled catalyst; it crosses over the transaxle to the rear of the engine.",
                    "Old qator kollektoridan chiquvchi quvur va unga yaqin katalizator; transaksl ustidan dvigatel orqasiga o‘tadi."),
                new Vector3(0.3f, 1f, 0.4f));

            // Rear bank: elbow down and back from the outlet, catalyst on the falling leg into the pocket under the dash.
            Vector3 d2 = new Vector3(0f, -0.6f, -0.8f).normalized;
            Vector3 b2 = new Vector3(o2.x + 0.075f, o2.y, o2.z);
            Vector3 s2 = b2 + d2 * 0.065f;
            Vector3 e2 = s2 + d2 * 0.17f;
            var down = Bent(0.04f, e2, e2 + d2 * 0.025f, new Vector3(0.468f, 0.212f, 1.362f));
            Bank(asm, id + ".rear-bank", false, o2, b2, d2, s2, down,
                T("Rear-Bank Down Pipe and Catalyst", "Orqa qator tushuvchi quvuri va katalizatori",
                    "Pipe from the rear-bank manifold with its close-coupled catalyst, dropping behind the engine into the Y-junction.",
                    "Orqa qator kollektoridan chiquvchi quvur va katalizator; dvigatel orqasidan Y-birlashmaga tushadi."),
                new Vector3(0.6f, 0.2f, 0.8f));

            YPipe(asm, id);
            ParticulateFilter(asm, id);
            CentrePipe(asm, id);
            Silencer(asm, id);
        }

        private Txt Cat(string bank, string bankUz) => T("Close-Coupled Catalyst", "Dvigatelga yaqin katalizator",
            "Three-way catalyst next to the " + bank + "-bank manifold: lights off within seconds of a cold start.",
            bankUz + " qator kollektori yonidagi uch komponentli katalizator: sovuq ishga tushirishdan bir necha soniyada ishlay boshlaydi.");

        private void Bank(Transform asm, string id, bool front, Vector3 outlet, Vector3 corner, Vector3 dir, Vector3 catStart, List<Vector3> after, Txt text, Vector3 explode)
        {
            string bank = front ? "front" : "rear", bankUz = front ? "Old" : "Orqa";
            Transform b = ctx.SubAssembly(asm, id, text, "Stainless steel 1.4512", explode, 0.8f);

            // Manifold joint: flange welded to the collector, flange on the down pipe, V-band clamp around both.
            Transform joint = ctx.SubAssembly(b, id + ".joint", T("Manifold Joint", "Kollektor birikmasi",
                    "V-band joint between the manifold collector and the down pipe: two machined flanges pulled together by a clamp.",
                    "Kollektor va tushuvchi quvur orasidagi V-tasmali birikma: ikki ishlangan flanesni xomut tortib turadi."),
                "Stainless steel", Vector3.right, 0.5f);
            Vector3 x = Vector3.right;
            var flange = new[] { new Vector2(0.0255f, 0f), new Vector2(0.040f, 0f), new Vector2(0.040f, 0.003f), new Vector2(0.036f, 0.007f), new Vector2(0.0255f, 0.007f), new Vector2(0.0255f, 0f) };
            Lathe(joint, id + ".joint.flange-manifold", T("Manifold flange", "Kollektor flanesi",
                    "Male V-band flange welded onto the manifold collector.", "Kollektor uchiga payvandlangan V-tasma flanesi."),
                "Stainless steel 1.4828", "v-band-flange", flange, 24, outlet - x * 0.007f, x, m.MachinedSteel, false, Vector3.left, 0.4f);
            Lathe(joint, id + ".joint.flange-pipe", T("Down-pipe flange", "Quvur flanesi",
                    "Female V-band flange with a locating spigot, welded onto the down pipe.", "Markazlovchi bo‘rtiqli V-tasma flanesi, tushuvchi quvurga payvandlangan."),
                "Stainless steel 1.4828", "v-band-flange", flange, 24, outlet + x * 0.007f, -x, m.MachinedSteel, false, Vector3.right, 0.4f);
            Mesh band = ctx.Mesh("v-band-clamp", k =>
            {
                k.Lathe(new[] { new Vector2(0.0405f, -0.008f), new Vector2(0.046f, -0.008f), new Vector2(0.047f, 0f), new Vector2(0.046f, 0.008f), new Vector2(0.0405f, 0.008f),
                    new Vector2(0.037f, 0.003f), new Vector2(0.037f, -0.003f), new Vector2(0.0405f, -0.008f) }, 28, 35f);
                k.Box(new Vector3(0f, 0f, 0.053f), new Vector3(0.016f, 0.014f, 0.014f));
            });
            Quaternion bandRot = Quaternion.LookRotation(Vector3.up, x);
            Part(joint, id + ".joint.clamp", T("V-band clamp", "V-tasmali xomut",
                    "Stainless band with a V-section that wedges the two flanges together; one T-bolt closes it.",
                    "Ikki flanesni ponasimon siquvchi V-kesimli zanglamas tasma; bitta T-bolt bilan yopiladi."),
                "Stainless steel 1.4301", band, m.ZincSteel, outlet, bandRot, false, Vector3.up, 0.5f);
            Mesh tbolt = ctx.Mesh("v-band-t-bolt", k => Fasteners.HexBolt(k, 0.006f, 0.03f, false));
            Part(joint, id + ".joint.t-bolt", T("T-bolt and nut M6", "T-bolt va gayka M6", "Tightens the V-band clamp.", "V-tasmali xomutni tortadi."),
                "Steel 10.9", tbolt, m.ZincSteel, outlet + Vector3.up * 0.053f + Vector3.forward * 0.012f, Quaternion.LookRotation(Vector3.up, Vector3.forward), true, Vector3.up, 0.3f);

            // Down-pipe elbow from the flange to the catalyst inlet.
            Vector3 pipeStart = outlet + x * 0.007f;
            var elbow = Bent(front ? 0.045f : 0.04f, pipeStart, corner, catStart);
            Pipe(b, id + ".elbow", T("Down-pipe elbow", "Tushuvchi quvur tirsagi",
                    "Mandrel-bent elbow that turns the gas from the manifold into the catalyst.",
                    "Gazni kollektordan katalizatorga buruvchi oprovkada egilgan tirsak."),
                "Stainless steel 1.4512, 1.5 mm", elbow, BankR, 16, m.MachinedSteel, false, explode, 0.4f);

            // Catalyst.
            float rCan = 0.047f, cone = 0.035f, shell = 0.11f;
            Transform cat = ctx.SubAssembly(b, id + ".catalyst", Cat(bank, bankUz), "Stainless shell, ceramic monolith", -dir, 0.6f);
            Canister(cat, id + ".catalyst", catStart, dir, BankR, rCan, cone, shell, "cc-cat",
                T("Catalyst shell", "Katalizator korpusi", "Stainless can holding the ceramic monolith (600 cells per square inch, Pd-Rh coating) in an intumescent mat.",
                    "Keramik monolit (dyuymga 600 katak, Pd-Rh qoplama)ni kengayuvchi to‘shamada ushlab turuvchi zanglamas korpus."),
                true);

            // Lambda sensors: upstream on the elbow, downstream right after the catalyst.
            // Upstream sensor: in the collector's underside for the front bank (the hood liner wraps the outlet), on the elbow
            // for the rear bank.
            Txt upText = T("Upstream Lambda Sensor", "Katalizatordan oldingi lyambda-zond",
                "Wide-band oxygen sensor before the catalyst: the engine control unit trims the mixture of this bank from it.",
                "Katalizatordan oldingi keng diapazonli kislorod datchigi: boshqaruv bloki shu qator aralashmasini u orqali to‘g‘rilaydi.");
            if (front) Lambda(b, id + ".lambda-upstream", upText, outlet + Vector3.left * 0.06f, Vector3.down, 0.032f);
            else Lambda(b, id + ".lambda-upstream", upText, catStart - dir * 0.02f, Vector3.ProjectOnPlane(Vector3.right + Vector3.up * 0.3f, dir).normalized, BankR);
            Vector3 afterStart = after[0];
            Vector3 afterDir = (after[Mathf.Min(3, after.Count - 1)] - after[0]).normalized;
            Lambda(b, id + ".lambda-downstream", T("Downstream Lambda Sensor", "Katalizatordan keyingi lyambda-zond",
                    "Switching oxygen sensor after the catalyst: monitors the catalyst's oxygen storage for the on-board diagnostics.",
                    "Katalizatordan keyingi kislorod datchigi: bort diagnostikasi uchun katalizatorning kislorod saqlashini kuzatadi."),
                afterStart + afterDir * 0.045f, Vector3.ProjectOnPlane(Vector3.right, afterDir).normalized, BankR, front ? Vector3.down : Vector3.up);

            Pipe(b, id + ".pipe", front
                    ? T("Crossover pipe", "O‘tish quvuri", "Pipe that carries the front bank's gas over the transaxle to the rear of the engine.",
                        "Old qator gazini transaksl ustidan dvigatel orqasiga olib o‘tuvchi quvur.")
                    : T("Down pipe", "Tushuvchi quvur", "Pipe from the rear catalyst down to the Y-junction.", "Orqa katalizatordan Y-birlashmagacha tushuvchi quvur."),
                "Stainless steel 1.4512, 1.5 mm", after, BankR, 16, m.MachinedSteel, false, explode, 0.5f);
        }

        /// <summary>Inlet cone, shell with seam beads and outlet cone along <paramref name="dir"/>, plus an optional heat shield.</summary>
        private void Canister(Transform parent, string id, Vector3 start, Vector3 dir, float rPipe, float rCan, float cone, float shell, string key, Txt shellText, bool heatShield)
        {
            Quaternion rot = InternalsContext.AlongY(dir);
            Mesh inCone = ctx.Mesh(key + "-inlet-cone", k => k.Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(rPipe + 0.0015f, 0f), new Vector2(rPipe + 0.0015f, 0.012f), new Vector2(rCan - 0.003f, cone - 0.002f), new Vector2(rCan - 0.003f, cone), new Vector2(0f, cone)
            }, 24, 25f));
            Part(parent, id + ".inlet-cone", T("Inlet cone", "Kirish konusi", "Diffuser that spreads the gas over the whole face of the monolith.",
                    "Gazni monolitning butun yuzasi bo‘ylab tarqatuvchi diffuzor."),
                "Stainless steel 1.4512", inCone, m.MachinedSteel, start, rot, false, -dir, 0.5f);
            Mesh body = ctx.Mesh(key + "-shell", k =>
            {
                var p = new List<Vector2> { new Vector2(0f, 0f), new Vector2(rCan - 0.004f, 0f), new Vector2(rCan, 0.004f) };
                foreach (float y in new[] { 0.25f, 0.75f })
                {
                    float c = shell * y;
                    p.Add(new Vector2(rCan, c - 0.004f)); p.Add(new Vector2(rCan + 0.0025f, c - 0.002f)); p.Add(new Vector2(rCan + 0.0025f, c + 0.002f)); p.Add(new Vector2(rCan, c + 0.004f));
                }
                p.Add(new Vector2(rCan, shell - 0.004f)); p.Add(new Vector2(rCan - 0.004f, shell)); p.Add(new Vector2(0f, shell));
                k.Lathe(p, 28, 35f);
            });
            Part(parent, id + ".shell", shellText, "Stainless steel 1.4512, ceramic monolith", body, m.CastAluminium, start + dir * cone, rot, false, Vector3.ProjectOnPlane(Vector3.up, dir).normalized, 0.5f);
            Mesh outCone = ctx.Mesh(key + "-outlet-cone", k => k.Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(rCan - 0.003f, 0f), new Vector2(rCan - 0.003f, 0.002f), new Vector2(rPipe + 0.0015f, cone - 0.012f), new Vector2(rPipe + 0.0015f, cone), new Vector2(0f, cone)
            }, 24, 25f));
            Part(parent, id + ".outlet-cone", T("Outlet cone", "Chiqish konusi", "Collects the cleaned gas back into the pipe.", "Tozalangan gazni yana quvurga yig‘adi."),
                "Stainless steel 1.4512", outCone, m.MachinedSteel, start + dir * (cone + shell), rot, false, dir, 0.5f);
            if (!heatShield) return;
            Mesh shield = ctx.Mesh(key + "-heat-shield", k => k.Lathe(new[]
            {
                new Vector2(rCan + 0.007f, 0.004f), new Vector2(rCan + 0.0095f, 0.004f), new Vector2(rCan + 0.0095f, shell - 0.004f), new Vector2(rCan + 0.007f, shell - 0.004f), new Vector2(rCan + 0.007f, 0.004f)
            }, 24, 30f, 200f, 220f));
            Part(parent, id + ".heat-shield", T("Heat shield", "Issiqlik ekrani", "Embossed aluminised-steel half shell that keeps the can's heat off the parts around it.",
                    "Korpus issiqligini atrofdagi qismlardan to‘suvchi bo‘rttirilgan alyuminlangan po‘lat yarim qobiq."),
                "Aluminised steel", shield, m.HeatShield, start + dir * cone, rot, false, Vector3.ProjectOnPlane(Vector3.up, dir).normalized, 0.8f);
        }

        private void Lambda(Transform parent, string id, Txt text, Vector3 onAxis, Vector3 outward, float pipeR, Vector3? leadSide = null)
        {
            Transform s = ctx.SubAssembly(parent, id, text, "Zirconia element, steel body", outward, 0.5f);
            Vector3 seat = onAxis + outward * (pipeR - 0.002f);
            Lathe(s, id + ".boss", T("Sensor boss M18x1.5", "Datchik uyasi M18x1.5", "Threaded bung welded into the pipe wall.", "Quvur devoriga payvandlangan rezbali uya."),
                "Stainless steel", "lambda-boss", new[] { new Vector2(0.0085f, -0.002f), new Vector2(0.0125f, -0.002f), new Vector2(0.0125f, 0.008f), new Vector2(0.0085f, 0.008f), new Vector2(0.0085f, -0.002f) },
                6, seat, outward, m.MachinedSteel, true, outward, 0.3f, 50f);
            Mesh body = ctx.Mesh("lambda-sensor", k =>
            {
                k.Lathe(new[] { new Vector2(0f, -0.012f), new Vector2(0.0055f, -0.012f), new Vector2(0.0055f, 0.006f), new Vector2(0f, 0.006f) }, 12);
                k.Lathe(new[] { new Vector2(0f, 0.008f), new Vector2(0.0127f, 0.008f), new Vector2(0.0127f, 0.020f), new Vector2(0.011f, 0.022f), new Vector2(0f, 0.022f) }, 6, 40f, 30f, 360f, true);
                k.Lathe(new[] { new Vector2(0f, 0.022f), new Vector2(0.0075f, 0.022f), new Vector2(0.0075f, 0.040f), new Vector2(0.0055f, 0.050f), new Vector2(0f, 0.050f) }, 14, 35f);
            });
            Part(s, id + ".sensor", T("Oxygen sensor", "Kislorod datchigi", "Heated zirconia cell behind a louvred steel guard tube.", "Panjarali po‘lat himoya ichidagi qizdiriladigan sirkoniy elementi."),
                "Stainless body, zirconia cell", body, m.MachinedSteel, seat + outward * 0.008f, InternalsContext.AlongY(outward), false, outward, 0.5f);
            Vector3 top = seat + outward * 0.058f;
            Vector3 side = Vector3.ProjectOnPlane(leadSide ?? (Mathf.Abs(outward.y) < 0.9f ? Vector3.up : Vector3.back), outward).normalized;
            var lead = Smooth(6, top, top + outward * 0.018f + side * 0.008f, top + outward * 0.025f + side * 0.05f, top + outward * 0.02f + side * 0.10f);
            Pipe(s, id + ".lead", T("Sensor lead", "Datchik simi", "Four-wire lead with a glass-fibre sleeve against the exhaust heat.", "Issiqlikka qarshi shisha tolali g‘ilofli to‘rt simli kabel."),
                "PTFE wires, glass-fibre sleeve", lead, 0.0028f, 8, m.WireBlack, true, outward, 0.4f);
            Mesh plug = ctx.Mesh("lambda-connector", k => k.RoundedBox(Vector3.zero, new Vector3(0.018f, 0.014f, 0.03f), 0.003f, 1));
            Vector3 end = lead[lead.Count - 1];
            Part(s, id + ".connector", T("Sensor connector", "Datchik ulagichi", "Sealed six-pin connector clipped to a bracket.", "Kronshteynga qisilgan zichlangan olti kontaktli ulagich."),
                "Glass-filled PBT", plug, m.GreyPlastic, end + side * 0.016f, Quaternion.LookRotation(side, outward), true, outward, 0.4f);
        }

        private void YPipe(Transform asm, string id)
        {
            string sid = id + ".y-pipe";
            Transform y = ctx.SubAssembly(asm, sid, T("Y-Junction and Decoupler", "Y-birlashma va dekupler",
                    "Fabricated Y-junction that merges both banks, and the flexible decoupler that isolates the engine's movement from the body-mounted exhaust.",
                    "Ikki qatorni birlashtiruvchi Y-birlashma va dvigatel tebranishini kuzovga mahkamlangan chiqarish qismidan ajratuvchi egiluvchan dekupler."),
                "Stainless steel", new Vector3(1f, -0.4f, 0.2f), 0.7f);
            Vector3 c = new Vector3(0.493f, 0.19f, 1.345f);
            Mesh body = ctx.Mesh("y-junction", k =>
            {
                k.RoundedBox(Vector3.zero, new Vector3(0.082f, 0.056f, 0.070f), 0.024f, 4);
                k.Push(new Vector3(-0.041f, 0f, 0f), InternalsContext.AlongY(Vector3.left));
                k.Lathe(new[] { new Vector2(0f, -0.004f), new Vector2(MergedR + 0.004f, -0.004f), new Vector2(MergedR, 0.018f), new Vector2(0f, 0.018f) }, 20, 35f);
                k.Pop();
            });
            Part(y, sid + ".junction", T("Y-junction", "Y-birlashma", "Two pressed half shells welded into a collector that merges both bank pipes into one.",
                    "Ikki qator quvurlarini bittaga birlashtiruvchi, payvandlangan ikki shtampovka yarim qobiqdan iborat kollektor."),
                "Stainless steel 1.4512", body, m.MachinedSteel, c, Quaternion.identity, false, Vector3.right, 0.4f);

            // Decoupler: braided bellows between two collars, along -x into the toe board.
            Vector3 a = c + new Vector3(-0.059f, 0f, 0f), b = new Vector3(0.392f, 0.19f, 1.343f);
            Vector3 dir = (b - a).normalized;
            float len = Vector3.Distance(a, b);
            Transform flex = ctx.SubAssembly(y, sid + ".decoupler", T("Flexible Decoupler", "Egiluvchan dekupler",
                    "Stainless bellows inside an interlock liner and a wire braid: absorbs the engine's rock without loading the pipes.",
                    "Ichki qulfli gilza va sim to‘r ichidagi zanglamas gofra: dvigatel tebranishini quvurlarga yuk bermasdan yutadi."),
                "Stainless steel", Vector3.down, 0.5f);
            Mesh bellows = ctx.Mesh("decoupler-bellows", k => k.Bellows(MergedR + 0.002f, MergedR + 0.002f, MergedR + 0.002f, MergedR + 0.008f, len - 0.02f, 9, 20, 0.006f));
            Part(flex, sid + ".decoupler.bellows", T("Bellows", "Gofra", "Multi-ply stainless bellows that flexes in every direction.", "Har tomonga egiluvchan ko‘p qatlamli zanglamas gofra."),
                "Stainless steel 1.4541", bellows, m.MachinedSteel, a + dir * 0.01f, InternalsContext.AlongY(dir), false, Vector3.down, 0.4f);
            Mesh braid = ctx.Mesh("decoupler-braid", k => k.Sleeve(MergedR + 0.0115f, MergedR + 0.0095f, 0f, len - 0.03f, 20));
            Part(flex, sid + ".decoupler.braid", T("Wire braid", "Sim to‘r", "Outer braid that limits stretch and protects the bellows.", "Cho‘zilishni cheklovchi va gofrani himoyalovchi tashqi to‘r."),
                "Stainless steel wire", braid, m.BraidedHose, a + dir * 0.015f, InternalsContext.AlongY(dir), false, Vector3.up, 0.5f);
            var collar = new[] { new Vector2(MergedR, 0f), new Vector2(MergedR + 0.014f, 0f), new Vector2(MergedR + 0.014f, 0.012f), new Vector2(MergedR, 0.012f), new Vector2(MergedR, 0f) };
            Lathe(flex, sid + ".decoupler.collar-inlet", T("Inlet collar", "Kirish halqasi", "Crimped collar that closes the braid on the inlet tube.", "To‘rni kirish naychasiga siqib yopuvchi halqa."),
                "Stainless steel", "decoupler-collar", collar, 20, a, dir, m.ZincSteel, false, -dir, 0.3f);
            Lathe(flex, sid + ".decoupler.collar-outlet", T("Outlet collar", "Chiqish halqasi", "Crimped collar on the outlet tube.", "Chiqish naychasidagi siqma halqa."),
                "Stainless steel", "decoupler-collar", collar, 20, b - dir * 0.012f, dir, m.ZincSteel, false, dir, 0.3f);

            // Bulkhead pipe from the decoupler through the toe board into the footwell channel and the filter.
            Transform bulk = ctx.SubAssembly(asm, id + ".front-pipe", T("Bulkhead Pipe", "Bo‘lim devori quvuri",
                    "Short pipe that takes the gas through a sealed, heat-shielded opening in the toe board into the floor channel.",
                    "Gazni oyoq qo‘yish devoridagi zichlangan, issiqlikdan himoyalangan teshik orqali pol kanaliga o‘tkazuvchi qisqa quvur."),
                "Stainless steel", Vector3.up, 0.6f);
            var path = Bent(0.045f, b, new Vector3(0.300f, 0.19f, 1.355f), new Vector3(0.300f, 0.19f, 1.315f));
            Pipe(bulk, id + ".front-pipe.pipe", T("Bulkhead pipe", "Devor quvuri", "Mandrel-bent pipe between the decoupler and the particulate filter.",
                    "Dekupler va zarrachalar filtri orasidagi egilgan quvur."),
                "Stainless steel 1.4512, 1.5 mm", path, MergedR, 16, m.MachinedSteel, false, Vector3.up, 0.4f);
            Vector3 grommetAt = new Vector3(0.378f, 0.19f, 1.345f);
            Mesh grommet = ctx.Mesh("exhaust-bulkhead-grommet", k =>
            {
                k.Lathe(new[] { new Vector2(MergedR + 0.004f, -0.012f), new Vector2(MergedR + 0.024f, -0.012f), new Vector2(MergedR + 0.024f, -0.008f), new Vector2(MergedR + 0.012f, -0.004f),
                    new Vector2(MergedR + 0.012f, 0.004f), new Vector2(MergedR + 0.024f, 0.008f), new Vector2(MergedR + 0.024f, 0.012f), new Vector2(MergedR + 0.004f, 0.012f), new Vector2(MergedR + 0.004f, -0.012f) }, 24, 35f);
            });
            Part(bulk, id + ".front-pipe.grommet", T("Bulkhead seal", "Devor zichlagichi", "Ceramic-fibre seal in a stainless retainer where the pipe passes the toe board.",
                    "Quvur oyoq qo‘yish devoridan o‘tadigan joydagi zanglamas ushlagichli keramik tolali zichlagich."),
                "Ceramic fibre, stainless steel", grommet, m.HeatShield, grommetAt, InternalsContext.AlongY(Vector3.left), false, Vector3.up, 0.3f);
        }

        private void ParticulateFilter(Transform asm, string id)
        {
            string sid = id + ".gpf";
            Transform g = ctx.SubAssembly(asm, sid, T("Gasoline Particulate Filter", "Benzin zarrachalar filtri",
                    "Wall-flow ceramic filter that traps soot particles; it regenerates by burning them off in hot, lean phases.",
                    "Qurum zarrachalarini ushlovchi devor orqali oqimli keramik filtr; issiq va kambag‘al rejimlarda ularni yoqib tozalanadi."),
                "Stainless shell, cordierite filter", Vector3.up, 0.8f);
            Vector3 start = new Vector3(0.300f, 0.19f, 1.315f);
            Vector3 dir = Vector3.back;
            float rCan = 0.054f, cone = 0.04f, shell = 0.16f;
            Canister(g, sid, start, dir, MergedR, rCan, cone, shell, "gpf", T("Filter shell", "Filtr korpusi",
                    "Stainless can around the cordierite wall-flow monolith: alternate channels are plugged so the gas must pass the porous walls.",
                    "Kordierit monolit atrofidagi zanglamas korpus: kanallar navbat bilan berkitilgan, gaz g‘ovak devorlardan o‘tishga majbur."), true);
            // Differential-pressure sensor on a bracket above the can, with its two sampling tubes.
            Vector3 dp = start + dir * (cone + shell * 0.5f) + Vector3.up * (rCan + 0.04f) + Vector3.left * 0.01f;
            Mesh dpBody = ctx.Mesh("gpf-dp-sensor", k =>
            {
                k.RoundedBox(Vector3.zero, new Vector3(0.036f, 0.022f, 0.030f), 0.004f, 2);
                foreach (float s in new[] { -1f, 1f })
                {
                    k.Push(new Vector3(0f, -0.011f, s * 0.008f), Quaternion.identity);
                    k.Cylinder(0.0028f, -0.012f, 0f, 8);
                    k.Pop();
                }
            });
            Part(g, sid + ".pressure-sensor", T("Differential-pressure sensor", "Bosimlar farqi datchigi",
                    "Measures the pressure drop across the filter to judge its soot load and trigger regeneration.",
                    "Filtrdagi bosim pasayishini o‘lchab, qurum to‘planishini aniqlaydi va tozalashni boshlaydi."),
                "Glass-filled PBT", dpBody, m.BlackPlastic, dp, Quaternion.identity, false, Vector3.up, 0.6f);
            foreach (bool inlet in new[] { true, false })
            {
                Vector3 tap = start + dir * (inlet ? cone * 0.5f : cone * 1.5f + shell) + Vector3.up * (inlet ? rCan * 0.55f : rCan * 0.55f);
                Vector3 port = dp + new Vector3(0f, -0.023f, inlet ? 0.008f : -0.008f);
                var tube = Smooth(6, port, port + Vector3.down * 0.012f, Vector3.Lerp(port, tap, 0.6f) + Vector3.up * 0.012f, tap + Vector3.up * 0.006f);
                Pipe(g, sid + ".pressure-tube-" + (inlet ? "inlet" : "outlet"), T(inlet ? "Inlet pressure tube" : "Outlet pressure tube", inlet ? "Kirish bosim naychasi" : "Chiqish bosim naychasi",
                        "Steel and silicone sampling tube to the pressure sensor.", "Bosim datchigiga boruvchi po‘lat va silikon naycha."),
                    "Stainless tube, silicone hose", tube, 0.003f, 8, m.ZincSteel, true, Vector3.up, 0.4f);
            }
            Vector3 tAt = start + dir * (cone * 0.6f) + Vector3.left * (rCan * 0.6f) + Vector3.up * 0.012f;
            Lathe(g, sid + ".temperature-sensor", T("Exhaust temperature sensor", "Gaz harorati datchigi", "Thermistor probe at the filter inlet that guards regeneration temperatures.",
                    "Filtr kirishidagi termistorli zond: tozalash haroratini nazorat qiladi."),
                "Stainless sheath", "egt-sensor", new[] { new Vector2(0f, -0.01f), new Vector2(0.003f, -0.01f), new Vector2(0.003f, 0.01f), new Vector2(0.0075f, 0.01f), new Vector2(0.0075f, 0.02f), new Vector2(0.004f, 0.024f), new Vector2(0.004f, 0.04f), new Vector2(0f, 0.04f) },
                10, tAt, (Vector3.left + Vector3.up * 0.4f).normalized, m.MachinedSteel, true, Vector3.left, 0.4f);
            var clamp = new[] { new Vector2(MergedR + 0.0015f, 0f), new Vector2(MergedR + 0.005f, 0f), new Vector2(MergedR + 0.005f, 0.014f), new Vector2(MergedR + 0.0015f, 0.014f), new Vector2(MergedR + 0.0015f, 0f) };
            Lathe(g, sid + ".clamp-inlet", T("Band clamp, inlet", "Kirish lenta xomuti", "Lap-joint band clamp sealing the slip joint at the filter inlet.", "Filtr kirishidagi surma birikmani zichlovchi lenta xomut."),
                "Stainless steel", "band-clamp-57", clamp, 20, start - dir * 0.002f, dir, m.ZincSteel, true, Vector3.up, 0.3f);
        }

        // Centre-pipe corridor on the right floor of the tub (car space; measured with the lab's 5 mm height field of the
        // seat pans, the floor mats and the floor: 2 mm clearance at the seat seams).
        private static readonly Vector3[] CentreFront =
        {
            new Vector3(0.300f, 0.190f, 1.065f), new Vector3(0.300f, 0.168f, 1.000f), new Vector3(0.300f, 0.160f, 0.800f), new Vector3(0.300f, 0.158f, 0.500f),
            new Vector3(0.306f, 0.159f, 0.420f)
        };
        private static readonly Vector3[] CentreMid =
        {
            new Vector3(0.306f, 0.159f, 0.420f), new Vector3(0.314f, 0.160f, 0.380f), new Vector3(0.307f, 0.153f, 0.360f), new Vector3(0.296f, 0.137f, 0.347f),
            new Vector3(0.270f, 0.120f, 0.330f), new Vector3(0.245f, 0.117f, 0.310f), new Vector3(0.232f, 0.1162f, 0.285f), new Vector3(0.226f, 0.1158f, 0.220f),
            new Vector3(0.223f, 0.1155f, 0.150f), new Vector3(0.221f, 0.117f, 0.100f), new Vector3(0.217f, 0.130f, 0.055f), new Vector3(0.212f, 0.152f, 0.000f),
            new Vector3(0.210f, 0.155f, -0.150f), new Vector3(0.212f, 0.157f, -0.300f), new Vector3(0.225f, 0.160f, -0.360f), new Vector3(0.255f, 0.155f, -0.405f),
            new Vector3(0.300f, 0.160f, -0.460f)
        };
        private static readonly Vector3[] CentreRear =
        {
            new Vector3(0.300f, 0.160f, -0.460f), new Vector3(0.305f, 0.165f, -0.700f), new Vector3(0.305f, 0.170f, -1.000f), new Vector3(0.305f, 0.180f, -1.150f),
            new Vector3(0.305f, 0.200f, -1.300f), new Vector3(0.305f, 0.205f, -1.450f), new Vector3(0.305f, 0.205f, SilencerZ0 + 0.004f)
        };

        private void CentrePipe(Transform asm, string id)
        {
            string sid = id + ".centre-pipe";
            Transform c = ctx.SubAssembly(asm, sid, T("Centre Pipe", "O‘rta quvur",
                    "Three-piece 38 mm pipe that threads between the seat pans and the floor mats from the filter to the rear silencer.",
                    "Filtrdan orqa so‘ndirgichgacha o‘rindiq tublari va pol gilamlari orasidan o‘tuvchi uch bo‘lakli 38 mm quvur."),
                "Stainless steel 1.4512", new Vector3(0.4f, 1f, 0f), 0.8f);
            // The filter's outlet spigot is 57 mm: a swaged reducer starts the front section.
            var front = Smooth(6, CentreFront);
            Pipe(c, sid + ".front-section", T("Front section", "Old bo‘lak", "Swaged from 57 to 38 mm at the filter outlet, then straight along the right footwell.",
                    "Filtr chiqishida 57 dan 38 mm gacha toraytirilgan, so‘ng o‘ng oyoq joyi bo‘ylab to‘g‘ri yo‘nalgan."),
                "Stainless steel 1.4512, 1.2 mm", front, CentreR, 14, m.MachinedSteel, false, Vector3.up, 0.5f,
                t => Mathf.Lerp(MergedR / CentreR, 1f, Mathf.Clamp01(t * 12f)));
            Pipe(c, sid + ".mid-section", T("Mid section", "O‘rta bo‘lak",
                    "S-bent section that dips to the floor under the seat seams and rises over the floor mats.",
                    "O‘rindiq choklari ostida polga tushib, gilamlar ustidan ko‘tariluvchi S-shakl bo‘lak."),
                "Stainless steel 1.4512, 1.2 mm", Smooth(8, CentreMid), CentreR, 14, m.MachinedSteel, false, Vector3.up, 0.6f);
            Pipe(c, sid + ".rear-section", T("Rear section", "Orqa bo‘lak", "Section that climbs over the rear floor step, passes the rear wall through a seal and enters the silencer.",
                    "Orqa pol pog‘onasidan ko‘tarilib, zichlagich orqali orqa devordan o‘tib so‘ndirgichga kiruvchi bo‘lak."),
                "Stainless steel 1.4512, 1.2 mm", Smooth(6, CentreRear), CentreR, 14, m.MachinedSteel, false, Vector3.up, 0.5f);
            var clamp = new[] { new Vector2(CentreR + 0.0012f, 0f), new Vector2(CentreR + 0.0045f, 0f), new Vector2(CentreR + 0.0045f, 0.012f), new Vector2(CentreR + 0.0012f, 0.012f), new Vector2(CentreR + 0.0012f, 0f) };
            Vector3 j1 = CentreFront[CentreFront.Length - 1], j2 = CentreRear[0];
            Lathe(c, sid + ".clamp-front", T("Band clamp", "Lenta xomut", "Lap-joint clamp on the slip joint between the front and mid sections.", "Old va o‘rta bo‘laklar orasidagi surma birikma xomuti."),
                "Stainless steel", "band-clamp-38", clamp, 18, j1 + Vector3.forward * 0.006f, Vector3.back, m.ZincSteel, true, Vector3.up, 0.3f);
            Lathe(c, sid + ".clamp-rear", T("Band clamp", "Lenta xomut", "Lap-joint clamp on the slip joint between the mid and rear sections.", "O‘rta va orqa bo‘laklar orasidagi surma birikma xomuti."),
                "Stainless steel", "band-clamp-38", clamp, 18, j2 + new Vector3(-0.004f, 0f, 0.006f), (CentreRear[1] - CentreRear[0]).normalized, m.ZincSteel, true, Vector3.up, 0.3f);
            // Seal where the rear section passes the rear floor step into the space under the deck.
            Vector3 wall = new Vector3(0.305f, 0.204f, -1.472f);
            Mesh seal = ctx.Mesh("exhaust-rear-grommet", k => k.Lathe(new[]
            {
                new Vector2(CentreR + 0.002f, -0.01f), new Vector2(CentreR + 0.018f, -0.01f), new Vector2(CentreR + 0.018f, -0.006f), new Vector2(CentreR + 0.008f, -0.003f),
                new Vector2(CentreR + 0.008f, 0.003f), new Vector2(CentreR + 0.018f, 0.006f), new Vector2(CentreR + 0.018f, 0.01f), new Vector2(CentreR + 0.002f, 0.01f), new Vector2(CentreR + 0.002f, -0.01f)
            }, 20, 35f));
            Part(c, sid + ".rear-seal", T("Rear wall seal", "Orqa devor zichlagichi", "Heat-resistant seal where the pipe passes the rear floor step.", "Quvur orqa pol pog‘onasidan o‘tadigan joydagi issiqbardosh zichlagich."),
                "Silicone, stainless retainer", seal, m.HeatShield, wall, InternalsContext.AlongY(Vector3.back), false, Vector3.up, 0.3f);
            // Rubber-isolated support on the raised rear floor.
            Transform sup = ctx.SubAssembly(c, sid + ".support", T("Pipe Support", "Quvur tayanchi",
                    "Rubber-isolated saddle bolted to the raised rear floor: carries the pipe without passing vibration to the tub.",
                    "Ko‘tarilgan orqa polga boltlangan rezina yostiqli tayanch: quvurni tebranishni kuzovga o‘tkazmay ushlab turadi."),
                "Steel and EPDM", Vector3.right, 0.5f);
            float fy = 0.129f;
            Vector3 sAt = new Vector3(0.305f, fy, -1.25f);
            float py = 0.1935f;   // pipe centre at z -1.25 (smoothed path)
            Mesh saddle = ctx.Mesh("centre-pipe-saddle", k =>
            {
                k.Box(new Vector3(0f, 0.002f, 0f), new Vector3(0.09f, 0.004f, 0.03f));
                k.Box(new Vector3(0f, (py - CentreR - fy) * 0.5f, 0f), new Vector3(0.012f, py - CentreR - fy - 0.006f, 0.026f));
            });
            Part(sup, sid + ".support.bracket", T("Support bracket", "Tayanch kronshteyni", "Pressed steel foot and post bolted to the floor.", "Polga boltlangan shtampovka po‘lat asos va ustun."),
                "Pressed steel, e-coat", saddle, m.BlackSteel, sAt, Quaternion.identity, false, Vector3.right, 0.4f);
            Mesh iso = ctx.Mesh("centre-pipe-isolator", k => k.Lathe(new[]
            {
                new Vector2(CentreR, -0.012f), new Vector2(CentreR + 0.008f, -0.012f), new Vector2(CentreR + 0.008f, 0.012f), new Vector2(CentreR, 0.012f), new Vector2(CentreR, -0.012f)
            }, 18, 40f));
            Part(sup, sid + ".support.isolator", T("Rubber isolator", "Rezina yostiq", "EPDM sleeve between the pipe and the saddle.", "Quvur va tayanch orasidagi EPDM gilza."),
                "EPDM rubber", iso, m.Rubber, new Vector3(0.305f, py, -1.25f), InternalsContext.AlongY(Vector3.back), false, Vector3.up, 0.3f);
            Mesh bolt = ctx.Mesh("bolt-M6x16", k => Fasteners.HexBolt(k, 0.006f, 0.016f));
            foreach (float s in new[] { -1f, 1f })
                Part(sup, sid + ".support.bolt-" + (s < 0 ? "left" : "right"), T("Bolt M6", "Bolt M6", "Fixes the support to a weld nut in the floor.", "Tayanchni poldagi payvand gaykaga mahkamlaydi."),
                    "Steel 8.8, zinc flake", bolt, m.ZincSteel, sAt + new Vector3(s * 0.034f, 0.004f, 0f), Quaternion.identity, true, Vector3.up, 0.3f);
        }

        private void Silencer(Transform asm, string id)
        {
            string sid = id + ".silencer";
            Transform s = ctx.SubAssembly(asm, sid, T("Rear Silencer", "Orqa so‘ndirgich",
                    "Three-chamber reflective silencer under the rear deck, with two tailpipes that exit under the diffuser.",
                    "Orqa supa ostidagi uch kamerali qaytaruvchi so‘ndirgich; ikki chiqish quvuri diffuzor ostidan chiqadi."),
                "Stainless steel 1.4512", new Vector3(0f, -0.5f, -1f), 1f);
            float cx = 0.29f, cy = 0.19f, w = 0.30f, h = 0.15f, rr = 0.05f;
            float z0 = SilencerZ0, z1 = SilencerZ1;
            var section = Profiles.RoundRect(w, h, rr, 4);
            Mesh shell = ctx.Mesh("silencer-shell", k =>
            {
                k.Sweep(section, new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, -(z0 - z1)) }, Vector3.up, false, false, 30f);
                k.Sweep(Profiles.RoundRect(w - 0.003f, h - 0.003f, rr - 0.0015f, 4), new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, -(z0 - z1)) }, Vector3.up, false, false, 30f, true);
                foreach (float f in new[] { 0.33f, 0.66f })
                    k.Sweep(Profiles.RoundRect(w + 0.004f, h + 0.004f, rr + 0.002f, 4), new[] { new Vector3(0f, 0f, -(z0 - z1) * f - 0.003f), new Vector3(0f, 0f, -(z0 - z1) * f + 0.003f) }, Vector3.up, true, true, 30f);
            });
            Vector3 front = new Vector3(cx, cy, z0);
            Part(s, sid + ".shell", T("Silencer shell", "So‘ndirgich korpusi", "Double-skin wrapped oval shell with two stiffening beads.", "Ikki qatlamli o‘ralgan oval korpus, ikki mustahkamlovchi bo‘rtma bilan."),
                "Stainless steel 1.4512, 0.8 + 0.6 mm", shell, m.MachinedSteel, front, Quaternion.identity, false, Vector3.up, 0.7f);
            Mesh cap = ctx.Mesh("silencer-cap", k => k.Extrude(Profiles.RoundRect(w + 0.006f, h + 0.006f, rr + 0.003f, 4), 0.006f));
            Part(s, sid + ".front-cap", T("Front end cap", "Old qopqoq", "Lock-seamed end cap with the inlet pipe's collar.", "Kirish quvuri yoqasi bilan qulflangan chokli yon qopqoq."),
                "Stainless steel 1.4512, 1.0 mm", cap, m.MachinedSteel, front + Vector3.back * 0.003f, Quaternion.identity, false, Vector3.forward, 0.6f);
            Part(s, sid + ".rear-cap", T("Rear end cap", "Orqa qopqoq", "Lock-seamed end cap carrying the two outlet pipes.", "Ikki chiqish quvurini ko‘taruvchi qulflangan chokli yon qopqoq."),
                "Stainless steel 1.4512, 1.0 mm", cap, m.MachinedSteel, new Vector3(cx, cy, z1 + 0.003f), Quaternion.identity, false, Vector3.back, 0.6f);

            Transform inner = ctx.SubAssembly(s, sid + ".internals", T("Silencer Internals", "So‘ndirgich ichki qismi",
                    "Two baffles split the can into three chambers; the perforated inlet tube and the two outlet tubes reflect and cancel pressure waves.",
                    "Ikki to‘siq korpusni uch kameraga bo‘ladi; teshikli kirish quvuri va ikki chiqish naychasi bosim to‘lqinlarini qaytarib so‘ndiradi."),
                "Stainless steel", Vector3.down, 0.8f);
            Mesh baffle = ctx.Mesh("silencer-baffle", k => k.Extrude(Profiles.RoundRect(w - 0.006f, h - 0.006f, rr - 0.003f, 4), 0.002f));
            float zb1 = z0 - 0.095f, zb2 = z1 + 0.085f;
            Part(inner, sid + ".internals.baffle-front", T("Front baffle", "Old to‘siq", "Perforated plate between the inlet and the middle chamber.", "Kirish va o‘rta kamera orasidagi teshikli plastina."),
                "Stainless steel 1.4512", baffle, m.ZincSteel, new Vector3(cx, cy, zb1), Quaternion.identity, false, Vector3.forward, 0.6f);
            Part(inner, sid + ".internals.baffle-rear", T("Rear baffle", "Orqa to‘siq", "Plate that closes the resonance chamber at the back.", "Orqadagi rezonans kamerani yopuvchi plastina."),
                "Stainless steel 1.4512", baffle, m.ZincSteel, new Vector3(cx, cy, zb2), Quaternion.identity, false, Vector3.back, 0.6f);
            float inX = CentreRear[CentreRear.Length - 1].x, inY = CentreRear[CentreRear.Length - 1].y;
            Mesh perf = ctx.Mesh("silencer-inlet-tube", k =>
            {
                var p = new List<Vector2> { new Vector2(0f, 0f), new Vector2(CentreR, 0f) };
                float len = zb1 - zb2 + 0.02f;
                for (int i = 0; i < 14; i++)
                {
                    float y0 = 0.02f + i * (len - 0.04f) / 14f;
                    p.Add(new Vector2(CentreR, y0)); p.Add(new Vector2(CentreR - 0.0015f, y0 + 0.002f)); p.Add(new Vector2(CentreR - 0.0015f, y0 + 0.006f)); p.Add(new Vector2(CentreR, y0 + 0.008f));
                }
                p.Add(new Vector2(CentreR, len)); p.Add(new Vector2(0f, len));
                k.Lathe(p, 16, 60f);
            });
            Part(inner, sid + ".internals.inlet-tube", T("Perforated inlet tube", "Teshikli kirish quvuri", "Louvred tube that lets the gas expand into the chambers.", "Gazning kameralarga kengayishiga imkon beruvchi teshikli quvur."),
                "Stainless steel 1.4512", perf, m.ZincSteel, new Vector3(inX, inY, z0 - 0.005f), InternalsContext.AlongY(Vector3.back), false, Vector3.right, 0.6f);
            float[] outX = { 0.205f, 0.380f };
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "left" : "right", sideUz = i == 0 ? "Chap" : "O‘ng";
                var tube = new List<Vector3> { new Vector3(outX[i], 0.155f, zb1 - 0.02f), new Vector3(outX[i], 0.155f, z1 + 0.004f) };
                Pipe(inner, sid + ".internals.outlet-tube-" + side, T(sideUz == "Chap" ? "Left outlet tube" : "Right outlet tube", sideUz + " chiqish naychasi",
                        "Tube from the middle chamber through the rear baffle to the tailpipe.", "O‘rta kameradan orqa to‘siq orqali chiqish quvurigacha boruvchi naycha."),
                    "Stainless steel 1.4512", tube, TailR - 0.002f, 14, m.ZincSteel, false, Vector3.right * (i == 0 ? -1f : 1f), 0.5f);
                var tail = Bent(0.05f, new Vector3(outX[i], 0.155f, z1 - 0.001f), new Vector3(outX[i], 0.152f, z1 - 0.04f), new Vector3(outX[i], 0.142f, -1.925f));
                Pipe(s, sid + ".tailpipe-" + side, T(i == 0 ? "Left tailpipe" : "Right tailpipe", sideUz + " chiqish quvuri", "Short tailpipe angled slightly down under the diffuser.",
                        "Diffuzor ostida biroz pastga qiya qisqa chiqish quvuri."),
                    "Stainless steel 1.4301", tail, TailR, 14, m.MachinedSteel, false, Vector3.back, 0.6f);
                Mesh tip = ctx.Mesh("tailpipe-tip", k => k.Lathe(new[]
                {
                    new Vector2(TailR + 0.001f, 0f), new Vector2(0.029f, 0f), new Vector2(0.031f, 0.004f), new Vector2(0.031f, 0.062f), new Vector2(0.028f, 0.066f),
                    new Vector2(0.026f, 0.066f), new Vector2(0.026f, 0.010f), new Vector2(TailR + 0.001f, 0.006f), new Vector2(TailR + 0.001f, 0f)
                }, 24, 35f));
                Part(s, sid + ".tip-" + side, T(i == 0 ? "Left tailpipe tip" : "Right tailpipe tip", sideUz + " chiqish uchligi", "Double-wall polished tip clamped on the tailpipe.",
                        "Chiqish quvuriga qisilgan ikki devorli sayqallangan uchlik."),
                    "Polished stainless steel 1.4301", tip, m.Chrome, new Vector3(outX[i], 0.142f, -1.925f), InternalsContext.AlongY(Vector3.back), false, Vector3.back, 0.6f);
                Hanger(s, sid + ".hanger-" + side, i == 0, new Vector3(i == 0 ? 0.20f : 0.38f, cy + h * 0.5f, (z0 + z1) * 0.5f));
            }
            Mesh shieldPlate = ctx.Mesh("silencer-heat-shield", k =>
            {
                var outline = Profiles.RoundRect(w + 0.03f, -(z1 - z0) + 0.02f, 0.03f, 3);
                k.Push(Vector3.zero, Quaternion.Euler(90f, 0f, 0f));
                k.Extrude(outline, 0.0015f);
                k.Pop();
            });
            Part(s, sid + ".heat-shield", T("Deck heat shield", "Supa issiqlik ekrani", "Embossed aluminium sheet under the rear deck that reflects the silencer's heat.",
                    "So‘ndirgich issiqligini qaytaruvchi, orqa supa ostidagi bo‘rttirilgan alyuminiy list."),
                "Embossed aluminium 0.5 mm", shieldPlate, m.HeatShield, new Vector3(cx, 0.296f, (z0 + z1) * 0.5f), Quaternion.identity, false, Vector3.up, 0.5f);
            var clamp = new[] { new Vector2(CentreR + 0.0012f, 0f), new Vector2(CentreR + 0.0045f, 0f), new Vector2(CentreR + 0.0045f, 0.012f), new Vector2(CentreR + 0.0012f, 0.012f), new Vector2(CentreR + 0.0012f, 0f) };
            Lathe(s, sid + ".clamp-inlet", T("Inlet band clamp", "Kirish lenta xomuti", "Clamp on the slip joint of the centre pipe and the silencer inlet.", "O‘rta quvur va so‘ndirgich kirishi birikmasidagi xomut."),
                "Stainless steel", "band-clamp-38", clamp, 18, new Vector3(inX, inY, z0 + 0.026f), Vector3.back, m.ZincSteel, true, Vector3.up, 0.3f);
        }

        private void Hanger(Transform parent, string id, bool left, Vector3 top)
        {
            Transform h = ctx.SubAssembly(parent, id, T(left ? "Left Hanger" : "Right Hanger", left ? "Chap osma" : "O‘ng osma",
                    "Rod welded to the silencer hooked into a rubber isolator that hangs from a bracket on the rear deck.",
                    "So‘ndirgichga payvandlangan sterjen orqa supadagi kronshteynga osilgan rezina yostiqqa ilinadi."),
                "Steel and EPDM", Vector3.up, 0.5f);
            Vector3 rodTop = top + Vector3.up * 0.018f;
            var rod = Bent(0.006f, top + Vector3.down * 0.004f, top + Vector3.up * 0.012f, rodTop + Vector3.forward * 0.02f);
            Pipe(h, id + ".rod", T("Hanger rod", "Osma sterjeni", "12 mm rod welded to the silencer shell.", "So‘ndirgich korpusiga payvandlangan 12 mm sterjen."),
                "Stainless steel 1.4301", rod, 0.006f, 10, m.MachinedSteel, false, Vector3.down, 0.3f);
            Mesh iso = ctx.Mesh("hanger-isolator", k =>
            {
                k.Push(Vector3.zero, Quaternion.Euler(90f, 0f, 0f));
                k.Lathe(new[] { new Vector2(0.007f, -0.008f), new Vector2(0.017f, -0.008f), new Vector2(0.017f, 0.008f), new Vector2(0.007f, 0.008f), new Vector2(0.007f, -0.008f) }, 16, 40f);
                k.Pop();
            });
            Part(h, id + ".isolator", T("Rubber isolator", "Rezina yostiq", "EPDM ring that filters exhaust vibration from the body.", "Chiqarish tebranishini kuzovdan ajratuvchi EPDM halqa."),
                "EPDM rubber", iso, m.Rubber, rodTop + Vector3.forward * 0.02f + Vector3.up * 0.002f, Quaternion.identity, false, Vector3.up, 0.4f);
            Mesh br = ctx.Mesh("hanger-bracket", k =>
            {
                k.Box(new Vector3(0f, 0f, 0f), new Vector3(0.05f, 0.003f, 0.03f));
                k.Box(new Vector3(0f, -0.0105f, 0f), new Vector3(0.004f, 0.018f, 0.012f));
            });
            Vector3 deck = new Vector3(top.x, 0.3025f, rodTop.z + 0.02f);
            Part(h, id + ".bracket", T("Deck bracket", "Supa kronshteyni", "Pressed bracket bolted under the rear deck.", "Orqa supa ostiga boltlangan shtampovka kronshteyn."),
                "Pressed steel, e-coat", br, m.BlackSteel, deck, Quaternion.identity, false, Vector3.up, 0.4f);
            Mesh bolt = ctx.Mesh("bolt-M6x12", k => Fasteners.HexBolt(k, 0.006f, 0.012f));
            foreach (float sx in new[] { -1f, 1f })
                Part(h, id + ".bolt-" + (sx < 0 ? "1" : "2"), T("Bolt M6", "Bolt M6", "Holds the bracket to a weld nut in the deck.", "Kronshteynni supadagi payvand gaykaga mahkamlaydi."),
                    "Steel 8.8, zinc flake", bolt, m.ZincSteel, deck + new Vector3(sx * 0.018f, -0.0015f, 0f), Quaternion.LookRotation(Vector3.forward, Vector3.down), true, Vector3.down, 0.3f);
        }

        private void ExhaustContacts()
        {
            // The V-band flanges are welded onto the manifold collectors.
            ctx.AllowContact("exhaust.system.front-bank.joint", "V6/Exhaust");
            ctx.AllowContact("exhaust.system.rear-bank.joint", "V6/Exhaust");
            ctx.AllowContact("exhaust.system.front-bank.elbow", "V6/Exhaust");
            ctx.AllowContact("exhaust.system.rear-bank.elbow", "V6/Exhaust");
            ctx.AllowContact("exhaust.system.front-bank.lambda-upstream", "V6/Exhaust");
            // The CAD collectors' own ends sit in the hood liner (front) and in the lower dash shell (rear), so the joints
            // that bolt to them share those shells.
            ctx.AllowContact("exhaust.system.front-bank.joint", "BodyHood");
            ctx.AllowContact("exhaust.system.front-bank.elbow", "BodyHood");
            ctx.AllowContact("exhaust.system.rear-bank.joint", "InteriorDash");
            // Through the toe board (the lower dash shell meets the floor here): decoupler end, pipe and seal.
            ctx.AllowContact("exhaust.system.y-pipe.decoupler", "InteriorDash");
            ctx.AllowContact("exhaust.system.front-pipe", "InteriorDash");
            // Through the rear floor step into the space under the deck.
            ctx.AllowContact("exhaust.system.centre-pipe.rear-section", "InteriorFloor");
            ctx.AllowContact("exhaust.system.centre-pipe.rear-section", "CarConcept");
            ctx.AllowContact("exhaust.system.centre-pipe.rear-seal", "InteriorFloor");
            ctx.AllowContact("exhaust.system.centre-pipe.rear-seal", "CarConcept");
            // Brackets bolted to the floor and under the deck.
            ctx.AllowContact("exhaust.system.centre-pipe.support", "InteriorFloor");
            ctx.AllowContact("exhaust.system.centre-pipe.support", "CarConcept");
            ctx.AllowContact("exhaust.system.silencer.hanger", "InteriorFloor");
            ctx.AllowContact("exhaust.system.silencer.hanger", "CarConcept");
            ctx.AllowContact("exhaust.system.silencer.heat-shield", "InteriorFloor");
            ctx.AllowContact("exhaust.system.silencer.heat-shield", "CarConcept");
        }

        // ================================================================================================ fuel

        private void FuelTank(Transform system)
        {
            const string id = "fuel.tank";
            Bounds tb = TankBounds;
            Transform asm = ctx.Assembly(system, id, T("Fuel Tank", "Yoqilg‘i baki",
                    "Blow-moulded tank in the rear-left floor bay with the pump and sender module, the filler neck and the evaporative-emission system.",
                    "Orqa chap pol bo‘shlig‘idagi puflab quyilgan bak: nasos va sath datchigi moduli, quyish bo‘yni va bug‘ chiqindilarini ushlash tizimi bilan."),
                "HDPE and steel", Vector3.up, 1f);
            Mesh shellMesh = ctx.Mesh("fuel-tank-shell", k =>
            {
                k.RoundedBox(Vector3.zero, tb.size, 0.03f, 4);
                // Raised boss for the sender module and a moulded rib pattern on the top.
                k.Push(new Vector3(-0.015f, tb.extents.y, -0.045f), Quaternion.identity);
                k.Lathe(new[] { new Vector2(0f, -0.004f), new Vector2(0.07f, -0.004f), new Vector2(0.066f, 0.004f), new Vector2(0f, 0.004f) }, 32, 40f);
                k.Pop();
                foreach (float z in new[] { -0.17f, 0.12f })
                    k.RoundedBox(new Vector3(0f, tb.extents.y, z), new Vector3(tb.size.x - 0.05f, 0.006f, 0.014f), 0.003f, 1);
            });
            Part(asm, id + ".shell", T("Tank shell", "Bak korpusi",
                    "Six-layer HDPE shell with an EVOH barrier against permeation; about 15 litres in this low bay.",
                    "Bug‘ o‘tishiga qarshi EVOH to‘siqli olti qatlamli HDPE korpus; bu past bo‘shliqda taxminan 15 litr."),
                "HDPE / EVOH coextrusion", shellMesh, m.GreyPlastic, tb.center, Quaternion.identity, false, Vector3.down, 0.6f);

            // Two straps over the tank, their feet bolted to the floor through the mat.
            Mesh strap = ctx.Mesh("fuel-tank-strap", k =>
            {
                float hx = tb.extents.x + 0.003f, top = tb.max.y - 0.1295f + 0.0015f;
                var path = Bent(0.02f, new Vector3(-hx - 0.03f, 0f, 0f), new Vector3(-hx, 0f, 0f), new Vector3(-hx, top, 0f), new Vector3(hx, top, 0f), new Vector3(hx, 0f, 0f), new Vector3(hx + 0.016f, 0f, 0f));
                k.Sweep(Profiles.Rect(0.0025f, 0.026f), path, Vector3.forward, true, true, 30f);
            });
            Mesh strapBolt = ctx.Mesh("bolt-M8x20", k => Fasteners.HexBolt(k, 0.008f, 0.02f));
            float baseY = 0.1295f;
            foreach (float z in new[] { tb.center.z + 0.14f, tb.center.z - 0.14f })
            {
                string side = z > tb.center.z ? "front" : "rear";
                Part(asm, id + ".strap-" + side, T(side == "front" ? "Front strap" : "Rear strap", side == "front" ? "Old tasma" : "Orqa tasma",
                        "Coated steel strap with a felt liner that clamps the tank to the floor.", "Bakni polga qisuvchi kigiz qoplamali bo‘yalgan po‘lat tasma."),
                    "Steel, e-coat, felt liner", strap, m.BlackSteel, new Vector3(tb.center.x, baseY, z), Quaternion.identity, false, Vector3.up, 0.5f);
                foreach (float sx in new[] { -1f, 1f })
                    Part(asm, id + ".strap-" + side + "-bolt-" + (sx < 0 ? "left" : "right"), T("Bolt M8", "Bolt M8", "Holds the strap foot to a weld nut in the floor.", "Tasma oyog‘ini poldagi payvand gaykaga mahkamlaydi."),
                        "Steel 8.8, zinc flake", strapBolt, m.ZincSteel, new Vector3(tb.center.x + sx * (tb.extents.x + (sx < 0f ? 0.022f : 0.013f)), baseY + 0.0025f, z), Quaternion.identity, true, Vector3.up, 0.3f);
            }

            // Pump and sender module through the top boss.
            Vector3 boss = new Vector3(tb.center.x - 0.015f, tb.max.y + 0.004f, tb.center.z - 0.045f);
            Transform sender = ctx.SubAssembly(asm, id + ".sender", T("Fuel Pump and Sender Unit", "Yoqilg‘i nasosi va sath datchigi",
                    "In-tank module: electric pump in a swirl pot, filter, pressure regulator and the float-arm level sender, closed by a flange.",
                    "Bak ichidagi modul: aylanma idishdagi elektr nasos, filtr, bosim regulyatori va qalqovichli sath datchigi; flanes bilan yopiladi."),
                "Acetal, steel and nitrile", Vector3.up, 1f);
            Lathe(sender, id + ".sender.lock-ring", T("Lock ring", "Qulflovchi halqa", "Bayonet ring that clamps the flange and its seal to the tank boss.",
                    "Flanes va zichlagichini bak bo‘rtig‘iga qisuvchi bayonet halqa."),
                "Acetal POM", "sender-lock-ring", new[] { new Vector2(0.052f, 0f), new Vector2(0.066f, 0f), new Vector2(0.066f, 0.010f), new Vector2(0.06f, 0.012f), new Vector2(0.052f, 0.012f), new Vector2(0.052f, 0f) },
                32, boss, Vector3.up, m.BlackPlastic, false, Vector3.up, 0.3f);
            Mesh flange = ctx.Mesh("sender-flange", k =>
            {
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.054f, 0f), new Vector2(0.054f, 0.006f), new Vector2(0.03f, 0.012f), new Vector2(0f, 0.012f) }, 32, 40f);
                foreach (float x in new[] { -0.018f, 0.018f })
                {
                    k.Push(new Vector3(x, 0.012f, 0f), Quaternion.identity);
                    k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0055f, 0f), new Vector2(0.0055f, 0.014f), new Vector2(0.0042f, 0.016f), new Vector2(0.0042f, 0.026f), new Vector2(0f, 0.026f) }, 12, 40f);
                    k.Pop();
                }
            });
            Part(sender, id + ".sender.flange", T("Module flange", "Modul flanesi", "Moulded cover with the supply and vent nipples and the electrical feed-through.",
                    "Ta’minot va shamollatish shtutserlari hamda elektr o‘tkazgichi bor quyma qopqoq."),
                "Acetal POM", flange, m.GreyPlastic, boss, Quaternion.identity, false, Vector3.up, 0.5f);
            Mesh pump = ctx.Mesh("sender-pump", k =>
            {
                float hgt = boss.y - 0.004f - tb.min.y - 0.012f;
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.046f, 0f), new Vector2(0.046f, hgt * 0.55f), new Vector2(0.040f, hgt * 0.58f), new Vector2(0.040f, hgt), new Vector2(0f, hgt) }, 28, 40f);
            });
            Part(sender, id + ".sender.pump", T("Pump module", "Nasos moduli", "Brushless turbine pump (about 4 bar, 120 l/h) in a swirl pot with its strainer and regulator.",
                    "Aylanma idishdagi cho‘tkasiz turbinali nasos (taxminan 4 bar, 120 l/soat), to‘r filtri va regulyatori bilan."),
                "Acetal, steel, copper", pump, m.BlackPlastic, new Vector3(boss.x, tb.min.y + 0.008f, boss.z), Quaternion.identity, false, Vector3.up, 1.2f);
            var arm = Bent(0.01f, new Vector3(boss.x + 0.04f, tb.min.y + 0.07f, boss.z), new Vector3(boss.x + 0.06f, tb.min.y + 0.06f, boss.z + 0.04f), new Vector3(boss.x + 0.07f, tb.min.y + 0.035f, boss.z + 0.12f));
            Pipe(sender, id + ".sender.float-arm", T("Float arm", "Qalqovich richagi", "Wire arm that turns the float's height into the sender's resistance.", "Qalqovich balandligini datchik qarshiligiga aylantiruvchi sim richag."),
                "Stainless wire", arm, 0.0015f, 6, m.MachinedSteel, true, Vector3.up, 1f);
            Mesh fl = ctx.Mesh("sender-float", k => k.RoundedBox(Vector3.zero, new Vector3(0.03f, 0.016f, 0.022f), 0.005f, 2));
            Part(sender, id + ".sender.float", T("Float", "Qalqovich", "Closed-cell nitrile float.", "Yopiq g‘ovakli nitril qalqovich."),
                "Nitrile foam", fl, m.Rubber, arm[arm.Count - 1] + Vector3.forward * 0.012f, Quaternion.identity, false, Vector3.up, 1f);
            Mesh plug = ctx.Mesh("sender-connector", k => k.RoundedBox(Vector3.zero, new Vector3(0.02f, 0.016f, 0.026f), 0.003f, 1));
            Part(sender, id + ".sender.connector", T("Module connector", "Modul ulagichi", "Pump power, sender signal and ground.", "Nasos quvvati, datchik signali va massa."),
                "Glass-filled PBT", plug, m.BlackPlastic, boss + new Vector3(0f, 0.022f, -0.03f), Quaternion.identity, true, Vector3.up, 0.6f);

            Vector3 rov = new Vector3(tb.max.x - 0.035f, tb.max.y + 0.004f, tb.min.z + 0.06f);
            Lathe(asm, id + ".vent-valve", T("Rollover vent valve", "Ag‘darilish shamollatish klapani", "Lets fuel vapour out to the canister but closes if the car rolls over.",
                    "Yoqilg‘i bug‘ini adsorberga chiqaradi, avtomobil ag‘darilsa yopiladi."),
                "Acetal POM", "rollover-valve", new[] { new Vector2(0f, 0f), new Vector2(0.016f, 0f), new Vector2(0.016f, 0.006f), new Vector2(0.012f, 0.008f), new Vector2(0.012f, 0.026f), new Vector2(0.0045f, 0.03f), new Vector2(0.0045f, 0.04f), new Vector2(0f, 0.04f) },
                20, rov, Vector3.up, m.BlackPlastic, false, Vector3.up, 0.6f);

            Filler(asm, id, tb);
            Evap(asm, id, tb, rov + Vector3.up * 0.04f);
        }

        private void Filler(Transform asm, string id, Bounds tb)
        {
            string sid = id + ".filler";
            Transform f = ctx.SubAssembly(asm, sid, T("Filler Neck", "Quyish bo‘yni",
                    "Steel filler pipe from the tank to the pocket behind the fuel flap in the left side trim, with its vapour recirculation line.",
                    "Bakdan chap yon qoplamadagi lyuk ortidagi uyaga boruvchi po‘lat quyish quvuri, bug‘ qaytarish naychasi bilan."),
                "Steel, zinc-nickel", Vector3.left, 0.8f);
            // Integration (combined car): 5.5 cm inboard of the first build, clear of the B-pillar reinforcement's inner
            // flange (x -0.817) behind the side trim.
            Vector3 housing = new Vector3(-0.735f, 0.43f, -0.82f);
            Vector3 axis = new Vector3(-1f, 0.35f, 0f).normalized;
            var neck = Smooth(8, new Vector3(tb.min.x + 0.03f, tb.max.y - 0.01f, -0.72f), new Vector3(tb.min.x - 0.01f, tb.max.y + 0.04f, -0.735f),
                new Vector3(-0.64f, 0.38f, -0.775f), new Vector3(-0.69f, 0.415f, -0.81f), housing + axis * 0.004f);
            Pipe(f, sid + ".neck", T("Filler pipe", "Quyish quvuri", "Zinc-nickel coated steel pipe that runs from the filler pocket down into the tank.",
                    "Quyish uyasidan bakka tushuvchi rux-nikel qoplamali po‘lat quvur."),
                "Steel, zinc-nickel", neck, 0.016f, 14, m.ZincSteel, false, Vector3.left, 0.5f);
            var vent = Smooth(8, new Vector3(tb.min.x + 0.05f, tb.max.y + 0.004f, -0.67f), new Vector3(tb.min.x + 0.01f, tb.max.y + 0.05f, -0.69f),
                new Vector3(-0.64f, 0.405f, -0.745f), new Vector3(-0.69f, 0.44f, -0.785f), housing + new Vector3(0.012f, 0.03f, 0.03f));
            Pipe(f, sid + ".vent-line", T("Recirculation line", "Bug‘ qaytarish naychasi", "Returns displaced vapour to the top of the filler pipe during refuelling.",
                    "Yoqilg‘i quyilayotganda siqib chiqarilgan bug‘ni quyish quvurining yuqorisiga qaytaradi."),
                "Nylon PA12", vent, 0.004f, 8, m.BlackPlastic, true, Vector3.left, 0.5f);
            Lathe(f, sid + ".housing", T("Filler pocket", "Quyish uyasi", "Moulded pocket recessed in the side trim behind the fuel flap, with a drain.",
                    "Yoqilg‘i lyuki ortidagi yon qoplamaga botirilgan quyma uya, suv chiqarish teshigi bilan."),
                "Polypropylene", "filler-housing", new[] { new Vector2(0.018f, -0.004f), new Vector2(0.046f, -0.004f), new Vector2(0.046f, 0.05f), new Vector2(0.052f, 0.052f), new Vector2(0.052f, 0.056f),
                    new Vector2(0.042f, 0.056f), new Vector2(0.042f, 0.004f), new Vector2(0.018f, 0.004f), new Vector2(0.018f, -0.004f) }, 24, housing, axis, m.BlackPlastic, false, Vector3.left, 0.4f);
            Lathe(f, sid + ".cap", T("Filler cap", "Quyish qopqog‘i", "Ratcheting cap with a pressure and vacuum relief valve, tethered to the pocket.",
                    "Bosim va vakuum klapanli, uyaga bog‘langan shaqildoqli qopqoq."),
                "Polyamide", "filler-cap", new[] { new Vector2(0f, 0.004f), new Vector2(0.03f, 0.004f), new Vector2(0.034f, 0.012f), new Vector2(0.034f, 0.024f), new Vector2(0.03f, 0.028f), new Vector2(0f, 0.028f) },
                24, housing + axis * 0.004f, axis, m.BlackPlastic, false, Vector3.left, 0.7f);
        }

        private void Evap(Transform asm, string id, Bounds tb, Vector3 valveTop)
        {
            string sid = id + ".evap";
            Transform e = ctx.SubAssembly(asm, sid, T("Evaporative Emission Control", "Bug‘ chiqindilarini ushlash tizimi",
                    "Charcoal canister that stores the tank's fuel vapour until the engine purges it into the intake.",
                    "Bak bug‘ini dvigatel kiritish tizimiga so‘rib olguncha saqlovchi ko‘mirli adsorber."),
                "Nylon and activated carbon", Vector3.back, 0.7f);
            Vector3 c = new Vector3(-0.42f, 0.176f, -0.955f);
            Mesh can = ctx.Mesh("evap-canister", k =>
            {
                k.Lathe(new[] { new Vector2(0f, -0.1f), new Vector2(0.034f, -0.1f), new Vector2(0.038f, -0.094f), new Vector2(0.038f, 0.094f), new Vector2(0.034f, 0.1f), new Vector2(0f, 0.1f) }, 24, 35f);
                foreach (float a in new[] { 0.065f, 0.085f })
                {
                    k.Push(new Vector3(-0.034f, a, 0f), InternalsContext.AlongY(Vector3.left));
                    k.Cylinder(0.0045f, 0f, 0.022f, 10);
                    k.Pop();
                }
            });
            Part(e, sid + ".canister", T("Charcoal canister", "Ko‘mirli adsorber", "Nylon housing packed with activated carbon pellets; three ports: tank, purge and fresh air.",
                    "Faollashtirilgan ko‘mir donachalari bilan to‘ldirilgan neylon korpus; uch shtutser: bak, so‘rish va toza havo."),
                "PA66 GF30, activated carbon", can, m.BlackPlastic, c, InternalsContext.AlongY(Vector3.right), false, Vector3.back, 0.5f);
            Vector3 port = c + new Vector3(0.065f, 0.056f, 0f);
            var vent = Smooth(8, valveTop, valveTop + Vector3.up * 0.03f, Vector3.Lerp(valveTop, port, 0.5f) + Vector3.up * 0.06f, port + Vector3.up * 0.03f + Vector3.forward * 0.012f, port + Vector3.forward * 0.012f);
            Pipe(e, sid + ".vent-line", T("Tank vent line", "Bak shamollatish naychasi", "Carries the vapour from the rollover valve to the canister.", "Bug‘ni ag‘darilish klapanidan adsorberga olib boradi."),
                "Nylon PA12", vent, 0.004f, 8, m.BlackPlastic, true, Vector3.up, 0.4f);
            Lathe(e, sid + ".vent-valve", T("Canister vent valve", "Adsorber shamollatish klapani", "Solenoid that closes the fresh-air port for the leak test.", "Germetiklik sinovi uchun toza havo teshigini yopuvchi solenoid."),
                "PA66, copper coil", "evap-vent-valve", new[] { new Vector2(0f, 0f), new Vector2(0.016f, 0f), new Vector2(0.016f, 0.03f), new Vector2(0.006f, 0.034f), new Vector2(0.006f, 0.046f), new Vector2(0f, 0.046f) },
                16, c + Vector3.left * 0.1f, Vector3.left, m.BlackPlastic, true, Vector3.left, 0.4f);
        }

        // Fuel-line corridor along the left floor of the tub and up the left side of the bay (car space, measured).
        private static readonly Vector3[] LineCabin =
        {
            new Vector3(-0.300f, 0.150f, -0.385f), new Vector3(-0.300f, 0.1335f, -0.320f), new Vector3(-0.300f, 0.1315f, -0.300f), new Vector3(-0.300f, 0.1295f, -0.275f),
            new Vector3(-0.300f, 0.129f, -0.200f), new Vector3(-0.300f, 0.130f, 0.100f), new Vector3(-0.300f, 0.127f, 0.300f), new Vector3(-0.300f, 0.137f, 0.400f),
            new Vector3(-0.300f, 0.138f, 0.450f), new Vector3(-0.300f, 0.138f, 0.800f), new Vector3(-0.300f, 0.140f, 1.100f),
            new Vector3(-0.320f, 0.150f, 1.300f)
        };
        private static readonly Vector3 LineBulkhead = new Vector3(-0.380f, 0.160f, 1.360f);

        private void FuelLines(Transform system)
        {
            const string id = "fuel.lines";
            Transform asm = ctx.Assembly(system, id, T("Fuel Lines and Rail", "Yoqilg‘i quvurlari va rampasi",
                    "Returnless supply from the tank to two port-injection rails with six injectors, and the canister purge line.",
                    "Bakdan oltita forsunkali ikki rampaga qaytishsiz ta’minot va adsorberni so‘rish naychasi."),
                "Nylon, steel and aluminium", Vector3.up, 1f);
            Bounds tb = TankBounds;
            Vector3 boss = new Vector3(tb.center.x - 0.015f, tb.max.y + 0.004f, tb.center.z - 0.045f);
            Vector3 nipple = boss + new Vector3(0.018f, 0.038f, 0f);

            Transform sup = ctx.SubAssembly(asm, id + ".supply", T("Supply and Purge Lines", "Ta’minot va so‘rish quvurlari",
                    "Clipped pair of nylon lines along the left floor channel, through the toe board and over the engine to the rails and the purge valve.",
                    "Chap pol kanali bo‘ylab, oyoq qo‘yish devori orqali va dvigatel ustidan rampalarga hamda so‘rish klapaniga boruvchi qisilgan ikki neylon quvur."),
                "Nylon PA12", Vector3.left, 0.7f);
            Vector3 railIn = new Vector3(Mathf.Lerp(FlangeRear.min.x, FlangeRear.max.x, 0f) + 0.012f, RailRearY, RailRearZ);
            var cabin = new List<Vector3> { nipple + Vector3.up * 0.026f, nipple + Vector3.up * 0.045f, new Vector3(-0.36f, 0.335f, -0.52f), new Vector3(-0.32f, 0.30f, -0.41f), new Vector3(-0.303f, 0.20f, -0.398f) };
            cabin.AddRange(LineCabin);
            var bay = new List<Vector3>
            {
                LineCabin[LineCabin.Length - 1], LineBulkhead, new Vector3(-0.45f, 0.17f, 1.42f), new Vector3(-0.458f, 0.25f, 1.515f), new Vector3(-0.458f, 0.50f, 1.525f), // integration: 1.7 cm inboard, clear of the front fuse box and the brake pedal cross-shaft's outer bearing
               
                new Vector3(-0.44f, 0.66f, 1.56f), new Vector3(-0.30f, 0.672f, 1.61f), new Vector3(-0.15f, 0.668f, 1.69f), new Vector3(railIn.x - 0.03f, RailRearY + 0.02f, RailRearZ - 0.01f),
                railIn + Vector3.left * 0.01f
            };
            Vector3 off = new Vector3(0.011f, -0.0005f, 0f);
            Pipe(sup, id + ".supply.cabin-run", T("Supply line, floor run", "Ta’minot quvuri, pol qismi", "8 mm nylon line from the pump module along the left floor channel to the toe board.",
                    "Nasos modulidan chap pol kanali bo‘ylab oyoq qo‘yish devorigacha 8 mm neylon quvur."),
                "Nylon PA12, 8 mm", Smooth(6, cabin.ToArray()), 0.004f, 8, m.BlackPlastic, false, Vector3.left, 0.5f);
            Pipe(sup, id + ".supply.bay-run", T("Supply line, engine-bay run", "Ta’minot quvuri, motor bo‘linmasi qismi",
                    "Heat-sleeved section that climbs the left of the bay and crosses over the rear valve cover to the rail inlet.",
                    "Bo‘linmaning chap tomonidan ko‘tarilib, orqa klapan qopqog‘i ustidan rampa kirishiga o‘tuvchi issiqlik g‘ilofli qism."),
                "Nylon PA12 in a glass-fibre sleeve", Smooth(6, bay.ToArray()), 0.0048f, 8, m.BlackPlastic, false, Vector3.up, 0.5f);
            // Purge line beside the supply line, from the canister to the purge valve near the throttle end of the plenum.
            Vector3 canPort = new Vector3(-0.335f, 0.232f, -0.955f);
            var purgeCabin = new List<Vector3> { canPort + Vector3.up * 0.004f, canPort + Vector3.up * 0.03f, new Vector3(-0.30f, 0.24f, -0.93f), new Vector3(-0.286f, 0.16f, -0.86f),
                new Vector3(-0.285f, 0.146f, -0.70f), new Vector3(-0.287f, 0.146f, -0.50f) };
            foreach (Vector3 p in LineCabin) purgeCabin.Add(p + off);
            Vector3 purgeValve = new Vector3(-0.15f, 0.70f, 1.66f);
            var purgeBay = new List<Vector3>();
            for (int i = 0; i < bay.Count - 2; i++) purgeBay.Add(bay[i] + off + (i > 3 ? Vector3.up * 0.008f : Vector3.zero));
            purgeBay.Add(purgeValve + Vector3.left * 0.03f);
            Pipe(sup, id + ".supply.purge-cabin-run", T("Purge line, floor run", "So‘rish quvuri, pol qismi", "10 mm nylon line from the canister along the supply line.",
                    "Adsorberdan ta’minot quvuri yonidan boruvchi 10 mm neylon quvur."),
                "Nylon PA12, 10 mm", Smooth(6, purgeCabin.ToArray()), 0.005f, 8, m.GreyPlastic, false, Vector3.left, 0.5f);
            Pipe(sup, id + ".supply.purge-bay-run", T("Purge line, engine-bay run", "So‘rish quvuri, motor bo‘linmasi qismi", "Section from the toe board to the purge valve.",
                    "Oyoq qo‘yish devoridan so‘rish klapanigacha bo‘lgan qism."),
                "Nylon PA12, 10 mm", Smooth(6, purgeBay.ToArray()), 0.005f, 8, m.GreyPlastic, false, Vector3.up, 0.5f);
            Lathe(sup, id + ".supply.purge-valve", T("Canister purge valve", "Adsorberni so‘rish klapani", "Pulse-width-modulated solenoid that meters the stored vapour into the intake.",
                    "Saqlangan bug‘ni kiritish tizimiga dozalab beruvchi impulsli solenoid."),
                "PA66, copper coil", "purge-valve", new[] { new Vector2(0f, -0.02f), new Vector2(0.006f, -0.02f), new Vector2(0.006f, -0.012f), new Vector2(0.017f, -0.01f), new Vector2(0.017f, 0.022f),
                    new Vector2(0.006f, 0.026f), new Vector2(0.006f, 0.036f), new Vector2(0f, 0.036f) }, 16, purgeValve, Vector3.right, m.BlackPlastic, false, Vector3.up, 0.5f);
            Mesh qc = ctx.Mesh("fuel-quick-connector", k =>
            {
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0075f, 0f), new Vector2(0.0075f, 0.022f), new Vector2(0.006f, 0.026f), new Vector2(0f, 0.026f) }, 12, 40f);
                k.Box(new Vector3(0f, 0.008f, 0f), new Vector3(0.019f, 0.006f, 0.006f));
            });
            Part(sup, id + ".supply.connector-tank", T("Quick connector, tank", "Tez ulagich, bak", "SAE J2044 push-on connector on the module's supply nipple.", "Modul ta’minot shtutseridagi SAE J2044 tez ulagichi."),
                "PA12 with steel retainer", qc, m.GreyPlastic, nipple + Vector3.up * 0.004f, Quaternion.identity, true, Vector3.up, 0.3f);
            Part(sup, id + ".supply.connector-rail", T("Quick connector, rail", "Tez ulagich, rampa", "Push-on connector on the rear rail's inlet.", "Orqa rampa kirishidagi tez ulagich."),
                "PA12 with steel retainer", qc, m.GreyPlastic, railIn + Vector3.left * 0.026f, InternalsContext.AlongY(Vector3.right), true, Vector3.up, 0.3f);
            Mesh clip = ctx.Mesh("fuel-line-clip", k =>
            {
                k.RoundedBox(new Vector3(0.0055f, 0f, 0f), new Vector3(0.026f, 0.014f, 0.012f), 0.003f, 1);
                k.Cylinder(0.0025f, -0.042f, 0f, 8);
            });
            float[] clipZ = { 0.55f, 0.80f, 1.15f };
            float[] clipY = { 0.138f, 0.138f, 0.1415f };
            for (int i = 0; i < clipZ.Length; i++)
                Part(sup, id + ".supply.clip-" + (i + 1), T("Line clip " + (i + 1), "Quvur qisqichi " + (i + 1), "Twin clip that holds both lines on a stud through the mat into the floor.", "Ikkala quvurni gilam orqali poldagi shpilkada ushlab turuvchi qo‘sh qisqich."),
                    "Acetal POM", clip, m.BlackPlastic, new Vector3(-0.300f, clipY[i], clipZ[i]), Quaternion.identity, true, Vector3.up, 0.3f);
            Vector3 gAt = LineBulkhead + new Vector3(-0.004f, 0f, 0.004f);
            Mesh grom = ctx.Mesh("fuel-bulkhead-grommet", k => k.Lathe(new[]
            {
                new Vector2(0f, -0.008f), new Vector2(0.018f, -0.008f), new Vector2(0.018f, -0.004f), new Vector2(0.012f, -0.002f), new Vector2(0.012f, 0.002f), new Vector2(0.018f, 0.004f),
                new Vector2(0.018f, 0.008f), new Vector2(0f, 0.008f)
            }, 18, 40f));
            Part(sup, id + ".supply.grommet", T("Bulkhead grommet", "Devor vtulkasi", "Two-hole rubber grommet where the lines pass the toe board.", "Quvurlar oyoq qo‘yish devoridan o‘tadigan joydagi ikki teshikli rezina vtulka."),
                "EPDM rubber", grom, m.Rubber, gAt, InternalsContext.AlongY((LineBulkhead - LineCabin[LineCabin.Length - 1]).normalized), false, Vector3.up, 0.3f);

            Rail(asm, id + ".rail-rear", false);
            Rail(asm, id + ".rail-front", true);
            // Crossover tube between the rails' right ends, around the charge-air hose.
            Vector3 rEnd = new Vector3(Mathf.Lerp(FlangeRear.min.x, FlangeRear.max.x, 1f) - 0.01f, RailRearY, RailRearZ);
            Vector3 fEnd = new Vector3(Mathf.Lerp(FlangeFront.min.x, FlangeFront.max.x, 1f) - 0.012f, RailFrontY, RailFrontZ);
            var crossover = Smooth(8, rEnd, rEnd + new Vector3(0.035f, 0.012f, 0.012f), new Vector3(0.355f, 0.655f, 1.83f), new Vector3(0.36f, 0.655f, 1.92f),
                new Vector3(0.31f, 0.635f, 1.985f), fEnd + Vector3.right * 0.03f, fEnd);
            Pipe(asm, id + ".crossover", T("Rail crossover tube", "Rampalar o‘tish naychasi", "Stainless tube that feeds the front rail from the rear one, routed clear of the charge-air hose.",
                    "Old rampani orqa rampadan ta’minlovchi, siqilgan havo shlangidan uzoqda yotqizilgan zanglamas naycha."),
                "Stainless steel 1.4301, 8 mm", crossover, 0.004f, 8, m.ZincSteel, false, Vector3.right, 0.5f);
        }

        private void Rail(Transform asm, string id, bool front)
        {
            Bounds fl = front ? FlangeFront : FlangeRear;
            float y = front ? RailFrontY : RailRearY, z = front ? RailFrontZ : RailRearZ;
            float x0 = fl.min.x + 0.012f, x1 = fl.max.x - 0.01f;
            float[] fr = front ? RunnersFront : RunnersRear;
            Vector3 injDir = new Vector3(0f, -0.85f, front ? 0.45f : -0.45f).normalized;
            Transform r = ctx.SubAssembly(asm, id, T(front ? "Front-Bank Fuel Rail" : "Rear-Bank Fuel Rail", front ? "Old qator yoqilg‘i rampasi" : "Orqa qator yoqilg‘i rampasi",
                    "Extruded aluminium rail that feeds three port injectors at about 4 bar; bolted to the intake runners.",
                    "Uchta forsunkani taxminan 4 bar bosimda ta’minlovchi presslangan alyuminiy rampa; kiritish kanallariga boltlangan."),
                "Aluminium 6063", new Vector3(0f, 1f, front ? 0.6f : -0.6f), 0.7f);
            Mesh body = ctx.Mesh("fuel-rail-" + (front ? "front" : "rear"), k =>
            {
                k.Sweep(Profiles.RoundRect(0.018f, 0.018f, 0.004f, 2), new[] { new Vector3(x0, 0f, 0f), new Vector3(x1, 0f, 0f) }, Vector3.up, true, true, 30f);
                foreach (float f in fr)
                {
                    float xi = Mathf.Lerp(fl.min.x, fl.max.x, f);
                    k.Push(new Vector3(xi, 0f, 0f), InternalsContext.AlongY(injDir));
                    k.Lathe(new[] { new Vector2(0.0085f, 0.004f), new Vector2(0.0115f, 0.004f), new Vector2(0.0115f, 0.026f), new Vector2(0.0095f, 0.028f), new Vector2(0.0085f, 0.028f), new Vector2(0.0085f, 0.004f) }, 16, 35f);
                    k.Pop();
                }
                foreach (float f in new[] { 0.33f, 0.68f })
                {
                    float xb = Mathf.Lerp(x0, x1, f);
                    k.Box(new Vector3(xb, 0.005f, front ? -0.012f : 0.012f), new Vector3(0.016f, 0.006f, 0.008f));
                }
            });
            Part(r, id + ".rail", T("Fuel rail", "Yoqilg‘i rampasi", "Square aluminium gallery with three brazed injector cups and two mounting lugs.",
                    "Uchta kavsharlangan forsunka uyasi va ikki mahkamlash qulog‘i bor kvadrat alyuminiy kanal."),
                "Aluminium 6063-T6", body, m.MachinedAluminium, new Vector3(0f, y, z), Quaternion.identity, false, Vector3.up, 0.6f);
            Mesh bolt = ctx.Mesh("bolt-M6x30", k => Fasteners.HexBolt(k, 0.006f, 0.03f));
            foreach (float f in new[] { 0.33f, 0.68f })
            {
                float xb = Mathf.Lerp(x0, x1, f);
                int n = f < 0.5f ? 1 : 2;
                Vector3 lug = new Vector3(xb, y + 0.008f, z + (front ? -0.012f : 0.012f));
                Part(r, id + ".bolt-" + n, T("Rail bolt M6", "Rampa bolti M6", "Bolts the rail lug through a spacer to the runner boss.", "Rampa qulog‘ini oraliq vtulka orqali kanal bo‘rtig‘iga mahkamlaydi."),
                    "Steel 8.8, zinc flake", bolt, m.ZincSteel, lug, Quaternion.identity, true, Vector3.up, 0.3f);
            }
            for (int i = 0; i < 3; i++)
            {
                float xi = Mathf.Lerp(fl.min.x, fl.max.x, fr[i]);
                int cyl = (front ? 4 : 1) + i;
                Injector(r, id + ".injector-" + cyl, cyl, new Vector3(xi, y, z) + injDir * 0.004f, injDir);
            }
            if (!front)
            {
                // Accessories on the rear rail only: the charge-air hose passes 2 cm above the front rail.
                Vector3 sAt = new Vector3(Mathf.Lerp(x0, x1, 0.40f), y + 0.009f, z);
                Lathe(r, id + ".pressure-sensor", T("Rail pressure sensor", "Rampa bosim datchigi", "Piezo-resistive sensor on the rail: the pump is speed-controlled to hold the target pressure.",
                        "Rampadagi pyezorezistiv datchik: nasos tezligi maqsadli bosimni ushlash uchun boshqariladi."),
                    "Stainless body, PBT housing", "rail-pressure-sensor", new[] { new Vector2(0f, 0f), new Vector2(0.006f, 0f), new Vector2(0.006f, 0.008f), new Vector2(0.0115f, 0.009f), new Vector2(0.0115f, 0.018f),
                        new Vector2(0.0095f, 0.02f), new Vector2(0.0095f, 0.036f), new Vector2(0f, 0.036f) }, 12, sAt, Vector3.up, m.MachinedSteel, false, Vector3.up, 0.5f);
                Mesh plug = ctx.Mesh("rail-sensor-connector", k => k.RoundedBox(Vector3.zero, new Vector3(0.016f, 0.012f, 0.022f), 0.003f, 1));
                Part(r, id + ".sensor-connector", T("Sensor connector", "Datchik ulagichi", "Three-pin sealed connector.", "Uch kontaktli zichlangan ulagich."),
                    "Glass-filled PBT", plug, m.BlackPlastic, sAt + Vector3.up * 0.042f, Quaternion.identity, true, Vector3.up, 0.4f);
                Lathe(r, id + ".test-port", T("Schrader test port", "Shreder sinov klapani", "Valve for checking the rail pressure in service.", "Servisda rampa bosimini tekshirish klapani."),
                    "Brass, steel core", "schrader-port", new[] { new Vector2(0f, 0f), new Vector2(0.0045f, 0f), new Vector2(0.0045f, 0.012f), new Vector2(0.006f, 0.013f), new Vector2(0.006f, 0.02f), new Vector2(0f, 0.02f) },
                    10, new Vector3(Mathf.Lerp(x0, x1, 0.86f), y + 0.009f, z), Vector3.up, m.Brass, true, Vector3.up, 0.3f);
            }
        }

        private void Injector(Transform rail, string id, int cyl, Vector3 top, Vector3 dir)
        {
            Transform inj = ctx.SubAssembly(rail, id, T("Injector, Cylinder " + cyl, cyl + "-silindr forsunkasi",
                    "Twelve-hole solenoid port injector that sprays fuel onto the back of the intake valves.",
                    "Yoqilg‘ini kiritish klapanlari orqasiga purkovchi o‘n ikki teshikli solenoid forsunka."),
                "Stainless steel and glass-filled nylon", -dir, 0.5f);
            Quaternion rot = InternalsContext.AlongY(dir);
            Mesh body = ctx.Mesh("injector-body", k => k.Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.0062f, 0f), new Vector2(0.0062f, 0.010f), new Vector2(0.0078f, 0.012f), new Vector2(0.0078f, 0.03f), new Vector2(0.0058f, 0.034f),
                new Vector2(0.0058f, 0.046f), new Vector2(0.0046f, 0.048f), new Vector2(0.0046f, 0.058f), new Vector2(0.0025f, 0.062f), new Vector2(0f, 0.062f)
            }, 16, 35f));
            Part(inj, id + ".body", T("Injector body", "Forsunka korpusi", "Solenoid, needle and twelve-hole spray plate in a nylon overmould.", "Neylon qoplamadagi solenoid, igna va o‘n ikki teshikli purkagich plastinasi."),
                "Stainless steel, PA66 GF30", body, m.GreyPlastic, top, rot, false, dir, 0.4f);
            Mesh plug = ctx.Mesh("injector-connector", k => k.RoundedBox(new Vector3(0f, 0f, 0.008f), new Vector3(0.011f, 0.012f, 0.016f), 0.002f, 1));
            Part(inj, id + ".connector", T("Injector connector", "Forsunka ulagichi", "Two-pin USCAR connector of the injector harness.", "Forsunka simlarining ikki kontaktli USCAR ulagichi."),
                "Glass-filled PBT", plug, m.BlackPlastic, top + dir * 0.022f + Vector3.right * 0.006f, Quaternion.LookRotation(Vector3.right, -dir), true, -dir, 0.4f);
            Mesh oring = ctx.Mesh("injector-o-ring", k => k.Lathe(new[]
            {
                new Vector2(0.0058f, -0.0015f), new Vector2(0.0078f, -0.0015f), new Vector2(0.0082f, 0f), new Vector2(0.0078f, 0.0015f), new Vector2(0.0058f, 0.0015f), new Vector2(0.0058f, -0.0015f)
            }, 14, 50f));
            Part(inj, id + ".o-ring-upper", T("Upper O-ring", "Yuqori zichlovchi halqa", "Fluorocarbon O-ring sealing the injector in the rail cup.", "Forsunkani rampa uyasida zichlovchi ftorkauchuk halqa."),
                "FKM", oring, m.Rubber, top + dir * 0.005f, rot, true, -dir, 0.3f);
            Part(inj, id + ".o-ring-lower", T("Lower O-ring", "Pastki zichlovchi halqa", "O-ring sealing the injector in the runner boss.", "Forsunkani kanal bo‘rtig‘ida zichlovchi halqa."),
                "FKM", oring, m.Rubber, top + dir * 0.052f, rot, true, dir, 0.3f);
            Mesh clip = ctx.Mesh("injector-clip", k => k.Lathe(new[]
            {
                new Vector2(0.0082f, -0.001f), new Vector2(0.0098f, -0.001f), new Vector2(0.0098f, 0.001f), new Vector2(0.0082f, 0.001f), new Vector2(0.0082f, -0.001f)
            }, 14, 50f, 30f, 300f));
            Part(inj, id + ".clip", T("Retaining clip", "Ushlab turuvchi qisqich", "Spring clip that locks the injector to the rail cup.", "Forsunkani rampa uyasiga qulflovchi prujinali qisqich."),
                "Spring steel", clip, m.ZincSteel, top + dir * 0.0005f, rot, true, -dir, 0.3f);
        }

        private void FuelContacts()
        {
            // Strap feet and their bolts go through the mat to weld nuts in the floor.
            ctx.AllowContact("fuel.tank.strap", "InteriorFloor");
            ctx.AllowContact("fuel.lines.supply.clip", "InteriorFloor");
            // The filler pocket is recessed into the side trim behind the fuel flap.
            ctx.AllowContact("fuel.tank.filler.housing", "InteriorMid");
            ctx.AllowContact("fuel.tank.filler.cap", "InteriorMid");
            // The lines pass the toe board through the grommet.
            ctx.AllowContact("fuel.lines.supply.grommet", "InteriorDash");
            ctx.AllowContact("fuel.lines.supply.bay-run", "InteriorDash");
            ctx.AllowContact("fuel.lines.supply.purge-bay-run", "InteriorDash");
            // Injectors sit in the runner bosses; the rails bolt to the runners.
            ctx.AllowContact("fuel.lines.rail-rear.injector", "V6/Intake");
            ctx.AllowContact("fuel.lines.rail-front.injector", "V6/Intake");
            ctx.AllowContact("fuel.lines.rail-rear.bolt", "V6/Intake");
            ctx.AllowContact("fuel.lines.rail-front.bolt", "V6/Intake");
            // The rails' injector cups seat on the runners' injector bosses (the bars clear the runners by about 6 mm).
            ctx.AllowContact("fuel.lines.rail-rear.rail", "V6/Intake");
            ctx.AllowContact("fuel.lines.rail-front.rail", "V6/Intake");
        }
    }
}
#endif
