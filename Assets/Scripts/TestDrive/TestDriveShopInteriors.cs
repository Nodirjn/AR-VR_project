using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Walk-in shops. When the visitor comes near a shop, its ground floor opens up: the solid block behind the shop
/// window is replaced by a real room (walls, floor, ceiling with light panels and lamps), the windows turn to clear
/// glass, a paved landing level with the sidewalk leads to the door, and the glass door slides open as you reach it. Inside, a fitted-out shop
/// in 3D: a pharmacy or market with full shelves and coolers, a clothing store with rails and tables, a gun shop
/// with racked rifles and a glass counter, a café, bakery, bookshop, furniture showroom, surf shop or office, with a
/// shopkeeper behind the counter. Shops close again (back to the cheap window with a room painted into it) once the
/// visitor is far away, so only the nearest few are ever built.
/// </summary>
public sealed class TestDriveShopInteriors : MonoBehaviour
{
    private const float FloorY = 0.075f, CeilingY = 3.75f;

    private sealed class Open
    {
        public TestDriveCity.ShopBuilding shop;
        public GameObject built;
        public Material[] paneMaterials;
        public Vector3 doorClosed;
        public float doorT;
        public Material doorMaterial;
        public float roomLeft, roomRight, roomBack;
        public readonly List<Renderer> plinths = new List<Renderer>();
    }

    private readonly List<Open> open = new List<Open>();
    private readonly Dictionary<int, Material> palette = new Dictionary<int, Material>();
    private Material clearGlass, lightPanel;
    private float nextCheck;
    private int maxOpen;
    private static Mesh cube, cylinder, sphere, capsule;

    public static void Install(Transform parent)
    {
        if (TestDriveCity.Shops.Count == 0) return;
        var go = new GameObject("Walk-in Shops");
        go.transform.SetParent(parent, false);
        TestDriveShopInteriors s = go.AddComponent<TestDriveShopInteriors>();
        bool quest = QualitySettings.GetQualityLevel() <= 1 || Application.platform == RuntimePlatform.Android;
        s.maxOpen = quest ? 1 : 3;
    }

    private static Transform Viewer => Camera.main != null ? Camera.main.transform : null;

    private void Update()
    {
        Transform v = Viewer;
        if (v == null) return;
        Vector3 eye = v.position;
        // Doors slide open for whoever is close.
        foreach (Open o in open)
        {
            if (o.shop.door == null) continue;
            Vector3 doorWorld = o.shop.root.TransformPoint(new Vector3(o.shop.doorX, 1.4f, o.shop.depth * 0.5f));
            bool near = Vector3.Distance(new Vector3(eye.x, doorWorld.y, eye.z), doorWorld) < 3.2f;
            o.doorT = Mathf.MoveTowards(o.doorT, near ? 1f : 0f, Time.deltaTime / 0.7f);
            float s = Mathf.SmoothStep(0f, 1f, o.doorT);
            o.shop.door.localPosition = o.doorClosed + Vector3.right * (1.45f * s);
        }
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.4f;

        // Which shops should be open: the nearest few within 24 m of their front.
        var wanted = new List<TestDriveCity.ShopBuilding>();
        var distances = new List<float>();
        foreach (TestDriveCity.ShopBuilding shop in TestDriveCity.Shops)
        {
            if (shop.root == null) continue;
            Vector3 front = shop.root.TransformPoint(new Vector3(0f, 0f, shop.depth * 0.5f));
            Vector3 local = shop.root.InverseTransformPoint(eye);
            bool inside = Mathf.Abs(local.x) < shop.width * 0.5f && local.z < shop.depth * 0.5f + 0.3f && local.z > -shop.depth * 0.5f;
            float d = inside ? 0f : Vector3.Distance(new Vector3(eye.x, 0f, eye.z), new Vector3(front.x, 0f, front.z));
            if (d < 24f) { wanted.Add(shop); distances.Add(d); }
        }
        // Keep the nearest maxOpen.
        for (int i = 0; i < wanted.Count; i++)
            for (int j = i + 1; j < wanted.Count; j++)
                if (distances[j] < distances[i])
                {
                    var ts = wanted[i]; wanted[i] = wanted[j]; wanted[j] = ts;
                    float td = distances[i]; distances[i] = distances[j]; distances[j] = td;
                }
        if (wanted.Count > maxOpen) wanted.RemoveRange(maxOpen, wanted.Count - maxOpen);
        for (int i = open.Count - 1; i >= 0; i--)
        {
            Open o = open[i];
            if (wanted.Contains(o.shop)) continue;
            // Never close the shop the visitor is in or right at.
            Vector3 local = o.shop.root.InverseTransformPoint(eye);
            if (local.z < o.shop.depth * 0.5f + 6f && local.z > -o.shop.depth * 0.5f - 1f && Mathf.Abs(local.x) < o.shop.width * 0.5f + 4f) continue;
            Close(o);
            open.RemoveAt(i);
        }
        foreach (TestDriveCity.ShopBuilding shop in wanted)
            if (open.Find(o => o.shop == shop) == null && open.Count < maxOpen + 1) open.Add(OpenUp(shop));
    }

    // ------------------------------------------------------------------ open and close

