using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;

/// <summary>
/// The city, made bigger and more lived-in at load:
/// - New streets in the scene's own materials: Ocean Drive along a new beach (z = -118), the avenue and the south street
///   carried out to x = ±320, two outer north-south streets at x = ±320 and the x = ±96 streets run down to the sea.
/// - New blocks along them (about seventy on a PC, a third of that on the headset), each with a shop on the ground
///   floor: clothing, gun shops, cafés, markets, bakeries, bookshops, furniture, pharmacies, surf shops, offices.
/// - Every ground-floor shop window in the city, old and new, shows a real room behind the glass (interior mapping,
///   Resources/TestDriveShopWindow.shader with the shop atlases in Resources/TestDrive/Shops).
/// - A beach: sand sloping into an animated sea (Resources/TestDriveWater.shader) with surf, umbrellas, loungers,
///   lifeguard towers and people; gulls over the water and pigeons on the forecourt (<see cref="TestDriveBirds"/>).
/// - Litter bins, recycling bins and dumpsters along the sidewalks and beside the buildings.
/// - Lanterns and trees along the new streets, a traffic loop round the new streets, walkers on the promenade.
/// Also the registry other parts read: <see cref="Streets"/>, <see cref="Junctions"/> and <see cref="ShopDoors"/>.
/// </summary>
public static class TestDriveCity
{
    public struct Street
    {
        public bool eastWest;   // along x (true) or along z
        public float centre;    // z of an east-west street, x of a north-south one
        public float from, to;  // extent along the street
    }

    public struct ShopDoor
    {
        public Vector3 position; // on the sidewalk in front of the door
        public Vector3 inward;   // into the shop
        public string kind;
    }

    public static readonly List<Street> Streets = new List<Street>();
    public static readonly List<Vector2> Junctions = new List<Vector2>();
    public static readonly List<ShopDoor> ShopDoors = new List<ShopDoor>();

    /// <summary>A building with a shop on its ground floor, for <see cref="TestDriveShopInteriors"/> to open up.</summary>
    public sealed class ShopBuilding
    {
        public Transform root;
        public float width, depth, height;
        public string kind;
        public Renderer mass;
        public Collider massCollider;
        public Material wall;
        public readonly List<Renderer> panes = new List<Renderer>();
        public Transform door;
        public Renderer doorPull;
        public float doorX;
    }

    public static readonly List<ShopBuilding> Shops = new List<ShopBuilding>();

    public const float OceanZ = -118f, OuterX = 320f, ShoreZ = -147f;
    /// <summary>The walkers' lines on a sidewalk, from the street's centre line; bins, trees and lanterns keep off them.</summary>
    public const float WalkInner = 11.6f, WalkOuter = 13.0f;
    private static readonly List<Vector3[]> walkLines = new List<Vector3[]>();
    private const float HalfRoad = 6.5f, SidewalkOffset = 11.8f;

    private static bool installed, quest;
    private static Material asphalt, curb, sidewalk, line, grass, sand, wallCream, wallBlue, wallWarm, wallGraphite, plinth, metal, glass, signFace, gold;
    private static readonly Dictionary<string, Material> shopMaterials = new Dictionary<string, Material>();
    private static Shader shopShader;
    private static TMP_FontAsset font;
    private static Transform root;

    private static readonly string[] Kinds = { "clothing", "guns", "cafe", "grocery", "bakery", "books", "furniture", "office", "pharmacy", "surf" };
    private static readonly Dictionary<string, string[]> SignNames = new Dictionary<string, string[]>
    {
        { "clothing", new[] { "URBAN THREADS", "LUXE FASHION", "DENIM & CO", "STREETWEAR" } },
        { "guns", new[] { "ARMS & AMMO", "LIBERTY GUNS", "SPORTSMAN ARMS" } },
        { "cafe", new[] { "OCEAN CAFE", "ESPRESSO BAR", "SEAFOOD GRILL", "DINER 66" } },
        { "grocery", new[] { "CORNER MARKET", "24/7 MART", "FRESH FOODS" } },
        { "bakery", new[] { "SUNRISE BAKERY", "DONUTS & COFFEE" } },
        { "books", new[] { "BOOKS & COMICS", "RECORD STORE" } },
        { "furniture", new[] { "HOME & LIVING", "DESIGN FURNITURE" } },
        { "office", new[] { "REAL ESTATE", "INSURANCE", "LAW OFFICE", "BANK" } },
        { "pharmacy", new[] { "CITY PHARMACY", "DRUGSTORE" } },
        { "surf", new[] { "SURF SHOP", "BEACH RENTALS", "SWIMWEAR" } },
    };

    public static void Install()
    {
        if (installed) return;
        installed = true;
        quest = QualitySettings.GetQualityLevel() <= 1 || Application.platform == RuntimePlatform.Android;
        if (GameObject.Find("Rolling City Ground") == null && GameObject.Find("East West Asphalt") == null) return; // no exterior in this scene
        root = new GameObject("City Expansion").transform;
        FindMaterials();
        RegisterExistingStreets();
        ShopWindowsInExistingBuildings();

        BuildStreets();
        BuildGround();
        BuildBlocks();
        BuildBeach();
        BuildParks();
        BuildLots();
        Physics.SyncTransforms();
        CollectWalkLines();
        StreetFurniture();
        Batch(root);
        // Sky and sun for the sea and the shop glass before the time of day takes over.
        Shader.SetGlobalColor("_TestDriveSkyTop", RenderSettings.ambientSkyColor.maxColorComponent > 0.05f ? RenderSettings.ambientSkyColor * 1.3f : new Color(0.45f, 0.6f, 0.8f));
        Shader.SetGlobalColor("_TestDriveSkyHorizon", RenderSettings.fogColor);
        if (RenderSettings.sun != null)
        {
            Shader.SetGlobalVector("_TestDriveSunDir", -RenderSettings.sun.transform.forward);
            Shader.SetGlobalColor("_TestDriveSunColor", RenderSettings.sun.color * RenderSettings.sun.intensity);
        }
        Shader.SetGlobalFloat("_TestDriveShopLight", 0.8f);
        TestDriveBirds.Install(root, quest);
        TestDriveShopInteriors.Install(root);
        Debug.Log("[Test Drive] City expansion: " + Streets.Count + " streets, " + ShopDoors.Count + " shop doors.");
    }

    // ------------------------------------------------------------------ materials

