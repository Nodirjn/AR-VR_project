using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Many kinds of vehicle on the city's roads instead of three. At load, extra traffic joins every route the scene
/// already drives (copies of its own traffic agents, so they follow lanes, stop at signals and yield exactly the
/// same way), each carrying one of the open-licence models in <c>Resources/TestDrive/Traffic</c>: saloons,
/// hatchbacks, estates, SUVs, a pickup, a minivan, a sports car, taxi, police car, ambulance, post van, utility and tow
/// trucks, a city bus, a school bus, a refuse truck and a fire engine ("Generic passenger car pack" and "Generic
/// civil service vehicles pack" by comrade1280, CC BY 4.0). Some parked cars get new models too.
/// </summary>
public static class TestDriveTrafficVariety
{
    private static readonly FieldInfo RouteField = typeof(ExteriorTrafficCarAgent).GetField("route", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo SignalsField = typeof(ExteriorTrafficCarAgent).GetField("signals", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly List<ExteriorTrafficCarAgent> spawned = new List<ExteriorTrafficCarAgent>();
    private static readonly FieldInfo LengthField = typeof(ExteriorTrafficCarAgent).GetField("bodyLength", BindingFlags.Instance | BindingFlags.NonPublic);
    private static bool installed;

    public static int Added { get; private set; }

    public static void Install()
    {
        if (installed) return;
        installed = true;
        GameObject[] loaded = Resources.LoadAll<GameObject>("TestDrive/Traffic");
        if (loaded == null || loaded.Length == 0)
        {
            Debug.LogWarning("[Test Drive] No models in Resources/TestDrive/Traffic; traffic keeps its three cars.");
            return;
        }
        var models = new List<GameObject>(loaded);
        var cars = models.FindAll(m => !IsLarge(m.name));
        Shuffle(models, 11);
        Shuffle(cars, 23);
        bool quest = QualitySettings.GetQualityLevel() <= 1 || Application.platform == RuntimePlatform.Android;

        var agents = new List<ExteriorTrafficCarAgent>(Object.FindObjectsByType<ExteriorTrafficCarAgent>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        if (agents.Count == 0 || RouteField == null) return;
        var holderObject = new GameObject("Traffic Variety Staging");
        holderObject.SetActive(false);
        Transform holder = holderObject.transform;

        // Group the agents by route; every route gets extra vehicles spread along it.
        var byRoute = new Dictionary<Transform[], List<ExteriorTrafficCarAgent>>();
        foreach (ExteriorTrafficCarAgent a in agents)
        {
            var route = RouteField.GetValue(a) as Transform[];
            if (route == null || route.Length < 2) continue;
            List<ExteriorTrafficCarAgent> list;
            if (!byRoute.TryGetValue(route, out list)) byRoute[route] = list = new List<ExteriorTrafficCarAgent>();
            list.Add(a);
        }
        var positions = new List<Vector3>();
        foreach (ExteriorTrafficCarAgent a in agents) positions.Add(a.transform.position);
        int pick = 0;
        foreach (var pair in byRoute)
        {
            Transform[] route = pair.Key;
            int extra = quest ? 1 : 2;
            for (int k = 0; k < extra; k++)
            {
                GameObject model = models[pick++ % models.Count];
                ExteriorTrafficCarAgent clone = Spawn(pair.Value[0], model, holder, route, positions);
                if (clone != null)
                {
                    // Each driver keeps their own pace, so the column spreads out instead of travelling as a train.
                    float cruise = CruiseOf(pair.Value[0]) * (0.78f + 0.11f * (k % 4));
                    clone.Configure(route, SignalsField != null ? SignalsField.GetValue(pair.Value[0]) as ExteriorTrafficSignal[] : null, cruise, 0f);
                    clone.SetStartIndex(ExteriorTrafficCarAgent.NearestTargetIndex(route, clone.transform.position, clone.transform.forward));
                    Added++;
                    spawned.Add(clone);
                }
            }
        }
        // Traffic on every street, both ways: a loop round each block of the street grid in each direction, and the
        // long loop round the whole city by the sea.
        if (TestDriveCity.Streets.Exists(st => Mathf.Approximately(st.centre, TestDriveCity.OceanZ)))
        {
            float o = TestDriveCity.OuterX, sea = TestDriveCity.OceanZ;
            var blocks = new[]
            {
                new Vector4(-96f, 96f, -47f, 60f), new Vector4(-96f, 96f, sea, -47f), new Vector4(96f, o, -47f, 60f), new Vector4(96f, o, sea, -47f),
                new Vector4(-o, -96f, -47f, 60f), new Vector4(-o, -96f, sea, -47f), new Vector4(-o, o, sea, 60f)
            };
            var signals = SignalsField != null ? SignalsField.GetValue(agents[0]) as ExteriorTrafficSignal[] : null;
            var random = new System.Random(3);
            foreach (Vector4 b in blocks)
            {
                bool outer = Mathf.Approximately(b.x, -o) && Mathf.Approximately(b.y, o);
                for (int dir = 0; dir < 2; dir++)
                {
                    Transform[] loop = RectLoop(b.x, b.y, b.z, b.w, dir == 0);
                    int n = outer ? (quest ? 2 : 4) : (quest ? 1 : 2);
                    for (int k = 0; k < n; k++)
                    {
                        GameObject model = models[pick++ % models.Count];
                        ExteriorTrafficCarAgent clone = Spawn(agents[0], model, holder, loop, positions);
                        if (clone == null) continue;
                        clone.Configure(loop, signals, 6.5f + (float)random.NextDouble() * 4f, 0f);
                        clone.SetStartIndex(ExteriorTrafficCarAgent.NearestTargetIndex(loop, clone.transform.position, clone.transform.forward));
                        Added++;
                        spawned.Add(clone);
                    }
                }
            }
        }
        Object.Destroy(holderObject);
        // A few of the cars (not the buses and lorries) run errands: pull in, the driver pops into a shop.
        var errands = new List<ExteriorTrafficCarAgent>();
        foreach (ExteriorTrafficCarAgent a in spawned) if (a != null && !IsLarge(a.name)) errands.Add(a);
        TestDriveCurbStops.Install(null, errands);

        // Parked cars along the kerbs: every other one becomes another make (cars only, no buses at the kerb).
        int parked = 0, index = 0;
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (t.name != "Parked Customer Vehicle") continue;
            if ((index++ & 1) == 1) continue;
            if (ReplaceVisual(t, cars[parked % cars.Count])) parked++;
        }
        Debug.Log("[Test Drive] Traffic variety: " + Added + " vehicles added (" + models.Count + " models), " + parked + " parked cars restyled.");
    }

    private static float CruiseOf(ExteriorTrafficCarAgent a)
    {
        FieldInfo f = typeof(ExteriorTrafficCarAgent).GetField("cruisingSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
        return f != null ? (float)f.GetValue(a) : 7.5f;
    }

    /// <summary>A loop round a block on the lanes 1.6 m right of the centre lines (right-hand traffic), either way round.</summary>
    private static Transform[] RectLoop(float x0, float x1, float z0, float z1, bool anticlockwise)
    {
        const float lane = 1.6f;
        Vector3[] corners = anticlockwise
            ? new[] { new Vector3(x0 - lane, 0f, z0 - lane), new Vector3(x1 + lane, 0f, z0 - lane), new Vector3(x1 + lane, 0f, z1 + lane), new Vector3(x0 - lane, 0f, z1 + lane) }
            : new[] { new Vector3(x0 + lane, 0f, z0 + lane), new Vector3(x0 + lane, 0f, z1 - lane), new Vector3(x1 - lane, 0f, z1 - lane), new Vector3(x1 - lane, 0f, z0 + lane) };
        return Loop("Block Loop " + x0 + "," + z0 + (anticlockwise ? " A" : " B"), corners, 10f);
    }

    private static Transform[] Loop(string name, Vector3[] corners, float spacing)
    {
        var parent = new GameObject(name).transform;
        var points = new List<Transform>();
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 a = corners[i], b = corners[(i + 1) % corners.Length];
            int steps = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / spacing));
            for (int j = 0; j < steps; j++)
            {
                var t = new GameObject("Waypoint " + points.Count).transform;
                t.SetParent(parent, false);
                t.position = Vector3.Lerp(a, b, j / (float)steps);
                points.Add(t);
            }
        }
        return points.ToArray();
    }