    private Open OpenUp(TestDriveCity.ShopBuilding shop)
    {
        var o = new Open { shop = shop };
        Transform b = shop.root;
        var built = new GameObject("Shop Interior").transform;
        built.SetParent(b, false);
        o.built = built.gameObject;
        float w = shop.width, d = shop.depth, h = shop.height, front = d * 0.5f;

        if (shop.mass != null) shop.mass.enabled = false;
        if (shop.massCollider != null) shop.massCollider.enabled = false;
        if (shop.doorPull != null) shop.doorPull.enabled = false;
        // The shop floor is level with the sidewalk, like the showroom's: the raised plinth goes while the shop is open.
        foreach (Transform child in b)
            if (child.name.Contains("Plinth"))
            {
                Renderer pr = child.GetComponent<Renderer>();
                if (pr == null || !pr.enabled) continue;
                pr.enabled = false;
                o.plinths.Add(pr);
            }
        Material plinth = o.plinths.Count > 0 ? o.plinths[0].sharedMaterial : Mat(new Color(0.22f, 0.22f, 0.23f), 0f, 0.35f);

        // The room is a comfortable shop-sized space around the door (at most 16 x 9 m); the rest of the ground floor
        // stays solid behind its painted windows.
        float rw = Mathf.Min(w, shop.kind == "office" ? 11f : 16f), rd = Mathf.Min(d, shop.kind == "office" ? 8f : (shop.kind == "furniture" || shop.kind == "grocery" ? 10f : 9f));
        float cx = Mathf.Clamp(shop.doorX, -w * 0.5f + rw * 0.5f, w * 0.5f - rw * 0.5f);
        float left = cx - rw * 0.5f, right = cx + rw * 0.5f, back = front - rd;
        o.roomLeft = left; o.roomRight = right; o.roomBack = back;

        // The building above and the solid parts around the room.
        Solid(built, "Upper Floors", new Vector3(0f, (CeilingY + h) * 0.5f, 0f), new Vector3(w, h - CeilingY, d), shop.wall, true);
        float bt = back + 0.3f + front;
        Solid(built, "Ground Floor Back", new Vector3(0f, CeilingY * 0.5f, -front + bt * 0.5f), new Vector3(w, CeilingY, bt), shop.wall, true);
        float lw = left + 0.3f + w * 0.5f, rwid = w * 0.5f - right + 0.3f;
        Solid(built, "Ground Floor Side", new Vector3(-w * 0.5f + lw * 0.5f, CeilingY * 0.5f, (back + front) * 0.5f), new Vector3(lw, CeilingY, rd), shop.wall, true);
        Solid(built, "Ground Floor Side", new Vector3(w * 0.5f - rwid * 0.5f, CeilingY * 0.5f, (back + front) * 0.5f), new Vector3(rwid, CeilingY, rd), shop.wall, true);
        Solid(built, "Shop Front Header", new Vector3(0f, CeilingY - 0.3f, front - 0.12f), new Vector3(w, 0.6f, 0.24f), shop.wall, false);
        // The plinth back as a band round the building, open at the door, and a low riser under the windows inside.
        float doorL = shop.doorX - 0.8f, doorR = shop.doorX + 0.8f, ph = 0.48f;
        Solid(built, "Plinth Band", new Vector3(-w * 0.5f - 0.0625f, ph * 0.5f, 0f), new Vector3(0.125f, ph, d + 0.2f), plinth, false);
        Solid(built, "Plinth Band", new Vector3(w * 0.5f + 0.0625f, ph * 0.5f, 0f), new Vector3(0.125f, ph, d + 0.2f), plinth, false);
        Solid(built, "Plinth Band", new Vector3(0f, ph * 0.5f, -front - 0.05f), new Vector3(w + 0.25f, ph, 0.1f), plinth, false);
        float l0 = -w * 0.5f - 0.125f, r1 = w * 0.5f + 0.125f;
        Solid(built, "Plinth Band", new Vector3((l0 + doorL) * 0.5f, ph * 0.5f, front - 0.075f), new Vector3(doorL - l0, ph, 0.35f), plinth, true);
        Solid(built, "Plinth Band", new Vector3((doorR + r1) * 0.5f, ph * 0.5f, front - 0.075f), new Vector3(r1 - doorR, ph, 0.35f), plinth, true);
        // Invisible front, except the doorway.
        Wall(built, -w * 0.5f, doorL, front - 0.05f);
        Wall(built, doorR, w * 0.5f, front - 0.05f);
        Entrance(built, shop.doorX, front);

        // Clear glass in the room's windows; the others keep their painted rooms.
        o.paneMaterials = new Material[shop.panes.Count];
        for (int i = 0; i < shop.panes.Count; i++)
        {
            if (shop.panes[i] == null) continue;
            float px = b.InverseTransformPoint(shop.panes[i].bounds.center).x;
            if (px < left + 0.3f || px > right - 0.3f) continue;
            o.paneMaterials[i] = shop.panes[i].sharedMaterial;
            shop.panes[i].sharedMaterial = ClearGlass();
        }
        if (shop.door != null)
        {
            o.doorClosed = shop.door.localPosition;
            o.doorMaterial = shop.door.GetComponent<Renderer>().sharedMaterial;
            shop.door.GetComponent<Renderer>().sharedMaterial = ClearGlass();
        }

        // The room itself, built in its own frame (centred on the room, front at the building's front).
        var frame = new GameObject("Shop Room").transform;
        frame.SetParent(built, false);
        frame.localPosition = new Vector3(cx, 0f, front - rd * 0.5f);
        var c = new Combiner(this);
        Room(c, shop, rw, rd);
        seats.Clear();
        switch (shop.kind)
        {
            case "pharmacy": Market(c, shop, rw, rd, true); break;
            case "grocery": Market(c, shop, rw, rd, false); break;
            case "clothing": Clothing(c, rw, rd, false); break;
            case "surf": Clothing(c, rw, rd, true); break;
            case "guns": Guns(c, rw, rd); break;
            case "cafe": Cafe(c, rw, rd); break;
            case "bakery": Bakery(c, rw, rd); break;
            case "books": Books(c, rw, rd); break;
            case "furniture": Furniture(c, rw, rd); break;
            default: Office(c, rw, rd, shop.root.GetInstanceID()); break;
        }
        // An entrance mat inside the door.
        c.Box(c.M(new Color(0.16f, 0.16f, 0.17f), 0f, 0.05f), new Vector3(shop.doorX - cx, FloorY + 0.006f, rd * 0.5f - 0.75f), new Vector3(1.6f, 0.012f, 1.0f));
        c.Build(frame);
        Lights(frame, rw, rd, shop.kind == "guns" || shop.kind == "cafe");
        Workers(frame);
        if (shop.kind != "office") Keeper(frame, shop, rw, rd);
        return o;
    }

