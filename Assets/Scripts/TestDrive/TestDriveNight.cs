using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Night lighting for the city while a test drive is on: every car's headlamps and tail lamps (the test car's,
/// the moving traffic's and the parked cars' sidelights), the pool of light a headlight throws on the road ahead,
/// a faint beam in the air, and the street lanterns turned into downlights with their own pool on the asphalt and a
/// glowing lamp head. Real lights do the lighting where it counts (the test car's lamps, the lanterns, the nearest
/// traffic); additive glow meshes (<c>Resources/TestDriveGlow.shader</c>) carry the look everywhere else, so a
/// street full of lit cars costs almost nothing on the headset. It follows <see cref="TestDriveDayNight.Daylight"/>.
/// </summary>
public sealed class TestDriveNight : MonoBehaviour
{
    private static readonly Color HeadColour = new Color(1f, 0.93f, 0.8f);
    private static readonly Color TailColour = new Color(1f, 0.08f, 0.04f);

    private sealed class CarLights
    {
        public Transform car;
        public TestDriveVehicle vehicle;       // the test car: its own switch decides
        public GameObject glow;                // lenses, pool, beams
        public Light[] spots = new Light[0];   // real lights, only on the nearest cars
        public float distance;
    }

    private sealed class Lantern
    {
        public Light light;
        public LightType type;
        public Quaternion rotation;
        public float spotAngle, innerAngle;
        public GameObject glow;
    }

    private TestDriveDayNight world;
    private Material lensMaterial, poolMaterial, beamMaterial, tailMaterial, lanternPoolMaterial, lanternHeadMaterial, lanternBeamMaterial;
    private readonly List<CarLights> cars = new List<CarLights>();
    private readonly List<Lantern> lanterns = new List<Lantern>();
    private bool running, quest, lanternsReady;
    private float level = -1f, nextSort;
    private Transform head;

    public static TestDriveNight Create(Transform parent, TestDriveDayNight world)
    {
        var go = new GameObject("Test Drive Night Lights");
        go.transform.SetParent(parent, false);
        TestDriveNight night = go.AddComponent<TestDriveNight>();
        night.world = world;
        night.quest = QualitySettings.GetQualityLevel() <= 1 || Application.platform == RuntimePlatform.Android;
        night.enabled = false;
        return night;
    }

    public void SetRunning(bool on, TestDriveVehicle testCar)
    {
        running = on;
        enabled = on;
        if (on)
        {
            if (!EnsureMaterials()) { enabled = false; return; }
            if (!lanternsReady) PrepareLanterns();
            Collect(testCar);
            level = -1f;
        }
        else
        {
            ApplyLevel(0f);
            RestoreLanterns();
        }
    }

    public void SetHead(Transform viewer) => head = viewer;

    // ------------------------------------------------------------------ materials and meshes

    private bool EnsureMaterials()
    {
        if (lensMaterial != null) return true;
        Shader shader = Resources.Load<Shader>("TestDriveGlow");
        if (shader == null || !shader.isSupported)
        {
            Debug.LogWarning("[Test Drive] Resources/TestDriveGlow.shader is missing; night glow is off (real lights still work).");
            return false;
        }
        Texture2D radial = RadialTexture(64), beam = BeamTexture(48, 96), spot = RadialTexture(32, 3f);
        lensMaterial = Glow(shader, spot, HeadColour * 6f, 0f, 0.5f);
        tailMaterial = Glow(shader, spot, TailColour * 4f, 0f, 0.5f);
        poolMaterial = Glow(shader, beam, HeadColour * 0.55f, 0f, 1f);
        beamMaterial = Glow(shader, beam, HeadColour * 0.05f, 2f, 2.5f);
        lanternPoolMaterial = Glow(shader, radial, new Color(1f, 0.78f, 0.52f) * 0.42f, 0f, 1f);
        lanternHeadMaterial = Glow(shader, spot, new Color(1f, 0.85f, 0.6f) * 5f, 0f, 0.5f);
        lanternBeamMaterial = Glow(shader, radial, new Color(1f, 0.8f, 0.55f) * 0.035f, 1.5f, 3f);
        return true;
    }

    private static Material Glow(Shader shader, Texture texture, Color colour, float edge, float nearFade)
    {
        var m = new Material(shader) { name = "Test Drive Glow", mainTexture = texture };
        m.SetColor("_Color", colour);
        m.SetFloat("_Edge", edge);
        m.SetFloat("_NearFade", nearFade);
        return m;
    }