    private static bool IsLarge(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains("bus") || n.Contains("garbage") || n.Contains("fire") || n.Contains("tow") || n.Contains("ambulance") || n.Contains("utility");
    }

    private static void Shuffle<T>(List<T> list, int seed)
    {
        var random = new System.Random(seed);
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }

    private static ExteriorTrafficCarAgent Spawn(ExteriorTrafficCarAgent template, GameObject model, Transform holder,
        Transform[] route, List<Vector3> taken)
    {
        // The waypoint farthest from every vehicle already on the roads.
        int best = -1;
        float bestDistance = 0f;
        for (int i = 0; i < route.Length; i++)
        {
            if (route[i] == null) continue;
            float nearest = float.MaxValue;
            foreach (Vector3 p in taken) nearest = Mathf.Min(nearest, (route[i].position - p).sqrMagnitude);
            if (nearest > bestDistance) { bestDistance = nearest; best = i; }
        }
        if (best < 0 || bestDistance < 28f * 28f) return null;
        Transform from = route[best], to = route[(best + 1) % route.Length];
        if (to == null) return null;

        GameObject copy = Object.Instantiate(template.gameObject, holder);
        copy.name = "Traffic Vehicle (" + model.name + ")";
        // Drop the template's body (its model, and anything rigged on it) and put the new one in its place.
        for (int i = copy.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(copy.transform.GetChild(i).gameObject);
        GameObject visual = Object.Instantiate(model, copy.transform, false);
        visual.name = "Imported Vehicle Model";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;
        copy.transform.localScale = Vector3.one;
        foreach (Collider c in visual.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

        ExteriorTrafficCarAgent agent = copy.GetComponent<ExteriorTrafficCarAgent>();
        Bounds b;
        if (LengthField != null && ModelBounds(visual.transform, out b)) LengthField.SetValue(agent, Mathf.Max(3f, b.size.z));
        Vector3 forward = Vector3.ProjectOnPlane(to.position - from.position, Vector3.up);
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        copy.transform.SetParent(template.transform.parent, false);
        copy.transform.SetPositionAndRotation(new Vector3(from.position.x, template.transform.position.y, from.position.z), Quaternion.LookRotation(forward.normalized, Vector3.up));
        agent.SetStartIndex((System.Array.IndexOf(route, from) + 1) % route.Length);
        copy.SetActive(true);
        taken.Add(from.position);
        return agent;
    }

    private static bool ReplaceVisual(Transform parked, GameObject model)
    {
        Bounds old;
        if (!ModelBounds(parked, out old)) return false;
        for (int i = parked.childCount - 1; i >= 0; i--)
        {
            Transform child = parked.GetChild(i);
            if (child.GetComponentInChildren<Renderer>(true) != null) Object.Destroy(child.gameObject);
        }
        foreach (Renderer r in parked.GetComponents<Renderer>()) r.enabled = false;
        GameObject visual = Object.Instantiate(model, parked, false);
        visual.name = "Imported Vehicle Model";
        // Same footprint centre and ground as the car it replaces, nose the same way; unit world scale.
        Vector3 s = parked.lossyScale;
        visual.transform.localScale = new Vector3(1f / Mathf.Max(1e-4f, s.x), 1f / Mathf.Max(1e-4f, s.y), 1f / Mathf.Max(1e-4f, s.z));
        visual.transform.position = parked.TransformPoint(new Vector3(old.center.x, old.min.y, old.center.z));
        visual.transform.localRotation = Quaternion.identity;
        foreach (Collider c in visual.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        return true;
    }

    private static bool ModelBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        Matrix4x4 toLocal = root.worldToLocalMatrix;
        foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (f.sharedMesh == null) continue;
            Bounds b = TestDriveWheelRig.CarBox(toLocal * f.transform.localToWorldMatrix, f.sharedMesh.bounds);
            if (!found) { bounds = b; found = true; }
            else bounds.Encapsulate(b);
        }
        return found;
    }
}
