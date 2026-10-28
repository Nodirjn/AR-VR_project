#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CarInternals
{
    /// <summary>
    /// Regroups the V6 CAD export (82 loose nodes under CarConcept/V6_Engine_Prototype(Clone)) into functional
    /// sub-assemblies and tags every node with CarPartInfo. Idempotent: groups from an earlier run are dissolved first.
    /// Every renderer, material, name and world pose is kept: groups sit at the V6 root's origin with no rotation and
    /// nodes keep their local transforms. Then every renderer gets a merged one-submesh copy of its glTF mesh and
    /// its one material in a single slot (<see cref="V6MeshMerge"/>).
    /// </summary>
    public static class V6Regroup
    {
        private const string IdRoot = "powertrain.engine";

        private sealed class GroupDef
        {
            public string Key, En, Uz, DescEn, DescUz, Material;
            public Vector3 Explode;
            public GroupDef Parent;
        }

        public static string Apply(Transform car, CarLayout layout)
        {
            Transform v6 = car.Find(CarLayout.V6Name);
            if (v6 == null) return "missing " + CarLayout.V6Name + " (skipped)";
            int dissolved = Flatten(v6);

            // Bank split: the crank axis runs across the car, so the banks are the front and the rear one.
            Transform crank = v6.Find("Crankshaft");
            float crankZ = crank != null ? CentreZ(crank, layout) : 0f;
            var defs = new Dictionary<string, GroupDef>();
            GroupDef G(string key, string en, string uz, string descEn, string descUz, string material, Vector3 explode, string parent = null)
            {
                var d = new GroupDef { Key = key, En = en, Uz = uz, DescEn = descEn, DescUz = descUz, Material = material, Explode = explode, Parent = parent != null ? defs[parent] : null };
                defs[key] = d;
                return d;
            }
            G("block", "Cylinder Block", "Silindrlar bloki",
                "Cast-aluminium block of the 60-degree V6: six bores, main-bearing webs and coolant jackets.",
                "60 darajali V6 dvigatelning quyma alyuminiy bloki: oltita silindr, tub podshipnik tayanchlari va sovutish ko‘ylagi.",
                "Cast aluminium", Vector3.zero);
            G("crank-train", "Crank Train", "Krivoship-shatun mexanizmi",
                "Crankshaft, connecting rods and pistons: turns the pressure of combustion into rotation.",
                "Tirsakli val, shatunlar va porshenlar: yonish bosimini aylanma harakatga aylantiradi.",
                "Forged steel", Vector3.down);
            G("head-front", "Front Cylinder Head", "Old silindrlar kallagi",
                "Head of the front bank: combustion chambers, valve seats and ports, closed by the valve cover.",
                "Old qator kallagi: yonish kameralari, klapan uyalari va kanallar; ustidan klapan qopqog‘i yopadi.",
                "Cast aluminium", new Vector3(0f, 0.55f, 0.85f));
            G("head-rear", "Rear Cylinder Head", "Orqa silindrlar kallagi",
                "Head of the rear bank: combustion chambers, valve seats and ports, closed by the valve cover.",
                "Orqa qator kallagi: yonish kameralari, klapan uyalari va kanallar; ustidan klapan qopqog‘i yopadi.",
                "Cast aluminium", new Vector3(0f, 0.55f, -0.85f));
            G("valvetrain-front", "Front Valve Train", "Old gaz taqsimlash mexanizmi",
                "Camshaft, rockers, valves and springs of the front bank: open and close the valves in time with the crank.",
                "Old qatorning taqsimlash vali, koromislolari, klapanlari va prujinalari: klapanlarni tirsakli val bilan hamohang ochib-yopadi.",
                "Steel", new Vector3(0f, 0.8f, 0.6f));
            G("valvetrain-rear", "Rear Valve Train", "Orqa gaz taqsimlash mexanizmi",
                "Camshaft, rockers, valves and springs of the rear bank: open and close the valves in time with the crank.",
                "Orqa qatorning taqsimlash vali, koromislolari, klapanlari va prujinalari: klapanlarni tirsakli val bilan hamohang ochib-yopadi.",
                "Steel", new Vector3(0f, 0.8f, -0.6f));
            foreach (string bank in new[] { "front", "rear" })
            {
                bool f = bank == "front";
                string enBank = f ? "Front" : "Rear", uzBank = f ? "Old" : "Orqa";
                G("camshaft-" + bank, enBank + " Camshaft", uzBank + " taqsimlash vali",
                    "Overhead camshaft with its bearing caps and shells; the lobes push the rocker rollers.",
                    "Kallak ustidagi taqsimlash vali, podshipnik qopqoqlari va vkladishlari; mushtlar koromislo roliklarini bosadi.",
                    "Hardened steel", Vector3.up, "valvetrain-" + bank);
                G("rockers-" + bank, enBank + " Rocker Arms", uzBank + " koromislolar",
                    "Roller rocker arms on pivot pins, with lash-adjusting screws.",
                    "O‘qlarga o‘rnatilgan rolikli koromislolar va zazorni rostlash vintlari.",
                    "Forged steel", Vector3.up, "valvetrain-" + bank);
                G("valves-" + bank, enBank + " Valves and Springs", uzBank + " klapanlar va prujinalar",
                    "Poppet valves and the springs that close them.",
                    "Tarelkasimon klapanlar va ularni yopadigan prujinalar.",
                    "Valve steel", Vector3.up, "valvetrain-" + bank);
            }
            G("intake", "Intake and Supercharging", "Havo kiritish va nagnetatsiya",
                "Air filters, twin superchargers, charge hoses and the intake manifold that feed the cylinders.",
                "Havo filtrlari, ikki nagnetatel, havo shlanglari va silindrlarni ta’minlovchi kiritish kollektori.",
                "Aluminium", Vector3.up);
            G("superchargers", "Superchargers", "Nagnetatellar",
                "Two belt-driven superchargers that compress the intake air.",
                "Tasma orqali aylanadigan va kiruvchi havoni siquvchi ikki nagnetatel.",
                "Cast aluminium", new Vector3(-1f, 0.4f, 0f), "intake");
            G("air-filters", "Air Filters", "Havo filtrlari",
                "Cone filters with housings that clean the air before the superchargers.",
                "Nagnetatellardan oldin havoni tozalovchi konussimon filtrlar va ularning korpuslari.",
                "Aluminium and filter media", new Vector3(-1f, 0.2f, 0f), "intake");
            G("charge-hoses", "Charge-Air Hoses", "Siqilgan havo shlanglari",
                "Hoses that carry the compressed air from the superchargers to the manifold.",
                "Siqilgan havoni nagnetatellardan kollektorga olib boruvchi shlanglar.",
                "Reinforced rubber", Vector3.up, "intake");
            G("exhaust-manifolds", "Exhaust Manifolds", "Chiqarish kollektorlari",
                "Cast-iron manifolds that collect the exhaust gas of each bank.",
                "Har bir qatorning ishlatilgan gazlarini yig‘uvchi cho‘yan kollektorlar.",
                "Cast iron", new Vector3(1f, 0.3f, 0f));
            G("timing", "Timing and Accessory Drive", "Taqsimlash va yordamchi uzatma",
                "Crank and camshaft pulleys, the drive flange and the timing cover at the ends of the engine.",
                "Tirsakli val va taqsimlash vallari shkivlari, uzatma flanetsi va dvigatel chetidagi taqsimlash qopqog‘i.",
                "Steel and aluminium", new Vector3(-1f, 0f, 0f));
            G("lubrication", "Lubrication", "Moylash tizimi",
                "Oil sump under the crankcase: stores and returns the engine oil.",
                "Karter ostidagi moy idishi: dvigatel moyini saqlaydi va qaytaradi.",
                "Pressed steel", Vector3.down);

            // Assign the CAD nodes.
            var assignment = new Dictionary<Transform, GroupDef>();
            var children = v6.Cast<Transform>().ToList();
            foreach (Transform c in children)
            {
                string n = c.name.ToLowerInvariant();
                bool front = CentreZ(c, layout) > crankZ;
                string bank = front ? "front" : "rear";
                GroupDef g;
                if (n == "engine block") g = defs["block"];
                else if (n.StartsWith("engine cylinder head")) g = defs["head-" + bank];
                else if (n.StartsWith("valve cover")) g = defs["head-" + bank];
                else if (n == "crankshaft" || n.StartsWith("piston assembly")) g = defs["crank-train"];
                else if (c.name == "cam1" || c.name == "cam001") g = defs["timing"]; // lower case: crank ends; "Cam001" is a bearing shell
                else if (n.StartsWith("cam shaft") || n.StartsWith("cam0") || n == "cam2") g = defs["camshaft-" + bank];
                else if (n.StartsWith("rocker arm assembly")) g = defs["rockers-" + bank];
                else if (n.StartsWith("engine valve") || n.StartsWith("rocker arm backing spring")) g = defs["valves-" + bank];
                else if (n.StartsWith("intake manifold")) g = defs["intake"];
                else if (n.StartsWith("air turbo")) g = defs["superchargers"];
                else if (n.StartsWith("engine air filter")) g = defs["air-filters"];
                else if (n.StartsWith("horse")) g = defs["charge-hoses"];
                else if (n.StartsWith("exhaust manifold")) g = defs["exhaust-manifolds"];
                else if (n.StartsWith("engine belt wheels") || n == "engine side cover") g = defs["timing"];
                else if (n == "oil pan") g = defs["lubrication"];
                else g = defs["block"];
                assignment[c] = g;
            }

            // Build the group objects (parents first) in a fixed order and move the nodes in.
            var groupObjects = new Dictionary<GroupDef, Transform>();
            Transform Obj(GroupDef d)
            {
                if (groupObjects.TryGetValue(d, out Transform t)) return t;
                Transform parent = d.Parent != null ? Obj(d.Parent) : v6;
                var go = new GameObject(d.En);
                go.transform.SetParent(parent, false);
                string id = IdRoot + "." + d.Key;
                Tag(go, CarNodeKind.SubAssembly, id, new Txt(d.En, d.Uz, d.DescEn, d.DescUz), d.Material, Local(v6, parent, d.Explode, layout), false);
                groupObjects[d] = go.transform;
                return go.transform;
            }
            foreach (GroupDef d in defs.Values) Obj(d);
            foreach (Transform c in children.OrderBy(c => c.name, StringComparer.Ordinal))
                c.SetParent(Obj(assignment[c]), false);

            // The engine itself and every CAD node.
            Tag(v6.gameObject, CarNodeKind.Assembly, IdRoot,
                new Txt("V6 Engine", "V6 dvigatel",
                    "Supercharged 60-degree V6 mounted across the front bay; drives the wheels through the transaxle.",
                    "Old bo‘linmada ko‘ndalang o‘rnatilgan, nagnetatelli 60 darajali V6; g‘ildiraklarni transaksl orqali harakatlantiradi."),
                "Aluminium and steel", Vector3.zero, false);
            int parts = 0, groups = defs.Count;
            var pistons = children.Where(c => c.name.StartsWith("Piston assembly", StringComparison.Ordinal)).OrderBy(c => CentreX(c, layout)).ToList();
            var used = new HashSet<string>();
            foreach (GroupDef d in defs.Values)
            {
                Transform g = groupObjects[d];
                foreach (Transform node in g.Cast<Transform>().ToList())
                {
                    if (node.GetComponent<CarPartInfo>() != null && defs.Values.Any(x => groupObjects[x] == node)) continue;
                    int cylinder = pistons.IndexOf(node) + 1;
                    parts += TagCad(node, IdRoot + "." + d.Key, cylinder, used, layout);
                }
            }
            // Number the camshaft bearing caps and shells of each bank 1..4 along the camshaft.
            foreach (string bank in new[] { "front", "rear" })
            {
                Transform cam = groupObjects[defs["camshaft-" + bank]];
                foreach (string prefix in new[] { "Camshaft bearing cap", "Camshaft bearing shell" })
                {
                    var list = cam.Cast<Transform>().Select(t => t.GetComponent<CarPartInfo>())
                        .Where(i => i != null && i.nameEnglish.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(i => CentreX(i.transform, layout)).ToList();
                    for (int i = 0; i < list.Count; i++)
                    {
                        bool shell = prefix.EndsWith("shell", StringComparison.Ordinal);
                        list[i].nameEnglish = prefix + " " + (i + 1);
                        list[i].nameUzbek = (shell ? "Taqsimlash vali vkladishi " : "Taqsimlash vali podshipnik qopqog‘i ") + (i + 1);
                        list[i].id = IdRoot + ".camshaft-" + bank + "." + (shell ? "bearing-shell-" : "bearing-cap-") + (i + 1);
                    }
                }
            }
            // Draw cost: one submesh per renderer instead of the CAD export's 6,250 (V6MeshMerge).
            string meshes = V6MeshMerge.Apply(v6);
            return "regrouped into " + groups + " groups (" + dissolved + " old groups dissolved), " + parts + " CAD parts tagged; " + meshes;
        }

        // Tags a CAD node and its children; returns the number of parts (renderers) tagged.
        private static int TagCad(Transform node, string parentId, int cylinder, HashSet<string> used, CarLayout layout)
        {
            bool group = node.GetComponent<Renderer>() == null && node.childCount > 0;
            Txt text = Describe(node.name, cylinder, out string material, out string slug);
            string id = parentId + "." + slug;
            int k = 2;
            while (!used.Add(id)) id = parentId + "." + slug + "-" + k++;
            // The 12 valve springs (the CAD's "Rocker arm backing spring", 19,822 triangles each) are 38 % of the V6's
            // triangles: fine detail, so Low (Quest) leaves them out as it leaves out the internals' fasteners.
            bool fine = !group && node.name.Trim().StartsWith("Rocker arm backing spring", StringComparison.OrdinalIgnoreCase);
            Tag(node.gameObject, group ? CarNodeKind.SubAssembly : CarNodeKind.Part, id, text, material, Vector3.zero, fine);
            if (!group) return 1;
            int n = 0;
            foreach (Transform c in node) n += TagCad(c, id, 0, used, layout);
            return n;
        }

        private static readonly Regex Index = new Regex(@"(\d{3})$");

        // Readable English and Uzbek name, description, material and id slug for a CAD node name.
        private static Txt Describe(string cad, int cylinder, out string material, out string slug)
        {
            string n = cad.Trim().ToLowerInvariant();
            Match m = Index.Match(n.Replace(" ", ""));
            int index = m.Success ? int.Parse(m.Groups[1].Value) + 1 : 1;
            string num = " " + index;
            material = "Steel";
            if (n.StartsWith("piston assembly"))
            {
                material = "Aluminium and steel";
                slug = "cylinder-" + cylinder;
                return new Txt("Piston and Connecting Rod, Cylinder " + cylinder, cylinder + "-silindr porsheni va shatuni",
                    "Piston, gudgeon pin, connecting rod and cap of one cylinder.",
                    "Bitta silindrning porsheni, porshen barmog‘i, shatuni va shatun qopqog‘i.");
            }
            if (n.StartsWith("rocker arm assembly"))
            {
                slug = "rocker-" + index;
                return new Txt("Rocker Arm Assembly" + num, "Koromislo yig‘masi" + num,
                    "Rocker arm with its pivot pins, roller followers and adjusting screw.",
                    "O‘qlari, rolikli itargichlari va rostlash vinti bilan koromislo.");
            }
            if (n.StartsWith("intake manifold mirored005") || n == "intake manifold mirored005")
            {
                material = "Cast aluminium";
                slug = "manifold";
                return new Txt("Intake Manifold", "Kiritish kollektori",
                    "Plenum and runners that share the charge air between the six cylinders.",
                    "Siqilgan havoni oltita silindr orasida taqsimlovchi resiver va kanallar.");
            }
            if (n.StartsWith("engine air filter003") || n.StartsWith("engine air filter007"))
            {
                material = "Aluminium and filter media";
                slug = "filter-" + (n.EndsWith("003") ? "1" : "2");
                return new Txt("Air Filter Unit " + (n.EndsWith("003") ? "1" : "2"), (n.EndsWith("003") ? "1" : "2") + "-havo filtri bloki",
                    "Cone filter element with its cap and housing.", "Qopqog‘i va korpusi bilan konussimon filtr elementi.");
            }
            if (n.StartsWith("air turbo (supercharger)002") || n.StartsWith("air turbo (supercharger)005"))
            {
                material = "Cast aluminium";
                string i = n.EndsWith("002") ? "1" : "2";
                slug = "supercharger-" + i;
                return new Txt("Supercharger " + i, i + "-nagnetatel",
                    "Belt-driven compressor that raises the intake pressure.", "Kiruvchi havo bosimini oshiradigan, tasma bilan aylanadigan kompressor.");
            }
            if (n == "engine block") { material = "Cast aluminium"; slug = "block"; return T("Cylinder block", "Silindrlar bloki", "Cast-aluminium crankcase with six bores and the main-bearing webs.", "Oltita silindr va tub podshipnik tayanchlari bo‘lgan quyma alyuminiy karter."); }
            if (n.StartsWith("engine cylinder head")) { material = "Cast aluminium"; slug = "head"; return T("Cylinder head", "Silindrlar kallagi", "Closes the bores: combustion chambers, valve seats, intake and exhaust ports.", "Silindrlarni yopadi: yonish kameralari, klapan uyalari, kiritish va chiqarish kanallari."); }
            if (n.StartsWith("valve cover")) { material = "Cast aluminium, black"; slug = "valve-cover"; return T("Valve cover", "Klapan qopqog‘i", "Oil-tight cover over the camshaft and rockers.", "Taqsimlash vali va koromislolarni moy o‘tkazmaydigan qilib yopuvchi qopqoq."); }
            if (n == "crankshaft") { material = "Forged steel"; slug = "crankshaft"; return T("Crankshaft", "Tirsakli val", "Forged shaft with six crank pins: turns the rods' push into torque.", "Oltita bo‘yinli bolg‘alangan val: shatunlar kuchini aylantiruvchi momentga aylantiradi."); }
            if (n.StartsWith("piston head")) { material = "Forged aluminium"; slug = "piston"; return T("Piston", "Porshen", "Takes the combustion pressure and seals the bore with its rings.", "Yonish bosimini qabul qiladi va halqalari bilan silindrni zichlaydi."); }
            if (n.StartsWith("piston pin")) { material = "Hardened steel"; slug = "gudgeon-pin"; return T("Gudgeon pin", "Porshen barmog‘i", "Hollow pin that joins the piston to the connecting rod.", "Porshenni shatun bilan bog‘lovchi ichi bo‘sh barmoq."); }
            if (n.StartsWith("piston rod cap")) { material = "Forged steel"; slug = "rod-cap"; return T("Connecting-rod cap", "Shatun qopqog‘i", "Bolted cap that clamps the rod's big end around the crank pin.", "Shatunning katta kallagini tirsakli val bo‘yniga mahkamlovchi boltli qopqoq."); }
            if (n.StartsWith("piston rod")) { material = "Forged steel"; slug = "connecting-rod"; return T("Connecting rod", "Shatun", "Links the piston to the crankshaft.", "Porshenni tirsakli val bilan bog‘laydi."); }
            if (n.StartsWith("cam shaft retainer")) { slug = "bearing-cap" + num.Trim(); return T("Camshaft bearing cap" + num, "Taqsimlash vali podshipnik qopqog‘i" + num, "Holds the camshaft journal in the head.", "Taqsimlash vali bo‘ynini kallakda ushlab turadi."); }
            if (n.StartsWith("cam shaft")) { material = "Chilled cast iron"; slug = "camshaft"; return T("Camshaft", "Taqsimlash vali", "Shaft with one lobe per rocker; turns at half crank speed.", "Har bir koromislo uchun mushti bor val; tirsakli valdan ikki marta sekin aylanadi."); }
            if (cad.Trim() == "cam1") { slug = "crank-sprocket"; return T("Crank timing sprocket", "Tirsakli val yulduzchasi", "Drives the camshaft belt from the front end of the crankshaft.", "Tirsakli valning old uchidan taqsimlash tasmasini harakatlantiradi."); }
            if (cad.Trim() == "cam001") { slug = "drive-flange"; return T("Crankshaft drive flange", "Tirsakli val uzatma flanetsi", "Output end of the crank that carries the flywheel and the transaxle drive.", "Maxovik va transaksl uzatmasini ko‘taradigan tirsakli valning chiqish uchi."); }
            if (n.StartsWith("cam")) { material = "Bronze"; slug = "bearing-shell" + num.Trim(); return T("Camshaft bearing shell" + num, "Taqsimlash vali vkladishi" + num, "Plain bearing between the camshaft journal and its cap.", "Taqsimlash vali bo‘yni va qopqog‘i orasidagi sirpanish podshipnigi."); }
            if (n.StartsWith("engine belt wheels 2")) { slug = "crank-pulley"; return T("Crankshaft belt pulley", "Tirsakli val shkivi", "Drives the superchargers and accessories through the belt.", "Tasma orqali nagnetatellar va yordamchi agregatlarni aylantiradi."); }
            if (n.StartsWith("engine belt wheels")) { slug = "cam-pulley"; return T("Camshaft pulley", "Taqsimlash vali shkivi", "Driven by the timing belt; turns the camshaft.", "Taqsimlash tasmasidan aylanadi va taqsimlash valini buradi."); }
            if (n == "engine side cover") { material = "Cast aluminium"; slug = "timing-cover"; return T("Timing cover", "Taqsimlash mexanizmi qopqog‘i", "Closes the timing drive at the end of the block.", "Blok chetidagi taqsimlash uzatmasini yopadi."); }
            if (n == "oil pan") { material = "Pressed steel"; slug = "oil-sump"; return T("Oil sump", "Moy karteri", "Holds the engine oil under the crankcase; the pump draws from it.", "Karter ostida dvigatel moyini saqlaydi; nasos moyni shu yerdan oladi."); }
            if (n.StartsWith("rocker arm backing spring")) { material = "Spring steel"; slug = "valve-spring"; return T("Valve spring", "Klapan prujinasi", "Closes the valve and keeps the rocker on the cam.", "Klapanni yopadi va koromisloni mushtga bosib turadi."); }
            if (n.StartsWith("rocker arm roll")) { slug = "roller"; return T("Roller follower", "Rolikli itargich", "Needle-bearing roller that runs on the cam lobe.", "Musht ustida yuradigan ignasimon podshipnikli rolik."); }
            if (n.StartsWith("rocker arm pin")) { slug = "pin"; return T("Rocker pivot pin", "Koromislo o‘qi", "Pin on which the rocker or its roller turns.", "Koromislo yoki uning roligi aylanadigan o‘q."); }
            if (n.StartsWith("rocker arm")) { material = "Forged steel"; slug = "rocker"; return T("Rocker arm", "Koromislo", "Lever that turns the cam lift into valve opening.", "Musht ko‘tarilishini klapan ochilishiga aylantiruvchi richag."); }
            if (n.StartsWith("hex screw")) { slug = "adjuster-screw"; return T("Lash adjusting screw M8x1", "Zazor rostlash vinti M8x1", "Fine-thread screw that sets the valve clearance.", "Klapan zazorini o‘rnatuvchi mayda rezbali vint."); }
            if (n.StartsWith("engine valve")) { material = "Valve steel"; slug = "valve"; return T("Poppet valve", "Tarelkasimon klapan", "Opens and seals a port of the combustion chamber.", "Yonish kamerasi kanalini ochadi va zich yopadi."); }
            if (n.StartsWith("exhaust manifold")) { material = "Cast iron"; slug = "exhaust-manifold"; return T("Exhaust manifold", "Chiqarish kollektori", "Collects the exhaust gas of one bank into a single outlet.", "Bitta qator gazlarini bitta chiqishga yig‘adi."); }
            if (n.StartsWith("intake manifold")) { material = "Cast aluminium"; slug = "runner"; return T("Intake manifold section" + num, "Kiritish kollektori qismi" + num, "Part of the plenum and runners that feed the cylinders.", "Silindrlarni ta’minlovchi resiver va kanallar qismi."); }
            if (n == "engine air filter" || n == "engine air filter004") { material = "Filter media"; slug = "filter-element"; return T("Air filter element", "Havo filtri elementi", "Pleated element that traps dust from the intake air.", "Kiruvchi havodagi changni ushlab qoluvchi burmali element."); }
            if (n.StartsWith("engine air filter") && (n.EndsWith("001") || n.EndsWith("005"))) { material = "Polished aluminium"; slug = "filter-cap"; return T("Air filter cap", "Havo filtri qopqog‘i", "End cap of the cone filter.", "Konussimon filtrning yopqich qopqog‘i."); }
            if (n.StartsWith("engine air filter")) { material = "Aluminium"; slug = "filter-housing"; return T("Air filter housing", "Havo filtri korpusi", "Housing that joins the filter to the supercharger inlet.", "Filtrni nagnetatel kirishiga ulovchi korpus."); }
            if (n.StartsWith("air turbo") && n.EndsWith("001") || n.EndsWith("004") && n.StartsWith("air turbo")) { material = "Cast aluminium"; slug = "outlet"; return T("Supercharger outlet", "Nagnetatel chiqish patrubkasi", "Outlet housing that sends the compressed air to the hoses.", "Siqilgan havoni shlanglarga yo‘naltiruvchi chiqish korpusi."); }
            if (n.StartsWith("air turbo")) { material = "Cast aluminium"; slug = "compressor"; return T("Supercharger compressor", "Nagnetatel kompressori", "Rotor housing with the belt pulley.", "Tasma shkivi bilan rotor korpusi."); }
            if (n.StartsWith("horse 1")) { material = "Reinforced rubber"; slug = "hose-1"; return T("Charge-air hose", "Siqilgan havo shlangi", "Hose from the superchargers to the intake manifold.", "Nagnetatellardan kiritish kollektoriga boradigan shlang."); }
            if (n.StartsWith("horse")) { material = "Reinforced rubber"; slug = "hose-2"; return T("Charge-air hose, short", "Siqilgan havo shlangi, qisqa", "Short hose between the superchargers.", "Nagnetatellar orasidagi qisqa shlang."); }
            slug = Regex.Replace(n, "[^a-z0-9]+", "-").Trim('-');
            return T(cad.Trim(), cad.Trim(), "Engine part.", "Dvigatel qismi.");
        }

        private static Txt T(string en, string uz, string descEn, string descUz) => new Txt(en, uz, descEn, descUz);

        private static void Tag(GameObject go, CarNodeKind kind, string id, Txt text, string material, Vector3 explodeLocal, bool fine)
        {
            CarPartInfo info = go.GetComponent<CarPartInfo>();
            if (info == null) info = go.AddComponent<CarPartInfo>();
            info.kind = kind;
            info.id = id;
            info.nameEnglish = text.En;
            info.nameUzbek = text.Uz;
            info.descriptionEnglish = text.DescEn;
            info.descriptionUzbek = text.DescUz;
            info.system = "Powertrain";
            info.material = material ?? "";
            info.explodeDirection = explodeLocal;
            info.explodeDistance = 1f;
            info.fineDetail = fine;
        }

        // A car-space direction in the local space of a node's parent.
        private static Vector3 Local(Transform v6, Transform parent, Vector3 carDirection, CarLayout layout)
        {
            if (carDirection.sqrMagnitude < 1e-8f) return Vector3.zero;
            Vector3 world = layout.CarToWorld.MultiplyVector(carDirection.normalized);
            return parent.InverseTransformDirection(world).normalized;
        }

        // Moves the nodes of earlier groups back under the V6 root and removes the groups.
        private static int Flatten(Transform v6)
        {
            int dissolved = 0;
            bool again = true;
            while (again)
            {
                again = false;
                foreach (Transform c in v6.Cast<Transform>().ToList())
                {
                    CarPartInfo info = c.GetComponent<CarPartInfo>();
                    bool ours = info != null && c.GetComponent<Renderer>() == null && info.id != null &&
                                info.id.StartsWith(IdRoot + ".", StringComparison.Ordinal) && info.id.Count(ch => ch == '.') >= 2 &&
                                !IsCadGroup(c.name);
                    if (!ours) continue;
                    foreach (Transform g in c.Cast<Transform>().ToList()) g.SetParent(v6, false);
                    UnityEngine.Object.DestroyImmediate(c.gameObject);
                    dissolved++;
                    again = true;
                }
            }
            return dissolved;
        }

        private static bool IsCadGroup(string name)
        {
            string n = name.ToLowerInvariant();
            return n.StartsWith("piston assembly") || n.StartsWith("rocker arm assembly") || n.StartsWith("intake manifold") ||
                   n.StartsWith("engine air filter") || n.StartsWith("air turbo");
        }

        private static float CentreZ(Transform t, CarLayout layout) => Centre(t, layout).z;
        private static float CentreX(Transform t, CarLayout layout) => Centre(t, layout).x;

        private static Vector3 Centre(Transform t, CarLayout layout)
        {
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return layout.ToCar(t.position);
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return layout.ToCar(b.center);
        }
    }
}
#endif
