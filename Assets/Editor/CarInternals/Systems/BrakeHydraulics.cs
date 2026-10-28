#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace CarInternals
{
    /// <summary>
    /// Brakes: hydraulics and parking brake around the glTF discs and calipers (not rebuilt). The central pendant
    /// pedal turns a cross-shaft behind the toe board; a lever on its left end drives an electromechanical booster
    /// and tandem master cylinder in the free pocket of the bay between the engine's left end and the left wheel
    /// well (straight ahead of the pedal sit the engine's rear exhaust manifold, the steering rack and the anti-roll
    /// bar). The ABS / ESC modulator hangs under the master cylinder; 4.75 mm copper-nickel lines run to braided
    /// hoses at each corner (front hose ends in the steered wheel frame) and electric parking-brake actuators sit on
    /// the rear calipers. Hardpoints come from the measured layout and a few probe rays (see Measure).
    /// </summary>
    [CarInternalsSubsystem("Brakes", 40)]
    public sealed class BrakeHydraulics : ICarInternalsSubsystem
    {
        private const float LineR = 0.002375f;   // 4.75 mm (3/16 in) rigid line
        private const float HoseR = 0.0052f;     // braided hose, 10.4 mm
        private const float Bend = 0.014f;       // centre-line bend radius of the rigid lines
        private const float NutLen = 0.0098f;    // tube nut M10x1, hex 11
        private static readonly Vector3 AbsSize = new Vector3(0.10f, 0.075f, 0.09f);

        private InternalsContext ctx;
        private CarPalette m;
        private CarLayout L;
        private Material lineMat;

        // Hardpoints in car space (Measure).
        private Vector3 pivot;          // brake-pedal pivot at the top of the glTF pedal arm
        private float bx, by, bz;       // booster axis (x, y) and its rear flange face (z)
        private float mcz;              // master-cylinder flange face (z)
        private Vector3 absC;           // modulator valve-block centre
        private float absFloor;         // bay floor under the modulator
        private float shaftWallX;       // where the cross-shaft passes the footwell side wall
        private float crossY, crossZ;   // front-right line across the footwell, behind the toe board
        private float fwall, rwall, cabinY;

        public void Build(InternalsContext context)
        {
            ctx = context;
            m = ctx.Mats;
            L = ctx.Layout;
            lineMat = m.Get("CI_Brake_CopperNickel", new Color(0.80f, 0.56f, 0.43f), 1f, 0.55f);
            Measure();
            Transform system = ctx.System("Brakes");
            PedalBox(system);
            Booster(system);
            Modulator(system);
            Lines(system);
            ParkingBrake(system);
            Contacts();
            ctx.Note("brakes: pedal pivot " + CarLayout.V(pivot) + ", booster axis x " + CarLayout.F(bx) + " y " + CarLayout.F(by) + " flange z " + CarLayout.F(bz) +
                     ", master cylinder z " + CarLayout.F(mcz) + ".." + CarLayout.F(mcz + 0.15f) + ", modulator " + CarLayout.V(absC) + " on floor y " + CarLayout.F(absFloor) +
                     ", cross-shaft wall x " + CarLayout.F(shaftWallX) + ", footwell crossing y " + CarLayout.F(crossY) + " z " + CarLayout.F(crossZ));
        }

        // ------------------------------------------------------------------------------------------------ layout

        private void Measure()
        {
            fwall = L.FrontWellWallX;
            rwall = L.RearWellWallX;
            cabinY = L.FloorTopY - 0.001f;
            if (!RendererBounds("InteriorPedalBrakeArm", out Bounds arm))
                arm = new Bounds(new Vector3(L.Pedals.center.x - 0.05f, L.Pedals.center.y, L.Pedals.center.z), new Vector3(0.045f, L.Pedals.size.y, L.Pedals.size.z));
            pivot = new Vector3(arm.center.x, arm.max.y - 0.010f, arm.max.z - 0.012f);
            bx = -(fwall - 0.095f);
            by = pivot.y + 0.070f;
            bz = pivot.z + 0.052f;
            mcz = bz + 0.086f;
            // Integration (combined car): 3 cm higher than first built, so the control unit and the bracket table clear
            // the alternator's slip-ring end and B+ stud underneath (the alternator sits on the belt plane below).
            absC = new Vector3(bx + 0.035f, by - 0.19f, bz + 0.165f);
            absFloor = ctx.Probe.Down(new Vector3(absC.x, absC.y - 0.06f, absC.z), 0.3f, out float fy) ? fy : L.FloorTopY + 0.033f;
            shaftWallX = ctx.Probe.Cast(new Vector3(pivot.x - 0.06f, pivot.y, pivot.z), Vector3.left, 0.6f, out float d, "InteriorDashSides") ? pivot.x - 0.06f - d : bx + 0.11f;
            crossY = by - 0.175f;
            crossZ = pivot.z - 0.013f;
        }

        private bool RendererBounds(string name, out Bounds b)
        {
            b = new Bounds();
            Transform t = FindDeep(L.Car, name);
            MeshFilter f = t != null ? t.GetComponent<MeshFilter>() : null;
            if (f == null || f.sharedMesh == null) return false;
            Vector3[] v;
            try { v = f.sharedMesh.vertices; } catch { return false; }
            if (v.Length == 0) return false;
            Matrix4x4 toCar = L.WorldToCar * t.localToWorldMatrix;
            b = new Bounds(toCar.MultiplyPoint3x4(v[0]), Vector3.zero);
            foreach (Vector3 p in v) b.Encapsulate(toCar.MultiplyPoint3x4(p));
            return true;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                Transform r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        private float BayFloor(float x, float z, float fallback)
        {
            return ctx.Probe.Down(new Vector3(x, 0.3f, z), 0.4f, out float y) ? y : fallback;
        }

        private static Quaternion Along(Vector3 dir) => InternalsContext.AlongY(dir);

        private static string Title(string words)
        {
            var parts = words.Split(' ');
            for (int i = 0; i < parts.Length; i++) if (parts[i].Length > 0) parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            return string.Join(" ", parts);
        }

        // ------------------------------------------------------------------------------------------------ pedal box

        private void PedalBox(Transform system)
        {
            string id = "brakes.pedal-box";
            Vector3 p = pivot;
            Transform box = ctx.Assembly(system, id, new Txt("Brake Pedal Box", "Tormoz pedali bloki",
                    "Pendant brake pedal on a cross-shaft that carries the driver's effort from the central pedal to the remote booster on the left.",
                    "Markaziy pedaldan chap tomondagi masofaviy kuchaytirgichga haydovchi kuchini uzatuvchi ko‘ndalang valdagi osma tormoz pedali."),
                "Steel", new Vector3(0f, 0.35f, -1f), 1f);

            Transform bracket = ctx.SubAssembly(box, id + ".bracket", new Txt("Pedal Bracket", "Pedal kronshteyni",
                    "Pressed-steel bracket hung from the steering-column support behind the toe board; carries the pivot bushes and the stop-lamp switch.",
                    "Oyoq tirgagi orqasida rul kolonkasi tayanchiga osilgan presslangan po‘lat kronshteyn; o‘q vtulkalari va stop-signal o‘chirgichini ko‘taradi."),
                "Pressed steel", Vector3.up, 0.6f);
            Mesh bracketMesh = ctx.Mesh("pedal-bracket", k =>
            {
                foreach (float sx in new[] { -0.026f, 0.026f })
                    k.PlanarPrism(new[]
                    {
                        new Vector3(sx, -0.019f, -0.012f), new Vector3(sx, -0.019f, 0.010f), new Vector3(sx, 0.002f, 0.013f), new Vector3(sx, 0.032f, 0.013f),
                        new Vector3(sx, 0.032f, -0.034f), new Vector3(sx, 0.006f, -0.034f)
                    }, 0.004f);
                k.RoundedBox(new Vector3(0f, 0.0335f, -0.0105f), new Vector3(0.08f, 0.003f, 0.047f), 0.001f, 1);
                k.PlanarPrism(new[] { new Vector3(0.026f, -0.012f, -0.033f), new Vector3(0.026f, 0.012f, -0.033f), new Vector3(0.026f, 0.012f, -0.056f), new Vector3(0.026f, -0.012f, -0.056f) }, 0.004f);
                k.Box(new Vector3(0.007f, 0f, -0.052f), new Vector3(0.042f, 0.024f, 0.003f));
            });
            ctx.Part(bracket, id + ".bracket.body", new Txt("Pedal bracket", "Pedal kronshteyni",
                    "Two side plates and a top web spot-welded into one stiff U-section.",
                    "Ikki yon plastina va ustki devor bitta bikr U-kesimga nuqtali payvandlangan."),
                "Pressed steel, e-coated", bracketMesh, new[] { m.BlackSteel }, p, Quaternion.identity);
            Mesh m8 = ctx.Mesh("bolt-M8x20-plain", k => Fasteners.HexBolt(k, 0.008f, 0.02f, false));
            foreach (float sx in new[] { -1f, 1f })
                ctx.Part(bracket, id + ".bracket.bolt-" + (sx < 0 ? "left" : "right"), new Txt("Bolt M8", "Bolt M8",
                        "Flange bolt that hangs the bracket from the steering-column support.", "Kronshteynni rul kolonkasi tayanchiga osib qo‘yuvchi flanetsli bolt."),
                    "Steel 8.8, zinc flake", m8, new[] { m.ZincSteel }, p + new Vector3(sx * 0.033f, 0.032f, -0.022f), Along(Vector3.down), true, Vector3.down, 0.4f);

            Transform shaft = ctx.SubAssembly(box, id + ".cross-shaft", new Txt("Cross-Shaft Linkage", "Ko‘ndalang val uzatmasi",
                    "Tube shaft turned by the pedal; the lever on its far end pushes the booster's input rod, so the pedal can sit on the centre line.",
                    "Pedal aylantiradigan quvursimon val; uning narigi uchidagi richag kuchaytirgichning kirish shtokini itaradi, shu sababli pedal markaziy chiziqda turadi."),
                "Steel", Vector3.left, 0.7f);
            float x0 = p.x + 0.030f, x1 = bx - 0.014f;
            Mesh tube = ctx.Mesh("cross-shaft", k => k.Sleeve(0.0095f, 0.0072f, 0f, x0 - x1, 16));
            ctx.Part(shaft, id + ".cross-shaft.tube", new Txt("Cross shaft", "Ko‘ndalang val",
                    "Seamless steel tube, 19 mm; twists the pedal effort across to the booster lever.",
                    "19 mm choksiz po‘lat quvur; pedal kuchini kuchaytirgich richagiga burab uzatadi."),
                "Seamless steel tube E355", tube, new[] { m.BlackSteel }, new Vector3(x0, p.y, p.z), Along(Vector3.left), false, Vector3.left, 0.5f);
            Mesh hub = ctx.Mesh("pedal-hub", k =>
            {
                k.Sleeve(0.016f, 0.0095f, -0.019f, 0.019f, 18);
                k.Box(new Vector3(0f, 0f, -0.0215f), new Vector3(0.012f, 0.009f, 0.010f));
            });
            Quaternion alongX = Along(Vector3.right);
            ctx.Part(shaft, id + ".cross-shaft.pedal-hub", new Txt("Pedal hub", "Pedal gupchagi",
                    "Hub welded to the pedal arm and keyed to the shaft; its rear lug works the stop-lamp switch.", "Pedal richagiga payvandlangan va valga shponka bilan biriktirilgan gupchak; orqa tirgagi stop-signal o‘chirgichini bosadi."),
                "Steel", hub, new[] { m.BlackSteel }, p, alongX);
            Mesh bush = ctx.Mesh("pedal-pivot-bush", k => k.Lathe(new[]
            {
                new Vector2(0.0096f, -0.004f), new Vector2(0.0135f, -0.004f), new Vector2(0.0135f, -0.0025f), new Vector2(0.0118f, -0.0025f),
                new Vector2(0.0118f, 0.004f), new Vector2(0.0096f, 0.004f), new Vector2(0.0096f, -0.004f)
            }, 16));
            foreach (float sx in new[] { -1f, 1f })
                ctx.Part(shaft, id + ".cross-shaft.bush-" + (sx < 0 ? "left" : "right"), new Txt("Pivot bush", "O‘q vtulkasi",
                        "Flanged acetal bush in the bracket plate; the shaft turns in it without play or grease.",
                        "Kronshteyn plastinasidagi flanetsli atsetal vtulka; val unda tirqishsiz va moysiz aylanadi."),
                    "Acetal (POM)", bush, new[] { m.GreyPlastic }, p + new Vector3(sx * 0.026f, 0f, 0f), Along(new Vector3(sx, 0f, 0f)), true, new Vector3(sx, 0f, 0f), 0.4f);
            Mesh seal = ctx.Mesh("cross-shaft-seal", k => k.Lathe(new[]
            {
                new Vector2(0.0096f, -0.008f), new Vector2(0.017f, -0.008f), new Vector2(0.022f, -0.004f), new Vector2(0.022f, 0.004f),
                new Vector2(0.017f, 0.008f), new Vector2(0.0096f, 0.008f), new Vector2(0.0096f, -0.008f)
            }, 18, 40f));
            ctx.Part(shaft, id + ".cross-shaft.seal", new Txt("Bulkhead seal", "Devor zichlagichi",
                    "Rubber grommet where the shaft leaves the footwell; keeps water and engine noise out of the cabin.",
                    "Val oyoq bo‘shlig‘idan chiqadigan joydagi rezina vtulka; salonga suv va motor shovqinini o‘tkazmaydi."),
                "EPDM rubber", seal, new[] { m.Rubber }, new Vector3(shaftWallX, p.y, p.z), alongX);
            Mesh bearing = ctx.Mesh("cross-shaft-bearing", k => k.Lathe(new[]
            {
                new Vector2(0.0096f, -0.0065f), new Vector2(0.0165f, -0.0065f), new Vector2(0.0165f, -0.0035f), new Vector2(0.0135f, -0.0035f),
                new Vector2(0.0135f, 0.0065f), new Vector2(0.0096f, 0.0065f), new Vector2(0.0096f, -0.0065f)
            }, 18));
            ctx.Part(shaft, id + ".cross-shaft.outer-bearing", new Txt("Outer bearing", "Tashqi podshipnik",
                    "Flanged plain bearing in the booster bracket that carries the shaft's far end.",
                    "Valning narigi uchini ko‘taruvchi, kuchaytirgich kronshteynidagi flanetsli sirpanish podshipnigi."),
                "Acetal (POM)", bearing, new[] { m.GreyPlastic }, new Vector3(bx + 0.030f, p.y, p.z), alongX);
            float h = by - p.y;
            Mesh lever = ctx.Mesh("booster-lever-" + Mathf.RoundToInt(h * 1000f), k =>
            {
                List<Vector2> slot = MeshKit.Slot(new Vector2(0f, 0f), new Vector2(h, 0f), 0.0115f, 8);
                var corners = new List<Vector3>();
                foreach (Vector2 s in slot) corners.Add(new Vector3(0f, s.x, s.y));
                k.PlanarPrism(corners, 0.008f);
                k.Push(Vector3.zero, Along(Vector3.right));
                k.Sleeve(0.017f, 0.0095f, -0.009f, 0.009f, 18);
                k.Pop();
                k.Push(new Vector3(0f, h, 0f), Along(Vector3.right));
                k.Sleeve(0.0105f, 0.0042f, -0.004f, 0.004f, 14);
                k.Pop();
            });
            ctx.Part(shaft, id + ".cross-shaft.lever", new Txt("Booster lever", "Kuchaytirgich richagi",
                    "Forged lever clamped on the shaft end; its clevis pin pushes the booster input rod forward.",
                    "Val uchiga qisilgan bolg‘alangan richag; uning vilka barmog‘i kuchaytirgichning kirish shtokini oldinga itaradi."),
                "Forged steel", lever, new[] { m.ForgedSteel }, new Vector3(bx, p.y, p.z), Quaternion.identity, false, Vector3.up, 0.5f);
            Mesh pin = ctx.Mesh("clevis-pin", k =>
            {
                k.Cylinder(0.0039f, -0.013f, 0.012f, 12);
                k.Lathe(new[] { new Vector2(0f, -0.016f), new Vector2(0.0065f, -0.016f), new Vector2(0.0065f, -0.013f), new Vector2(0f, -0.013f) }, 14);
            });
            ctx.Part(shaft, id + ".cross-shaft.clevis-pin", new Txt("Clevis pin", "Vilka barmog‘i",
                    "Headed pin that joins the lever to the input-rod clevis.", "Richagni kirish shtoki vilkasi bilan biriktiruvchi qalpoqli barmoq."),
                "Steel", pin, new[] { m.MachinedSteel }, new Vector3(bx, by, p.z), alongX, true, Vector3.right, 0.4f);
            Mesh split = ctx.Mesh("split-pin-small", k => k.Tube(MeshKit.Bends(new[] { new Vector3(0f, 0f, -0.009f), new Vector3(0f, 0f, 0.009f), new Vector3(0f, -0.005f, 0.009f), new Vector3(0f, -0.005f, 0.003f) }, 0.0025f), 0.001f, 6));
            ctx.Part(shaft, id + ".cross-shaft.split-pin", new Txt("Split pin", "Shplint", "Locks the clevis pin.", "Vilka barmog‘ini qulflaydi."),
                "Steel", split, new[] { m.ZincSteel }, new Vector3(bx + 0.0105f, by, p.z), Quaternion.identity, true);

            Mesh spring = ctx.Mesh("pedal-return-spring", k =>
            {
                List<Vector3> helix = MeshKit.HelixPath(0.0165f, 0.019f, 0.0032f, 3.2f, 0.5f, 20);
                var path = new List<Vector3> { helix[0] + new Vector3(0f, 0f, -0.026f) };
                path.AddRange(helix);
                Vector3 last = helix[helix.Count - 1];
                path.Add(last + new Vector3(0f, 0f, 0.002f));
                path.Add(new Vector3(last.x, last.y, 0.022f));
                k.Tube(path, 0.0016f, 8, true, true, Vector3.up);
            });
            ctx.Part(box, id + ".return-spring", new Txt("Return spring", "Qaytaruvchi prujina",
                    "Torsion spring around the shaft that brings the pedal back to its stop when released.",
                    "Pedal qo‘yib yuborilganda uni tayanchiga qaytaruvchi, val atrofidagi buraluvchi prujina."),
                "Spring steel, painted", spring, new[] { m.SpringBlue }, p + new Vector3(-0.030f, 0f, 0f), Along(Vector3.left), false, Vector3.left, 0.5f);

            Transform sw = ctx.SubAssembly(box, id + ".stop-lamp-switch", new Txt("Stop-Lamp Switch", "Stop-signal o‘chirgichi",
                    "Plunger switch on the bracket that lights the brake lamps and tells the control units the pedal is pressed.",
                    "Kronshteyndagi plunjerli o‘chirgich: tormoz chiroqlarini yoqadi va boshqaruv bloklariga pedal bosilganini bildiradi."),
                "Plastic and steel", Vector3.forward, 0.5f);
            Vector3 swAt = p + new Vector3(0f, 0f, -0.068f);
            Quaternion swRot = Along(Vector3.forward);
            Mesh swBody = ctx.Mesh("stop-lamp-switch", k =>
            {
                var prof = new List<Vector2> { new Vector2(0f, -0.004f), new Vector2(0.0072f, -0.004f) };
                for (int i = 0; i < 9; i++)
                {
                    float y0 = 0.001f + i * 0.0033f;
                    prof.Add(new Vector2(0.0072f, y0)); prof.Add(new Vector2(0.0064f, y0 + 0.0016f));
                }
                prof.Add(new Vector2(0.0072f, 0.031f)); prof.Add(new Vector2(0.004f, 0.033f)); prof.Add(new Vector2(0f, 0.033f));
                k.Lathe(prof, 14, 50f);
                k.RoundedBox(new Vector3(0f, -0.011f, 0f), new Vector3(0.017f, 0.014f, 0.014f), 0.002f, 1);
            });
            ctx.Part(sw, id + ".stop-lamp-switch.body", new Txt("Switch body", "O‘chirgich korpusi",
                    "Threaded switch barrel in the bracket tab behind the hub lug, with two contact sets for the lamps and the control units.",
                    "Kronshteyn qulog‘ida gupchak tirgagi orqasiga o‘rnatilgan rezbali korpus; chiroqlar va boshqaruv bloklari uchun ikki juft kontakt."),
                "Glass-filled nylon", swBody, new[] { m.BlackPlastic }, swAt, swRot);
            Mesh plunger = ctx.Mesh("stop-lamp-plunger", k => k.Lathe(new[] { new Vector2(0f, 0.032f), new Vector2(0.0028f, 0.032f), new Vector2(0.0028f, 0.0395f), new Vector2(0.0018f, 0.0415f), new Vector2(0f, 0.0415f) }, 10));
            ctx.Part(sw, id + ".stop-lamp-switch.plunger", new Txt("Plunger", "Plunjer", "Spring-loaded plunger that rests on the hub lug.", "Gupchak tirgagiga tiraladigan prujinali plunjer."),
                "Acetal", plunger, new[] { m.GreyPlastic }, swAt, swRot, true, Vector3.forward, 0.4f);
            Mesh nut12 = ctx.Mesh("nut-M12-thin", k => Fasteners.HexNut(k, 0.0105f));
            ctx.Part(sw, id + ".stop-lamp-switch.lock-nut", new Txt("Lock nut", "Kontrgayka", "Holds the switch at its set position in the bracket.", "O‘chirgichni kronshteynda sozlangan holatda ushlaydi."),
                "Nylon", nut12, new[] { m.BlackPlastic }, p + new Vector3(0f, 0f, -0.0505f), swRot, true, Vector3.forward, 0.4f);
            Mesh plug = ctx.Mesh("connector-plug-small", k => k.RoundedBox(new Vector3(0f, 0f, 0f), new Vector3(0.015f, 0.014f, 0.012f), 0.002f, 1));
            ctx.Part(sw, id + ".stop-lamp-switch.connector", new Txt("Connector", "Ulagich", "Four-pin sealed plug of the lamp and network circuits.", "Chiroq va tarmoq zanjirlarining to‘rt kontaktli zichlangan vilkasi."),
                "Glass-filled nylon", plug, new[] { m.BlackPlastic }, swAt + Vector3.back * 0.026f, Quaternion.identity, true, Vector3.back, 0.4f);
        }

        // ------------------------------------------------------------------------------------------------ booster and master cylinder

        private void Booster(Transform system)
        {
            string id = "brakes.booster";
            Vector3 o = new Vector3(bx, by, bz);
            Transform asm = ctx.Assembly(system, id, new Txt("Brake Booster and Master Cylinder", "Tormoz kuchaytirgichi va bosh silindr",
                    "Electromechanical booster that multiplies the pedal force and a tandem master cylinder that turns it into pressure for two independent circuits.",
                    "Pedal kuchini oshiruvchi elektromexanik kuchaytirgich va uni ikki mustaqil kontur uchun bosimga aylantiruvchi tandem bosh silindr."),
                "Aluminium and steel", Vector3.up, 1.2f);

            // Mounting bracket: a small bulkhead of its own, bolted to the wheel-well wall, with the shaft bearing tab.
            Transform mount = ctx.SubAssembly(asm, id + ".bracket", new Txt("Booster Mounting Bracket", "Kuchaytirgich kronshteyni",
                    "Pressed-steel bracket bolted to the wheel-well wall; the booster studs pass through its face and its lower tab carries the cross-shaft.",
                    "G‘ildirak arkasi devoriga boltlangan presslangan po‘lat kronshteyn; kuchaytirgich shpilkalari uning yuzidan o‘tadi, pastki qulog‘i ko‘ndalang valni ko‘taradi."),
                "Pressed steel", Vector3.back, 0.6f);
            float wallX = -(fwall - 0.0075f) - bx;
            Mesh plate = ctx.Mesh("booster-bracket", k =>
            {
                float zf = -0.004f;
                k.PlanarPrism(new[]
                {
                    new Vector3(wallX + 0.0015f, -0.075f, zf), new Vector3(0.058f, -0.075f, zf), new Vector3(0.075f, -0.058f, zf), new Vector3(0.075f, 0.052f, zf),
                    new Vector3(0.057f, 0.07f, zf), new Vector3(-0.04f, 0.07f, zf), new Vector3(wallX + 0.0015f, 0.03f, zf)
                }, 0.004f);
                k.PlanarPrism(new[]
                {
                    new Vector3(wallX, -0.07f, -0.006f), new Vector3(wallX, 0.03f, -0.006f), new Vector3(wallX, 0.03f, 0.045f), new Vector3(wallX, 0.0f, 0.062f), new Vector3(wallX, -0.07f, 0.062f)
                }, 0.003f);
                k.PlanarPrism(new[] { new Vector3(wallX + 0.001f, 0.028f, -0.002f), new Vector3(wallX + 0.001f, 0.028f, 0.044f), new Vector3(wallX + 0.03f, 0.028f, -0.002f) }, 0.003f);
                float ty = pivot.y - by, tz = pivot.z - bz;
                k.PlanarPrism(new[]
                {
                    new Vector3(0.030f, ty - 0.022f, tz - 0.02f), new Vector3(0.030f, ty - 0.022f, -0.003f), new Vector3(0.030f, -0.058f, -0.003f),
                    new Vector3(0.030f, -0.058f, tz + 0.018f), new Vector3(0.030f, ty + 0.02f, tz - 0.02f)
                }, 0.005f);
            });
            ctx.Part(mount, id + ".bracket.body", new Txt("Mounting bracket", "Mahkamlash kronshteyni",
                    "Face plate, wall flange, gusset and bearing tab pressed and welded from 2.5 mm steel.",
                    "2,5 mm po‘latdan presslangan va payvandlangan yuz plastinasi, devor flanetsi, kosinka va podshipnik qulog‘i."),
                "Pressed steel S355, e-coated", plate, new[] { m.BlackSteel }, o, Quaternion.identity);
            foreach (float dy in new[] { -0.045f, 0.015f })
                ctx.Part(mount, id + ".bracket.bolt-" + (dy < 0 ? "lower" : "upper"), new Txt("Bolt M8", "Bolt M8",
                        "Flange bolt into a weld nut in the wheel-well wall.", "G‘ildirak arkasi devoridagi payvand gaykaga buraladigan flanetsli bolt."),
                    "Steel 8.8, zinc flake", ctx.Mesh("bolt-M8x20-plain", k => Fasteners.HexBolt(k, 0.008f, 0.02f, false)), new[] { m.ZincSteel },
                    o + new Vector3(wallX + 0.0015f, dy, 0.03f), Along(Vector3.right), true, Vector3.right, 0.4f);

            // Booster.
            Transform servo = ctx.SubAssembly(asm, id + ".servo", new Txt("Electromechanical Booster", "Elektromexanik kuchaytirgich",
                    "Brushless motor and two-stage gear that add force to the driver's push rod; it also brakes on its own for the stability and assist systems.",
                    "Haydovchi shtokiga kuch qo‘shuvchi cho‘tkasiz motor va ikki pog‘onali uzatma; barqarorlik va yordamchi tizimlar uchun mustaqil ham tormozlaydi."),
                "Aluminium and steel", Vector3.up, 0.6f);
            Quaternion fwd = Along(Vector3.forward);
            Mesh housing = ctx.Mesh("booster-housing", k => k.Lathe(new[]
            {
                new Vector2(0f, -0.018f), new Vector2(0.014f, -0.018f), new Vector2(0.016f, -0.016f), new Vector2(0.016f, 0f), new Vector2(0.074f, 0f),
                new Vector2(0.076f, 0.002f), new Vector2(0.076f, 0.006f), new Vector2(0.071f, 0.009f), new Vector2(0.071f, 0.058f), new Vector2(0.068f, 0.066f),
                new Vector2(0.058f, 0.073f), new Vector2(0.040f, 0.077f), new Vector2(0.034f, 0.078f), new Vector2(0.034f, 0.086f), new Vector2(0f, 0.086f)
            }, 36, 30f));
            ctx.Part(servo, id + ".servo.housing", new Txt("Booster housing", "Kuchaytirgich korpusi",
                    "Die-cast housing of the ball-screw drive and the reaction disc that blends motor and pedal force.",
                    "Sharikli vint uzatmasi va motor hamda pedal kuchini qo‘shuvchi reaksiya diskining quyma korpusi."),
                "Die-cast aluminium", housing, new[] { m.CastAluminium }, o, fwd);
            Mesh motor = ctx.Mesh("booster-motor", k =>
            {
                k.Sub(0);
                k.Push(new Vector3(0.062f, -0.066f, 0.004f), Along(Vector3.forward));
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.024f, 0f), new Vector2(0.026f, 0.003f), new Vector2(0.026f, 0.068f), new Vector2(0.022f, 0.073f), new Vector2(0.011f, 0.073f), new Vector2(0.011f, 0.079f), new Vector2(0f, 0.079f) }, 24, 40f);
                k.Pop();
                k.Sub(1);
                k.Push(new Vector3(0.042f, -0.045f, 0.066f), Quaternion.Euler(0f, 0f, -47f));
                k.RoundedBox(Vector3.zero, new Vector3(0.022f, 0.064f, 0.018f), 0.005f, 2);
                k.Pop();
            }, 2);
            ctx.Part(servo, id + ".servo.motor", new Txt("Motor and gear case", "Motor va reduktor korpusi",
                    "Brushless DC motor whose helical gear in the case drives the booster's ball screw.",
                    "Korpusdagi qiya tishli uzatmasi kuchaytirgichning sharikli vintini aylantiruvchi cho‘tkasiz o‘zgarmas tok motori."),
                "Steel can, aluminium case", motor, new[] { m.BlackSteel, m.CastAluminium }, o, Quaternion.identity, false, new Vector3(0.6f, -0.8f, 0f), 0.5f);
            Mesh ecu = ctx.Mesh("booster-ecu", k =>
            {
                k.RoundedBox(new Vector3(-0.005f, 0.084f, 0.036f), new Vector3(0.085f, 0.022f, 0.062f), 0.004f, 1);
                k.Box(new Vector3(-0.005f, 0.0715f, 0.036f), new Vector3(0.05f, 0.004f, 0.04f));
                for (int i = 0; i < 5; i++) k.Box(new Vector3(-0.037f + i * 0.016f, 0.0965f, 0.036f), new Vector3(0.004f, 0.003f, 0.05f));
            });
            ctx.Part(servo, id + ".servo.control-unit", new Txt("Booster control unit", "Kuchaytirgich boshqaruv bloki",
                    "Controller on the housing that reads the pedal-travel sensor and drives the motor.",
                    "Pedal yurishi datchigini o‘qib motorni boshqaruvchi, korpus ustidagi kontroller."),
                "Aluminium heat sink, PBT cover", ecu, new[] { m.BlackPlastic }, o, Quaternion.identity, false, Vector3.up, 0.5f);
            Mesh conn = ctx.Mesh("booster-connector", k => k.RoundedBox(Vector3.zero, new Vector3(0.016f, 0.014f, 0.024f), 0.002f, 1));
            ctx.Part(servo, id + ".servo.connector", new Txt("Connector", "Ulagich", "Sealed power and CAN connector.", "Quvvat va CAN uchun zichlangan ulagich."),
                "Glass-filled nylon", conn, new[] { m.BlackPlastic }, o + new Vector3(0.0455f, 0.084f, 0.036f), Quaternion.identity, true, Vector3.right, 0.4f);
            Mesh stud = ctx.Mesh("stud-M8x30", k => k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0038f, 0f), new Vector2(0.004f, 0.001f), new Vector2(0.004f, 0.029f), new Vector2(0.0034f, 0.03f), new Vector2(0f, 0.03f) }, 10));
            Mesh nut8 = ctx.Mesh("nut-M8", k => Fasteners.HexNut(k, 0.008f, true));
            int n = 0;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sy in new[] { -1f, 1f })
                {
                    n++;
                    Vector3 s = o + new Vector3(sx * 0.05f, sy * 0.05f, 0.004f);
                    ctx.Part(servo, id + ".servo.stud-" + n, new Txt("Mounting stud M8", "Mahkamlash shpilkasi M8", "Stud pressed into the booster flange, through the bracket.", "Kuchaytirgich flanetsiga presslangan, kronshteyndan o‘tuvchi shpilka."),
                        "Steel 8.8", stud, new[] { m.ZincSteel }, s, Along(Vector3.back), true, Vector3.back, 0.4f);
                    ctx.Part(servo, id + ".servo.nut-" + n, new Txt("Nut M8", "Gayka M8", "Flange nut on the bracket's back face.", "Kronshteyn orqa yuzidagi flanetsli gayka."),
                        "Steel, zinc flake", nut8, new[] { m.ZincSteel }, new Vector3(s.x, s.y, bz - 0.006f), Along(Vector3.back), true, Vector3.back, 0.6f);
                }
            Transform rod = ctx.SubAssembly(servo, id + ".servo.input-rod", new Txt("Input Rod", "Kirish shtoki",
                    "Adjustable push rod from the shaft lever into the booster's valve body.",
                    "Val richagidan kuchaytirgichning klapan korpusiga kiruvchi rostlanadigan itaruvchi shtok."),
                "Steel", Vector3.back, 0.5f);
            float zc = pivot.z;
            Mesh rodMesh = ctx.Mesh("booster-input-rod", k =>
            {
                k.Push(Vector3.zero, Along(Vector3.forward));
                k.Cylinder(0.0048f, zc + 0.020f - bz, 0.012f, 12);
                k.Lathe(new[] { new Vector2(0.0048f, zc + 0.0265f - bz), new Vector2(0.0072f, zc + 0.0265f - bz), new Vector2(0.0072f, zc + 0.0335f - bz), new Vector2(0.0048f, zc + 0.0335f - bz) }, 6, 40f, 30f, 360f, true);
                k.Pop();
            });
            ctx.Part(rod, id + ".servo.input-rod.rod", new Txt("Input rod", "Kirish shtoki",
                    "Threaded rod with a jam nut that sets the pedal free play.", "Pedal erkin yurishini belgilovchi kontrgaykali rezbali shtok."),
                "Steel", rodMesh, new[] { m.MachinedSteel }, o, Quaternion.identity, false, Vector3.back, 0.4f);
            Mesh clevis = ctx.Mesh("booster-rod-clevis", k =>
            {
                foreach (float sx in new[] { -0.0085f, 0.0085f })
                    k.RoundedBox(new Vector3(sx, 0f, 0.002f), new Vector3(0.004f, 0.02f, 0.026f), 0.0015f, 1);
                k.RoundedBox(new Vector3(0f, 0f, 0.0185f), new Vector3(0.021f, 0.02f, 0.01f), 0.002f, 1);
            });
            ctx.Part(rod, id + ".servo.input-rod.clevis", new Txt("Rod clevis", "Shtok vilkasi", "Forged fork that straddles the shaft lever.", "Val richagini ikki tomondan qamrab oluvchi bolg‘alangan vilka."),
                "Forged steel", clevis, new[] { m.ForgedSteel }, new Vector3(bx, by, zc), Quaternion.identity);
            float bootLen = bz - 0.002f - (zc + 0.034f);
            Mesh boot = ctx.Mesh("booster-rod-boot-" + Mathf.RoundToInt(bootLen * 1000f), k => k.Bellows(0.0165f, 0.0062f, 0.009f, 0.0145f, bootLen, 3, 16, 0.004f));
            ctx.Part(rod, id + ".servo.input-rod.boot", new Txt("Rod boot", "Shtok g‘ilofi", "Rubber bellows that keeps dirt out of the booster's valve body.", "Kuchaytirgich klapan korpusini changdan saqlovchi rezina gofra."),
                "EPDM rubber", boot, new[] { m.Rubber }, new Vector3(bx, by, bz - 0.002f), Along(Vector3.back));

            // Master cylinder.
            Transform mc = ctx.SubAssembly(asm, id + ".master-cylinder", new Txt("Tandem Master Cylinder", "Tandem bosh silindr",
                    "Two pistons in one bore: the primary feeds one diagonal circuit, the secondary the other, so a leak never loses all brakes.",
                    "Bitta silindrdagi ikki porshen: birlamchisi bir diagonal konturni, ikkilamchisi ikkinchisini ta’minlaydi, shu sababli oqish hech qachon barcha tormozlarni yo‘qotmaydi."),
                "Machined aluminium", Vector3.forward, 0.7f);
            Vector3 mo = new Vector3(bx, by, mcz);
            Mesh body = ctx.Mesh("master-cylinder-body", k =>
            {
                k.Push(Vector3.zero, Along(Vector3.forward));
                k.Lathe(new[]
                {
                    new Vector2(0f, -0.006f), new Vector2(0.031f, -0.006f), new Vector2(0.031f, 0f), new Vector2(0.025f, 0.004f), new Vector2(0.025f, 0.012f),
                    new Vector2(0.0235f, 0.014f), new Vector2(0.0235f, 0.137f), new Vector2(0.0205f, 0.145f), new Vector2(0.011f, 0.149f), new Vector2(0f, 0.149f)
                }, 28, 35f);
                k.Pop();
                List<Vector2> ear = MeshKit.Slot(new Vector2(-0.042f, 0f), new Vector2(0.042f, 0f), 0.0145f, 8);
                k.Push(new Vector3(0f, 0f, 0.006f), Quaternion.identity);
                k.Extrude(ear, 0.012f);
                k.Pop();
                foreach (float z in new[] { 0.040f, 0.100f })
                {
                    k.Push(new Vector3(0f, 0.016f, z), Quaternion.identity);
                    k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0105f, 0f), new Vector2(0.0105f, 0.013f), new Vector2(0.0078f, 0.015f), new Vector2(0f, 0.015f) }, 16);
                    k.Pop();
                }
                foreach (float z in new[] { 0.050f, 0.110f })
                {
                    k.Push(new Vector3(0f, -0.016f, z), Along(Vector3.down));
                    k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0088f, 0f), new Vector2(0.0088f, 0.014f), new Vector2(0f, 0.014f) }, 14);
                    k.Pop();
                }
            });
            ctx.Part(mc, id + ".master-cylinder.body", new Txt("Cylinder body", "Silindr korpusi",
                    "Anodised aluminium body with two reservoir inlets on top and the primary and secondary outlets below.",
                    "Ustida bachokning ikki kirishi, ostida birlamchi va ikkilamchi chiqishlari bo‘lgan anodlangan alyuminiy korpus."),
                "Aluminium 6061-T6, anodised", body, new[] { m.MachinedAluminium }, mo, Quaternion.identity);
            Mesh mcStud = ctx.Mesh("stud-M8x26", k => k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0038f, 0f), new Vector2(0.004f, 0.001f), new Vector2(0.004f, 0.025f), new Vector2(0.0034f, 0.026f), new Vector2(0f, 0.026f) }, 10));
            foreach (float sx in new[] { -1f, 1f })
            {
                string side = sx < 0 ? "left" : "right";
                ctx.Part(mc, id + ".master-cylinder.stud-" + side, new Txt("Flange stud M8", "Flanets shpilkasi M8", "Booster stud that carries the cylinder flange.", "Silindr flanetsini ko‘taruvchi kuchaytirgich shpilkasi."),
                    "Steel 8.8", mcStud, new[] { m.ZincSteel }, mo + new Vector3(sx * 0.042f, 0f, -0.006f), fwd, true, Vector3.forward, 0.4f);
                ctx.Part(mc, id + ".master-cylinder.nut-" + side, new Txt("Nut M8", "Gayka M8", "Flange nut that clamps the cylinder to the booster.", "Silindrni kuchaytirgichga qisuvchi flanetsli gayka."),
                    "Steel, zinc flake", nut8, new[] { m.ZincSteel }, mo + new Vector3(sx * 0.042f, 0f, 0.012f), fwd, true, Vector3.forward, 0.6f);
            }
            Mesh grommet = ctx.Mesh("reservoir-grommet", k => k.Lathe(new[] { new Vector2(0.0072f, 0f), new Vector2(0.0115f, 0f), new Vector2(0.0115f, 0.003f), new Vector2(0.0098f, 0.005f), new Vector2(0.0072f, 0.005f), new Vector2(0.0072f, 0f) }, 16));
            foreach (float z in new[] { 0.040f, 0.100f })
                ctx.Part(mc, id + ".master-cylinder.grommet-" + (z < 0.07f ? "primary" : "secondary"), new Txt("Reservoir grommet", "Bachok vtulkasi",
                        "Rubber seal between the reservoir spigot and the cylinder inlet.", "Bachok patrubkasi va silindr kirishi orasidagi rezina zichlagich."),
                    "EPDM rubber", grommet, new[] { m.Rubber }, mo + new Vector3(0f, 0.031f, z), Quaternion.identity, true, Vector3.up, 0.4f);
            Transform res = ctx.SubAssembly(mc, id + ".master-cylinder.reservoir", new Txt("Fluid Reservoir", "Tormoz suyuqligi bachogi",
                    "Translucent tank that keeps both chambers topped up; its float switch warns of a low level.",
                    "Ikkala kamerani to‘ldirib turuvchi shaffof idish; uning qalqovichli o‘chirgichi past sath haqida ogohlantiradi."),
                "Polypropylene", Vector3.up, 0.6f);
            Mesh tank = ctx.Mesh("brake-fluid-reservoir", k =>
            {
                k.RoundedBox(new Vector3(0f, 0.071f, 0.072f), new Vector3(0.07f, 0.056f, 0.118f), 0.011f, 2);
                foreach (float z in new[] { 0.040f, 0.100f })
                {
                    k.Push(new Vector3(0f, 0.034f, z), Quaternion.identity);
                    k.Cylinder(0.0071f, 0f, 0.012f, 14);
                    k.Pop();
                }
                k.Push(new Vector3(0f, 0.098f, 0.085f), Quaternion.identity);
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0175f, 0f), new Vector2(0.0175f, 0.012f), new Vector2(0f, 0.012f) }, 22);
                k.Pop();
            });
            ctx.Part(res, id + ".master-cylinder.reservoir.tank", new Txt("Reservoir tank", "Bachok idishi",
                    "Blow-moulded tank with MIN and MAX marks, divided inside so each circuit keeps its own reserve.",
                    "MIN va MAX belgili, puflab quyilgan idish; ichki to‘siq har bir konturga o‘z zaxirasini qoldiradi."),
                "Translucent polypropylene", tank, new[] { m.Reservoir }, mo, Quaternion.identity);
            Mesh cap = ctx.Mesh("reservoir-cap", k =>
            {
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0205f, 0f), new Vector2(0.0215f, 0.002f), new Vector2(0.0215f, 0.013f), new Vector2(0.0195f, 0.016f), new Vector2(0f, 0.016f) }, 16, 40f, 0f, 360f, true);
            });
            ctx.Part(res, id + ".master-cylinder.reservoir.cap", new Txt("Filler cap", "Quyish qopqog‘i", "Vented screw cap with a diaphragm that keeps moisture out of the fluid.", "Suyuqlikka namlik kirishiga yo‘l qo‘ymaydigan membranali, shamollatiladigan burama qopqoq."),
                "Polyamide", cap, new[] { m.BlackPlastic }, mo + new Vector3(0f, 0.110f, 0.085f), Quaternion.identity, false, Vector3.up, 0.5f);
            Mesh level = ctx.Mesh("reservoir-level-switch", k => k.RoundedBox(Vector3.zero, new Vector3(0.012f, 0.016f, 0.02f), 0.002f, 1));
            ctx.Part(res, id + ".master-cylinder.reservoir.level-switch", new Txt("Level switch connector", "Sath o‘chirgichi ulagichi",
                    "Plug of the float switch that lights the brake warning lamp.", "Tormoz ogohlantirish chirog‘ini yoquvchi qalqovichli o‘chirgich vilkasi."),
                "Glass-filled nylon", level, new[] { m.BlackPlastic }, mo + new Vector3(-0.040f, 0.066f, 0.03f), Quaternion.identity, true, Vector3.left, 0.4f);
        }

        // ------------------------------------------------------------------------------------------------ modulator

        private void Modulator(Transform system)
        {
            string id = "brakes.abs";
            Vector3 c = absC;
            Transform asm = ctx.Assembly(system, id, new Txt("ABS and ESC Modulator", "ABS va ESC modulyatori",
                    "Valve block with its own pump and control unit: it holds, releases and builds each wheel's pressure for ABS, traction and stability control.",
                    "O‘z nasosi va boshqaruv blokiga ega klapan bloki: ABS, tortish va barqarorlik nazorati uchun har bir g‘ildirak bosimini ushlaydi, tushiradi va oshiradi."),
                "Aluminium, steel and plastic", new Vector3(0f, 0.7f, 0.7f), 1f);
            Transform hyd = ctx.SubAssembly(asm, id + ".hydraulic-unit", new Txt("Hydraulic Unit", "Gidravlik blok",
                    "Machined block with twelve solenoid valves, two return pumps and low-pressure accumulators, six line ports.",
                    "O‘n ikki solenoid klapan, ikki qaytarish nasosi va past bosim akkumulyatorlari joylashgan, oltita quvur portli frezalangan blok."),
                "Aluminium 6082", Vector3.up, 0.6f);
            Mesh block = ctx.Mesh("abs-valve-block", k =>
            {
                k.RoundedBox(Vector3.zero, AbsSize, 0.004f, 1);
                float top = AbsSize.y * 0.5f, rear = -AbsSize.z * 0.5f;
                foreach (float z in new[] { mcz + 0.05f - absC.z, mcz + 0.11f - absC.z })
                {
                    k.Push(new Vector3(bx - absC.x, top, z), Quaternion.identity);
                    k.Sleeve(0.0078f, 0.0052f, 0f, 0.0012f, 16);
                    k.Pop();
                }
                foreach (Vector2 pt in RearPorts())
                {
                    k.Push(new Vector3(pt.x, pt.y, rear), Along(Vector3.back));
                    k.Sleeve(0.0078f, 0.0052f, 0f, 0.0012f, 16);
                    k.Pop();
                }
                foreach (float x in new[] { -0.025f, 0.025f })
                {
                    k.Push(new Vector3(x, -AbsSize.y * 0.5f, 0.012f), Along(Vector3.down));
                    k.Cylinder(0.0125f, -0.001f, 0.003f, 18);
                    k.Pop();
                }
            });
            ctx.Part(hyd, id + ".hydraulic-unit.valve-block", new Txt("Valve block", "Klapan bloki",
                    "Aluminium block drilled with the fluid passages; inlet and outlet valves per wheel and the circuit switching valves.",
                    "Suyuqlik kanallari parmalangan alyuminiy blok; har bir g‘ildirak uchun kirish va chiqish klapanlari hamda kontur almashtirish klapanlari."),
                "Aluminium 6082-T6", block, new[] { m.MachinedAluminium }, c, Quaternion.identity);
            Mesh pump = ctx.Mesh("abs-pump-motor", k =>
            {
                k.Sub(0);
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.026f, 0f), new Vector2(0.028f, 0.003f), new Vector2(0.028f, 0.046f), new Vector2(0.026f, 0.049f), new Vector2(0f, 0.049f) }, 24, 40f);
                k.Sub(1);
                k.Lathe(new[] { new Vector2(0f, 0.049f), new Vector2(0.018f, 0.049f), new Vector2(0.018f, 0.055f), new Vector2(0.008f, 0.058f), new Vector2(0f, 0.058f) }, 20, 40f);
            }, 2);
            ctx.Part(hyd, id + ".hydraulic-unit.pump-motor", new Txt("Pump motor", "Nasos motori",
                    "DC motor whose eccentric drives the two piston pumps that return fluid during ABS cycles and build pressure for ESC.",
                    "Ekssentrigi ABS sikllarida suyuqlikni qaytaradigan va ESC uchun bosim hosil qiladigan ikki porshenli nasosni harakatlantiruvchi o‘zgarmas tok motori."),
                "Steel can", pump, new[] { m.BlackSteel, m.ZincSteel }, c + new Vector3(-AbsSize.x * 0.5f, -0.005f, 0f), Along(Vector3.left), false, Vector3.left, 0.6f);

            Transform ecuG = ctx.SubAssembly(asm, id + ".control-unit", new Txt("Electronic Control Unit", "Elektron boshqaruv bloki",
                    "Controller and valve coils in one housing on the block; reads the four wheel-speed sensors and the yaw sensor.",
                    "Blok ustidagi bitta korpusda kontroller va klapan g‘altaklari; to‘rtta g‘ildirak tezligi datchigi va burilish datchigini o‘qiydi."),
                "Plastic and electronics", Vector3.right, 0.6f);
            Mesh ecu = ctx.Mesh("abs-ecu", k =>
            {
                k.RoundedBox(new Vector3(0.015f, 0f, 0f), new Vector3(0.03f, 0.084f, 0.098f), 0.005f, 1);
                for (int i = 0; i < 4; i++) k.Box(new Vector3(0.031f, -0.03f + i * 0.02f, 0f), new Vector3(0.003f, 0.004f, 0.08f));
            });
            ctx.Part(ecuG, id + ".control-unit.housing", new Txt("Control-unit housing", "Boshqaruv bloki korpusi",
                    "Sealed housing that clips over the valve domes; the coils inside switch the valves within milliseconds.",
                    "Klapan gumbazlari ustiga kiydiriladigan zichlangan korpus; ichidagi g‘altaklar klapanlarni millisekundlarda almashtiradi."),
                "PBT-GF30", ecu, new[] { m.BlackPlastic }, c + new Vector3(AbsSize.x * 0.5f, 0f, 0f), Quaternion.identity);
            Mesh plug = ctx.Mesh("abs-connector", k =>
            {
                k.RoundedBox(Vector3.zero, new Vector3(0.024f, 0.05f, 0.026f), 0.003f, 1);
                k.Box(new Vector3(0.0135f, 0f, 0.002f), new Vector3(0.003f, 0.04f, 0.018f));
            });
            ctx.Part(ecuG, id + ".control-unit.connector", new Txt("Harness connector", "Simlar ulagichi",
                    "46-pin sealed connector with a locking lever for power, sensors and the vehicle network.",
                    "Quvvat, datchiklar va avtomobil tarmog‘i uchun qulflash dastakli 46 kontaktli zichlangan ulagich."),
                "PBT-GF30", plug, new[] { m.BlackPlastic }, c + new Vector3(AbsSize.x * 0.5f + 0.014f, 0.004f, AbsSize.z * 0.5f + 0.012f), Quaternion.identity, false, Vector3.forward, 0.5f);

            Transform brk = ctx.SubAssembly(asm, id + ".bracket", new Txt("Mounting Bracket", "Mahkamlash kronshteyni",
                    "Steel bracket on the bay floor with three rubber isolators that keep pump noise out of the body.",
                    "Nasos shovqinini kuzovga o‘tkazmaydigan uchta rezina izolyatorli, bo‘lim polidagi po‘lat kronshteyn."),
                "Steel and rubber", Vector3.down, 0.6f);
            float yb = c.y - AbsSize.y * 0.5f;        // block bottom
            float yt = yb - 0.012f;                    // top of the bracket
            float yf = absFloor + 0.0015f;             // bracket base
            Mesh bmesh = ctx.Mesh("abs-bracket-" + Mathf.RoundToInt((yt - yf) * 1000f), k =>
            {
                k.Box(new Vector3(-0.005f, yf - c.y + 0.0015f, 0f), new Vector3(0.1f, 0.003f, 0.1f));
                k.Box(new Vector3(0f, yt - c.y - 0.0015f, 0f), new Vector3(0.09f, 0.003f, 0.09f));
                foreach (float z in new[] { -0.046f, 0.046f })
                    k.Box(new Vector3(0f, (yt + yf) * 0.5f - c.y, z), new Vector3(0.07f, yt - yf, 0.003f));
            });
            ctx.Part(brk, id + ".bracket.body", new Txt("Bracket", "Kronshteyn", "Pressed-steel table with folded legs, bolted to the floor.", "Polga boltlangan, bukilgan oyoqli presslangan po‘lat stol."),
                "Pressed steel, e-coated", bmesh, new[] { m.BlackSteel }, c, Quaternion.identity);
            Mesh iso = ctx.Mesh("abs-isolator", k => k.Lathe(new[]
            {
                new Vector2(0.0035f, 0f), new Vector2(0.009f, 0f), new Vector2(0.0095f, 0.002f), new Vector2(0.0072f, 0.006f), new Vector2(0.0095f, 0.010f),
                new Vector2(0.009f, 0.012f), new Vector2(0.0035f, 0.012f), new Vector2(0.0035f, 0f)
            }, 12, 50f));
            Mesh m6 = ctx.Mesh("bolt-M6x22", k => Fasteners.HexBolt(k, 0.006f, 0.022f, false));
            Vector3[] isoAt = { new Vector3(-0.034f, 0f, -0.03f), new Vector3(0.034f, 0f, -0.03f), new Vector3(0f, 0f, 0.034f) };
            for (int i = 0; i < 3; i++)
            {
                Vector3 pos = new Vector3(c.x + isoAt[i].x, yt, c.z + isoAt[i].z);
                ctx.Part(brk, id + ".bracket.isolator-" + (i + 1), new Txt("Rubber isolator", "Rezina izolyator", "Bonded rubber mount that damps pump vibration.", "Nasos tebranishini so‘ndiruvchi yopishtirilgan rezina tayanch."),
                    "Natural rubber", iso, new[] { m.Rubber }, pos, Quaternion.identity, true, Vector3.down, 0.4f);
                ctx.Part(brk, id + ".bracket.bolt-" + (i + 1), new Txt("Bolt M6", "Bolt M6", "Shoulder bolt through the isolator into the block.", "Izolyator orqali blokka kiruvchi tayanchli bolt."),
                    "Steel 8.8, zinc flake", m6, new[] { m.ZincSteel }, new Vector3(pos.x, yt - 0.003f, pos.z), Along(Vector3.down), true, Vector3.down, 0.5f);
            }
            Mesh m8 = ctx.Mesh("bolt-M8x20-plain", k => Fasteners.HexBolt(k, 0.008f, 0.02f, false));
            foreach (float z in new[] { -0.038f, 0.038f })
                ctx.Part(brk, id + ".bracket.floor-bolt-" + (z < 0 ? "rear" : "front"), new Txt("Bolt M8", "Bolt M8", "Holds the bracket to a weld nut in the floor.", "Kronshteynni poldagi payvand gaykaga mahkamlaydi."),
                    "Steel 8.8, zinc flake", m8, new[] { m.ZincSteel }, new Vector3(c.x - 0.045f, yf + 0.003f, c.z + z), Quaternion.identity, true, Vector3.up, 0.4f);
        }

        // Rear-face outlet ports of the valve block (x, y relative to the block centre): FL, FR, RL, RR.
        private static Vector2[] RearPorts() => new[] { new Vector2(-0.032f, 0.018f), new Vector2(0.032f, 0.018f), new Vector2(-0.032f, -0.018f), new Vector2(0.032f, -0.018f) };

        // ------------------------------------------------------------------------------------------------ lines

        private void Lines(Transform system)
        {
            string id = "brakes.lines";
            Transform asm = ctx.Assembly(system, id, new Txt("Brake Lines", "Tormoz quvurlari",
                    "4.75 mm rigid lines from the master cylinder through the modulator to braided hoses at each wheel, split into two diagonal circuits.",
                    "Bosh silindrdan modulyator orqali har bir g‘ildirakdagi to‘qima qobiqli shlanglargacha boradigan, ikki diagonal konturga bo‘lingan 4,75 mm qattiq quvurlar."),
                "Copper-nickel tube and braided hose", Vector3.up, 0.8f);
            Mesh nut = ctx.Mesh("tube-nut-M10x1", k =>
            {
                float R = 0.011f / (2f * Mathf.Cos(30f * Mathf.Deg2Rad));
                k.Lathe(new[] { new Vector2(0.0026f, 0f), new Vector2(R * 0.9f, 0f), new Vector2(R, 0.0012f), new Vector2(R, 0.0088f), new Vector2(R * 0.9f, NutLen), new Vector2(0.0026f, NutLen) }, 6, 40f, 30f, 360f, true);
                k.Lathe(new[] { new Vector2(0f, -0.004f), new Vector2(0.0048f, -0.004f), new Vector2(0.0048f, 0f), new Vector2(0f, 0f) }, 8);
            });
            float absTop = absC.y + AbsSize.y * 0.5f, absRear = absC.z - AbsSize.z * 0.5f;

            // Feed lines: master-cylinder outlets straight down into the modulator inlets.
            Transform feed = ctx.SubAssembly(asm, id + ".feed", new Txt("Master-Cylinder Feed Lines", "Bosh silindr ta’minot quvurlari",
                    "Two short lines that carry the primary and secondary circuit pressure from the master cylinder into the modulator.",
                    "Birlamchi va ikkilamchi kontur bosimini bosh silindrdan modulyatorga olib boruvchi ikki qisqa quvur."),
                "Copper-nickel tube", Vector3.up, 0.5f);
            string[] circuit = { "primary", "secondary" };
            float[] portZ = { mcz + 0.05f, mcz + 0.11f };
            for (int i = 0; i < 2; i++)
            {
                Vector3 top = new Vector3(bx, by - 0.030f, portZ[i]), bottom = new Vector3(bx, absTop, portZ[i]);
                Line(feed, id + ".feed." + circuit[i], i == 0 ? "Primary feed line" : "Secondary feed line", i == 0 ? "Birlamchi ta’minot quvuri" : "Ikkilamchi ta’minot quvuri",
                    (i == 0 ? "Primary circuit (front left and rear right) from the master cylinder to the modulator." : "Secondary circuit (front right and rear left) from the master cylinder to the modulator."),
                    (i == 0 ? "Birlamchi kontur (old chap va orqa o‘ng): bosh silindrdan modulyatorga." : "Ikkilamchi kontur (old o‘ng va orqa chap): bosh silindrdan modulyatorga."),
                    new List<Vector3> { top + Vector3.down * NutLen, bottom + Vector3.up * NutLen }, Vector3.up);
                TubeNut(feed, id + ".feed." + circuit[i] + "-nut-cylinder", nut, top, Vector3.down);
                TubeNut(feed, id + ".feed." + circuit[i] + "-nut-modulator", nut, bottom, Vector3.up);
            }

            Vector2[] ports = RearPorts();
            Vector3 Port(int i) => new Vector3(absC.x + ports[i].x, absC.y + ports[i].y, absRear);

            // Front left: along the bay side of the left wheel-well wall, over the drive shaft, to the union behind the coilover.
            WheelInfo fl = L.FrontLeft, fr = L.FrontRight;
            float wx = -(fwall - 0.017f);
            float flZ = fl.Centre.z - 0.25f;
            Vector3 uFL = new Vector3(-fwall, by + 0.005f, flZ);
            Vector3 p0 = Port(0);
            var flPath = new List<Vector3>
            {
                p0 + Vector3.back * NutLen, new Vector3(p0.x, p0.y, absRear - 0.026f), new Vector3(wx, by - 0.10f, absRear - 0.048f),
                new Vector3(wx, by - 0.10f, crossZ), new Vector3(wx, uFL.y, crossZ - 0.02f), new Vector3(wx, uFL.y, flZ), new Vector3(uFL.x + 0.012f, uFL.y, flZ)
            };
            Transform gFL = ctx.SubAssembly(asm, id + ".front-left", new Txt("Front Left Brake Line", "Old chap tormoz quvuri", "", ""), "", Vector3.left, 0.7f);
            ctx.Note("brakes.lines.front-left union " + CarLayout.V(uFL));
            Corner(gFL, id + ".front-left", "front left", "old chap", fl, flPath, uFL, Vector3.left, nut, 10f, "primary", new[] { 0.32f, 0.55f });

            // Front right: back over the anti-roll bar, across the footwell behind the toe board, out to the right wall.
            Vector3 p1 = Port(1);
            Vector3 uFR = new Vector3(fwall, by, crossZ);
            var frPath = new List<Vector3>
            {
                p1 + Vector3.back * NutLen, new Vector3(p1.x, p1.y, absRear - 0.026f), new Vector3(p1.x, crossY + 0.01f, absRear - 0.048f),
                new Vector3(p1.x, crossY + 0.01f, crossZ + 0.013f), new Vector3(p1.x + 0.033f, crossY, crossZ), new Vector3(-p1.x - 0.033f, crossY, crossZ),
                // Integration (combined car): rise beside the transaxle's front final drive (x >= 0.46) before turning
                // outboard, so the line passes over its housing.
                new Vector3(-p1.x - 0.021f, crossY + 0.06f, crossZ), new Vector3(-p1.x - 0.011f, uFR.y - 0.01f, crossZ), new Vector3(fwall - 0.03f, uFR.y, crossZ), new Vector3(uFR.x - 0.012f, uFR.y, crossZ)
            };
            Transform gFR = ctx.SubAssembly(asm, id + ".front-right", new Txt("Front Right Brake Line", "Old o‘ng tormoz quvuri", "", ""), "", Vector3.right, 0.7f);
            Corner(gFR, id + ".front-right", "front right", "old o‘ng", fr, frPath, uFR, Vector3.right, nut, 10f, "secondary", new[] { 0.3f, 0.7f });
            // Grommets where the line passes the footwell side walls, clips on the toe board between them.
            Mesh grom = ctx.Mesh("line-grommet", k => k.Lathe(new[] { new Vector2(LineR, -0.005f), new Vector2(0.0062f, -0.005f), new Vector2(0.0078f, -0.0025f), new Vector2(0.0078f, 0.0025f), new Vector2(0.0062f, 0.005f), new Vector2(LineR, 0.005f), new Vector2(LineR, -0.005f) }, 12, 50f));
            foreach (float sx in new[] { -1f, 1f })
            {
                float gx = sx * 0.37f;
                if (ctx.Probe.Cast(new Vector3(0f, crossY, crossZ), new Vector3(sx, 0f, 0f), 0.6f, out float gd, "InteriorDashSides")) gx = sx * gd;
                ctx.Part(gFR, id + ".front-right.grommet-" + (sx < 0 ? "left" : "right"), new Txt("Grommet", "Rezina vtulka",
                        "Rubber grommet where the line passes the footwell side wall.", "Quvur oyoq bo‘shlig‘i yon devoridan o‘tadigan joydagi rezina vtulka."),
                    "EPDM rubber", grom, new[] { m.Rubber }, new Vector3(gx, crossY, crossZ), Along(Vector3.right), true, new Vector3(sx, 0f, 0f), 0.4f);
            }
            int clipN = 0;
            foreach (float cx in new[] { -0.16f, 0.16f })
            {
                if (!ctx.Probe.Cast(new Vector3(cx, crossY, crossZ), Vector3.forward, 0.1f, out float face, "InteriorDash")) continue;
                clipN++;
                LineClip(gFR, id + ".front-right.clip-" + clipN, new Vector3(cx, crossY, crossZ), Vector3.back, Vector3.right, face - LineR, false);
            }

            // Rear lines: under the anti-roll bar, down to the bay floor, through the corridor behind the left wheel
            // well, under the carpet along the left floor edge, then each to its rear wheel-well wall.
            BuildRearLines(asm, id, nut);
        }

        private void BuildRearLines(Transform asm, string id, Mesh nut)
        {
            WheelInfo rl = L.RearLeft, rr = L.RearRight;
            float absRear = absC.z - AbsSize.z * 0.5f;
            Vector2[] ports = RearPorts();
            Vector3 pRL = new Vector3(absC.x + ports[2].x, absC.y + ports[2].y, absRear), pRR = new Vector3(absC.x + ports[3].x, absC.y + ports[3].y, absRear);
            float lx = -(fwall + 0.147f);               // left floor edge (under the carpet lip), ~-0.742
            float c1z = pivot.z - 0.048f;
            float y2 = BayFloor(bx - 0.03f, c1z - 0.03f, 0.127f) + 0.0045f;
            float y3 = BayFloor(-(fwall - 0.04f), 1.30f, 0.124f) + 0.0045f;
            float y4 = BayFloor(-(fwall - 0.04f), 1.13f, 0.122f) + 0.0045f;
            float y5 = BayFloor(-0.70f, 0.95f, 0.121f) + 0.0045f;
            float y6 = BayFloor(lx + 0.005f, 0.90f, 0.121f) + 0.0045f;
            float y7 = BayFloor(lx, 0.797f, 0.126f) + 0.0045f;
            var centre = new List<Vector3>
            {
                new Vector3(bx, by - 0.25f, absRear - 0.06f), new Vector3(bx, by - 0.25f, c1z), new Vector3(bx - 0.03f, y2, c1z - 0.03f),
                new Vector3(-(fwall - 0.04f), y3, 1.30f), new Vector3(-(fwall - 0.04f), y4, 1.13f), new Vector3(-0.70f, y5, 0.95f), new Vector3(lx + 0.005f, y6, 0.90f),
                new Vector3(lx, y7, 0.797f), new Vector3(lx, cabinY, 0.716f), new Vector3(lx, cabinY, rl.Centre.z + 0.525f), new Vector3(-(rwall - 0.012f), cabinY, rl.Centre.z + 0.33f)
            };
            List<Vector3> outer = OffsetXZ(centre, -0.0045f), inner = OffsetXZ(centre, 0.0045f);
            float rampTop = BayFloor(-(rwall - 0.018f), rl.Centre.z + 0.25f, 0.13f);
            float lineYwall = 0.40f, ramp0 = rl.Centre.z + 0.327f, ramp1 = rl.Centre.z + 0.282f, wallZ = rl.Centre.z + 0.25f;
            float vx = rwall - 0.018f;
            var rlPath = new List<Vector3> { pRL + Vector3.back * NutLen, new Vector3(pRL.x, pRL.y, absRear - 0.03f) };
            rlPath.AddRange(outer);
            rlPath.Add(new Vector3(-vx, cabinY, ramp0));
            rlPath.Add(new Vector3(-vx, rampTop + 0.0045f, ramp1));
            rlPath.Add(new Vector3(-vx, rampTop + 0.0045f, wallZ));
            rlPath.Add(new Vector3(-vx, lineYwall, wallZ));
            rlPath.Add(new Vector3(-(rwall - 0.012f), lineYwall, wallZ));
            float rearEdgeZ = rl.Centre.z + 0.335f;
            var rrPath = new List<Vector3> { pRR + Vector3.back * NutLen, new Vector3(pRR.x, pRR.y, absRear - 0.03f) };
            rrPath.AddRange(inner);
            rrPath.Add(new Vector3(-(rwall - 0.03f), cabinY, rearEdgeZ));
            rrPath.Add(new Vector3(vx - 0.006f, cabinY, rearEdgeZ));
            rrPath.Add(new Vector3(vx, cabinY, ramp0));
            rrPath.Add(new Vector3(vx, rampTop + 0.0045f, ramp1));
            rrPath.Add(new Vector3(vx, rampTop + 0.0045f, wallZ));
            rrPath.Add(new Vector3(vx, lineYwall, wallZ));
            rrPath.Add(new Vector3(rwall - 0.012f, lineYwall, wallZ));
            Vector3 uRL = new Vector3(-rwall, lineYwall, wallZ), uRR = new Vector3(rwall, lineYwall, wallZ);
            Transform gRL = ctx.SubAssembly(asm, id + ".rear-left", new Txt("Rear Left Brake Line", "Orqa chap tormoz quvuri", "", ""), "", new Vector3(-1f, 0f, -0.3f), 0.7f);
            Corner(gRL, id + ".rear-left", "rear left", "orqa chap", rl, rlPath, uRL, Vector3.left, nut, 32f, "secondary", null);
            Transform gRR = ctx.SubAssembly(asm, id + ".rear-right", new Txt("Rear Right Brake Line", "Orqa o‘ng tormoz quvuri", "", ""), "", new Vector3(1f, 0f, -0.3f), 0.7f);
            Corner(gRR, id + ".rear-right", "rear right", "orqa o‘ng", rr, rrPath, uRR, Vector3.right, nut, 35f, "primary", null);
            // Double clips along the shared run (bay floor and cabin floor edge), one grommet in the side trim.
            int n = 0;
            float[] clipZ = { 1.30f, 1.17f, 0.55f, 0.15f, -0.25f, -0.65f };
            foreach (float z in clipZ)
            {
                Vector3 at = PointAtZ(centre, z);
                n++;
                LineClip(gRL, id + ".rear-left.clip-" + n, at, Vector3.up, Vector3.forward, at.y - (z > 1f ? BayFloor(at.x, z, at.y - 0.0045f) : cabinY - 0.0035f), true);
            }
            Vector3 trim = PointAtZ(centre, 0.837f);
            Mesh dg = ctx.Mesh("line-grommet-double", k => k.RoundedBox(Vector3.zero, new Vector3(0.02f, 0.012f, 0.008f), 0.003f, 1));
            ctx.Part(gRL, id + ".rear-left.grommet", new Txt("Grommet", "Rezina vtulka", "Twin-line rubber grommet where both rear lines pass the footwell side trim.", "Ikkala orqa quvur oyoq bo‘shlig‘i yon qoplamasidan o‘tadigan joydagi ikki teshikli rezina vtulka."),
                "EPDM rubber", dg, new[] { m.Rubber }, trim, Quaternion.LookRotation(DirAtZ(centre, 0.837f), Vector3.up), true, Vector3.up, 0.4f);
            int k2 = 0;
            foreach (float x in new[] { -0.30f, 0f, 0.30f })
            {
                k2++;
                LineClip(gRR, id + ".rear-right.clip-" + k2, new Vector3(x, cabinY, rearEdgeZ), Vector3.up, Vector3.right, cabinY - (L.FloorTopY - 0.0045f), false);
            }
            LineClip(gRL, id + ".rear-left.wall-clip", new Vector3(-vx, 0.27f, wallZ), Vector3.right, Vector3.up, rwall - 0.006f - vx, false);
            LineClip(gRR, id + ".rear-right.wall-clip", new Vector3(vx, 0.27f, wallZ), Vector3.left, Vector3.up, rwall - 0.006f - vx, false);
            ctx.Note("brakes.lines rear unions " + CarLayout.V(uRL) + " " + CarLayout.V(uRR) + ", floor run x " + CarLayout.F(lx) + " y " + CarLayout.F(cabinY));
        }

        // Rigid line of one wheel, its tube nuts and the hose group at the wheel-well wall.
        private void Corner(Transform g, string id, string en, string uz, WheelInfo w, List<Vector3> path, Vector3 union, Vector3 wallNormal, Mesh nut, float banjoDeg, string circuit, float[] clipsAt)
        {
            CarPartInfo info = g.GetComponent<CarPartInfo>();
            string En = Title(en);
            info.descriptionEnglish = "Rigid line of the " + circuit + " circuit from the modulator to the " + en + " wheel, with its braided hose to the caliper.";
            info.descriptionUzbek = (circuit == "primary" ? "Birlamchi" : "Ikkilamchi") + " konturning modulyatordan " + uz + " g‘ildirakkacha qattiq quvuri va supportgacha to‘qima qobiqli shlangi.";
            info.material = "Copper-nickel tube, braided hose";
            Line(g, id + ".rigid", "Rigid line, " + en, "Qattiq quvur, " + uz,
                "4.75 mm copper-nickel tube bent to the body, flared at both ends for its tube nuts.",
                "Kuzov shakliga bukilgan 4,75 mm mis-nikel quvur, ikki uchi gaykalari uchun kengaytirilgan.", path, Vector3.up);
            Vector3 start = path[0], startDir = (path[0] - path[1]).normalized;
            TubeNut(g, id + ".nut-modulator", nut, start + startDir * NutLen, -startDir);
            Vector3 end = path[path.Count - 1], endDir = (path[path.Count - 1] - path[path.Count - 2]).normalized;
            TubeNut(g, id + ".nut-union", nut, end + endDir * NutLen, -endDir);
            if (clipsAt != null)
                for (int i = 0; i < clipsAt.Length; i++)
                {
                    Vector3 at = PointAlong(path, clipsAt[i], out Vector3 dir);
                    if (Mathf.Abs(Mathf.Abs(at.x) - (fwall - 0.017f)) < 0.004f && Mathf.Abs(dir.x) < 0.3f)
                        LineClip(g, id + ".clip-" + (i + 1), at, new Vector3(-Mathf.Sign(at.x), 0f, 0f), dir, fwall - 0.005f - Mathf.Abs(at.x), false);
                }
            Hose(g, id + ".hose", En, uz, w, union, wallNormal, banjoDeg);
        }

        // ------------------------------------------------------------------------------------------------ hoses

        private void Hose(Transform parent, string id, string en, string uz, WheelInfo w, Vector3 union, Vector3 n, float banjoDeg)
        {
            Transform g = ctx.SubAssembly(parent, id, new Txt(en + " Hose", char.ToUpperInvariant(uz[0]) + uz.Substring(1) + " shlang",
                    "Braided flexible hose from the body bracket to the caliper's banjo union; it lets the wheel " + (w.Front ? "steer and " : "") + "move up and down.",
                    "Kuzov kronshteynidan support banjo ulagichigacha boradigan to‘qima qobiqli egiluvchan shlang; g‘ildirakka " + (w.Front ? "burilish va " : "") + "yuqoriga-pastga harakatlanishga imkon beradi."),
                "PTFE hose, stainless braid", w.Outward, 0.6f);
            Vector3 a = w.Outward;
            float ang = (w.CaliperAngle + banjoDeg) * Mathf.Deg2Rad;
            Vector3 radial = w.Rolling * Mathf.Cos(ang) + Vector3.up * Mathf.Sin(ang);
            Vector3 eye = w.Centre + a * (w.CaliperAxialMin - 0.0065f) + radial * 0.165f;
            Vector3 b1 = union + n * 0.041f, b2 = union + n * 0.066f;
            // Front: the hose leaves the banjo radially (up and back from the disc) and turns in behind the knuckle;
            // rear: straight toward the wall bracket in the caliper plane.
            Vector3 neck = w.Front ? radial : Vector3.ProjectOnPlane(b2 - eye, a);
            neck = neck.sqrMagnitude > 1e-6f ? neck.normalized : radial;
            Vector3 e1 = eye + neck * 0.044f, e2 = eye + neck * 0.066f;
            var pts = new List<Vector3> { e1, e2, Vector3.Lerp(e2, b2, 0.35f) + Vector3.down * 0.012f, Vector3.Lerp(e2, b2, 0.7f) + Vector3.down * 0.008f, b2, b1 };
            Mesh hose = ctx.Mesh("hose-" + id.Substring(id.IndexOf('.') + 1).Replace(".hose", "").Replace("lines.", ""), k =>
            {
                k.Push(Matrix4x4.Translate(-eye));
                k.Sub(0);
                k.Tube(MeshKit.CatmullRom(pts, 8), HoseR, 10, false, false, Vector3.up);
                k.Sub(1);
                k.Push(eye, Along(neck));
                k.Lathe(new[] { new Vector2(0f, 0.024f), new Vector2(0.0058f, 0.024f), new Vector2(0.0068f, 0.027f), new Vector2(0.0068f, 0.044f), new Vector2(0f, 0.044f) }, 14, 40f);
                k.Pop();
                k.Push(union, Along(n));
                k.Lathe(new[] { new Vector2(0f, 0.021f), new Vector2(0.0068f, 0.021f), new Vector2(0.0068f, 0.038f), new Vector2(0.0058f, 0.041f), new Vector2(0f, 0.041f) }, 14, 40f);
                k.Pop();
                k.Pop();
            }, 2);
            ctx.Part(g, id + ".hose", new Txt("Flexible hose", "Egiluvchan shlang",
                    "PTFE-lined hose in a stainless braid with crimped steel ends; it does not swell under pressure, so the pedal stays firm.",
                    "Zanglamas to‘qima qobiqli, siqib mahkamlangan po‘lat uchli PTFE shlang; bosim ostida kengaymaydi, pedal qattiq qoladi."),
                "PTFE, stainless steel braid", hose, new[] { m.BraidedHose, m.ZincSteel }, eye, Quaternion.identity);
            Mesh banjo = ctx.Mesh("banjo-fitting", k =>
            {
                k.Lathe(new[] { new Vector2(0.0052f, -0.005f), new Vector2(0.0092f, -0.005f), new Vector2(0.0098f, -0.004f), new Vector2(0.0098f, 0.004f), new Vector2(0.0092f, 0.005f), new Vector2(0.0052f, 0.005f), new Vector2(0.0052f, -0.005f) }, 20, 40f);
                k.Push(Vector3.zero, Quaternion.FromToRotation(Vector3.up, Vector3.forward));
                k.Cylinder(0.0045f, 0.007f, 0.025f, 12);
                k.Pop();
            });
            ctx.Part(g, id + ".banjo", new Txt("Banjo fitting", "Banjo ulagich", "Ring fitting on the hose end that feeds the caliper through the hollow bolt.", "Shlang uchidagi halqasimon ulagich: kovak bolt orqali supportga suyuqlik beradi."),
                "Steel, zinc-nickel", banjo, new[] { m.ZincSteel }, eye, Quaternion.LookRotation(neck, a), false, -a, 0.4f);
            Mesh bolt = ctx.Mesh("banjo-bolt-M10x1", k => Fasteners.HexBolt(k, 0.010f, 0.024f, false));
            ctx.Part(g, id + ".banjo-bolt", new Txt("Banjo bolt M10x1", "Banjo bolti M10x1", "Hollow bolt with a cross-drilling that passes fluid into the caliper.", "Suyuqlikni supportga o‘tkazuvchi ko‘ndalang teshikli kovak bolt."),
                "Steel 8.8", bolt, new[] { m.ZincSteel }, eye - a * 0.0065f, Along(-a), true, -a, 0.6f);
            Mesh washer = ctx.Mesh("copper-washer-10", k => k.Sleeve(0.0088f, 0.0052f, 0f, 0.0015f, 12));
            ctx.Part(g, id + ".washer-inner", new Txt("Sealing washer", "Zichlovchi shayba", "Annealed copper washer that seals the banjo to the caliper.", "Banjoni supportga zichlovchi yumshatilgan mis shayba."),
                "Annealed copper", washer, new[] { m.Copper }, eye + a * 0.005f, Along(a), true, a, 0.4f);
            ctx.Part(g, id + ".washer-outer", new Txt("Sealing washer", "Zichlovchi shayba", "Copper washer under the bolt head.", "Bolt kallagi ostidagi mis shayba."),
                "Annealed copper", washer, new[] { m.Copper }, eye - a * 0.0065f, Along(a), true, -a, 0.5f);
            Mesh fitting = ctx.Mesh("hose-end-fitting", k =>
            {
                float R = 0.017f / (2f * Mathf.Cos(30f * Mathf.Deg2Rad));
                k.Lathe(new[] { new Vector2(0f, 0.003f), new Vector2(R * 0.92f, 0.003f), new Vector2(R, 0.0045f), new Vector2(R, 0.0195f), new Vector2(R * 0.92f, 0.021f), new Vector2(0f, 0.021f) }, 6, 40f, 30f, 360f, true);
                k.Cylinder(0.0052f, -0.008f, 0.003f, 12);
            });
            ctx.Part(g, id + ".end-fitting", new Txt("Hose end fitting", "Shlang uchi shtutseri", "Hex fitting held in the wall bracket; the rigid line's tube nut screws into it through the wall.", "Devor kronshteynida ushlanadigan olti qirrali shtutser; qattiq quvur gaykasi devor orqali unga buraladi."),
                "Steel, zinc-nickel", fitting, new[] { m.ZincSteel }, union, Along(n), false, n, 0.4f);
            Vector3 u = Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.forward;
            Mesh plate = ctx.Mesh("hose-bracket", k =>
            {
                List<Vector2> outline = Profiles.RoundRect(0.044f, 0.032f, 0.006f, 3);
                var c3 = new List<Vector3>();
                foreach (Vector2 q in outline) c3.Add(new Vector3(q.x, q.y, 0f));
                k.PlanarPrism(c3, 0.003f);
                k.Push(new Vector3(0f, 0f, 0.0015f), Quaternion.FromToRotation(Vector3.up, Vector3.forward));
                k.Sleeve(0.0125f, 0.0098f, 0f, 0.004f, 16);
                k.Pop();
            });
            ctx.Part(g, id + ".bracket", new Txt("Hose bracket", "Shlang kronshteyni", "Steel bracket welded to the wheel-well wall that holds the hose end.", "Shlang uchini ushlab turuvchi, g‘ildirak arkasi devoriga payvandlangan po‘lat kronshteyn."),
                "Pressed steel", plate, new[] { m.BlackSteel }, union + n * 0.0015f, Quaternion.LookRotation(n, u), false, n, 0.3f);
            Mesh clip = ctx.Mesh("hose-retainer-clip", k =>
            {
                k.Box(new Vector3(0f, 0.0105f, 0f), new Vector3(0.022f, 0.0016f, 0.004f));
                foreach (float sx in new[] { -0.0095f, 0.0095f }) k.Box(new Vector3(sx, 0f, 0f), new Vector3(0.0016f, 0.021f, 0.004f));
            });
            ctx.Part(g, id + ".retainer", new Txt("Retaining clip", "Ushlagich qisqich", "Spring-steel clip that locks the fitting in the bracket.", "Shtutserni kronshteynda qulflovchi prujina po‘lat qisqich."),
                "Spring steel", clip, new[] { m.ZincSteel }, union + n * 0.006f, Quaternion.LookRotation(n, u), true, u, 0.4f);
            ctx.Note(id + ": banjo " + CarLayout.V(eye) + " axis " + CarLayout.V(a) + ", wall union " + CarLayout.V(union));
        }

        // ------------------------------------------------------------------------------------------------ parking brake

        private void ParkingBrake(Transform system)
        {
            string id = "brakes.parking-brake";
            Transform asm = ctx.Assembly(system, id, new Txt("Electric Parking Brake", "Elektr to‘xtash tormozi",
                    "Motor-on-caliper actuators that clamp the rear pads mechanically when parked; the modulator's control unit drives them.",
                    "To‘xtaganda orqa kolodkalarni mexanik qisuvchi, supportga o‘rnatilgan motorli ijro mexanizmlari; ularni modulyatorning boshqaruv bloki boshqaradi."),
                "Plastic and steel", Vector3.back, 1f);
            Actuator(asm, id + ".rear-left", "Rear Left Actuator", "Orqa chap ijro mexanizmi", L.RearLeft, 32f, new Vector3(-rwall, 0.40f, L.RearLeft.Centre.z + 0.25f), Vector3.left);
            Actuator(asm, id + ".rear-right", "Rear Right Actuator", "Orqa o‘ng ijro mexanizmi", L.RearRight, 35f, new Vector3(rwall, 0.40f, L.RearRight.Centre.z + 0.25f), Vector3.right);
            // Switch on the left flank of the steering-column housing, in reach of the driver's left hand.
            Vector3 from = new Vector3(-0.35f, 0.50f, L.SteeringWheelCentre.z + 0.33f);
            if (ctx.Probe.Cast(from, Vector3.right, 0.4f, out float d, "InteriorSteeringBase"))
            {
                Transform sw = ctx.SubAssembly(asm, id + ".switch", new Txt("Parking-Brake Switch", "To‘xtash tormozi o‘chirgichi",
                        "Pull-to-apply, push-to-release switch with two redundant contact paths to the control unit.",
                        "Tortib yoqiladigan, bosib bo‘shatiladigan, boshqaruv blokiga ikki zaxira kontakt yo‘li bilan ulangan o‘chirgich."),
                    "Plastic", Vector3.left, 0.5f);
                Vector3 at = from + Vector3.right * (d - 0.006f);
                Mesh body = ctx.Mesh("epb-switch-module", k => k.RoundedBox(Vector3.zero, new Vector3(0.012f, 0.032f, 0.052f), 0.003f, 1));
                ctx.Part(sw, id + ".switch.module", new Txt("Switch module", "O‘chirgich moduli", "Moulded housing with the contacts and the indicator lamp.", "Kontaktlar va indikator chirog‘i joylashgan quyma korpus."),
                    "Polycarbonate-ABS", body, new[] { m.BlackPlastic }, at, Quaternion.identity);
                Mesh paddle = ctx.Mesh("epb-switch-paddle", k => k.RoundedBox(Vector3.zero, new Vector3(0.008f, 0.016f, 0.028f), 0.003f, 1));
                ctx.Part(sw, id + ".switch.paddle", new Txt("Pull paddle", "Tortish tugmasi", "Paddle marked P that the driver pulls to apply.", "Haydovchi yoqish uchun tortadigan P belgili tugma."),
                    "Polycarbonate-ABS", paddle, new[] { m.GreyPlastic }, at + Vector3.left * 0.009f, Quaternion.identity, true, Vector3.left, 0.4f);
            }
        }

        private void Actuator(Transform asm, string id, string en, string uz, WheelInfo w, float banjoDeg, Vector3 union, Vector3 n)
        {
            Transform g = ctx.SubAssembly(asm, id, new Txt(en, uz,
                    "Motor, belt and planetary gear on the back of the caliper; a spindle pushes the piston to clamp the pads and holds them by self-locking.",
                    "Support orqasidagi motor, tasma va planetar uzatma; shpindel porshenni itarib kolodkalarni qisadi va o‘z-o‘zidan tormozlanib ushlab turadi."),
                "Plastic and steel", w.Outward, 0.7f);
            float ang = w.CaliperAngle * Mathf.Deg2Rad;
            Vector3 radial = w.Rolling * Mathf.Cos(ang) + Vector3.up * Mathf.Sin(ang);
            Vector3 tangent = -w.Rolling * Mathf.Sin(ang) + Vector3.up * Mathf.Cos(ang);
            // Integration (combined car): seated 5 mm into the caliper's back face (a declared contact), so the gear
            // housing clears the rear anti-roll bar's lever eye at the right corner.
            Vector3 face = w.Centre + w.Outward * (w.CaliperAxialMin + 0.005f) + radial * ((w.CaliperRadiusMin + w.CaliperRadiusMax) * 0.5f);
            Vector3 X = -tangent, Y = -w.Outward;
            Quaternion rot = Quaternion.LookRotation(Vector3.Cross(X, Y), Y);
            Mesh housing = ctx.Mesh("epb-gear-housing", k =>
            {
                k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.034f, 0f), new Vector2(0.034f, 0.004f), new Vector2(0.03f, 0.007f), new Vector2(0.03f, 0.05f), new Vector2(0.026f, 0.058f), new Vector2(0f, 0.058f) }, 24, 35f);
                k.RoundedBox(new Vector3(0.023f, 0.064f, 0f), new Vector3(0.082f, 0.016f, 0.044f), 0.007f, 2);
            });
            ctx.Part(g, id + ".housing", new Txt("Gear housing", "Reduktor korpusi", "Housing of the belt stage and the two-stage planetary gear, sealed to the caliper.", "Tasma bosqichi va ikki pog‘onali planetar uzatma korpusi, supportga zichlangan."),
                "Glass-filled polyamide", housing, new[] { m.BlackPlastic }, face, rot);
            Mesh motor = ctx.Mesh("epb-motor", k => k.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.0165f, 0f), new Vector2(0.0175f, 0.002f), new Vector2(0.0175f, 0.054f), new Vector2(0.015f, 0.057f), new Vector2(0f, 0.057f) }, 18, 40f));
            ctx.Part(g, id + ".motor", new Txt("Actuator motor", "Ijro motori", "12 V brush motor that spins the gear train for about one second per apply.", "Har bir qo‘llashda taxminan bir soniya uzatmani aylantiruvchi 12 V cho‘tkali motor."),
                "Steel can", motor, new[] { m.BlackSteel }, face + rot * new Vector3(0.046f, 0.0f, 0f), rot, false, X, 0.5f);
            Mesh plug = ctx.Mesh("epb-connector", k => k.RoundedBox(Vector3.zero, new Vector3(0.014f, 0.012f, 0.018f), 0.002f, 1));
            Vector3 plugAt = face + rot * new Vector3(0.046f, 0.064f, 0.026f);
            ctx.Part(g, id + ".connector", new Txt("Connector", "Ulagich", "Two-pin sealed motor connector.", "Ikki kontaktli zichlangan motor ulagichi."),
                "Glass-filled nylon", plug, new[] { m.BlackPlastic }, plugAt, rot, true, Y, 0.4f);
            Mesh m6 = ctx.Mesh("bolt-M6x22", k => Fasteners.HexBolt(k, 0.006f, 0.022f, false));
            foreach (float sz in new[] { -1f, 1f })
                ctx.Part(g, id + ".bolt-" + (sz < 0 ? "a" : "b"), new Txt("Bolt M6", "Bolt M6", "Torx bolt that holds the gear housing on the caliper.", "Reduktor korpusini supportga mahkamlovchi Torx bolt."),
                    "Steel 10.9", m6, new[] { m.ZincSteel }, face + rot * new Vector3(-0.024f, 0.004f, sz * 0.024f), rot, true, Y, 0.4f);
            Vector3 bodyEnd = union + n * 0.005f + Vector3.down * 0.05f;
            // Down under the drive-shaft line, forward, then up the wall to the body connector.
            Vector3 drop = new Vector3(Mathf.Lerp(plugAt.x, bodyEnd.x, 0.15f), w.Centre.y - 0.135f, plugAt.z + 0.01f);
            Vector3 low = new Vector3(Mathf.Lerp(plugAt.x, bodyEnd.x, 0.45f), w.Centre.y - 0.145f, w.Centre.z + 0.115f);
            var lead = new List<Vector3> { plugAt + rot * new Vector3(0f, 0f, 0.009f), plugAt + rot * new Vector3(0f, 0.01f, 0.03f), drop, low, bodyEnd + n * 0.03f + Vector3.down * 0.03f, bodyEnd };
            Mesh leadMesh = ctx.Mesh("epb-lead-" + (w.Left ? "left" : "right"), k =>
            {
                k.Push(Matrix4x4.Translate(-plugAt));
                k.Tube(MeshKit.CatmullRom(lead, 5), 0.0032f, 7, true, true, Vector3.up);
                k.Push(bodyEnd, Quaternion.LookRotation(n, Vector3.up));
                k.Box(Vector3.zero, new Vector3(0.016f, 0.012f, 0.01f));
                k.Pop();
                k.Pop();
            });
            ctx.Part(g, id + ".lead", new Txt("Motor lead", "Motor simi", "Two-core lead with a body connector by the hose bracket, run back to the modulator.", "Shlang kronshteyni yonidagi kuzov ulagichli, modulyatorga boradigan ikki tomirli sim."),
                "Copper, PVC jacket", leadMesh, new[] { m.WireBlack }, plugAt, Quaternion.identity, true, Y, 0.4f);
        }

        // ------------------------------------------------------------------------------------------------ helpers

        private void Line(Transform parent, string id, string en, string uz, string descEn, string descUz, List<Vector3> corners, Vector3 normalHint)
        {
            Vector3 o = corners[0];
            Mesh mesh = ctx.Mesh("line-" + id.Substring("brakes.lines.".Length), k =>
            {
                k.Push(Matrix4x4.Translate(-o));
                k.Tube(MeshKit.Bends(corners, Bend, 5), LineR, 8, true, true, normalHint);
                k.Pop();
            });
            ctx.Part(parent, id, new Txt(en, uz, descEn, descUz), "Copper-nickel CuNi10Fe1Mn", mesh, new[] { lineMat }, o, Quaternion.identity);
        }

        private void TubeNut(Transform parent, string id, Mesh nut, Vector3 seat, Vector3 outward)
        {
            ctx.Part(parent, id, new Txt("Tube nut M10x1", "Quvur gaykasi M10x1", "Flare nut that presses the line's double flare into the port seat.", "Quvurning ikki qavat kengaytmasini port o‘rindig‘iga bosuvchi gayka."),
                "Steel, zinc-nickel", nut, new[] { m.ZincSteel }, seat, Along(outward), true, outward, 0.4f);
        }

        // Nylon clip holding a line (or two side by side) to a surface; 'up' points away from the surface, 'along' runs with the line.
        private void LineClip(Transform parent, string id, Vector3 linePoint, Vector3 up, Vector3 along, float standoff, bool twin)
        {
            standoff = Mathf.Max(standoff, LineR + 0.001f);
            Vector3 f = Vector3.ProjectOnPlane(along, up);
            if (f.sqrMagnitude < 1e-6f) f = Vector3.ProjectOnPlane(Vector3.forward, up);
            Quaternion rot = Quaternion.LookRotation(f.normalized, up.normalized);
            string key = "line-clip-" + (twin ? "twin-" : "") + Mathf.RoundToInt(standoff * 1000f);
            Mesh mesh = ctx.Mesh(key, k =>
            {
                float w = twin ? 0.021f : 0.012f;
                k.Box(new Vector3(0f, -standoff + 0.001f, 0f), new Vector3(w + 0.006f, 0.002f, 0.009f));
                if (standoff > 0.008f) k.Box(new Vector3(0f, -standoff * 0.5f, 0f), new Vector3(0.006f, standoff - 0.004f, 0.007f));
                k.Box(new Vector3(0f, 0.0006f, 0f), new Vector3(w, 0.0075f, 0.008f));
            });
            ctx.Part(parent, id, new Txt(twin ? "Twin line clip" : "Line clip", twin ? "Ikki quvurli qisqich" : "Quvur qisqichi",
                    "Glass-filled nylon clip on a weld stud that holds the line against vibration.", "Quvurni tebranishdan ushlab turuvchi, payvand shpilkadagi shisha tolali neylon qisqich."),
                "Glass-filled nylon PA66", mesh, new[] { m.BlackPlastic }, linePoint, rot, true, up, 0.3f);
        }

        // Offsets a polyline sideways in the horizontal plane (positive = to the left of the travel direction, +x
        // for a line running to -z), keeping heights; vertical segments take the neighbouring direction.
        private static List<Vector3> OffsetXZ(List<Vector3> path, float d)
        {
            int n = path.Count;
            var dirs = new Vector2[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 v = new Vector2(path[i + 1].x - path[i].x, path[i + 1].z - path[i].z);
                dirs[i] = v.sqrMagnitude > 1e-8f ? v.normalized : Vector2.zero;
            }
            for (int i = 0; i < n - 1; i++)
                if (dirs[i] == Vector2.zero)
                    for (int j = 1; j < n; j++)
                    {
                        if (i + j < n - 1 && dirs[i + j] != Vector2.zero) { dirs[i] = dirs[i + j]; break; }
                        if (i - j >= 0 && dirs[i - j] != Vector2.zero) { dirs[i] = dirs[i - j]; break; }
                    }
            var result = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                Vector2 a = dirs[Mathf.Max(0, i - 1)], b = dirs[Mathf.Min(n - 2, i)];
                Vector2 na = new Vector2(-a.y, a.x), nb = new Vector2(-b.y, b.x);
                Vector2 nm = (na + nb).sqrMagnitude > 1e-8f ? (na + nb).normalized : nb;
                float scale = 1f / Mathf.Max(0.35f, Vector2.Dot(nm, nb));
                Vector2 off = nm * d * scale;
                result.Add(new Vector3(path[i].x + off.x, path[i].y, path[i].z + off.y));
            }
            return result;
        }

        // Point of a polyline (mostly running along -z) at a given z.
        private static Vector3 PointAtZ(List<Vector3> path, float z)
        {
            for (int i = 0; i < path.Count - 1; i++)
            {
                float z0 = path[i].z, z1 = path[i + 1].z;
                if ((z <= z0 && z >= z1) || (z >= z0 && z <= z1))
                {
                    float t = Mathf.Abs(z1 - z0) > 1e-6f ? (z - z0) / (z1 - z0) : 0f;
                    return Vector3.Lerp(path[i], path[i + 1], t);
                }
            }
            return path[path.Count - 1];
        }

        private static Vector3 DirAtZ(List<Vector3> path, float z)
        {
            for (int i = 0; i < path.Count - 1; i++)
            {
                float z0 = path[i].z, z1 = path[i + 1].z;
                if ((z <= z0 && z >= z1) || (z >= z0 && z <= z1)) return (path[i + 1] - path[i]).normalized;
            }
            return Vector3.back;
        }

        // Point at a fraction of a polyline's length, and the direction there.
        private static Vector3 PointAlong(List<Vector3> path, float t, out Vector3 dir)
        {
            float total = 0f;
            for (int i = 0; i < path.Count - 1; i++) total += Vector3.Distance(path[i], path[i + 1]);
            float target = total * Mathf.Clamp01(t), run = 0f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                float len = Vector3.Distance(path[i], path[i + 1]);
                if (run + len >= target && len > 1e-6f)
                {
                    dir = (path[i + 1] - path[i]) / len;
                    return Vector3.Lerp(path[i], path[i + 1], (target - run) / len);
                }
                run += len;
            }
            dir = (path[path.Count - 1] - path[path.Count - 2]).normalized;
            return path[path.Count - 1];
        }

        // ------------------------------------------------------------------------------------------------ contacts

        private void Contacts()
        {
            // The pedal hub sits on the glTF pedal arm inside its housing; the shaft passes the footwell side wall.
            ctx.AllowContact("brakes.pedal-box", "InteriorPedalBrake");
            ctx.AllowContact("brakes.pedal-box", "InteriorSteeringBase");
            ctx.AllowContact("brakes.pedal-box", "InteriorDash");
            // Lever and clevis, outer bearing in the booster bracket's tab.
            ctx.AllowContact("brakes.pedal-box.cross-shaft", "brakes.booster");
            ctx.AllowContact("brakes.booster.bracket", "InteriorFloor");
            ctx.AllowContact("brakes.booster.bracket", "CarConcept");
            // Tube nuts in the master-cylinder and modulator ports.
            ctx.AllowContact("brakes.lines", "brakes.booster.master-cylinder");
            ctx.AllowContact("brakes.lines", "brakes.abs");
            // Unions through the wheel-well walls, clips on the floor, rear lines under the carpet.
            ctx.AllowContact("brakes.lines", "InteriorFloor");
            ctx.AllowContact("brakes.lines", "CarConcept");
            ctx.AllowContact("brakes.lines.front-right", "InteriorDash");
            ctx.AllowContact("brakes.lines.rear-", "InteriorDashSides");
            // Banjo unions on the glTF calipers.
            ctx.AllowContact("brakes.lines.front-left.hose", "WheelFrontL/WheelFrontLBrakePad");
            ctx.AllowContact("brakes.lines.front-right.hose", "WheelFrontR/WheelFrontRBrakePad");
            ctx.AllowContact("brakes.lines.rear-left.hose", "WheelRearL/WheelRearLBrakePad");
            ctx.AllowContact("brakes.lines.rear-right.hose", "WheelRearR/WheelRearRBrakePad");
            ctx.AllowContact("brakes.parking-brake.rear-left", "WheelRearL/WheelRearLBrakePad");
            ctx.AllowContact("brakes.parking-brake.rear-right", "WheelRearR/WheelRearRBrakePad");
            ctx.AllowContact("brakes.parking-brake", "InteriorFloor");
            ctx.AllowContact("brakes.parking-brake", "CarConcept");
            ctx.AllowContact("brakes.parking-brake.rear-left.lead", "brakes.lines.rear-left");
            ctx.AllowContact("brakes.parking-brake.rear-right.lead", "brakes.lines.rear-right");
            ctx.AllowContact("brakes.parking-brake.switch", "InteriorSteeringBase");
            ctx.AllowContact("brakes.abs.bracket", "InteriorFloor");
            ctx.AllowContact("brakes.abs.bracket", "CarConcept");
            // Rear hose and actuator meet the rear hub carrier of the rear-suspension builder (combined later).
            ctx.AllowContact("brakes.lines.rear-left.hose", "suspension.rear-left");
            ctx.AllowContact("brakes.lines.rear-right.hose", "suspension.rear-right");
            ctx.AllowContact("brakes.parking-brake.rear-left", "suspension.rear-left");
            ctx.AllowContact("brakes.parking-brake.rear-right", "suspension.rear-right");
        }
    }
}
#endif