    private static Material Find(string objectName, Color fallback)
    {
        GameObject go = GameObject.Find(objectName);
        Renderer r = go != null ? go.GetComponent<Renderer>() : null;
        if (r == null)
            foreach (Renderer any in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (any.name == objectName) { r = any; break; }
        if (r != null && r.sharedMaterial != null) return r.sharedMaterial;
        return Lit(fallback, 0f, 0.4f);
    }

    private static Material Lit(Color colour, float metallic, float smoothness)
    {
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(s) { name = "City " + ColorUtility.ToHtmlStringRGB(colour) };
        m.SetColor("_BaseColor", colour);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        return m;
    }

    private static void FindMaterials()
    {
        asphalt = Find("East West Asphalt", new Color(0.12f, 0.12f, 0.13f));
        curb = Find("Raised Granite Curb", new Color(0.7f, 0.68f, 0.63f));
        sidewalk = Find("Concrete Sidewalk", new Color(0.6f, 0.6f, 0.58f));
        line = Find("Broken Center Line", new Color(0.92f, 0.92f, 0.88f));
        grass = Find("Rolling City Ground", new Color(0.34f, 0.39f, 0.29f));
        metal = Find("Tapered Galvanized Pole", new Color(0.5f, 0.52f, 0.55f));
        glass = Find("Upper Floor Window", new Color(0.3f, 0.38f, 0.45f));
        signFace = Find("Storefront Header Band", new Color(0.08f, 0.08f, 0.09f));
        plinth = Find("Ground Floor Plinth", new Color(0.18f, 0.18f, 0.2f));
        gold = Find("Entrance Door Pull", new Color(0.8f, 0.65f, 0.35f));
        // The four facade finishes, from the buildings that wear them.
        wallCream = FacadeOf("Aurelle Quarter Market", new Color(0.82f, 0.78f, 0.7f));
        wallBlue = FacadeOf("NOVA Pharmacy", new Color(0.3f, 0.38f, 0.48f));
        wallWarm = FacadeOf("South Cafe", new Color(0.62f, 0.45f, 0.35f));
        wallGraphite = FacadeOf("North Offices", new Color(0.25f, 0.26f, 0.28f));
        sand = Lit(new Color(0.84f, 0.76f, 0.6f), 0f, 0.12f);
        shopShader = Resources.Load<Shader>("TestDriveShopWindow");
        font = TMP_Settings.defaultFontAsset;
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
    }

    private static Material FacadeOf(string building, Color fallback)
    {
        GameObject b = GameObject.Find(building);
        if (b != null)
        {
            Transform mass = b.transform.Find("Facade Mass");
            if (mass != null && mass.GetComponent<Renderer>() != null) return mass.GetComponent<Renderer>().sharedMaterial;
        }
        return Lit(fallback, 0f, 0.3f);
    }

    private static Material ShopMaterial(string kind)
    {
        Material m;
        if (shopMaterials.TryGetValue(kind, out m)) return m;
        Texture2D atlas = Resources.Load<Texture2D>("TestDrive/Shops/shop_" + kind);
        if (shopShader == null || !shopShader.isSupported || atlas == null) m = glass;
        else
        {
            m = new Material(shopShader) { name = "Shop Window " + kind, mainTexture = atlas };
            m.SetFloat("_Depth", kind == "guns" || kind == "grocery" ? 5f : 4f);
        }
        shopMaterials[kind] = m;
        return m;
    }

    // ------------------------------------------------------------------ shops in the existing buildings

    private static string KindFor(string buildingName)
    {
        string n = buildingName.ToLowerInvariant();
        if (n.Contains("market street")) return "surf";
        if (n.Contains("market")) return "grocery";
        if (n.Contains("pharmacy") || n.Contains("clinic") || n.Contains("courtyard")) return "pharmacy";
        if (n.Contains("cafe")) return "cafe";
        if (n.Contains("bakery") || n.Contains("storefront")) return "bakery";
        if (n.Contains("retail")) return "books";
        if (n.Contains("showroom block")) return "furniture";
        if (n.Contains("office") || n.Contains("hotel")) return n.Contains("hotel") ? "cafe" : "office";
        if (n.Contains("south apartments")) return "guns";
        return "clothing";
    }

    private static void ShopWindowsInExistingBuildings()
    {
        var done = new HashSet<Transform>();
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r.name != "Storefront Reflective Glazing" || r.transform.parent == null) continue;
            Transform building = r.transform.parent;
            string kind = KindFor(building.name);
            // A fresh pane in the same place: the scene's own pane is static-batched (its mesh merged into world
            // space), which the room-behind-the-glass shader cannot work with.
            GameObject pane = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(pane.GetComponent<Collider>());
            pane.name = "Shop Window";
            pane.transform.SetParent(building, true);
            pane.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
            Vector3 lossy = r.transform.lossyScale, parentScale = building.lossyScale;
            pane.transform.localScale = new Vector3(lossy.x / Mathf.Max(1e-4f, parentScale.x), lossy.y / Mathf.Max(1e-4f, parentScale.y), lossy.z / Mathf.Max(1e-4f, parentScale.z));
            Renderer paneRenderer = pane.GetComponent<Renderer>();
            paneRenderer.sharedMaterial = ShopMaterial(kind);
            paneRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.enabled = false;
            ShopBuilding shop = Shops.Find(x => x.root == building);
            if (shop == null)
            {
                Transform massT = building.Find("Facade Mass");
                if (massT != null)
                {
                    shop = new ShopBuilding { root = building, kind = kind, mass = massT.GetComponent<Renderer>(), massCollider = massT.GetComponent<Collider>() };
                    shop.width = massT.localScale.x; shop.height = massT.localScale.y; shop.depth = massT.localScale.z;
                    shop.wall = shop.mass != null ? shop.mass.sharedMaterial : wallCream;
                    Transform pull = building.Find("Entrance Door Pull");
                    if (pull != null) shop.doorPull = pull.GetComponent<Renderer>();
                    Transform oldDoor = building.Find("Shop Entrance Glass");
                    if (oldDoor != null)
                    {
                        // Its own door (the scene's is static-batched and could not slide open).
                        GameObject door = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Object.Destroy(door.GetComponent<Collider>());
                        door.name = "Shop Door";
                        door.transform.SetParent(building, false);
                        door.transform.localPosition = oldDoor.localPosition;
                        door.transform.localRotation = oldDoor.localRotation;
                        door.transform.localScale = oldDoor.localScale;
                        door.GetComponent<Renderer>().sharedMaterial = ShopMaterial(kind);
                        if (oldDoor.GetComponent<Renderer>() != null) oldDoor.GetComponent<Renderer>().enabled = false;
                        shop.door = door.transform;
                        shop.doorX = oldDoor.localPosition.x;
                    }
                    Shops.Add(shop);
                }
            }
            if (shop != null) shop.panes.Add(paneRenderer);
            if (done.Add(building))
            {
                Transform door = building.Find("Shop Entrance Glass");
                Vector3 p = door != null ? door.position : building.TransformPoint(new Vector3(0f, 1.4f, 9f));
                ShopDoors.Add(new ShopDoor { position = new Vector3(p.x, 0f, p.z) + building.forward * 1.3f, inward = -building.forward, kind = kind });
            }
        }
    }

    // ------------------------------------------------------------------ streets

    private static void RegisterExistingStreets()
    {
        foreach (float z in new[] { -47f, 60f, 118f }) Streets.Add(new Street { eastWest = true, centre = z, from = -140f, to = 140f });
        foreach (float x in new[] { -96f, 96f }) Streets.Add(new Street { eastWest = false, centre = x, from = -60f, to = 131f });
        foreach (float x in new[] { -96f, 96f })
            foreach (float z in new[] { -47f, 60f, 118f }) Junctions.Add(new Vector2(x, z));
        Junctions.Add(new Vector2(1f, 60f));   // the dealership crossing
        Junctions.Add(new Vector2(-50f, 60f)); // the parking driveway
    }

    private static void BuildStreets()
    {
        var group = Group("New Streets");
        // Ocean Drive, the avenue and the south street carried out to the outer streets.
        EastWest(group, OceanZ, -OuterX - HalfRoad, OuterX + HalfRoad, new[] { -OuterX, -96f, 96f, OuterX });
        EastWest(group, 60f, 140f, OuterX + HalfRoad, new[] { OuterX });
        EastWest(group, 60f, -OuterX - HalfRoad, -140f, new[] { -OuterX });
        EastWest(group, -47f, 140f, OuterX + HalfRoad, new[] { OuterX });
        EastWest(group, -47f, -OuterX - HalfRoad, -140f, new[] { -OuterX });
        NorthSouth(group, OuterX, OceanZ - HalfRoad, 60f + HalfRoad, new[] { OceanZ, -47f, 60f });
        NorthSouth(group, -OuterX, OceanZ - HalfRoad, 60f + HalfRoad, new[] { OceanZ, -47f, 60f });
        NorthSouth(group, 96f, OceanZ, -60f, new[] { OceanZ });
        NorthSouth(group, -96f, OceanZ, -60f, new[] { OceanZ });
        foreach (float x in new[] { -OuterX, -96f, 96f, OuterX }) Junctions.Add(new Vector2(x, OceanZ));
        foreach (float x in new[] { -OuterX, OuterX }) { Junctions.Add(new Vector2(x, -47f)); Junctions.Add(new Vector2(x, 60f)); }
    }

    private static void EastWest(Transform parent, float z, float x0, float x1, float[] crossings)
    {
        Streets.Add(new Street { eastWest = true, centre = z, from = x0, to = x1 });
        float mid = (x0 + x1) * 0.5f, len = x1 - x0;
        Box(parent, "East West Asphalt", new Vector3(mid, -0.018f, z), new Vector3(len, 0.09f, 13f), asphalt, true);
        for (int side = -1; side <= 1; side += 2)
        {
            foreach (Vector2 run in Runs(x0, x1, crossings, 6.8f))
                Box(parent, "Raised Granite Curb", new Vector3((run.x + run.y) * 0.5f, 0.035f, z + side * 6.65f), new Vector3(run.y - run.x, 0.22f, 0.3f), curb, false);
            foreach (Vector2 run in Runs(x0, x1, crossings, 13.8f))
                Box(parent, "Concrete Sidewalk", new Vector3((run.x + run.y) * 0.5f, 0.015f, z + side * SidewalkOffset), new Vector3(run.y - run.x, 0.08f, 4f), sidewalk, true);
        }
        for (float x = x0 + 4f; x < x1 - 4f; x += 7.5f)
        {
            if (Near(x, crossings, 8.6f)) continue;
            Box(parent, "Broken Center Line", new Vector3(x, 0.032f, z), new Vector3(4.2f, 0.018f, 0.13f), line, false);
            Box(parent, "Lane Dash", new Vector3(x, 0.032f, z - 3.7f), new Vector3(2.7f, 0.016f, 0.1f), line, false);
            Box(parent, "Lane Dash", new Vector3(x, 0.032f, z + 3.7f), new Vector3(2.7f, 0.016f, 0.1f), line, false);
        }
    }

    private static void NorthSouth(Transform parent, float x, float z0, float z1, float[] crossings)
    {
        if (z1 < z0) { float t = z0; z0 = z1; z1 = t; }
        Streets.Add(new Street { eastWest = false, centre = x, from = z0, to = z1 });
        float mid = (z0 + z1) * 0.5f, len = z1 - z0;
        Box(parent, "North South Asphalt", new Vector3(x, -0.017f, mid), new Vector3(13f, 0.09f, len), asphalt, true);
        for (int side = -1; side <= 1; side += 2)
        {
            foreach (Vector2 run in Runs(z0, z1, crossings, 6.8f))
                Box(parent, "Raised Granite Curb", new Vector3(x + side * 6.65f, 0.035f, (run.x + run.y) * 0.5f), new Vector3(0.3f, 0.22f, run.y - run.x), curb, false);
            foreach (Vector2 run in Runs(z0, z1, crossings, 13.8f))
                Box(parent, "Concrete Sidewalk", new Vector3(x + side * SidewalkOffset, 0.015f, (run.x + run.y) * 0.5f), new Vector3(4f, 0.08f, run.y - run.x), sidewalk, true);
        }
        for (float z = z0 + 4f; z < z1 - 4f; z += 7.5f)
        {
            if (Near(z, crossings, 8.6f)) continue;
            Box(parent, "Broken Center Line", new Vector3(x, 0.033f, z), new Vector3(0.13f, 0.018f, 4.2f), line, false);
        }
    }

    private static List<Vector2> Runs(float a, float b, float[] gaps, float halfGap)
    {
        var cuts = new List<Vector2>();
        foreach (float g in gaps) cuts.Add(new Vector2(g - halfGap, g + halfGap));
        cuts.Sort((p, q) => p.x.CompareTo(q.x));
        var runs = new List<Vector2>();
        float start = a;
        foreach (Vector2 c in cuts)
        {
            if (c.y <= start || c.x >= b) continue;
            if (c.x > start + 0.5f) runs.Add(new Vector2(start, Mathf.Min(c.x, b)));
            start = Mathf.Max(start, c.y);
        }
        if (b > start + 0.5f) runs.Add(new Vector2(start, b));
        return runs;
    }

    private static bool Near(float v, float[] list, float d)
    {
        foreach (float x in list) if (Mathf.Abs(v - x) < d) return true;
        return false;
    }

    // ------------------------------------------------------------------ ground

    private static void BuildGround()
    {
        var group = Group("New Ground");
        // East and west of the old district, and a wide apron round everything so the edge of the world is far away.
        // Grass with its texture repeating every 4 m (a stretched box made it one flat grey colour).
        Material lawn = Lit(new Color(0.3f, 0.45f, 0.22f), 0f, 0.12f);
        if (grass != null && grass.HasProperty("_BaseMap") && grass.GetTexture("_BaseMap") != null)
        {
            lawn = new Material(grass) { name = "Tiled City Grass" };
            lawn.SetTextureScale("_BaseMap", Vector2.one);
            lawn.SetColor("_BaseColor", grass.GetColor("_BaseColor") * new Color(0.95f, 1.08f, 0.9f));
        }
        Plane(group, "Outer City Ground", new Rect(-700f, -140f, 1400f, 520f), -0.06f, lawn);
        Plane(group, "East District Ground", new Rect(OuterX - 118f, -135f, 160f, 230f), 0.005f, lawn);
        Plane(group, "West District Ground", new Rect(-OuterX - 42f, -135f, 160f, 230f), 0.005f, lawn);
        Plane(group, "Ocean Drive Verge", new Rect(-OuterX - 15f, OceanZ - 12f, 2 * OuterX + 30f, 30f), 0.004f, lawn);
    }

    private static void Plane(Transform parent, string name, Rect r, float y, Material m)
    {
        var mesh = new Mesh { name = name };
        mesh.vertices = new[] { new Vector3(r.xMin, y, r.yMin), new Vector3(r.xMax, y, r.yMin), new Vector3(r.xMax, y, r.yMax), new Vector3(r.xMin, y, r.yMax) };
        mesh.uv = new[] { new Vector2(r.xMin, r.yMin) / 4f, new Vector2(r.xMax, r.yMin) / 4f, new Vector2(r.xMax, r.yMax) / 4f, new Vector2(r.xMin, r.yMax) / 4f };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
        BoxCollider c = go.AddComponent<BoxCollider>();
        c.center = new Vector3(r.center.x, y - 0.05f, r.center.y);
        c.size = new Vector3(r.width, 0.1f, r.height);
    }

    // ------------------------------------------------------------------ blocks

    private static void BuildBlocks()
    {
        var group = Group("New Blocks");
        var random = new System.Random(2026);
        int kindIndex = 0, made = 0;
        // Ocean Drive's north side faces the sea (yaw 180: front towards -z).
        foreach (Vector2 span in new[] { new Vector2(-306f, -112f), new Vector2(-80f, 80f), new Vector2(112f, 306f) })
            made += Row(group, span, OceanZ + HalfRoad + 5.5f, 180f, random, ref kindIndex, true);
        {
            foreach (float side in new[] { 1f, -1f })
            {
                Vector2 span = side > 0 ? new Vector2(150f, 306f) : new Vector2(-306f, -150f);
                made += Row(group, span, 60f + HalfRoad + 5.5f, 180f, random, ref kindIndex, false);   // avenue, north side
                made += Row(group, span, 60f - HalfRoad - 5.5f, 0f, random, ref kindIndex, false);     // avenue, south side
                if (!quest)
                {
                    made += Row(group, span, -47f + HalfRoad + 5.5f, 180f, random, ref kindIndex, false);
                    made += Row(group, span, -47f - HalfRoad - 5.5f, 0f, random, ref kindIndex, false);
                }
            }
        }
    }

    /// <summary>Buildings side by side along x, their street faces on the line <paramref name="frontZ"/>.</summary>
    private static int Row(Transform parent, Vector2 span, float frontZ, float yaw, System.Random random, ref int kindIndex, bool seaside)
    {
        int count = 0;
        float x = span.x;
        while (x < span.y - 14f)
        {
            float width = Mathf.Min(span.y - x, 16f + (float)random.NextDouble() * 14f);
            float depth = 16f + (float)random.NextDouble() * 4f;
            int floors = 2 + random.Next(seaside ? 5 : 7);
            float height = floors * (3.3f + (float)random.NextDouble() * 0.4f) + 0.6f;
            string kind = seaside && random.NextDouble() < 0.45 ? (random.NextDouble() < 0.5 ? "surf" : "cafe") : Kinds[kindIndex++ % Kinds.Length];
            string[] names = SignNames[kind];
            string sign = names[random.Next(names.Length)];
            Material wall = new[] { wallCream, wallBlue, wallWarm, wallGraphite }[random.Next(4)];
            float centreZ = frontZ + (yaw > 90f ? depth * 0.5f : -depth * 0.5f);
            Building(parent, sign, new Vector3(x + width * 0.5f, 0f, centreZ), yaw, width, depth, height, floors, wall, kind, sign);
            count++;
            x += width + 2.5f + (float)random.NextDouble() * 3f;
        }
        return count;
    }

    private static void Building(Transform parent, string name, Vector3 position, float yaw, float width, float depth, float height, int floors,
        Material wall, string kind, string sign)
    {
        Transform b = new GameObject(name).transform;
        b.SetParent(parent, false);
        b.localPosition = position;
        b.localRotation = Quaternion.Euler(0f, yaw, 0f);
        float front = depth * 0.5f;
        Box(b, "Facade Mass", new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, depth), wall, true);
        Box(b, "Ground Floor Plinth", new Vector3(0f, 0.24f, 0f), new Vector3(width + 0.25f, 0.48f, depth + 0.2f), plinth, false);
        Box(b, "Roof Parapet", new Vector3(0f, height + 0.4f, 0f), new Vector3(width + 0.5f, 0.8f, depth + 0.45f), plinth, false);
        Box(b, "Street Side Canopy", new Vector3(0f, 3.72f, front + 0.48f), new Vector3(width - 0.4f, 0.18f, 1.05f), metal, false);
        int bays = Mathf.Max(3, Mathf.FloorToInt(width / 3.4f));
        float bay = width / bays;
        Material shop = ShopMaterial(kind);
        for (int i = 0; i < bays; i++)
        {
            float x = -width * 0.5f + bay * (i + 0.5f);
            if (i == bays / 2) continue; // the entrance
            GameObject pane = Box(b, "Storefront Reflective Glazing", new Vector3(x, 1.82f, front + 0.045f), new Vector3(Mathf.Max(1.25f, bay - 0.22f), 2.55f, 0.08f), shop, false);
            pane.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Box(b, "Storefront Mullion", new Vector3(x - bay * 0.5f + 0.04f, 1.83f, front + 0.11f), new Vector3(0.11f, 2.72f, 0.12f), metal, false);
        }
        Box(b, "Storefront Header Band", new Vector3(0f, 3.23f, front + 0.11f), new Vector3(width - 0.18f, 0.78f, 0.18f), signFace, false);
        Text(b, sign, new Vector3(0f, 3.24f, front + 0.215f), width - 1.2f, 0.5f, kind == "guns" ? new Color(1f, 0.45f, 0.3f) : new Color(0.93f, 0.81f, 0.61f));
        // The door: a pane of the shop's own room, so you see into the shop through it too.
        float doorX = -width * 0.5f + bay * (bays / 2 + 0.5f);
        GameObject door = Box(b, "Shop Entrance Glass", new Vector3(doorX, 1.42f, front + 0.12f), new Vector3(1.55f, 2.72f, 0.07f), shop, false);
        door.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Box(b, "Entrance Door Pull", new Vector3(doorX + 0.56f, 1.45f, front + 0.2f), new Vector3(0.055f, 0.7f, 0.05f), gold, false);
        Vector3 doorWorld = b.TransformPoint(new Vector3(doorX, 0f, front + 1.3f));
        var shopBuilding = new ShopBuilding { root = b, width = width, depth = depth, height = height, kind = kind, wall = wall, door = door.transform, doorX = doorX };
        Transform massT = b.Find("Facade Mass");
        shopBuilding.mass = massT.GetComponent<Renderer>();
        shopBuilding.massCollider = massT.GetComponent<Collider>();
        foreach (Transform child in b)
            if (child.name == "Storefront Reflective Glazing") shopBuilding.panes.Add(child.GetComponent<Renderer>());
        Transform pullT = b.Find("Entrance Door Pull");
        if (pullT != null) shopBuilding.doorPull = pullT.GetComponent<Renderer>();
        Shops.Add(shopBuilding);
        ShopDoors.Add(new ShopDoor { position = new Vector3(doorWorld.x, 0f, doorWorld.z), inward = -b.forward, kind = kind });
        // Upper floors.
        for (int floor = 1; floor < floors; floor++)
        {
            float level = floor * (height / floors);
            Box(b, "Continuous Floor Reveal", new Vector3(0f, level, 0f), new Vector3(width + 0.1f, 0.1f, depth + 0.1f), metal, false);
            float h = Mathf.Min(1.75f, height / floors * 0.54f);
            for (int i = 0; i < bays; i++)
            {
                float x = -width * 0.5f + bay * (i + 0.5f);
                Box(b, "Upper Floor Window", new Vector3(x, level + (height / floors) * 0.53f, front + 0.045f), new Vector3(bay * 0.69f, h, 0.08f), glass, false);
            }
            // The side facing the next street gets windows too, on the corner buildings' long faces.
            for (float z = -depth * 0.5f + 2f; z < depth * 0.5f - 1.5f; z += 3.4f)
            {
                Box(b, "Upper Floor Window", new Vector3(width * 0.5f + 0.045f, level + (height / floors) * 0.53f, z), new Vector3(0.08f, h, 2.2f), glass, false);
                Box(b, "Upper Floor Window", new Vector3(-width * 0.5f - 0.045f, level + (height / floors) * 0.53f, z), new Vector3(0.08f, h, 2.2f), glass, false);
            }
        }
        Box(b, "Roof Mechanical Screen", new Vector3(-width * 0.23f, height + 1.3f, 0f), new Vector3(width * 0.28f, 1f, depth * 0.52f), plinth, false);
        // Dumpster round the side, as behind any American shop.
        if (width > 18f) Dumpster(b, new Vector3(width * 0.5f + 1.3f, 0f, -depth * 0.25f), Quaternion.Euler(0f, 90f, 0f));
    }

    private static void Text(Transform parent, string text, Vector3 local, float width, float height, Color colour)
    {
        var go = new GameObject("Shop Sign");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // TextMeshPro reads along its -Z; the street is at +Z
        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.5f;
        tmp.fontSizeMax = 4f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.characterSpacing = 6f;
        tmp.color = colour;
        tmp.rectTransform.sizeDelta = new Vector2(width, height);
    }

    // ------------------------------------------------------------------ beach and sea

    private static void BuildBeach()
    {
        var group = Group("Beach and Sea");
        float x0 = -OuterX - 25f, x1 = OuterX + 25f, top = OceanZ - 13.9f, bottom = -205f;
        // Sand: from the promenade's edge down under the water line.
        var mesh = new Mesh { name = "Beach Sand" };
        int nx = 40, nz = 14;
        var v = new Vector3[(nx + 1) * (nz + 1)];
        var uv = new Vector2[v.Length];
        var tris = new List<int>();
        for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float x = Mathf.Lerp(x0, x1, i / (float)nx), z = Mathf.Lerp(top, bottom, j / (float)nz);
                float depthIn = (top - z);
                float y = 0.02f - depthIn * 0.024f - Mathf.Max(0f, depthIn - 18f) * 0.03f + Mathf.Sin(x * 0.07f) * 0.08f * Mathf.Clamp01(depthIn / 8f);
                v[j * (nx + 1) + i] = new Vector3(x, y, z);
                uv[j * (nx + 1) + i] = new Vector2(x * 0.25f, z * 0.25f);
            }
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(b); tris.Add(d); tris.Add(c);
            }
        mesh.vertices = v; mesh.uv = uv; mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var sandGo = new GameObject("Beach Sand");
        sandGo.transform.SetParent(group, false);
        sandGo.AddComponent<MeshFilter>().sharedMesh = mesh;
        sandGo.AddComponent<MeshRenderer>().sharedMaterial = sand;
        sandGo.AddComponent<MeshCollider>().sharedMesh = mesh;

        // The sea.
        Shader water = Resources.Load<Shader>("TestDriveWater");
        Material sea = water != null && water.isSupported ? new Material(water) { name = "Sea" } : Lit(new Color(0.05f, 0.2f, 0.3f), 0f, 0.95f);
        if (water != null) sea.SetFloat("_ShoreZ", ShoreZ);
        var seaMesh = new Mesh { name = "Sea" };
        float sx = 2500f, sz0 = top - 10f, sz1 = -2600f;
        seaMesh.vertices = new[] { new Vector3(-sx, -0.35f, sz1), new Vector3(sx, -0.35f, sz1), new Vector3(sx, -0.35f, sz0), new Vector3(-sx, -0.35f, sz0) };
        seaMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        seaMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        seaMesh.RecalculateBounds();
        var seaGo = new GameObject("Sea");
        seaGo.transform.SetParent(group, false);
        seaGo.AddComponent<MeshFilter>().sharedMesh = seaMesh;
        MeshRenderer seaRenderer = seaGo.AddComponent<MeshRenderer>();
        seaRenderer.sharedMaterial = sea;
        seaRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Beach life: umbrellas with loungers, lifeguard towers.
        var random = new System.Random(31);
        Color[] canopies = { new Color(0.85f, 0.2f, 0.18f), new Color(0.1f, 0.45f, 0.75f), new Color(0.95f, 0.8f, 0.2f), new Color(0.95f, 0.95f, 0.92f), new Color(0.1f, 0.6f, 0.5f) };
        Material pole = Lit(new Color(0.85f, 0.85f, 0.82f), 0f, 0.4f);
        Material lounger = Lit(new Color(0.92f, 0.9f, 0.85f), 0f, 0.35f);
        Material towel = Lit(new Color(0.2f, 0.4f, 0.75f), 0f, 0.1f);
        int clusters = quest ? 14 : 30;
        for (int k = 0; k < clusters; k++)
        {
            float x = Mathf.Lerp(x0 + 20f, x1 - 20f, (k + 0.5f) / clusters) + (float)(random.NextDouble() - 0.5) * 10f;
            float z = OceanZ - 20f - (float)random.NextDouble() * 6f;
            float y = SandY(z, x);
            var u = new GameObject("Beach Umbrella").transform;
            u.SetParent(group, false);
            u.position = new Vector3(x, y, z);
            Cylinder(u, "Umbrella Pole", new Vector3(0f, 1.15f, 0f), 0.03f, 2.3f, pole);
            Cone(u, "Umbrella Canopy", new Vector3(0f, 2.3f, 0f), 1.35f, 0.45f, Lit(canopies[random.Next(canopies.Length)], 0f, 0.25f));
            for (int s = -1; s <= 1; s += 2)
            {
                var l = Box(u, "Sun Lounger", new Vector3(s * 0.75f, 0.28f, 0.1f), new Vector3(0.62f, 0.08f, 1.9f), lounger, true);
                Box(u, "Lounger Back", new Vector3(s * 0.75f, 0.55f, -0.72f), new Vector3(0.62f, 0.08f, 0.7f), lounger, false).transform.localRotation = Quaternion.Euler(-50f, 0f, 0f);
                if (random.NextDouble() < 0.5) Box(u, "Beach Towel", new Vector3(s * 0.75f, 0.33f, 0.15f), new Vector3(0.58f, 0.01f, 1.6f), towel, false);
                l.name = "Sun Lounger";
            }
            u.rotation = Quaternion.Euler(0f, (float)(random.NextDouble() - 0.5) * 30f, 0f);
        }
        Material tower = Lit(new Color(0.95f, 0.95f, 0.92f), 0f, 0.3f), red = Lit(new Color(0.8f, 0.15f, 0.12f), 0f, 0.3f);
        foreach (float x in new[] { -220f, -60f, 100f, 250f })
        {
            float z = OceanZ - 30f, y = SandY(z, x);
            var t = new GameObject("Lifeguard Tower").transform;
            t.SetParent(group, false);
            t.position = new Vector3(x, y, z);
            t.rotation = Quaternion.Euler(0f, 180f, 0f);
            for (int a = -1; a <= 1; a += 2) for (int c = -1; c <= 1; c += 2) Cylinder(t, "Tower Leg", new Vector3(a * 0.9f, 1.1f, c * 0.9f), 0.06f, 2.2f, tower);
            Box(t, "Tower Cabin", new Vector3(0f, 3.0f, 0f), new Vector3(2.2f, 1.6f, 2.2f), tower, true);
            Box(t, "Tower Roof", new Vector3(0f, 3.9f, 0f), new Vector3(2.6f, 0.15f, 2.6f), red, false);
            Box(t, "Tower Ramp", new Vector3(0f, 1.1f, -2.2f), new Vector3(0.8f, 0.08f, 3f), tower, false).transform.localRotation = Quaternion.Euler(-40f, 0f, 0f);
        }
    }

    // ------------------------------------------------------------------ parks

    /// <summary>
    /// Green space between the blocks: a pocket park with a playground behind the south street, and two large
    /// parks in the new districts (lawns, paths, trees, benches, a playground, a fountain, a basketball court).
    /// </summary>
    private static void BuildParks()
    {
        var group = Group("Parks and Playgrounds");
        Park(group, new Rect(-27f, -80f, 54f, 17f), false);
        Park(group, new Rect(152f, -12f, 152f, 37f), true);
        Park(group, new Rect(-304f, -12f, 152f, 37f), true);
    }

    private static void Park(Transform parent, Rect area, bool large)
    {
        var park = new GameObject(large ? "City Park" : "Pocket Park").transform;
        park.SetParent(parent, false);
        Vector3 c = new Vector3(area.center.x, Ground(area.center.x, area.center.y), area.center.y);
        Material lawn = Lit(new Color(0.24f, 0.42f, 0.18f), 0f, 0.15f), path = Lit(new Color(0.72f, 0.66f, 0.56f), 0f, 0.2f);
        Material border = Lit(new Color(0.55f, 0.55f, 0.52f), 0f, 0.3f), rubber = Lit(new Color(0.55f, 0.18f, 0.15f), 0f, 0.1f);
        Box(park, "Park Lawn", new Vector3(c.x, c.y + 0.03f, c.z), new Vector3(area.width, 0.06f, area.height), lawn, true);
        Box(park, "Park Kerb", new Vector3(c.x, c.y + 0.07f, area.yMin), new Vector3(area.width, 0.14f, 0.2f), border, false);
        Box(park, "Park Kerb", new Vector3(c.x, c.y + 0.07f, area.yMax), new Vector3(area.width, 0.14f, 0.2f), border, false);
        // Paths: one along the length, one across.
        Box(park, "Park Path", new Vector3(c.x, c.y + 0.065f, c.z), new Vector3(area.width - 1f, 0.02f, 2.4f), path, false);
        Box(park, "Park Path", new Vector3(c.x, c.y + 0.066f, c.z), new Vector3(2.4f, 0.02f, area.height - 1f), path, false);
        // Playground on a rubber pad.
        Vector3 play = new Vector3(c.x - area.width * 0.25f, c.y, c.z + (large ? area.height * 0.22f : 0f));
        Box(park, "Playground Safety Surface", play + new Vector3(0f, 0.075f, 0f), new Vector3(12f, 0.03f, large ? 10f : 7f), rubber, false);
        Playground(park, play);
        if (large)
        {
            Fountain(park, new Vector3(c.x, c.y, c.z));
            Court(park, new Vector3(c.x + area.width * 0.28f, c.y, c.z + area.height * 0.22f));
        }
        // Trees round the edges and benches along the paths.
        GameObject tree = null;
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (t.name == "Street Tree" && t.Find("Generated Tree") != null) { tree = t.gameObject; break; }
        GameObject bench = GameObject.Find("Street Bench");
        var random = new System.Random((int)(area.x * 7 + area.y));
        for (float x = area.xMin + 4f; x < area.xMax - 3f; x += large ? 13f : 11f)
            foreach (float z in new[] { area.yMin + 2.2f, area.yMax - 2.2f })
            {
                if (tree == null) break;
                Vector3 p = new Vector3(x + (float)(random.NextDouble() - 0.5) * 3f, 0f, z);
                if (Mathf.Abs(p.x - play.x) < 8f && Mathf.Abs(p.z - play.z) < 7f) continue;
                GameObject copy = Object.Instantiate(tree, park);
                copy.transform.position = new Vector3(p.x, Ground(p.x, p.z), p.z);
                copy.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
            }
        if (bench != null)
            for (float x = area.xMin + 8f; x < area.xMax - 6f; x += large ? 18f : 14f)
                foreach (int side in new[] { -1, 1 })
                {
                    if (Mathf.Abs(x - c.x) < 4f) continue;
                    GameObject copy = Object.Instantiate(bench, park);
                    copy.name = "Street Bench";
                    copy.transform.position = new Vector3(x, Ground(x, c.z) + 0.05f, c.z + side * 2.2f);
                    copy.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0f, -side), Vector3.up);
                }
    }

    /// <summary>
    /// The open land behind the new blocks: parking lots full of cars, small parks with playgrounds, service lanes;
    /// and along the new streets the things a street has: bus stops with people waiting, fire hydrants, newspaper
    /// boxes and a hot-dog cart.
    /// </summary>
    private static void BuildLots()
    {
        var group = Group("Lots and Street Life");
        foreach (int side in new[] { 1, -1 })
        {
            float x0 = side > 0 ? 152f : -304f;
            // Behind the avenue's north side.
            ParkingLot(group, new Rect(x0, 94f, 70f, 26f));
            Park(group, new Rect(x0 + 78f, 94f, 72f, 30f), false);
            // The service lane behind the south street's south side, in front of the beach blocks' backs.
            Box(group, "Service Lane", new Vector3(x0 + 77f, 0.01f, -83f), new Vector3(152f, 0.04f, 6f), asphalt, false);
        }
        // Behind the beach blocks in the middle.
        Box(group, "Service Lane", new Vector3(0f, 0.01f, -84.5f), new Vector3(150f, 0.04f, 4f), asphalt, false);

        var random = new System.Random(55);
        Material red = Lit(new Color(0.75f, 0.08f, 0.06f), 0.3f, 0.55f), shelterFrame = Lit(new Color(0.2f, 0.22f, 0.24f), 0.8f, 0.5f);
        foreach (Street s in Streets)
        {
            bool isNew = s.from < -141f || s.to > 141f || s.centre <= OceanZ + 1f || (!s.eastWest && s.to <= -59f);
            if (!isNew) continue;
            for (float a = s.from + 22f; a < s.to - 22f; a += 55f)
            {
                if (NearJunction(s, a, 18f)) continue;
                int side = random.NextDouble() < 0.5 ? 1 : -1;
                if (s.eastWest && Mathf.Approximately(s.centre, OceanZ)) side = 1;
                // Fire hydrant at the kerb.
                Vector3 h = StreetPoint(s, a, side * 10.25f);
                if (Free(h, 0.3f) && !OnWalkLine(h, 1f))
                {
                    var hy = new GameObject("Fire Hydrant").transform;
                    hy.SetParent(group, false);
                    hy.position = h + Vector3.up * 0.055f;
                    Cylinder(hy, "Hydrant Body", new Vector3(0f, 0.32f, 0f), 0.11f, 0.6f, red);
                    Cone(hy, "Hydrant Cap", new Vector3(0f, 0.62f, 0f), 0.12f, 0.12f, red);
                    Cylinder(hy, "Hydrant Nozzle", new Vector3(0f, 0.42f, 0f), 0.05f, 0.32f, red).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    Cylinder(hy, "Hydrant Base", new Vector3(0f, 0.03f, 0f), 0.15f, 0.06f, red);
                }
            }
            // A bus stop on each new street.
            float at = (s.from + s.to) * 0.5f + 40f;
            if (!NearJunction(s, at, 20f)) BusStop(group, s, at, s.eastWest && Mathf.Approximately(s.centre, OceanZ) ? 1 : -1, shelterFrame);
        }
        HotDogCart(group, new Vector3(OuterX - 110f, 0f, -47f + 10.6f));
        HotDogCart(group, new Vector3(-OuterX + 110f, 0f, -47f + 10.6f));
        HotDogCart(group, new Vector3(30f, 0f, OceanZ - 10.6f));
    }

    private static void ParkingLot(Transform parent, Rect r)
    {
        Material lineMat = line;
        Box(parent, "Parking Lot", new Vector3(r.center.x, 0.012f, r.center.y), new Vector3(r.width, 0.03f, r.height), asphalt, false);
        GameObject[] models = Resources.LoadAll<GameObject>("TestDrive/Traffic");
        var cars = new List<GameObject>();
        foreach (GameObject m in models) { string n = m.name.ToLowerInvariant(); if (!n.Contains("bus") && !n.Contains("truck") && !n.Contains("garbage") && !n.Contains("fire") && !n.Contains("tow") && !n.Contains("ambulance") && !n.Contains("utility")) cars.Add(m); }
        var random = new System.Random((int)r.x);
        foreach (float row in new[] { r.yMin + 5f, r.yMax - 5f })
            for (float x = r.xMin + 2f; x < r.xMax - 2f; x += 3f)
            {
                Box(parent, "Parking Stall Line", new Vector3(x, 0.03f, row), new Vector3(0.1f, 0.01f, 5f), lineMat, false);
                if (cars.Count == 0 || random.NextDouble() < 0.3 || x + 3f > r.xMax - 2f) continue;
                GameObject car = Object.Instantiate(cars[random.Next(cars.Count)], parent);
                car.name = "Parked Car";
                car.transform.position = new Vector3(x + 1.5f, 0.03f, row);
                car.transform.rotation = Quaternion.Euler(0f, (row < r.center.y ? 0f : 180f) + (float)(random.NextDouble() - 0.5) * 4f, 0f);
                BoxCollider c = car.AddComponent<BoxCollider>();
                c.center = new Vector3(0f, 0.8f, 0f);
                c.size = new Vector3(1.9f, 1.5f, 4.3f);
            }
    }

    private static void BusStop(Transform parent, Street s, float along, int side, Material frame)
    {
        Vector3 p = StreetPoint(s, along, side * 12.6f);
        if (!Free(p, 1.2f)) return;
        Vector3 toRoad = s.eastWest ? new Vector3(0f, 0f, -side) : new Vector3(-side, 0f, 0f);
        var stop = new GameObject("Bus Stop").transform;
        stop.SetParent(parent, false);
        stop.position = p + Vector3.up * 0.055f;
        stop.rotation = Quaternion.LookRotation(toRoad, Vector3.up);
        Material glassPane = Lit(new Color(0.7f, 0.8f, 0.85f), 0f, 0.95f), ad = Lit(new Color(0.95f, 0.85f, 0.6f), 0f, 0.4f);
        ad.EnableKeyword("_EMISSION"); ad.SetColor("_EmissionColor", new Color(0.9f, 0.8f, 0.55f) * 0.6f);
        foreach (float x in new[] { -1.6f, 1.6f }) Box(stop, "Shelter Post", new Vector3(x, 1.2f, -0.6f), new Vector3(0.08f, 2.4f, 0.08f), frame, false);
        Box(stop, "Shelter Roof", new Vector3(0f, 2.45f, 0f), new Vector3(3.6f, 0.08f, 1.6f), frame, false);
        Box(stop, "Shelter Back", new Vector3(0f, 1.3f, -0.62f), new Vector3(3.2f, 1.8f, 0.03f), glassPane, false);
        Box(stop, "Shelter Advert", new Vector3(1.75f, 1.3f, 0f), new Vector3(0.1f, 1.7f, 1.1f), ad, false);
        Box(stop, "Shelter Bench", new Vector3(0f, 0.46f, -0.35f), new Vector3(2.6f, 0.06f, 0.4f), frame, true);
        Box(stop, "Bus Stop Sign", new Vector3(-1.9f, 2.6f, 0.5f), new Vector3(0.5f, 0.5f, 0.04f), Lit(new Color(0.1f, 0.4f, 0.8f), 0f, 0.4f), false);
        Box(stop, "Bus Stop Pole", new Vector3(-1.9f, 1.3f, 0.5f), new Vector3(0.06f, 2.6f, 0.06f), frame, false);
        Label(stop, "BUS", new Vector3(-1.9f, 2.6f, 0.53f), 0.45f, 0.3f, Color.white);
        var bench = new GameObject("Street Bench").transform;   // the bench sitters use it
        bench.SetParent(stop, false);
        bench.localPosition = new Vector3(0f, -0.05f, -0.37f);
        bench.localRotation = Quaternion.identity;
    }

    private static void HotDogCart(Transform parent, Vector3 at)
    {
        if (!Free(at, 1.2f) || OnWalkLine(at, 1.2f)) return;
        var cart = new GameObject("Hot Dog Cart").transform;
        cart.SetParent(parent, false);
        cart.position = at + Vector3.up * 0.06f;
        Material steel = Lit(new Color(0.8f, 0.82f, 0.84f), 0.9f, 0.7f), black = Lit(new Color(0.05f, 0.05f, 0.05f), 0f, 0.4f);
        Box(cart, "Cart Body", new Vector3(0f, 0.65f, 0f), new Vector3(1.8f, 0.9f, 0.9f), steel, true);
        foreach (float x in new[] { -0.6f, 0.6f }) Cylinder(cart, "Cart Wheel", new Vector3(x, 0.22f, 0.46f), 0.22f, 0.06f, black).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Cylinder(cart, "Umbrella Pole", new Vector3(0f, 1.6f, 0f), 0.025f, 2.1f, steel);
        Cone(cart, "Umbrella", new Vector3(0f, 2.55f, 0f), 1.3f, 0.45f, Lit(new Color(0.95f, 0.75f, 0.1f), 0f, 0.3f));
        Box(cart, "Cart Sign", new Vector3(0f, 0.9f, 0.46f), new Vector3(1.2f, 0.3f, 0.01f), Lit(new Color(0.8f, 0.1f, 0.1f), 0f, 0.4f), false);
        Label(cart, "HOT DOGS", new Vector3(0f, 0.9f, 0.47f), 1.1f, 0.26f, Color.white);
    }

    private static void Playground(Transform parent, Vector3 at)
    {
        Material red = Lit(new Color(0.8f, 0.18f, 0.12f), 0.1f, 0.5f), yellow = Lit(new Color(0.95f, 0.75f, 0.1f), 0.1f, 0.5f);
        Material blue = Lit(new Color(0.12f, 0.35f, 0.7f), 0.1f, 0.5f), steel = Lit(new Color(0.7f, 0.72f, 0.75f), 0.8f, 0.5f), wood = Lit(new Color(0.55f, 0.38f, 0.22f), 0f, 0.3f);
        var p = new GameObject("Playground").transform;
        p.SetParent(parent, false);
        p.position = at;
        // Climbing tower with a slide.
        for (int a = -1; a <= 1; a += 2) for (int b = -1; b <= 1; b += 2) Cylinder(p, "Tower Post", new Vector3(-2.5f + a * 0.8f, 1.2f, b * 0.8f), 0.06f, 2.4f, yellow);
        Box(p, "Tower Deck", new Vector3(-2.5f, 1.3f, 0f), new Vector3(1.8f, 0.08f, 1.8f), wood, true);
        Box(p, "Tower Roof", new Vector3(-2.5f, 2.5f, 0f), new Vector3(2f, 0.1f, 2f), red, false).transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        var slide = Box(p, "Slide", new Vector3(-0.85f, 0.7f, 0f), new Vector3(2.4f, 0.06f, 0.7f), blue, false);
        slide.transform.localRotation = Quaternion.Euler(0f, 0f, -30f);
        Box(p, "Ladder", new Vector3(-3.5f, 0.7f, 0f), new Vector3(0.08f, 1.4f, 0.6f), steel, false).transform.localRotation = Quaternion.Euler(0f, 0f, 15f);
        // Swings: an A-frame and two seats on chains.
        for (int s = -1; s <= 1; s += 2)
        {
            Cylinder(p, "Swing Leg", new Vector3(2.5f + s * 1.6f, 1.1f, -0.5f), 0.05f, 2.4f, steel).transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            Cylinder(p, "Swing Leg", new Vector3(2.5f + s * 1.6f, 1.1f, 0.5f), 0.05f, 2.4f, steel).transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
        }
        Cylinder(p, "Swing Beam", new Vector3(2.5f, 2.25f, 0f), 0.06f, 3.3f, steel).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        foreach (float x in new[] { 1.8f, 3.2f })
        {
            Cylinder(p, "Swing Chain", new Vector3(x - 0.2f, 1.45f, 0f), 0.01f, 1.6f, steel);
            Cylinder(p, "Swing Chain", new Vector3(x + 0.2f, 1.45f, 0f), 0.01f, 1.6f, steel);
            Box(p, "Swing Seat", new Vector3(x, 0.62f, 0f), new Vector3(0.5f, 0.05f, 0.22f), red, false);
        }
        // Seesaw and a sandpit.
        Box(p, "Seesaw Pivot", new Vector3(0f, 0.25f, 2.6f), new Vector3(0.3f, 0.5f, 0.3f), yellow, false);
        Box(p, "Seesaw Plank", new Vector3(0f, 0.52f, 2.6f), new Vector3(3f, 0.06f, 0.3f), blue, false).transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
        Box(p, "Sandpit Edge", new Vector3(3f, 0.15f, 2.6f), new Vector3(2.6f, 0.3f, 2.2f), wood, false);
        Box(p, "Sandpit Sand", new Vector3(3f, 0.27f, 2.6f), new Vector3(2.4f, 0.05f, 2f), sand, false);
    }

    private static void Fountain(Transform parent, Vector3 at)
    {
        Material stone = Lit(new Color(0.75f, 0.73f, 0.68f), 0f, 0.35f);
        Material water = Lit(new Color(0.15f, 0.35f, 0.45f), 0f, 0.95f);
        var f = new GameObject("Fountain").transform;
        f.SetParent(parent, false);
        f.position = at;
        Cylinder(f, "Fountain Basin", new Vector3(0f, 0.3f, 0f), 3.2f, 0.6f, stone);
        Cylinder(f, "Fountain Water", new Vector3(0f, 0.58f, 0f), 3.0f, 0.04f, water);
        Cylinder(f, "Fountain Column", new Vector3(0f, 1.1f, 0f), 0.35f, 1.6f, stone);
        Cylinder(f, "Fountain Bowl", new Vector3(0f, 1.9f, 0f), 1.1f, 0.2f, stone);
        var c = f.gameObject.AddComponent<CapsuleCollider>();
        c.radius = 3.2f; c.height = 1.2f; c.center = new Vector3(0f, 0.3f, 0f);
    }

    private static void Court(Transform parent, Vector3 at)
    {
        Material court = Lit(new Color(0.2f, 0.38f, 0.55f), 0f, 0.25f), white = Lit(Color.white, 0f, 0.3f), steel = Lit(new Color(0.3f, 0.3f, 0.32f), 0.6f, 0.4f);
        Material orange = Lit(new Color(0.95f, 0.4f, 0.1f), 0.2f, 0.4f);
        var k = new GameObject("Basketball Court").transform;
        k.SetParent(parent, false);
        k.position = at;
        Box(k, "Court Surface", new Vector3(0f, 0.08f, 0f), new Vector3(28f, 0.03f, 15f), court, false);
        Box(k, "Court Line", new Vector3(0f, 0.1f, 0f), new Vector3(0.08f, 0.01f, 15f), white, false);
        foreach (int s in new[] { -1, 1 })
        {
            Cylinder(k, "Hoop Post", new Vector3(s * 14.6f, 1.6f, 0f), 0.08f, 3.2f, steel);
            Box(k, "Backboard", new Vector3(s * 13.9f, 3.25f, 0f), new Vector3(0.05f, 1.05f, 1.8f), white, false);
            Cylinder(k, "Hoop", new Vector3(s * 13.6f, 3.05f, 0f), 0.23f, 0.02f, orange);
        }
    }

    private static float Ground(float x, float z)
    {
        RaycastHit hit;
        Physics.SyncTransforms();
        if (Physics.Raycast(new Vector3(x, 30f, z), Vector3.down, out hit, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return hit.point.y < 1f ? hit.point.y : 0f;
        return 0f;
    }

    public static float SandY(float z, float x)
    {
        float top = OceanZ - 13.9f, depthIn = top - z;
        return 0.02f - depthIn * 0.024f - Mathf.Max(0f, depthIn - 18f) * 0.03f + Mathf.Sin(x * 0.07f) * 0.08f * Mathf.Clamp01(depthIn / 8f);
    }

    // ------------------------------------------------------------------ street furniture

    private static void StreetFurniture()
    {
        var group = Group("Bins, Lanterns and Trees");
        GameObject lantern = GameObject.Find("Warm LED Street Lantern");
        GameObject tree = null;
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (t.name == "Street Tree" && t.Find("Generated Tree") != null) { tree = t.gameObject; break; }
        Material binGreen = Lit(new Color(0.12f, 0.25f, 0.16f), 0.3f, 0.35f), binBlack = Lit(new Color(0.05f, 0.05f, 0.05f), 0.2f, 0.3f);
        Material binBlue = Lit(new Color(0.1f, 0.25f, 0.55f), 0f, 0.4f);
        var random = new System.Random(77);
        int bins = 0;
        foreach (Street s in Streets)
        {
            bool isNew = s.from < -141f || s.to > 141f || s.centre <= OceanZ + 1f || (!s.eastWest && s.to <= -59f);
            // One pair of bins per street, half way along it (tried a little either side if the spot is taken).
            int placedHere = bins;
            float mid = (s.from + s.to) * 0.5f;
            foreach (float shift in new[] { 0f, 9f, -9f, 18f, -18f, 30f, -30f })
            {
                if (bins > placedHere || NearJunction(s, mid + shift, 16f)) continue;
                TryBin(group, s, mid + shift, s.centre > 0f ? 1 : -1, binGreen, binBlack, binBlue, random, ref bins, false);
            }
            for (float a = s.from + 10f; a < s.to - 10f; a += 26f)
            {
                if (NearJunction(s, a, 16f)) continue;
                for (int side = -1; side <= 1; side += 2)
                {
                    if (s.eastWest && Mathf.Approximately(s.centre, OceanZ) && side < 0) continue;
                    if (!isNew) continue;
                    if (lantern != null && ((int)(a / 26f) % 2 == 0)) Place(lantern, group, s, a + 6f, side, 7.65f, true);
                    else if (tree != null) Place(tree, group, s, a + 6f, side, 8.6f, false);
                }
            }
        }
        Physics.SyncTransforms();
        LightGaps(group, lantern);
        Debug.Log("[Test Drive] Street furniture: " + bins + " bins.");
    }

    private static bool NearJunction(Street s, float along, float d)
    {
        foreach (Vector2 j in Junctions)
        {
            Vector2 p = s.eastWest ? new Vector2(along, s.centre) : new Vector2(s.centre, along);
            if ((p - j).sqrMagnitude < d * d) return true;
        }
        return false;
    }

    private static Vector3 StreetPoint(Street s, float along, float lateral)
    {
        return s.eastWest ? new Vector3(along, 0f, s.centre + lateral) : new Vector3(s.centre + lateral, 0f, along);
    }

    /// <summary>Every walker's route in the scene (and the promenade lines), so street furniture stays off them.</summary>
    private static void CollectWalkLines()
    {
        FieldInfo f = typeof(ExteriorPedestrianAgent).GetField("waypoints", BindingFlags.Instance | BindingFlags.NonPublic);
        if (f != null)
            foreach (ExteriorPedestrianAgent w in Object.FindObjectsByType<ExteriorPedestrianAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var path = f.GetValue(w) as Transform[];
                if (path == null || path.Length < 2) continue;
                var line = new Vector3[path.Length + 1];
                for (int i = 0; i <= path.Length; i++) line[i] = path[i % path.Length] != null ? path[i % path.Length].position : Vector3.zero;
                walkLines.Add(line);
            }
        float x = OuterX - 20f;
        foreach (float lateral in new[] { WalkInner, WalkOuter, -WalkInner, -WalkOuter })
            walkLines.Add(new[] { new Vector3(-x, 0f, OceanZ + lateral), new Vector3(x, 0f, OceanZ + lateral) });
    }

    public static bool OnWalkLine(Vector3 p, float clearance)
    {
        foreach (Vector3[] line in walkLines)
            for (int i = 0; i + 1 < line.Length; i++)
            {
                Vector3 a = line[i], b = line[i + 1];
                Vector3 ab = new Vector3(b.x - a.x, 0f, b.z - a.z), ap = new Vector3(p.x - a.x, 0f, p.z - a.z);
                float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude) : 0f;
                if ((ap - ab * t).sqrMagnitude < clearance * clearance) return true;
            }
        return false;
    }

    private static bool Place(GameObject template, Transform parent, Street s, float along, int side, float lateral, bool faceRoad)
    {
        Vector3 p = StreetPoint(s, along, side * lateral);
        if (!Free(p, 0.6f) || OnWalkLine(p, 1.3f)) return false;
        GameObject copy = Object.Instantiate(template, parent);
        copy.transform.position = new Vector3(p.x, template.transform.position.y, p.z);
        // The lantern's arm reaches along its +Z: turn it over the carriageway.
        Vector3 toRoad = s.eastWest ? new Vector3(0f, 0f, -side) : new Vector3(-side, 0f, 0f);
        copy.transform.rotation = faceRoad ? Quaternion.LookRotation(toRoad, Vector3.up) : Quaternion.Euler(0f, along * 37f % 360f, 0f);
        return true;
    }

    /// <summary>
    /// Every street lit at night: wherever a stretch of any street (the original ones too) has no lantern within
    /// about 15 m, a lantern goes in, on alternate sides, so the whole city has an even, moderate street lighting
    /// with pools of light every 25-30 m rather than dark gaps; fewer on the Quest.
    /// </summary>
    private static void LightGaps(Transform parent, GameObject lantern)
    {
        if (lantern == null) return;
        var lamps = new List<Vector3>();
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (t.name.StartsWith("Warm LED Street Lantern", System.StringComparison.Ordinal)) lamps.Add(t.position);
        float spacing = quest ? 36f : 26f, reach = quest ? 19f : 15f;
        int added = 0;
        foreach (Street s in Streets)
        {
            int index = 0;
            for (float a = s.from + 8f; a < s.to - 8f; a += spacing, index++)
            {
                int side = index % 2 == 0 ? 1 : -1;
                if (s.eastWest && Mathf.Approximately(s.centre, OceanZ)) side = 1;      // the promenade side has its own lamps
                if (NearJunction(s, a, 11f)) continue;
                Vector3 want = StreetPoint(s, a, side * 7.65f);
                bool lit = false;
                foreach (Vector3 l in lamps)
                    if (new Vector2(l.x - want.x, l.z - want.z).sqrMagnitude < reach * reach) { lit = true; break; }
                if (lit) continue;
                foreach (float shift in new[] { 0f, 4f, -4f, 8f, -8f })
                {
                    if (NearJunction(s, a + shift, 11f)) continue;
                    if (!Place(lantern, parent, s, a + shift, side, 7.65f, true)) continue;
                    lamps.Add(StreetPoint(s, a + shift, side * 7.65f));
                    added++;
                    break;
                }
            }
        }
        Debug.Log("[Test Drive] Street lighting: " + added + " lanterns added where streets were dark.");
    }

    private static Material binGreenMat, binBlueMat, binBlackMat, binGreyMat, binWhiteMat;

    private static void BinMaterials()
    {
        if (binGreenMat != null) return;
        binGreenMat = Lit(new Color(0.07f, 0.22f, 0.13f), 0.55f, 0.55f);   // powder-coated park-department green
        binBlueMat = Lit(new Color(0.05f, 0.24f, 0.55f), 0.1f, 0.6f);      // recycling blue
        binBlackMat = Lit(new Color(0.035f, 0.035f, 0.04f), 0.3f, 0.45f);
        binGreyMat = Lit(new Color(0.35f, 0.36f, 0.37f), 0.85f, 0.6f);
        binWhiteMat = Lit(new Color(0.92f, 0.92f, 0.9f), 0f, 0.5f);
    }

    private static void TryBin(Transform parent, Street s, float along, int side, Material green, Material black, Material blue, System.Random random, ref int count, bool promenade)
    {
        BinMaterials();
        // At the very kerb edge of the sidewalk (it runs 9.8-13.8 m from the centre line), clear of the walkers,
        // the shop doors and anything solid.
        float lateral = 10.2f;
        Vector3 p = StreetPoint(s, along, side * lateral);
        foreach (ShopDoor d in ShopDoors) if ((d.position - p).sqrMagnitude < 9f) return;
        if (!Free(p, 0.6f) || OnWalkLine(p, 1.2f)) return;
        Vector3 toRoad = s.eastWest ? new Vector3(0f, 0f, -side) : new Vector3(-side, 0f, 0f);
        var bin = new GameObject("Street Litter Bin").transform;
        bin.SetParent(parent, false);
        bin.position = p + Vector3.up * 0.055f;
        // Its label faces the sidewalk (away from the road), where people use it.
        bin.rotation = Quaternion.LookRotation(-toRoad, Vector3.up);
        WheelieBins(bin, random);
        count++;
    }

    /// <summary>A city litter bin: slatted steel drum, rim, domed lid with a mouth, base ring and a label.</summary>
    private static void RoundBin(Transform bin, Material body, string label)
    {
        Cylinder(bin, "Bin Base", new Vector3(0f, 0.03f, 0f), 0.31f, 0.06f, binBlackMat);
        Cylinder(bin, "Bin Liner", new Vector3(0f, 0.47f, 0f), 0.27f, 0.86f, binBlackMat);
        for (int i = 0; i < 18; i++)
        {
            float a = i / 18f * Mathf.PI * 2f;
            var slat = Box(bin, "Bin Slat", new Vector3(Mathf.Sin(a) * 0.29f, 0.47f, Mathf.Cos(a) * 0.29f), new Vector3(0.07f, 0.84f, 0.025f), body, false);
            slat.transform.localRotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f);
        }
        Cylinder(bin, "Bin Band", new Vector3(0f, 0.2f, 0f), 0.305f, 0.05f, body);
        Cylinder(bin, "Bin Band", new Vector3(0f, 0.78f, 0f), 0.305f, 0.05f, body);
        Cylinder(bin, "Bin Rim", new Vector3(0f, 0.91f, 0f), 0.32f, 0.04f, binGreyMat);
        Cone(bin, "Bin Lid", new Vector3(0f, 0.93f, 0f), 0.32f, 0.16f, body);
        Box(bin, "Bin Mouth", new Vector3(0f, 0.98f, 0.2f), new Vector3(0.22f, 0.06f, 0.08f), binBlackMat, false);
        Box(bin, "Bin Plaque", new Vector3(0f, 0.55f, 0.305f), new Vector3(0.22f, 0.1f, 0.01f), binWhiteMat, false);
        Label(bin, label, new Vector3(0f, 0.55f, 0.312f), 0.2f, 0.08f, body == binBlueMat ? new Color(0.05f, 0.24f, 0.55f) : new Color(0.07f, 0.22f, 0.13f));
        var c = bin.gameObject.AddComponent<CapsuleCollider>();
        c.height = 1.1f; c.radius = 0.32f; c.center = new Vector3(0f, 0.55f, 0f);
    }

    /// <summary>A pair of 240-litre wheelie bins: trash (black or green lid) and recycling (blue), with handles and wheels.</summary>
    private static void WheelieBins(Transform bin, System.Random random)
    {
        Material[] bodies = { random.NextDouble() < 0.5 ? binBlackMat : binGreenMat, binBlueMat };
        for (int i = 0; i < 2; i++)
        {
            float x = (i - 0.5f) * 0.66f;
            Material body = bodies[i];
            // Slightly tapered body: a lower and an upper block.
            Box(bin, "Wheelie Bin Body", new Vector3(x, 0.32f, 0f), new Vector3(0.54f, 0.56f, 0.66f), body, false);
            Box(bin, "Wheelie Bin Body", new Vector3(x, 0.78f, 0.01f), new Vector3(0.58f, 0.4f, 0.7f), body, false);
            Box(bin, "Wheelie Bin Lid", new Vector3(x, 1.0f, 0.03f), new Vector3(0.62f, 0.045f, 0.76f), body, false);
            Box(bin, "Wheelie Bin Lid Lip", new Vector3(x, 0.975f, 0.4f), new Vector3(0.6f, 0.06f, 0.03f), body, false);
            Cylinder(bin, "Wheelie Bin Handle", new Vector3(x, 0.95f, -0.38f), 0.018f, 0.5f, binBlackMat).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Cylinder(bin, "Wheelie Bin Axle", new Vector3(x, 0.09f, -0.3f), 0.015f, 0.6f, binGreyMat).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            foreach (float w in new[] { -0.26f, 0.26f })
                Cylinder(bin, "Wheelie Bin Wheel", new Vector3(x + w, 0.09f, -0.3f), 0.09f, 0.05f, binBlackMat).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Box(bin, "Wheelie Bin Label", new Vector3(x, 0.72f, 0.365f), new Vector3(0.3f, 0.14f, 0.01f), binWhiteMat, false);
            Label(bin, i == 1 ? "RECYCLE" : "TRASH", new Vector3(x, 0.72f, 0.372f), 0.28f, 0.1f, i == 1 ? new Color(0.05f, 0.24f, 0.55f) : new Color(0.1f, 0.1f, 0.1f));
        }
        BoxCollider col = bin.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.52f, 0f);
        col.size = new Vector3(1.32f, 1.05f, 0.8f);
    }

    private static void Label(Transform parent, string text, Vector3 local, float width, float height, Color colour)
    {
        var go = new GameObject("Bin Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.05f;
        tmp.fontSizeMax = 1f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = colour;
        tmp.rectTransform.sizeDelta = new Vector2(width, height);
    }

    private static void Dumpster(Transform parent, Vector3 local, Quaternion rotation)
    {
        Material steel = Lit(new Color(0.1f, 0.3f, 0.2f), 0.4f, 0.3f), lid = Lit(new Color(0.08f, 0.08f, 0.08f), 0f, 0.2f);
        var d = new GameObject("Dumpster").transform;
        d.SetParent(parent, false);
        d.localPosition = local;
        d.localRotation = rotation;
        Box(d, "Dumpster Body", new Vector3(0f, 0.65f, 0f), new Vector3(1.9f, 1.2f, 1.25f), steel, true);
        Box(d, "Dumpster Lid", new Vector3(0f, 1.3f, -0.05f), new Vector3(1.95f, 0.08f, 1.3f), lid, false).transform.localRotation = Quaternion.Euler(-6f, 0f, 0f);
    }

    /// <summary>Nothing solid at the spot, except ground.</summary>
    public static bool Free(Vector3 p, float radius)
    {
        Collider[] around = Physics.OverlapCapsule(p + Vector3.up * (radius + 0.15f), p + Vector3.up * 2.2f, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach (Collider c in around)
        {
            string n = c.name;
            if (n.Contains("Ground") || n.Contains("Asphalt") || n.Contains("Sidewalk") || n.Contains("Sand") || n.Contains("Plaza") || n.Contains("Paving")) continue;
            if (c is CharacterController) continue;
            return false;
        }
        return true;
    }

    private static bool InTree(Transform t)
    {
        for (; t != null; t = t.parent) if (t.name == "Street Tree") return true;
        return false;
    }

    /// <summary>Static batching for everything built here that never moves, except the shop windows (their shader
    /// works in each pane's own space) and the signs (TextMeshPro keeps its own mesh).</summary>
    private static void Batch(Transform top)
    {
        var list = new List<GameObject>();
        foreach (MeshRenderer r in top.GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = r.name;
            if (n == "Storefront Reflective Glazing" || n == "Shop Entrance Glass" || n == "Shop Sign" || n == "Sea" || n == "Facade Mass" || n == "Entrance Door Pull" || r.GetComponent<TextMeshPro>() != null) continue;
            if (r.GetComponentInParent<TestDriveBirds>() != null) continue;
            if (r.GetComponentInParent<Light>() != null || r.transform.parent != null && r.transform.parent.GetComponentInChildren<Light>() != null) continue;
            if (r.GetComponent<MeshFilter>() == null) continue;
            if (r.GetComponentInParent<Transform>() != null && InTree(r.transform)) continue;
            if (!r.GetComponent<MeshFilter>().sharedMesh || !r.GetComponent<MeshFilter>().sharedMesh.isReadable) continue;
            list.Add(r.gameObject);
        }
        if (list.Count > 0) StaticBatchingUtility.Combine(list.ToArray(), top.gameObject);
    }

    // ------------------------------------------------------------------ primitives

    private static Transform Group(string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(root, false);
        return t;
    }

    private static GameObject Box(Transform parent, string name, Vector3 local, Vector3 size, Material material, bool collider)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (!collider) Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;
        go.isStatic = true;
        return go;
    }

    private static GameObject Cylinder(Transform parent, string name, Vector3 local, float radius, float height, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    private static Mesh coneMesh;

    private static GameObject Cone(Transform parent, string name, Vector3 local, float radius, float height, Material material)
    {
        if (coneMesh == null)
        {
            // Outer surface and underside with their own vertices and normals (a two-sided cone on shared vertices
            // has normals that cancel to zero, which lights as NaN and blooms into white blobs).
            coneMesh = new Mesh { name = "Cone" };
            int n = 24;
            var v = new List<Vector3>();
            var nm = new List<Vector3>();
            var tri = new List<int>();
            for (int i = 0; i < n; i++)
            {
                float a0 = i / (float)n * Mathf.PI * 2f, a1 = (i + 1) / (float)n * Mathf.PI * 2f, am = (a0 + a1) * 0.5f;
                Vector3 r0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), r1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 n0 = new Vector3(r0.x, 0.6f, r0.z).normalized, n1 = new Vector3(r1.x, 0.6f, r1.z).normalized, nmid = new Vector3(Mathf.Cos(am), 0.6f, Mathf.Sin(am)).normalized;
                int k = v.Count;
                v.Add(Vector3.up); nm.Add(nmid);
                v.Add(r0); nm.Add(n0);
                v.Add(r1); nm.Add(n1);
                tri.Add(k); tri.Add(k + 2); tri.Add(k + 1);
                k = v.Count;
                v.Add(Vector3.up * 0.02f); nm.Add(Vector3.down);
                v.Add(r0); nm.Add(Vector3.down);
                v.Add(r1); nm.Add(Vector3.down);
                tri.Add(k); tri.Add(k + 1); tri.Add(k + 2);
            }
            coneMesh.SetVertices(v); coneMesh.SetNormals(nm); coneMesh.SetTriangles(tri, 0); coneMesh.RecalculateBounds();
        }
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = new Vector3(radius, height, radius);
        go.AddComponent<MeshFilter>().sharedMesh = coneMesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }
}