    /// <summary>Soft round spot: 1 in the middle, smooth to 0 at the rim.</summary>
    private static Texture2D RadialTexture(int size, float sharpness = 1.6f)
    {
        var t = new Texture2D(size, size, TextureFormat.R8, false) { wrapMode = TextureWrapMode.Clamp, name = "Glow Radial" };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float v = Mathf.Pow(Mathf.Clamp01(1f - r), sharpness);
                px[y * size + x] = new Color(v, v, v, 1f);
            }
        t.SetPixels(px);
        t.Apply(false, true);
        return t;
    }

    /// <summary>A headlamp's throw: brightest a few metres ahead, wide, fading out with distance (v along, u across).</summary>
    private static Texture2D BeamTexture(int w, int h)
    {
        var t = new Texture2D(w, h, TextureFormat.R8, false) { wrapMode = TextureWrapMode.Clamp, name = "Glow Beam" };
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = (y + 0.5f) / h, u = (x + 0.5f) / w * 2f - 1f;
                float along = Mathf.SmoothStep(0f, 1f, v / 0.12f) * Mathf.Pow(1f - v, 1.6f);
                float across = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(u)), 1.2f);
                float value = Mathf.Clamp01(along * across * 1.6f);
                px[y * w + x] = new Color(value, value, value, 1f);
            }
        t.SetPixels(px);
        t.Apply(false, true);
        return t;
    }

    private static Mesh Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        var m = new Mesh { name = "Glow Quad" };
        m.vertices = new[] { a, b, c, d };
        m.normals = new[] { normal, normal, normal, normal };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateBounds();
        return m;
    }

    /// <summary>Open cone along +Z from <paramref name="r0"/> to <paramref name="r1"/>; UV v along the length.</summary>
    private static Mesh Cone(float r0, float r1, float length, int segments)
    {
        var v = new List<Vector3>();
        var n = new List<Vector3>();
        var uv = new List<Vector2>();
        var tri = new List<int>();
        for (int i = 0; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            v.Add(dir * r0); n.Add(dir); uv.Add(new Vector2(i / (float)segments, 0f));
            v.Add(dir * r1 + Vector3.forward * length); n.Add(dir); uv.Add(new Vector2(i / (float)segments, 1f));
        }
        for (int i = 0; i < segments; i++)
        {
            int k = i * 2;
            tri.Add(k); tri.Add(k + 1); tri.Add(k + 2);
            tri.Add(k + 1); tri.Add(k + 3); tri.Add(k + 2);
        }
        var m = new Mesh { name = "Glow Cone" };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
        m.RecalculateBounds();
        return m;
    }

    private static GameObject Part(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Quaternion rotation)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return go;
    }

    // ------------------------------------------------------------------ cars

    private void Collect(TestDriveVehicle testCar)
    {
        cars.RemoveAll(c => c.car == null);
        if (testCar != null && cars.Find(c => c.car == testCar.transform) == null)
            cars.Add(Build(testCar.transform, testCar, testCar.Headlights));
        foreach (ExteriorTrafficCarAgent agent in Object.FindObjectsByType<ExteriorTrafficCarAgent>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (cars.Find(c => c.car == agent.transform) != null) continue;
            CarLights lights = Build(agent.transform, null, null);
            if (lights != null) cars.Add(lights);
        }
    }

    private CarLights Build(Transform car, TestDriveVehicle vehicle, Light[] ownSpots)
    {
        Bounds local;
        if (!LocalBounds(car, out local) || local.size.z < 2.5f) return null;
        var lights = new CarLights { car = car, vehicle = vehicle };
        var glow = new GameObject("Night Glow");
        glow.transform.SetParent(car, false);
        lights.glow = glow;
        Transform g = glow.transform;

        // Lamp positions: the model's own markers (the generic traffic models carry them), else the usual layout.
        float halfW = local.extents.x;
        float lampY = Mathf.Clamp(local.min.y + local.size.y * 0.45f, local.min.y + 0.55f, local.min.y + 0.95f);
        Vector3 headL = Marker(car, "Headlight Left", new Vector3(local.center.x - (halfW - 0.33f), lampY, local.max.z - 0.12f));
        Vector3 headR = Marker(car, "Headlight Right", new Vector3(local.center.x + (halfW - 0.33f), lampY, local.max.z - 0.12f));
        Vector3 tailL = Marker(car, "Taillight Left", new Vector3(local.center.x - (halfW - 0.25f), lampY + 0.12f, local.min.z + 0.08f));
        Vector3 tailR = Marker(car, "Taillight Right", new Vector3(local.center.x + (halfW - 0.25f), lampY + 0.12f, local.min.z + 0.08f));
        float ground = local.min.y + 0.035f;

        Mesh lens = Quad(new Vector3(-0.17f, -0.1f, 0f), new Vector3(0.17f, -0.1f, 0f), new Vector3(0.17f, 0.1f, 0f), new Vector3(-0.17f, 0.1f, 0f), Vector3.forward);
        Mesh tail = Quad(new Vector3(-0.13f, -0.07f, 0f), new Vector3(0.13f, -0.07f, 0f), new Vector3(0.13f, 0.07f, 0f), new Vector3(-0.13f, 0.07f, 0f), Vector3.back);
        Part("Lens L", g, lens, lensMaterial, headL + Vector3.forward * 0.04f, Quaternion.identity);
        Part("Lens R", g, lens, lensMaterial, headR + Vector3.forward * 0.04f, Quaternion.identity);
        Part("Tail L", g, tail, tailMaterial, tailL - Vector3.forward * 0.04f, Quaternion.Euler(0f, 180f, 0f));
        Part("Tail R", g, tail, tailMaterial, tailR - Vector3.forward * 0.04f, Quaternion.Euler(0f, 180f, 0f));

        // The pool on the road: a trapezoid from just ahead of the bumper, widening to a lane and a half at 18 m.
        float front = local.max.z + 0.6f, far = front + 17f;
        float centre = local.center.x;
        Mesh pool = Quad(new Vector3(centre - 1.1f, 0f, front), new Vector3(centre + 1.1f, 0f, front),
            new Vector3(centre + 3.6f, 0f, far), new Vector3(centre - 3.6f, 0f, far), Vector3.up);
        Part("Road Pool", g, pool, poolMaterial, new Vector3(0f, ground, 0f), Quaternion.identity);
        // Faint beams through the air.
        Mesh cone = Cone(0.1f, 1.5f, 11f, 14);
        Quaternion down = Quaternion.Euler(4f, 0f, 0f);
        Part("Beam L", g, cone, beamMaterial, headL + Vector3.forward * 0.05f, down);
        Part("Beam R", g, cone, beamMaterial, headR + Vector3.forward * 0.05f, down);

        if (ownSpots != null) lights.spots = ownSpots;
        else
        {
            // One real lamp per traffic car (between the headlamps), switched on only for the nearest few.
            var go = new GameObject("Traffic Headlight");
            go.transform.SetParent(car, false);
            go.transform.localPosition = (headL + headR) * 0.5f + Vector3.forward * 0.1f;
            go.transform.localRotation = Quaternion.Euler(5f, 0f, 0f);
            Light l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = HeadColour;
            l.intensity = 160f;
            l.range = 45f;
            l.spotAngle = 70f;
            l.innerSpotAngle = 30f;
            l.shadows = LightShadows.None;
            l.enabled = false;
            lights.spots = new[] { l };
        }
        glow.SetActive(false);
        return lights;
    }

    private static Vector3 Marker(Transform car, string name, Vector3 fallback)
    {
        foreach (Transform t in car.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return car.InverseTransformPoint(t.position);
        return fallback;
    }

    private static bool LocalBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        Matrix4x4 toLocal = root.worldToLocalMatrix;
        foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>())
        {
            if (r.name.StartsWith("Lens") || r.name.StartsWith("Tail") || r.name.StartsWith("Beam") || r.name == "Road Pool") continue;
            if (r.transform.IsChildOf(root) && r.GetComponentInParent<TestDriveDriver>() != null) continue;
            MeshFilter f = r.GetComponent<MeshFilter>();
            if (f == null || f.sharedMesh == null) continue;
            Bounds b = TestDriveWheelRig.CarBox(toLocal * r.transform.localToWorldMatrix, f.sharedMesh.bounds);
            if (!found) { bounds = b; found = true; }
            else bounds.Encapsulate(b);
        }
        return found;
    }

    // ------------------------------------------------------------------ street lanterns

    private void PrepareLanterns()
    {
        lanternsReady = true;
        Mesh pool = Quad(new Vector3(-7.5f, 0f, -7.5f), new Vector3(7.5f, 0f, -7.5f), new Vector3(7.5f, 0f, 7.5f), new Vector3(-7.5f, 0f, 7.5f), Vector3.up);
        Mesh headQuad = Quad(new Vector3(-0.28f, 0f, -0.7f), new Vector3(0.28f, 0f, -0.7f), new Vector3(0.28f, 0f, 0.7f), new Vector3(-0.28f, 0f, 0.7f), Vector3.down);
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (l.name != "Warm Road LED") continue;
            var lantern = new Lantern { light = l, type = l.type, rotation = l.transform.rotation, spotAngle = l.spotAngle, innerAngle = l.innerSpotAngle };
            var glow = new GameObject("Lantern Glow");
            glow.transform.SetParent(l.transform.parent, true);
            glow.transform.position = l.transform.position;
            glow.transform.rotation = Quaternion.identity;
            float groundY = 0.03f;
            RaycastHit hit;
            if (Physics.Raycast(l.transform.position + Vector3.down * 0.5f, Vector3.down, out hit, 15f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                groundY = hit.point.y + 0.04f;
            float drop = l.transform.position.y - groundY;
            Part("Lantern Pool", glow.transform, pool, lanternPoolMaterial, new Vector3(0f, -drop, 0f), Quaternion.identity);
            Part("Lantern Head", glow.transform, headQuad, lanternHeadMaterial, new Vector3(0f, 0.07f, 0f), Quaternion.identity);
            Part("Lantern Beam", glow.transform, Cone(0.25f, 4.2f, drop, 16), lanternBeamMaterial, Vector3.zero, Quaternion.LookRotation(Vector3.down, Vector3.forward));
            glow.SetActive(false);
            lantern.glow = glow;
            lanterns.Add(lantern);
        }
    }

    private void RestoreLanterns()
    {
        foreach (Lantern l in lanterns)
        {
            if (l.light == null) continue;
            l.light.type = l.type;
            l.light.transform.rotation = l.rotation;
            l.light.spotAngle = l.spotAngle;
            l.light.innerSpotAngle = l.innerAngle;
            if (l.glow != null) l.glow.SetActive(false);
        }
    }

    // ------------------------------------------------------------------ frame

    private void Update()
    {
        if (!running || world == null) return;
        float target = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.55f, world.Daylight));
        if (Mathf.Abs(target - level) > 0.02f) ApplyLevel(target);

        // Real lamps only on the nearest traffic: a pool of light from the test car and a handful of others is what
        // the eye reads; the rest carry the glow alone.
        if (Time.time >= nextSort && level > 0.05f && head != null)
        {
            nextSort = Time.time + 0.5f;
            Vector3 eye = head.position;
            var traffic = new List<CarLights>();
            foreach (CarLights c in cars)
            {
                if (c.car == null || c.vehicle != null) continue;
                c.distance = (c.car.position - eye).sqrMagnitude;
                traffic.Add(c);
            }
            traffic.Sort((a, b) => a.distance.CompareTo(b.distance));
            int budget = quest ? 2 : 8;
            for (int i = 0; i < traffic.Count; i++)
                foreach (Light l in traffic[i].spots)
                    if (l != null) l.enabled = i < budget && traffic[i].car.gameObject.activeInHierarchy;
        }
        // The test car's glow follows its own light switch.
        foreach (CarLights c in cars)
            if (c.vehicle != null && c.glow != null) c.glow.SetActive(c.vehicle.LightsOn);
    }

    private void ApplyLevel(float value)
    {
        level = value;
        bool on = value > 0.05f;
        foreach (CarLights c in cars)
        {
            if (c.car == null || c.vehicle != null) continue;
            if (c.glow != null) c.glow.SetActive(on);
            if (!on) foreach (Light l in c.spots) if (l != null) l.enabled = false;
        }
        foreach (Lantern l in lanterns)
        {
            if (l.light == null) continue;
            if (on)
            {
                // A downlight: the lantern's head points at the road, so the light lands as a pool on the asphalt.
                l.light.type = LightType.Spot;
                l.light.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                l.light.spotAngle = 135f;
                l.light.innerSpotAngle = 70f;
            }
            if (l.glow != null) l.glow.SetActive(on);
        }
        float fade = Mathf.Clamp01(value);
        if (lanternPoolMaterial != null)
        {
            lanternPoolMaterial.SetColor("_Color", new Color(1f, 0.78f, 0.52f) * 0.42f * fade);
            lanternHeadMaterial.SetColor("_Color", new Color(1f, 0.85f, 0.6f) * 5f * fade);
            lanternBeamMaterial.SetColor("_Color", new Color(1f, 0.8f, 0.55f) * 0.035f * fade);
        }
    }
}