    /// <summary>A paved landing in front of the door, level with the sidewalk: large stone tiles with fine joints and
    /// a brushed steel threshold, the way the showroom's entrance is laid.</summary>
    private void Entrance(Transform parent, float doorX, float front)
    {
        var c = new Combiner(this);
        Material grout = c.M(new Color(0.3f, 0.3f, 0.3f), 0f, 0.2f);
        Material stoneA = c.M(new Color(0.74f, 0.72f, 0.68f), 0f, 0.55f), stoneB = c.M(new Color(0.66f, 0.65f, 0.62f), 0f, 0.5f);
        Material steel = c.M(new Color(0.75f, 0.76f, 0.78f), 1f, 0.7f);
        const float tile = 0.6f, joint = 0.008f;
        float x0 = doorX - 1.5f, z0 = front + 0.1f;
        c.Box(grout, new Vector3(doorX, 0.045f, z0 + 0.9f), new Vector3(3.0f, 0.02f, 1.8f));
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 3; j++)
                c.Box(((i + j) & 1) == 0 ? stoneA : stoneB, new Vector3(x0 + tile * (i + 0.5f), 0.062f, z0 + tile * (j + 0.5f)), new Vector3(tile - joint, 0.016f, tile - joint));
        c.Box(steel, new Vector3(doorX, 0.074f, front + 0.06f), new Vector3(1.75f, 0.01f, 0.12f));
        c.Build(parent);
    }

    private void Close(Open o)
    {
        TestDriveCity.ShopBuilding shop = o.shop;
        if (o.built != null) Destroy(o.built);
        if (shop.mass != null) shop.mass.enabled = true;
        if (shop.massCollider != null) shop.massCollider.enabled = true;
        if (shop.doorPull != null) shop.doorPull.enabled = true;
        foreach (Renderer pr in o.plinths) if (pr != null) pr.enabled = true;
        for (int i = 0; i < shop.panes.Count && i < o.paneMaterials.Length; i++)
            if (shop.panes[i] != null && o.paneMaterials[i] != null) shop.panes[i].sharedMaterial = o.paneMaterials[i];
        if (shop.door != null)
        {
            shop.door.localPosition = o.doorClosed;
            if (o.doorMaterial != null) shop.door.GetComponent<Renderer>().sharedMaterial = o.doorMaterial;
        }
    }

    private static void Wall(Transform parent, float x0, float x1, float z)
    {
        if (x1 - x0 < 0.05f) return;
        var go = new GameObject("Shop Front Collider");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3((x0 + x1) * 0.5f, (FloorY + CeilingY) * 0.5f, z);
        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.size = new Vector3(x1 - x0, CeilingY - FloorY, 0.1f);
    }

    private static GameObject Solid(Transform parent, string name, Vector3 local, Vector3 size, Material m, bool collider)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (!collider) Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    // ------------------------------------------------------------------ materials

    private Material Mat(Color c, float metallic, float smoothness)
    {
        int key = Mathf.RoundToInt(c.r * 255) | Mathf.RoundToInt(c.g * 255) << 8 | Mathf.RoundToInt(c.b * 255) << 16 ^ Mathf.RoundToInt(metallic * 10) << 24 ^ Mathf.RoundToInt(smoothness * 10) << 28;
        Material m;
        if (palette.TryGetValue(key, out m)) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Shop " + ColorUtility.ToHtmlStringRGB(c) };
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        palette[key] = m;
        return m;
    }

    private Material Emissive(Color c, float strength)
    {
        Material m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Shop Light" };
        m.SetColor("_BaseColor", c);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * strength);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        return m;
    }

    private Material ClearGlass()
    {
        if (clearGlass != null) return clearGlass;
        Material m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Shop Clear Glass" };
        m.SetColor("_BaseColor", new Color(0.75f, 0.82f, 0.85f, 0.12f));
        m.SetFloat("_Smoothness", 0.96f);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 1f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        clearGlass = m;
        return m;
    }

    // ------------------------------------------------------------------ geometry batching

    /// <summary>Collects boxes, cylinders and the like per material, then builds one mesh per material.</summary>
    private sealed class Combiner
    {
        private readonly TestDriveShopInteriors owner;
        private readonly Dictionary<Material, List<CombineInstance>> parts = new Dictionary<Material, List<CombineInstance>>();
        public readonly List<Bounds> solids = new List<Bounds>();

        public Combiner(TestDriveShopInteriors owner) { this.owner = owner; }

        public void Add(Mesh mesh, Material m, Vector3 pos, Vector3 size, float yaw = 0f, float pitch = 0f, float roll = 0f)
        {
            List<CombineInstance> list;
            if (!parts.TryGetValue(m, out list)) parts[m] = list = new List<CombineInstance>();
            list.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(pos, Quaternion.Euler(pitch, yaw, roll), size) });
        }

        public void Box(Material m, Vector3 pos, Vector3 size, float yaw = 0f) => Add(Cube, m, pos, size, yaw);
        public void Solid(Material m, Vector3 pos, Vector3 size) { Add(Cube, m, pos, size); solids.Add(new Bounds(pos, size)); }
        public void Cyl(Material m, Vector3 pos, float radius, float height, float pitch = 0f, float roll = 0f) => Add(Cylinder, m, pos, new Vector3(radius * 2f, height * 0.5f, radius * 2f), 0f, pitch, roll);
        public void Ball(Material m, Vector3 pos, Vector3 size) => Add(Sphere, m, pos, size);
        public void Pill(Material m, Vector3 pos, Vector3 size, float yaw = 0f, float pitch = 0f, float roll = 0f) => Add(Capsule, m, pos, size, yaw, pitch, roll);
        public Material M(Color c, float metal = 0f, float smooth = 0.35f) => owner.Mat(c, metal, smooth);
        public Material Glow(Color c, float s) => owner.Emissive(c, s);

        public void Build(Transform parent)
        {
            foreach (var pair in parts)
            {
                // Split very large lists so no mesh passes the 16-bit index limit by much.
                List<CombineInstance> all = pair.Value;
                for (int start = 0; start < all.Count; start += 1500)
                {
                    int count = Mathf.Min(1500, all.Count - start);
                    var mesh = new Mesh { name = "Shop " + pair.Key.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    mesh.CombineMeshes(all.GetRange(start, count).ToArray(), true, true);
                    var go = new GameObject("Shop Fittings");
                    go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    MeshRenderer r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = pair.Key;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            foreach (Bounds b in solids)
            {
                var go = new GameObject("Fitting Collider");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = b.center;
                go.AddComponent<BoxCollider>().size = b.size;
            }
        }
    }

    private static Mesh Primitive(PrimitiveType type, ref Mesh cache)
    {
        if (cache != null) return cache;
        GameObject g = GameObject.CreatePrimitive(type);
        cache = g.GetComponent<MeshFilter>().sharedMesh;
        Destroy(g);
        return cache;
    }

    private static Mesh Cube => Primitive(PrimitiveType.Cube, ref cube);
    private static Mesh Cylinder => Primitive(PrimitiveType.Cylinder, ref cylinder);
    private static Mesh Sphere => Primitive(PrimitiveType.Sphere, ref sphere);
    private static Mesh Capsule => Primitive(PrimitiveType.Capsule, ref capsule);

    // ------------------------------------------------------------------ the room

    private void Room(Combiner c, TestDriveCity.ShopBuilding shop, float w, float d)
    {
        string k = shop.kind;
        bool wood = k == "clothing" || k == "cafe" || k == "books" || k == "furniture" || k == "surf" || k == "bakery";
        Color floor = wood ? new Color(0.45f, 0.31f, 0.2f) : k == "guns" ? new Color(0.28f, 0.27f, 0.26f) : new Color(0.82f, 0.82f, 0.8f);
        Color walls = k == "guns" ? new Color(0.32f, 0.25f, 0.19f) : k == "cafe" ? new Color(0.72f, 0.55f, 0.42f) : k == "books" ? new Color(0.5f, 0.38f, 0.28f) : new Color(0.93f, 0.92f, 0.89f);
        float iw = w - 0.6f, id = d - 0.6f;
        // Floor: planks or tiles as strips, so the grain and joints read.
        if (wood)
            for (float x = -iw * 0.5f; x < iw * 0.5f - 0.01f; x += 0.2f)
            {
                float t = Mathf.PerlinNoise(x * 3.1f, 0.5f);
                c.Box(c.M(floor * (0.85f + 0.3f * t), 0f, 0.45f), new Vector3(x + 0.1f, FloorY - 0.02f, 0f), new Vector3(0.195f, 0.04f, id));
            }
        else
            for (float x = -iw * 0.5f; x < iw * 0.5f - 0.01f; x += 0.6f)
                for (float z = -id * 0.5f; z < id * 0.5f - 0.01f; z += 0.6f)
                {
                    bool odd = (Mathf.RoundToInt(x / 0.6f) + Mathf.RoundToInt(z / 0.6f)) % 2 == 0;
                    c.Box(c.M(floor * (odd ? 1f : 0.93f), 0f, 0.7f), new Vector3(x + 0.3f, FloorY - 0.02f, z + 0.3f), new Vector3(0.595f, 0.04f, 0.595f));
                }
        c.Solid(c.M(floor * 0.5f), new Vector3(0f, FloorY - 0.06f, 0f), new Vector3(iw, 0.04f, id));
        // Wall linings, skirting, ceiling with light panels.
        c.Box(c.M(walls, 0f, 0.2f), new Vector3(0f, (FloorY + CeilingY) * 0.5f, -d * 0.5f + 0.31f), new Vector3(iw, CeilingY - FloorY, 0.02f));
        c.Box(c.M(walls, 0f, 0.2f), new Vector3(-w * 0.5f + 0.31f, (FloorY + CeilingY) * 0.5f, 0f), new Vector3(0.02f, CeilingY - FloorY, id));
        c.Box(c.M(walls, 0f, 0.2f), new Vector3(w * 0.5f - 0.31f, (FloorY + CeilingY) * 0.5f, 0f), new Vector3(0.02f, CeilingY - FloorY, id));
        Material skirting = c.M(new Color(0.15f, 0.15f, 0.15f), 0f, 0.4f);
        c.Box(skirting, new Vector3(0f, FloorY + 0.06f, -d * 0.5f + 0.33f), new Vector3(iw, 0.12f, 0.03f));
        c.Box(c.M(new Color(0.95f, 0.95f, 0.94f), 0f, 0.1f), new Vector3(0f, CeilingY - 0.02f, 0f), new Vector3(iw, 0.04f, id));
        Material panel = c.Glow(k == "guns" || k == "cafe" || k == "books" ? new Color(1f, 0.85f, 0.65f) : new Color(1f, 0.97f, 0.92f), 2.2f);
        for (float x = -iw * 0.5f + 1.5f; x < iw * 0.5f - 1f; x += 3f)
            for (float z = -id * 0.5f + 1.5f; z < id * 0.5f - 1f; z += 3f)
                c.Box(panel, new Vector3(x, CeilingY - 0.045f, z), new Vector3(1.2f, 0.02f, 0.6f));
    }

    private void Lights(Transform parent, float w, float d, bool warm)
    {
        Color colour = warm ? new Color(1f, 0.82f, 0.62f) : new Color(1f, 0.96f, 0.9f);
        int n = Mathf.Clamp(Mathf.RoundToInt(w / 7f), 1, 3);
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Shop Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(-w * 0.5f + w * (i + 0.5f) / n, CeilingY - 0.4f, -d * 0.1f);
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = colour;
            l.intensity = 2.2f;
            l.range = Mathf.Max(7f, d * 0.7f);
            l.shadows = LightShadows.None;
        }
    }

    private void Keeper(Transform parent, TestDriveCity.ShopBuilding shop, float w, float d)
    {
        // The shopkeeper stands behind the counter by the back wall, facing the door.
        ExteriorPedestrianAgent walker = FindAnyObjectByType<ExteriorPedestrianAgent>(FindObjectsInactive.Include);
        if (walker == null) return;
        Vector3 local = new Vector3(w * 0.25f, FloorY, -d * 0.5f + 1.35f);
        Vector3 world = parent.TransformPoint(local);
        TestDriveIdlePerson p = TestDriveIdlePerson.Create(walker.gameObject, parent, world, Quaternion.LookRotation(parent.forward, Vector3.up), TestDriveIdlePerson.Mode.Stand, 0.4f);
        if (p != null) p.Stand();
    }

    // ------------------------------------------------------------------ fit-outs

    private static readonly Color[] ProductColours =
    {
        new Color(0.85f, 0.15f, 0.12f), new Color(0.95f, 0.75f, 0.1f), new Color(0.1f, 0.45f, 0.8f), new Color(0.15f, 0.6f, 0.3f),
        new Color(0.95f, 0.95f, 0.93f), new Color(0.95f, 0.5f, 0.1f), new Color(0.55f, 0.2f, 0.6f), new Color(0.2f, 0.2f, 0.22f),
        new Color(0.6f, 0.4f, 0.25f), new Color(0.9f, 0.6f, 0.7f), new Color(0.3f, 0.75f, 0.85f), new Color(0.75f, 0.85f, 0.25f)
    };
    private static readonly Color[] PharmacyColours =
    {
        new Color(0.97f, 0.97f, 0.96f), new Color(0.92f, 0.95f, 1f), new Color(0.2f, 0.55f, 0.85f), new Color(0.15f, 0.65f, 0.45f),
        new Color(0.95f, 0.45f, 0.4f), new Color(0.98f, 0.85f, 0.3f), new Color(0.6f, 0.45f, 0.8f), new Color(0.85f, 0.9f, 0.92f)
    };

    /// <summary>Products along a shelf: boxes, bottles and jars of varied sizes, a few gaps, the odd one turned.</summary>
    private static void Stock(Combiner c, Vector3 from, Vector3 along, float length, float depth, Vector3 outward, Color[] colours, System.Random r, bool small)
    {
        float t = 0.03f;
        Vector3 across = Vector3.Cross(Vector3.up, along).normalized;
        if (Vector3.Dot(across, outward) < 0f) across = -across;
        while (t < length - 0.06f)
        {
            float wdt = small ? 0.05f + (float)r.NextDouble() * 0.07f : 0.07f + (float)r.NextDouble() * 0.14f;
            float h = small ? 0.08f + (float)r.NextDouble() * 0.12f : 0.12f + (float)r.NextDouble() * 0.22f;
            if (r.NextDouble() < 0.06) { t += wdt; continue; }
            Material m = c.M(colours[r.Next(colours.Length)], 0f, 0.45f);
            int facings = 1 + r.Next(3);
            for (int f = 0; f < facings && t < length - 0.05f; f++)
            {
                Vector3 p = from + along * (t + wdt * 0.5f) + Vector3.up * (h * 0.5f) + across * 0.0f;
                if (r.NextDouble() < 0.25)
                    c.Cyl(m, p, wdt * 0.42f, h);                                   // bottle / jar
                else
                    c.Box(m, p, new Vector3(Mathf.Abs(along.x) > 0.5f ? wdt : depth * 0.8f, h, Mathf.Abs(along.x) > 0.5f ? depth * 0.8f : wdt));
                t += wdt + 0.008f;
            }
            t += 0.01f;
        }
    }

    private void Market(Combiner c, TestDriveCity.ShopBuilding shop, float w, float d, bool pharmacy)
    {
        var r = new System.Random(shop.root.GetInstanceID());
        Color[] colours = pharmacy ? PharmacyColours : ProductColours;
        Material metal = c.M(new Color(0.85f, 0.86f, 0.87f), 0.6f, 0.5f), white = c.M(new Color(0.94f, 0.94f, 0.93f), 0f, 0.4f);
        float x0 = -w * 0.5f + 2.2f, x1 = w * 0.5f - 2.2f;
        float zBack = -d * 0.5f + 0.75f, zFront = d * 0.5f - 3.2f;
        // Wall shelving along the back.
        for (int lvl = 0; lvl < 5; lvl++)
        {
            float y = FloorY + 0.15f + lvl * 0.42f;
            c.Box(white, new Vector3(0f, y, zBack), new Vector3(w - 1f, 0.03f, 0.45f));
            Stock(c, new Vector3(-w * 0.5f + 0.6f, y + 0.015f, zBack), Vector3.right, w - 1.2f, 0.4f, Vector3.forward, colours, r, pharmacy);
        }
        c.Box(white, new Vector3(0f, FloorY + 1.1f, zBack - 0.24f), new Vector3(w - 1f, 2.2f, 0.03f));
        // Gondolas: double-sided island shelving.
        float len = Mathf.Max(2f, zFront - (zBack + 1.4f));
        float zc = (zFront + zBack + 1.4f) * 0.5f;
        for (float x = x0; x <= x1; x += 2.3f)
        {
            c.Solid(white, new Vector3(x, FloorY + 0.08f, zc), new Vector3(0.95f, 0.16f, len));
            c.Box(metal, new Vector3(x, FloorY + 0.85f, zc), new Vector3(0.05f, 1.55f, len));
            for (int side = -1; side <= 1; side += 2)
                for (int lvl = 0; lvl < 4; lvl++)
                {
                    float y = FloorY + 0.3f + lvl * 0.38f;
                    c.Box(metal, new Vector3(x + side * 0.24f, y, zc), new Vector3(0.42f, 0.025f, len));
                    Stock(c, new Vector3(x + side * 0.24f, y + 0.013f, zc - len * 0.5f), Vector3.forward, len, 0.38f, Vector3.right * side, colours, r, pharmacy);
                }
            c.Box(c.M(pharmacy ? new Color(0.1f, 0.55f, 0.35f) : new Color(0.85f, 0.15f, 0.12f), 0f, 0.4f), new Vector3(x, FloorY + 1.72f, zc + len * 0.5f), new Vector3(0.9f, 0.22f, 0.03f));
        }
        // Coolers with lit glass doors down one side (markets), a dispensary counter (pharmacies).
        if (!pharmacy)
            for (float z = zBack + 1f; z < zFront; z += 0.85f)
            {
                c.Solid(c.M(new Color(0.15f, 0.15f, 0.16f), 0.5f, 0.5f), new Vector3(w * 0.5f - 0.75f, FloorY + 1.0f, z), new Vector3(0.7f, 2f, 0.8f));
                c.Box(c.Glow(new Color(0.85f, 0.92f, 1f), 0.8f), new Vector3(w * 0.5f - 1.105f, FloorY + 1.05f, z), new Vector3(0.01f, 1.7f, 0.7f));
                for (int lvl = 0; lvl < 4; lvl++)
                    for (int k = 0; k < 5; k++)
                        c.Cyl(c.M(ProductColours[(lvl * 5 + k + (int)(z * 3)) % ProductColours.Length], 0.1f, 0.7f), new Vector3(w * 0.5f - 1.0f, FloorY + 0.4f + lvl * 0.42f, z - 0.28f + k * 0.14f), 0.035f, 0.25f);
            }
        else
        {
            c.Solid(white, new Vector3(-w * 0.25f, FloorY + 0.5f, zBack + 1.3f), new Vector3(3.2f, 1f, 0.6f));
            c.Box(c.Glow(new Color(0.1f, 0.75f, 0.4f), 2f), new Vector3(0f, FloorY + 2.6f, zBack - 0.2f), new Vector3(0.6f, 0.18f, 0.02f));
            c.Box(c.Glow(new Color(0.1f, 0.75f, 0.4f), 2f), new Vector3(0f, FloorY + 2.6f, zBack - 0.2f), new Vector3(0.18f, 0.6f, 0.02f));
        }
        Checkout(c, w, d);
    }

    private void Checkout(Combiner c, float w, float d)
    {
        Vector3 at = new Vector3(w * 0.5f - 2.2f, FloorY, d * 0.5f - 1.8f);
        c.Solid(c.M(new Color(0.25f, 0.25f, 0.27f), 0.2f, 0.4f), at + new Vector3(0f, 0.45f, 0f), new Vector3(1.6f, 0.9f, 0.7f));
        c.Box(c.M(new Color(0.1f, 0.1f, 0.1f), 0.3f, 0.8f), at + new Vector3(0f, 0.91f, 0f), new Vector3(1.62f, 0.03f, 0.72f));
        c.Box(c.M(new Color(0.1f, 0.1f, 0.12f), 0.3f, 0.5f), at + new Vector3(0.4f, 1.05f, -0.1f), new Vector3(0.35f, 0.25f, 0.05f));
        c.Box(c.Glow(new Color(0.4f, 0.7f, 1f), 1.2f), at + new Vector3(0.4f, 1.05f, -0.07f), new Vector3(0.3f, 0.2f, 0.01f));
    }

    private void Clothing(Combiner c, float w, float d, bool surf)
    {
        var r = new System.Random((int)(w * 100 + d));
        Color[] cloth = surf
            ? new[] { new Color(0.1f, 0.5f, 0.8f), new Color(0.95f, 0.6f, 0.15f), new Color(0.1f, 0.1f, 0.1f), new Color(0.9f, 0.3f, 0.4f), new Color(0.2f, 0.7f, 0.6f) }
            : new[] { new Color(0.08f, 0.1f, 0.18f), new Color(0.6f, 0.08f, 0.1f), new Color(0.9f, 0.9f, 0.87f), new Color(0.25f, 0.33f, 0.25f), new Color(0.72f, 0.58f, 0.4f), new Color(0.06f, 0.06f, 0.07f), new Color(0.4f, 0.3f, 0.5f), new Color(0.75f, 0.45f, 0.25f) };
        Material rail = c.M(new Color(0.75f, 0.75f, 0.73f), 0.9f, 0.7f);
        // Wall rails with garments down both sides.
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * (w * 0.5f - 0.75f);
            c.Box(rail, new Vector3(x, FloorY + 1.75f, 0f), new Vector3(0.03f, 0.03f, d - 4f));
            for (float z = -d * 0.5f + 2.2f; z < d * 0.5f - 2.2f; z += 0.07f + (float)r.NextDouble() * 0.03f)
            {
                Material m = c.M(cloth[r.Next(cloth.Length)], 0f, 0.15f);
                float len = 0.65f + (float)r.NextDouble() * 0.4f;
                c.Box(m, new Vector3(x, FloorY + 1.72f - len * 0.5f, z), new Vector3(0.48f, len, 0.035f), (float)(r.NextDouble() - 0.5) * 6f);
            }
            // Shelves of folded stock above.
            c.Box(c.M(new Color(0.9f, 0.88f, 0.84f), 0f, 0.3f), new Vector3(x, FloorY + 2.3f, 0f), new Vector3(0.45f, 0.03f, d - 4f));
            for (float z = -d * 0.5f + 2.3f; z < d * 0.5f - 2.3f; z += 0.4f)
                for (int k = 0; k < 4; k++)
                    c.Box(c.M(cloth[Mathf.Abs((int)(z * 7f) + k) % cloth.Length], 0f, 0.15f), new Vector3(x, FloorY + 2.34f + k * 0.05f, z), new Vector3(0.3f, 0.045f, 0.32f));
        }
        // Display tables with folded piles, a couple of mannequins.
        for (float z = -d * 0.25f; z <= d * 0.15f; z += 2.6f)
        {
            Material wood = c.M(new Color(0.5f, 0.35f, 0.22f), 0f, 0.45f);
            c.Solid(wood, new Vector3(0f, FloorY + 0.4f, z), new Vector3(1.8f, 0.8f, 1f));
            for (float x = -0.6f; x <= 0.61f; x += 0.4f)
                for (int k = 0; k < 4 + r.Next(3); k++)
                    c.Box(c.M(cloth[r.Next(cloth.Length)], 0f, 0.15f), new Vector3(x, FloorY + 0.83f + k * 0.045f, z + (float)(r.NextDouble() - 0.5) * 0.1f), new Vector3(0.32f, 0.04f, 0.3f));
        }
        Material skin = c.M(new Color(0.92f, 0.9f, 0.86f), 0f, 0.6f);
        foreach (float x in new[] { -w * 0.2f, w * 0.2f })
        {
            Vector3 at = new Vector3(x, FloorY, d * 0.5f - 1.4f);
            c.Cyl(c.M(new Color(0.2f, 0.2f, 0.2f), 0.8f, 0.6f), at + new Vector3(0f, 0.02f, 0f), 0.22f, 0.04f);
            c.Cyl(c.M(new Color(0.2f, 0.2f, 0.2f), 0.8f, 0.6f), at + new Vector3(0f, 0.5f, 0f), 0.015f, 1f);
            c.Pill(c.M(cloth[r.Next(cloth.Length)], 0f, 0.2f), at + new Vector3(0f, 1.3f, 0f), new Vector3(0.38f, 0.42f, 0.24f));
            c.Ball(skin, at + new Vector3(0f, 1.82f, 0f), new Vector3(0.2f, 0.25f, 0.21f));
        }
        if (surf)
            for (int i = 0; i < 6; i++)
                c.Pill(c.M(cloth[i % cloth.Length], 0f, 0.7f), new Vector3(-w * 0.3f + i * 0.5f, FloorY + 1.2f, -d * 0.5f + 0.5f), new Vector3(0.5f, 1.05f, 0.08f), 0f, 0f, 0f);
        else
            c.Box(c.M(new Color(0.85f, 0.88f, 0.9f), 1f, 0.95f), new Vector3(0f, FloorY + 1.3f, -d * 0.5f + 0.34f), new Vector3(1.2f, 2f, 0.02f));
        Checkout(c, w, d);
    }

    private void Guns(Combiner c, float w, float d)
    {
        var r = new System.Random(7);
        Material panel = c.M(new Color(0.3f, 0.22f, 0.15f), 0f, 0.35f), peg = c.M(new Color(0.55f, 0.47f, 0.36f), 0f, 0.2f);
        Material black = c.M(new Color(0.06f, 0.06f, 0.065f), 0.7f, 0.45f), wood = c.M(new Color(0.38f, 0.22f, 0.12f), 0f, 0.5f), tan = c.M(new Color(0.55f, 0.48f, 0.35f), 0.1f, 0.3f);
        float zb = -d * 0.5f + 0.36f;
        c.Box(peg, new Vector3(0f, FloorY + 1.75f, zb), new Vector3(w - 1.2f, 2.3f, 0.03f));
        // Long guns racked on the back wall, five rows.
        for (int row = 0; row < 5; row++)
        {
            float y = FloorY + 0.85f + row * 0.42f;
            for (float x = -w * 0.5f + 1f; x < w * 0.5f - 1.8f; x += 1.35f)
            {
                Material stock = r.NextDouble() < 0.5 ? wood : r.NextDouble() < 0.5 ? black : tan;
                float z = zb + 0.06f;
                c.Box(black, new Vector3(x + 0.35f, y, z), new Vector3(0.75f, 0.025f, 0.025f));              // barrel
                c.Box(black, new Vector3(x - 0.15f, y - 0.01f, z), new Vector3(0.32f, 0.07f, 0.04f));           // receiver
                c.Box(black, new Vector3(x - 0.08f, y - 0.07f, z), new Vector3(0.06f, 0.12f, 0.035f), 0f);       // magazine/grip
                c.Box(stock, new Vector3(x - 0.48f, y - 0.03f, z), new Vector3(0.36f, 0.09f, 0.04f));           // stock
                c.Box(stock, new Vector3(x + 0.1f, y - 0.025f, z), new Vector3(0.28f, 0.045f, 0.045f));         // fore-end
                if (r.NextDouble() < 0.5) c.Cyl(black, new Vector3(x - 0.12f, y + 0.06f, z), 0.022f, 0.3f, 0f, 90f); // scope
            }
        }
        // The long glass counter with handguns under the glass.
        float zc = -d * 0.5f + 2.1f;
        c.Solid(c.M(new Color(0.15f, 0.12f, 0.1f), 0f, 0.4f), new Vector3(-0.6f, FloorY + 0.4f, zc), new Vector3(w - 3.2f, 0.8f, 0.7f));
        c.Box(c.M(new Color(0.8f, 0.78f, 0.72f), 0f, 0.4f), new Vector3(-0.6f, FloorY + 0.82f, zc), new Vector3(w - 3.3f, 0.02f, 0.6f));
        for (float x = -w * 0.5f + 1.6f; x < w * 0.5f - 3f; x += 0.45f)
        {
            c.Box(black, new Vector3(x, FloorY + 0.85f, zc), new Vector3(0.2f, 0.025f, 0.05f), 20f);
            c.Box(black, new Vector3(x - 0.06f, FloorY + 0.85f, zc + 0.05f), new Vector3(0.05f, 0.025f, 0.1f), 20f);
        }
        c.Box(ClearGlass(), new Vector3(-0.6f, FloorY + 0.95f, zc), new Vector3(w - 3.2f, 0.24f, 0.7f));
        // Ammunition shelves down the side.
        Color[] ammo = { new Color(0.2f, 0.4f, 0.2f), new Color(0.75f, 0.15f, 0.1f), new Color(0.9f, 0.55f, 0.1f), new Color(0.15f, 0.25f, 0.5f), new Color(0.85f, 0.85f, 0.8f) };
        float sx = w * 0.5f - 0.7f;
        for (int lvl = 0; lvl < 5; lvl++)
        {
            float y = FloorY + 0.3f + lvl * 0.4f;
            c.Box(panel, new Vector3(sx, y, 0f), new Vector3(0.5f, 0.03f, d - 3f));
            Stock(c, new Vector3(sx, y + 0.015f, -d * 0.5f + 1.6f), Vector3.forward, d - 3.2f, 0.4f, Vector3.left, ammo, r, true);
        }
        // A flag over the counter and a target poster.
        c.Box(c.M(new Color(0.7f, 0.1f, 0.1f), 0f, 0.2f), new Vector3(-w * 0.25f, CeilingY - 0.5f, zb + 0.02f), new Vector3(1.5f, 0.6f, 0.01f));
        c.Box(c.M(new Color(0.1f, 0.15f, 0.4f), 0f, 0.2f), new Vector3(-w * 0.25f - 0.45f, CeilingY - 0.37f, zb + 0.025f), new Vector3(0.6f, 0.34f, 0.01f));
        Checkout(c, w, d);
    }

    private void Cafe(Combiner c, float w, float d)
    {
        var r = new System.Random(3);
        Material wood = c.M(new Color(0.42f, 0.28f, 0.17f), 0f, 0.5f), dark = c.M(new Color(0.12f, 0.1f, 0.09f), 0f, 0.4f), chrome = c.M(new Color(0.85f, 0.86f, 0.88f), 1f, 0.85f);
        Material marble = c.M(new Color(0.9f, 0.89f, 0.86f), 0f, 0.8f);
        float zc = -d * 0.5f + 1.8f;
        c.Solid(wood, new Vector3(0f, FloorY + 0.5f, zc), new Vector3(w * 0.6f, 1f, 0.7f));
        c.Box(marble, new Vector3(0f, FloorY + 1.02f, zc), new Vector3(w * 0.6f + 0.05f, 0.04f, 0.75f));
        c.Box(chrome, new Vector3(-w * 0.15f, FloorY + 1.25f, zc - 0.1f), new Vector3(0.7f, 0.42f, 0.45f));    // espresso machine
        c.Box(dark, new Vector3(w * 0.15f, FloorY + 1.25f, zc), new Vector3(0.9f, 0.4f, 0.55f));
        c.Box(ClearGlass(), new Vector3(w * 0.15f, FloorY + 1.25f, zc), new Vector3(0.92f, 0.42f, 0.57f));
        for (int i = 0; i < 8; i++)
            c.Ball(c.M(new Color(0.7f + 0.1f * (i % 3), 0.45f, 0.2f), 0f, 0.4f), new Vector3(w * 0.15f - 0.35f + i * 0.1f, FloorY + 1.1f, zc + (i % 2) * 0.15f), new Vector3(0.09f, 0.05f, 0.09f));
        // Menu board and shelves of cups behind the counter.
        c.Box(dark, new Vector3(0f, FloorY + 2.3f, -d * 0.5f + 0.35f), new Vector3(2.4f, 1f, 0.04f));
        for (int line = 0; line < 6; line++)
            c.Box(c.M(new Color(0.92f, 0.92f, 0.88f), 0f, 0.1f), new Vector3(-0.4f, FloorY + 2.65f - line * 0.13f, -d * 0.5f + 0.375f), new Vector3(1.2f + (line % 3) * 0.2f, 0.025f, 0.005f));
        // Tables and chairs.
        for (float x = -w * 0.5f + 1.5f; x < w * 0.5f - 1.2f; x += 1.9f)
            for (float z = -d * 0.5f + 4.2f; z < d * 0.5f - 1.4f; z += 2.1f)
            {
                c.Cyl(marble, new Vector3(x, FloorY + 0.74f, z), 0.38f, 0.03f);
                c.Cyl(dark, new Vector3(x, FloorY + 0.37f, z), 0.04f, 0.72f);
                c.Cyl(dark, new Vector3(x, FloorY + 0.01f, z), 0.22f, 0.02f);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 chair = new Vector3(x + s * 0.6f, FloorY, z);
                    c.Box(wood, chair + new Vector3(0f, 0.45f, 0f), new Vector3(0.42f, 0.04f, 0.42f));
                    c.Box(wood, chair + new Vector3(s * 0.2f, 0.7f, 0f), new Vector3(0.03f, 0.5f, 0.4f));
                    foreach (Vector2 leg in new[] { new Vector2(-0.18f, -0.18f), new Vector2(0.18f, -0.18f), new Vector2(-0.18f, 0.18f), new Vector2(0.18f, 0.18f) })
                        c.Box(dark, chair + new Vector3(leg.x, 0.22f, leg.y), new Vector3(0.03f, 0.44f, 0.03f));
                }
                if (r.NextDouble() < 0.6) c.Cyl(c.M(Color.white, 0f, 0.8f), new Vector3(x + 0.1f, FloorY + 0.8f, z), 0.045f, 0.09f);
            }
        // Pendant lamps.
        for (float x = -w * 0.3f; x <= w * 0.31f; x += w * 0.3f)
        {
            c.Cyl(dark, new Vector3(x, CeilingY - 0.5f, zc + 0.6f), 0.005f, 1f);
            c.Ball(c.Glow(new Color(1f, 0.8f, 0.5f), 3f), new Vector3(x, CeilingY - 1.0f, zc + 0.6f), new Vector3(0.22f, 0.22f, 0.22f));
        }
    }

    private void Bakery(Combiner c, float w, float d)
    {
        var r = new System.Random(5);
        Material wood = c.M(new Color(0.55f, 0.38f, 0.22f), 0f, 0.45f), white = c.M(new Color(0.95f, 0.94f, 0.9f), 0f, 0.4f);
        float zb = -d * 0.5f + 0.6f;
        for (int lvl = 0; lvl < 4; lvl++)
        {
            float y = FloorY + 0.7f + lvl * 0.45f;
            c.Box(wood, new Vector3(0f, y, zb), new Vector3(w - 1.4f, 0.04f, 0.5f));
            for (float x = -w * 0.5f + 0.9f; x < w * 0.5f - 0.9f; x += 0.28f)
            {
                Color crust = new Color(0.62f + (float)r.NextDouble() * 0.15f, 0.38f + (float)r.NextDouble() * 0.1f, 0.15f);
                c.Pill(c.M(crust, 0f, 0.35f), new Vector3(x, y + 0.08f, zb + (float)(r.NextDouble() - 0.5) * 0.15f), new Vector3(0.14f, 0.12f, 0.14f), 0f, 0f, 90f);
            }
        }
        float zc = -d * 0.5f + 2.2f;
        c.Solid(white, new Vector3(0f, FloorY + 0.45f, zc), new Vector3(w * 0.6f, 0.9f, 0.8f));
        c.Box(ClearGlass(), new Vector3(0f, FloorY + 1.15f, zc + 0.1f), new Vector3(w * 0.6f, 0.5f, 0.6f));
        for (float x = -w * 0.27f; x < w * 0.27f; x += 0.22f)
            c.Ball(c.M(r.NextDouble() < 0.5 ? new Color(0.85f, 0.6f, 0.3f) : new Color(0.45f, 0.25f, 0.12f), 0f, 0.4f), new Vector3(x, FloorY + 0.96f, zc + 0.1f), new Vector3(0.14f, 0.07f, 0.14f));
        Checkout(c, w, d);
    }

    private void Books(Combiner c, float w, float d)
    {
        var r = new System.Random(11);
        Material wood = c.M(new Color(0.3f, 0.2f, 0.12f), 0f, 0.45f);
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * (w * 0.5f - 0.6f);
            c.Box(wood, new Vector3(x, FloorY + 1.2f, 0f), new Vector3(0.45f, 2.4f, d - 2.4f));
            for (int lvl = 0; lvl < 6; lvl++)
            {
                float y = FloorY + 0.15f + lvl * 0.38f;
                for (float z = -d * 0.5f + 1.3f; z < d * 0.5f - 1.3f; z += 0.035f + (float)r.NextDouble() * 0.02f)
                {
                    float h = 0.2f + (float)r.NextDouble() * 0.1f;
                    Color col = new Color((float)r.NextDouble() * 0.7f + 0.1f, (float)r.NextDouble() * 0.5f + 0.1f, (float)r.NextDouble() * 0.5f + 0.1f);
                    c.Box(c.M(new Color(Mathf.Round(col.r * 5) / 5, Mathf.Round(col.g * 5) / 5, Mathf.Round(col.b * 5) / 5), 0f, 0.3f), new Vector3(x - side * 0.24f, y + h * 0.5f, z), new Vector3(0.18f, h, 0.03f));
                }
            }
        }
        for (float z = -d * 0.2f; z < d * 0.2f; z += 2.4f)
        {
            c.Solid(wood, new Vector3(0f, FloorY + 0.42f, z), new Vector3(2f, 0.84f, 1.1f));
            for (int k = 0; k < 12; k++)
                c.Box(c.M(ProductColours[k % ProductColours.Length], 0f, 0.3f), new Vector3(-0.8f + (k % 6) * 0.32f, FloorY + 0.88f + (k / 6) * 0.04f, z - 0.2f + (k / 6) * 0.4f), new Vector3(0.22f, 0.035f, 0.3f), (float)(r.NextDouble() - 0.5) * 20f);
        }
        Checkout(c, w, d);
    }

    private void Furniture(Combiner c, float w, float d)
    {
        var r = new System.Random(13);
        Color[] fabrics = { new Color(0.55f, 0.5f, 0.45f), new Color(0.2f, 0.25f, 0.32f), new Color(0.45f, 0.3f, 0.2f), new Color(0.35f, 0.42f, 0.35f) };
        Material leg = c.M(new Color(0.15f, 0.12f, 0.1f), 0f, 0.4f);
        for (float x = -w * 0.5f + 2.2f; x < w * 0.5f - 2f; x += 4.2f)
            for (float z = -d * 0.5f + 2.2f; z < d * 0.5f - 2.5f; z += 4f)
            {
                Material f = c.M(fabrics[r.Next(fabrics.Length)], 0f, 0.15f);
                c.Box(c.M(new Color(0.75f, 0.7f, 0.62f), 0f, 0.1f), new Vector3(x, FloorY + 0.005f, z + 0.8f), new Vector3(3f, 0.01f, 2.2f));   // rug
                c.Solid(f, new Vector3(x, FloorY + 0.28f, z), new Vector3(2.2f, 0.42f, 0.9f));                                                     // sofa seat
                c.Box(f, new Vector3(x, FloorY + 0.62f, z - 0.38f), new Vector3(2.2f, 0.5f, 0.2f));
                c.Box(f, new Vector3(x - 1.05f, FloorY + 0.45f, z), new Vector3(0.18f, 0.35f, 0.9f));
                c.Box(f, new Vector3(x + 1.05f, FloorY + 0.45f, z), new Vector3(0.18f, 0.35f, 0.9f));
                c.Box(c.M(new Color(0.85f, 0.8f, 0.7f), 0f, 0.2f), new Vector3(x - 0.5f, FloorY + 0.55f, z - 0.15f), new Vector3(0.4f, 0.35f, 0.1f), 10f); // cushion
                c.Box(c.M(new Color(0.45f, 0.3f, 0.18f), 0f, 0.6f), new Vector3(x, FloorY + 0.4f, z + 1.2f), new Vector3(1.1f, 0.05f, 0.6f));            // coffee table
                foreach (Vector2 l in new[] { new Vector2(-0.5f, -0.25f), new Vector2(0.5f, -0.25f), new Vector2(-0.5f, 0.25f), new Vector2(0.5f, 0.25f) })
                    c.Box(leg, new Vector3(x + l.x, FloorY + 0.19f, z + 1.2f + l.y), new Vector3(0.04f, 0.38f, 0.04f));
                c.Cyl(leg, new Vector3(x + 1.5f, FloorY + 0.75f, z - 0.2f), 0.015f, 1.5f);
                c.Cyl(c.Glow(new Color(1f, 0.85f, 0.65f), 1.5f), new Vector3(x + 1.5f, FloorY + 1.55f, z - 0.2f), 0.2f, 0.3f);
            }
        Checkout(c, w, d);
    }

    /// <summary>A small open-plan office: one bench of desks facing each other (four or six), a few colleagues at
    /// them working, a meeting table by the window, a whiteboard, a printer and plants.</summary>
    private void Office(Combiner c, float w, float d, int seed)
    {
        var r = new System.Random(seed);
        Material desk = c.M(new Color(0.86f, 0.85f, 0.83f), 0f, 0.45f), frameM = c.M(new Color(0.18f, 0.18f, 0.19f), 0.6f, 0.5f);
        Material dark = c.M(new Color(0.08f, 0.08f, 0.09f), 0.2f, 0.55f), fabric = c.M(new Color(0.22f, 0.25f, 0.3f), 0f, 0.15f);
        Material screen = c.Glow(new Color(0.55f, 0.68f, 0.85f), 0.9f), screenB = c.Glow(new Color(0.85f, 0.87f, 0.9f), 0.8f);
        Material felt = c.M(new Color(0.45f, 0.5f, 0.47f), 0f, 0.1f), plant = c.M(new Color(0.16f, 0.38f, 0.17f), 0f, 0.25f), pot = c.M(new Color(0.85f, 0.84f, 0.8f), 0f, 0.4f);
        int columns = w >= 9f ? 3 : 2;
        float pitch = 1.45f, zc = -d * 0.5f + 3.4f;
        float x0 = -(columns - 1) * pitch * 0.5f - 1.2f;
        // The low screen between the two rows.
        c.Box(felt, new Vector3(x0 + (columns - 1) * pitch * 0.5f, FloorY + 0.98f, zc), new Vector3(columns * pitch, 0.42f, 0.03f));
        int occupied = 0, wanted = 2 + r.Next(columns == 3 ? 4 : 2);    // two to five at work
        for (int row = 0; row < 2; row++)
        {
            float side = row == 0 ? -1f : 1f;                              // row 0 sits on the back side, facing the door
            float dz = zc + side * 0.4f;
            for (int i = 0; i < columns; i++)
            {
                float x = x0 + i * pitch;
                c.Solid(desk, new Vector3(x, FloorY + 0.74f, dz), new Vector3(1.4f, 0.03f, 0.78f));
                c.Box(frameM, new Vector3(x - 0.66f, FloorY + 0.37f, dz), new Vector3(0.04f, 0.72f, 0.7f));
                c.Box(frameM, new Vector3(x + 0.66f, FloorY + 0.37f, dz), new Vector3(0.04f, 0.72f, 0.7f));
                // A monitor on an arm at the far edge, keyboard and mouse, a mug or papers.
                float far = dz - side * 0.27f, near = dz + side * 0.12f;
                c.Box(dark, new Vector3(x, FloorY + 0.86f, far), new Vector3(0.06f, 0.22f, 0.06f));
                c.Box(dark, new Vector3(x, FloorY + 1.07f, far), new Vector3(0.6f, 0.36f, 0.025f));
                c.Box(r.NextDouble() < 0.5 ? screen : screenB, new Vector3(x, FloorY + 1.07f, far + side * 0.0135f), new Vector3(0.57f, 0.33f, 0.003f));
                c.Box(dark, new Vector3(x, FloorY + 0.763f, near), new Vector3(0.44f, 0.015f, 0.14f));
                c.Box(dark, new Vector3(x + 0.33f, FloorY + 0.765f, near), new Vector3(0.06f, 0.02f, 0.1f));
                if (r.NextDouble() < 0.6) c.Cyl(c.M(ProductColours[r.Next(ProductColours.Length)], 0f, 0.5f), new Vector3(x - 0.45f, FloorY + 0.8f, near - side * 0.1f), 0.04f, 0.1f);
                else c.Box(c.M(new Color(0.96f, 0.96f, 0.95f), 0f, 0.1f), new Vector3(x - 0.42f, FloorY + 0.758f, near - side * 0.1f), new Vector3(0.21f, 0.006f, 0.297f), (float)r.NextDouble() * 20f - 10f);
                // The chair, pulled out a little or turned when nobody's in it.
                bool sat = occupied < wanted && r.NextDouble() < 0.75;
                float chairZ = dz + side * (sat ? 0.62f : 0.85f), yaw = sat ? 0f : (float)r.NextDouble() * 50f - 25f;
                Chair(c, new Vector3(x, FloorY, chairZ), (row == 0 ? 0f : 180f) + yaw, fabric, frameM);
                if (sat)
                {
                    occupied++;
                    seats.Add(new Seat
                    {
                        hips = new Vector3(x, FloorY + 0.54f, chairZ - side * 0.04f),
                        yaw = row == 0 ? 0f : 180f,
                        keyboard = new Vector3(x, FloorY + 0.8f, near)
                    });
                }
            }
        }
        // A meeting table by the window.
        Vector3 meet = new Vector3(w * 0.5f - 1.7f, FloorY, d * 0.5f - 2.2f);
        c.Cyl(desk, meet + Vector3.up * 0.74f, 0.55f, 0.03f);
        c.Cyl(frameM, meet + Vector3.up * 0.37f, 0.04f, 0.72f);
        c.Cyl(frameM, meet + Vector3.up * 0.015f, 0.3f, 0.03f);
        for (int k = 0; k < 3; k++)
        {
            float a = k * 120f + 30f;
            Vector3 off = Quaternion.Euler(0f, a, 0f) * Vector3.forward * 0.85f;
            Chair(c, meet + off, a + 180f, fabric, frameM);
        }
        // Whiteboard, printer, storage and plants.
        float zb = -d * 0.5f + 0.33f;
        c.Box(c.M(new Color(0.97f, 0.97f, 0.97f), 0f, 0.85f), new Vector3(-w * 0.2f, FloorY + 1.55f, zb + 0.02f), new Vector3(1.8f, 1.0f, 0.02f));
        c.Box(frameM, new Vector3(-w * 0.2f, FloorY + 1.03f, zb + 0.05f), new Vector3(1.8f, 0.03f, 0.06f));
        for (int k = 0; k < 4; k++)
            c.Box(c.M(new[] { new Color(0.15f, 0.3f, 0.7f), new Color(0.75f, 0.15f, 0.12f), new Color(0.1f, 0.1f, 0.1f) }[k % 3], 0f, 0.3f),
                new Vector3(-w * 0.2f - 0.6f + k * 0.3f + (float)r.NextDouble() * 0.1f, FloorY + 1.6f + (float)r.NextDouble() * 0.25f, zb + 0.032f), new Vector3(0.18f + (float)r.NextDouble() * 0.2f, 0.012f, 0.002f));
        c.Solid(desk, new Vector3(w * 0.25f, FloorY + 0.38f, zb + 0.25f), new Vector3(1.6f, 0.76f, 0.45f));
        c.Solid(c.M(new Color(0.9f, 0.9f, 0.9f), 0f, 0.4f), new Vector3(w * 0.25f + 0.3f, FloorY + 0.9f, zb + 0.25f), new Vector3(0.5f, 0.28f, 0.42f));
        c.Box(dark, new Vector3(w * 0.25f + 0.3f, FloorY + 1.045f, zb + 0.25f), new Vector3(0.44f, 0.012f, 0.36f));
        foreach (Vector3 at in new[] { new Vector3(-w * 0.5f + 0.75f, FloorY, d * 0.5f - 1.1f), new Vector3(-w * 0.5f + 0.75f, FloorY, -d * 0.5f + 0.8f), new Vector3(w * 0.5f - 0.75f, FloorY, -d * 0.5f + 0.8f) })
        {
            c.Cyl(pot, at + Vector3.up * 0.22f, 0.22f, 0.44f);
            c.Ball(plant, at + Vector3.up * 0.75f, new Vector3(0.6f, 0.7f, 0.6f));
            c.Ball(plant, at + new Vector3(0.12f, 1.05f, 0.05f), new Vector3(0.4f, 0.5f, 0.4f));
        }
    }

    private static void Chair(Combiner c, Vector3 at, float yaw, Material fabric, Material frame)
    {
        Quaternion q = Quaternion.Euler(0f, yaw, 0f);
        c.Box(fabric, at + Vector3.up * 0.47f, new Vector3(0.47f, 0.07f, 0.46f), yaw);
        c.Box(fabric, at + q * new Vector3(0f, 0.8f, -0.24f), new Vector3(0.45f, 0.52f, 0.05f), yaw);
        c.Cyl(frame, at + Vector3.up * 0.25f, 0.025f, 0.4f);
        c.Box(frame, at + Vector3.up * 0.05f, new Vector3(0.55f, 0.03f, 0.05f), yaw + 30f);
        c.Box(frame, at + Vector3.up * 0.05f, new Vector3(0.55f, 0.03f, 0.05f), yaw - 30f);
        c.Box(frame, at + Vector3.up * 0.05f, new Vector3(0.05f, 0.03f, 0.55f), yaw);
    }

    private struct Seat { public Vector3 hips, keyboard; public float yaw; }
    private readonly List<Seat> seats = new List<Seat>();

    /// <summary>The people at the office desks, sat down and typing.</summary>
    private void Workers(Transform frame)
    {
        if (seats.Count == 0) return;
        ExteriorPedestrianAgent[] walkers = FindObjectsByType<ExteriorPedestrianAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (walkers.Length == 0) return;
        for (int i = 0; i < seats.Count; i++)
        {
            Seat s = seats[i];
            Vector3 hips = frame.TransformPoint(s.hips);
            Quaternion facing = frame.rotation * Quaternion.Euler(0f, s.yaw, 0f);
            GameObject template = walkers[Mathf.Abs(i * 5 + frame.GetInstanceID()) % walkers.Length].gameObject;
            TestDriveIdlePerson p = TestDriveIdlePerson.Create(template, frame, hips - Vector3.up * 0.9f, facing, TestDriveIdlePerson.Mode.Sit, 0.15f + i * 0.17f);
            if (p == null) continue;
            p.Sit(hips, frame.TransformPoint(new Vector3(0f, FloorY, 0f)).y);
            p.Work(frame.TransformPoint(s.keyboard));
        }
    }
}
