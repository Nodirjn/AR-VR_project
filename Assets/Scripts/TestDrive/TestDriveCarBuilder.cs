using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns a copy of a showroom display car into a <see cref="TestDriveVehicle"/>: the copy is set nose along +Z with
/// its wheels on y = 0, its wheels are rigged and given wheel colliders, its driver's door gets a hinge (the model's
/// own door node, or a door cut out of a one-piece body mesh), the steering wheel a pivot, and the driver's eye point
/// is found from the cabin geometry (seat cushion and headlining). Everything is done once per car, at run time.
/// </summary>
public static class TestDriveCarBuilder
{
    private const float DoorDepth = 0.17f;
    private const float SuspensionTarget = 0.5f;

    public static TestDriveVehicle Build(GameObject template, TestDriveCarProfile profile)
    {
        var rootObject = new GameObject("Test Drive Car - " + profile.DisplayName);
        rootObject.SetActive(false);
        Transform root = rootObject.transform;
        template.transform.SetParent(root, true);
        template.name = "Model";
        StripShowroomComponents(template);
        template.SetActive(true);

        Normalise(root, template.transform, profile);
        List<TestDriveWheelRig.Wheel> rig = TestDriveWheelRig.Build(root);

        Bounds body = ModelBounds(root, template.transform, true);
        var vehicle = rootObject.AddComponent<TestDriveVehicle>();
        vehicle.Profile = profile;
        vehicle.HalfLength = body.size.z * 0.5f;
        vehicle.Height = body.max.y;

        // Axles from the rigged wheels, or typical proportions when the model has no recognisable wheels.
        float frontZ = 0f, rearZ = 0f, radius = 0f, track = 0f;
        int fronts = 0, rears = 0;
        for (int i = 0; i < rig.Count; i++)
        {
            radius += rig[i].radius;
            track += Mathf.Abs(rig[i].hub.x) * 2f;
            if (rig[i].front) { frontZ += rig[i].hub.z; fronts++; }
            else { rearZ += rig[i].hub.z; rears++; }
        }
        bool rigged = fronts == 2 && rears == 2;
        if (rigged)
        {
            frontZ /= fronts;
            rearZ /= rears;
            radius /= rig.Count;
            track /= rig.Count;
        }
        else
        {
            if (rig.Count > 0) Debug.LogWarning("[Test Drive] " + profile.DisplayName + ": found " + rig.Count +
                                                " wheels; the visible wheels will not turn.");
            frontZ = body.max.z - body.size.z * 0.19f;
            rearZ = body.min.z + body.size.z * 0.2f;
            radius = 0.32f;
            track = body.size.x * 0.82f;
        }
        vehicle.WheelBase = frontZ - rearZ;
        vehicle.Track = track;
        vehicle.WheelRadius = radius;

        List<MeshRenderer> bodyRenderers = BodyRenderers(root);
        SideProfile side = SideProfile.Measure(bodyRenderers, root, rearZ + radius + 0.2f, frontZ - radius - 0.2f, body.max.y);
        vehicle.HalfWidth = side.HalfWidth(body.max.y * 0.45f, body.extents.x);

        BuildSteering(root, template.transform, vehicle, bodyRenderers, frontZ, rearZ, body.max.y);
        BuildEye(root, vehicle, bodyRenderers, frontZ, track, body.max.y);
        BuildDoor(root, template.transform, vehicle, profile, bodyRenderers, side, frontZ, rearZ, radius, body.max.y);
        TestDriveCluster.Create(root, vehicle, bodyRenderers);
        ClearGlass(root);
        BuildPhysics(root, vehicle, profile, rig, rigged, frontZ, rearZ, radius, track, body);
        BuildLights(root, vehicle, radius);

        rootObject.AddComponent<AudioSource>();
        vehicle.Audio = rootObject.AddComponent<TestDriveAudio>();
        vehicle.TrafficMover = new TestDriveTraffic.Mover
        {
            Body = vehicle.Body,
            HalfLength = vehicle.HalfLength,
            HalfWidth = vehicle.HalfWidth
        };
        vehicle.Initialise();
        return vehicle;
    }

    // ------------------------------------------------------------------ model

    private static void StripShowroomComponents(GameObject model)
    {
        foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Rigidbody r in model.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(r);
        foreach (LODGroup l in model.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(l);
        foreach (MonoBehaviour m in model.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(m);
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
            r.probeAnchor = null;
            r.allowOcclusionWhenDynamic = true;
        }
    }

    /// <summary>Centres the model on the root, wheels on y = 0, and rescales it when its length is implausible for
    /// the profile (a model imported in centimetres, for example).</summary>
    private static void Normalise(Transform root, Transform model, TestDriveCarProfile profile)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            Bounds b = ModelBounds(root, model, false);
            if (b.size.z <= 0.01f) return;
            float scale = profile.Length / b.size.z;
            if (pass == 0 && (scale < 0.85f || scale > 1.15f))
            {
                model.localScale *= scale;
                model.localPosition *= scale;
                continue;
            }
            model.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
            return;
        }
        Bounds last = ModelBounds(root, model, false);
        model.localPosition -= new Vector3(last.center.x, last.min.y, last.center.z);
    }

    private static Bounds ModelBounds(Transform root, Transform model, bool enabledOnly)
    {
        Matrix4x4 toCar = root.worldToLocalMatrix;
        bool found = false;
        Bounds bounds = new Bounds();
        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || (enabledOnly && !renderer.enabled)) continue;
            Bounds b = TestDriveWheelRig.CarBox(toCar * filter.transform.localToWorldMatrix, filter.sharedMesh.bounds);
            if (!found) { bounds = b; found = true; }
            else bounds.Encapsulate(b);
        }
        return bounds;
    }

    private static List<MeshRenderer> BodyRenderers(Transform root)
    {
        var list = new List<MeshRenderer>();
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!renderer.enabled) continue;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            if (TestDriveWheelRig.IsWheelPart(renderer.transform, root) || UnderWheelPivot(renderer.transform, root)) continue;
            list.Add(renderer);
        }
        return list;
    }

    private static bool UnderWheelPivot(Transform node, Transform root)
    {
        for (Transform t = node; t != null && t != root; t = t.parent)
            if (t.name.EndsWith(" Wheel Steer") || t.name.EndsWith(" Wheel Spin")) return true;
        return false;
    }

    // ------------------------------------------------------------------ cabin

    /// <summary>The outer surface of the body seen from the left: for each 2.5 cm of height, how far out (−x) the
    /// left side reaches over the doors' length. Mirrors, which sit at the front of the door, are left out.</summary>
    private sealed class SideProfile
    {
        private const float Bin = 0.025f;
        private float[] reach = new float[0];

        public static SideProfile Measure(List<MeshRenderer> renderers, Transform root, float zMin, float zMax, float height)
        {
            var profile = new SideProfile();
            int bins = Mathf.Max(1, Mathf.CeilToInt((height + 0.1f) / Bin));
            profile.reach = new float[bins];
            if (zMax <= zMin) return profile;
            float z0 = zMin, z1 = zMin + (zMax - zMin) * 0.7f;
            Matrix4x4 toCar = root.worldToLocalMatrix;
            for (int r = 0; r < renderers.Count; r++)
            {
                Mesh mesh = renderers[r].GetComponent<MeshFilter>().sharedMesh;
                if (!mesh.isReadable) continue;
                Matrix4x4 m = toCar * renderers[r].transform.localToWorldMatrix;
                Vector3[] vertices = mesh.vertices;
                for (int v = 0; v < vertices.Length; v++)
                {
                    Vector3 p = m.MultiplyPoint3x4(vertices[v]);
                    if (p.x >= 0f || p.z < z0 || p.z > z1 || p.y < 0f) continue;
                    int bin = Mathf.Min(bins - 1, (int)(p.y / Bin));
                    if (-p.x > profile.reach[bin]) profile.reach[bin] = -p.x;
                }
            }
            return profile;
        }

        /// <summary>Outer reach at a height, the largest of the neighbouring bins.</summary>
        public float At(float y)
        {
            if (reach.Length == 0) return 0f;
            int bin = Mathf.Clamp((int)(y / Bin), 0, reach.Length - 1);
            float best = 0f;
            for (int k = -2; k <= 2; k++)
            {
                int i = bin + k;
                if (i >= 0 && i < reach.Length) best = Mathf.Max(best, reach[i]);
            }
            return best;
        }

        public float HalfWidth(float y, float fallback)
        {
            float w = At(y);
            return w > 0.3f ? w : fallback;
        }
    }

    private static Transform FindNode(Transform root, System.Predicate<string> match)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (match(t.name)) return t;
        return null;
    }

    /// <summary>A pivot at the steering wheel's centre whose local Z is the column axis (pointing forward), found
    /// from the wheel's vertices: the axis is the direction of least spread of the rim.</summary>
    private static void BuildSteering(Transform root, Transform model, TestDriveVehicle vehicle, List<MeshRenderer> bodyRenderers,
        float frontZ, float rearZ, float height)
    {
        // The rim itself when the model names it ("SteeringWheel"), else any steering node.
        Transform node = FindNode(model, n => TestDriveWheelRig.Has(n, "steeringwheel") || TestDriveWheelRig.Has(n, "steering_wheel") || TestDriveWheelRig.Has(n, "steering wheel"));
        if (node != null && node.parent != null && node.parent != model && TestDriveWheelRig.Has(node.parent.name, "steering") &&
            !TestDriveWheelRig.Has(node.parent.name, "column") && !TestDriveWheelRig.Has(node.parent.name, "dash"))
            node = node.parent;   // the rim's parts sit under one steering node: take them all
        if (node == null) node = FindNode(model, n => TestDriveWheelRig.Has(n, "steering") && !TestDriveWheelRig.Has(n, "column"));
        if (node == null)
        {
            BuildSteeringFromIsland(root, vehicle, bodyRenderers, frontZ, rearZ, height);
            return;
        }
        Matrix4x4 toCar = root.worldToLocalMatrix;
        var points = new List<Vector3>();
        foreach (MeshFilter filter in node.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            Matrix4x4 m = toCar * filter.transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            int step = Mathf.Max(1, vertices.Length / 4000);
            for (int v = 0; v < vertices.Length; v += step) points.Add(m.MultiplyPoint3x4(vertices[v]));
        }
        Vector3 pivot = root.InverseTransformPoint(node.position);
        Vector3 axis = Vector3.forward;
        if (points.Count > 30)
        {
            Vector3 mean = Vector3.zero;
            for (int i = 0; i < points.Count; i++) mean += points[i];
            mean /= points.Count;
            float radius;
            TestDriveCabin.Fit(points, mean, out axis, out radius);
            vehicle.SteeringRadius = radius;
            // A dummy node usually sits at the hub; otherwise use the rim's centre.
            if ((pivot - mean).magnitude > 0.12f) pivot = mean;
        }
        if (Vector3.Dot(axis, Vector3.forward) < 0f) axis = -axis;
        // The column leans back toward the driver; a plane that came out sideways is not a steering wheel.
        if (Vector3.Dot(axis, Vector3.forward) < 0.5f) return;
        if (pivot.x > 0.05f || pivot.y < 0.4f) return;   // driver on the left, or in the middle (the concept car)

        var pivotObject = new GameObject("Steering Wheel Pivot");
        Transform p = pivotObject.transform;
        p.SetParent(root, false);
        p.localPosition = pivot;
        p.localRotation = Quaternion.LookRotation(axis, Vector3.up);
        node.SetParent(p, true);
        vehicle.SteeringPivot = p;
    }

    /// <summary>
    /// Models without a steering node (the E30, the M4) still model the wheel as its own ring of triangles inside a
    /// body mesh: it is found, moved into a mesh of its own and hung on a pivot, so it turns with the steering.
    /// </summary>
    private static void BuildSteeringFromIsland(Transform root, TestDriveVehicle vehicle, List<MeshRenderer> bodyRenderers,
        float frontZ, float rearZ, float height)
    {
        TestDriveCabin.SteeringWheel wheel = TestDriveCabin.FindWheelIsland(root, bodyRenderers, 0f, frontZ, rearZ, height, true);
        if (wheel == null) return;
        var pivotObject = new GameObject("Steering Wheel Pivot");
        Transform p = pivotObject.transform;
        p.SetParent(root, false);
        p.localPosition = wheel.centre;
        p.localRotation = Quaternion.LookRotation(wheel.axis, Vector3.up);
        vehicle.SteeringPivot = p;
        vehicle.SteeringRadius = wheel.radius;

        MeshRenderer renderer = wheel.renderer;
        Mesh source = renderer.GetComponent<MeshFilter>().sharedMesh;
        var islands = new HashSet<int> { wheel.island };
        if (wheel.map.Pick(islands) == 0) return;
        Matrix4x4 toCar = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
        Mesh rim = TestDriveMeshCut.Extract(source, toCar, wheel.map.selection, wheel.centre, source.name + " Steering Wheel");
        Mesh rest = TestDriveMeshCut.Remainder(source, wheel.map.selection, source.name + " Without Steering Wheel");
        var part = new GameObject("Steering Wheel") { layer = renderer.gameObject.layer };
        part.transform.SetParent(p, false);
        part.transform.localRotation = Quaternion.Inverse(p.localRotation);   // vertices are in car axes
        part.AddComponent<MeshFilter>().sharedMesh = rim;
        MeshRenderer copy = part.AddComponent<MeshRenderer>();
        copy.sharedMaterials = renderer.sharedMaterials;
        copy.shadowCastingMode = renderer.shadowCastingMode;
        copy.lightProbeUsage = renderer.lightProbeUsage;
        copy.reflectionProbeUsage = renderer.reflectionProbeUsage;
        renderer.GetComponent<MeshFilter>().sharedMesh = rest;
    }

    /// <summary>The driver's eye: behind the steering wheel (or at typical proportions), 0.7 m above the seat cushion
    /// and at least 10 cm under the headlining, both measured from the cabin's triangles.</summary>
    private static void BuildEye(Transform root, TestDriveVehicle vehicle, List<MeshRenderer> bodyRenderers, float frontZ,
        float track, float height)
    {
        float driverX, eyeZ;
        if (vehicle.SteeringPivot != null)
        {
            Vector3 s = vehicle.SteeringPivot.localPosition;
            driverX = s.x;
            eyeZ = s.z - 0.5f;
        }
        else
        {
            driverX = -track * 0.5f * 0.52f;
            eyeZ = frontZ - vehicle.WheelBase * 0.615f;
        }
        float guess = height - 0.27f;
        // Seat cushion: upward faces just in front of the hip point (a percentile, so a bolster does not count);
        // headlining: downward faces above and ahead of the head (the headrest's underside is behind it).
        var cushions = new List<float>();
        float liner = float.PositiveInfinity;
        Matrix4x4 toCar = root.worldToLocalMatrix;
        for (int r = 0; r < bodyRenderers.Count; r++)
        {
            Mesh mesh = bodyRenderers[r].GetComponent<MeshFilter>().sharedMesh;
            if (!mesh.isReadable) continue;
            float localLiner = liner;
            TestDriveMeshCut.Select(mesh, toCar * bodyRenderers[r].transform.localToWorldMatrix, (a, b, c, n) =>
            {
                Vector3 m = (a + b + c) / 3f;
                if (Mathf.Abs(m.x - driverX) < 0.08f && Mathf.Abs(m.z - (eyeZ - 0.1f)) < 0.12f
                    && m.y > 0.18f && m.y < Mathf.Min(guess - 0.38f, height - 0.8f) && n.y > 0.6f)
                    cushions.Add(m.y);
                if (Mathf.Abs(m.x - driverX) < 0.16f && m.z > eyeZ - 0.05f && m.z < eyeZ + 0.3f
                    && m.y > guess + 0.02f && n.y < -0.4f && m.y < localLiner)
                    localLiner = m.y;
                return false;
            });
            liner = localLiner;
        }
        float eyeY = guess;
        if (cushions.Count > 0)
        {
            cushions.Sort();
            float cushion = cushions[Mathf.Min(cushions.Count - 1, (int)(cushions.Count * 0.6f))];
            vehicle.SeatCushion = cushion;
            eyeY = Mathf.Clamp(cushion + 0.68f, guess - 0.12f, guess + 0.05f);
        }
        if (!float.IsInfinity(liner)) eyeY = Mathf.Min(eyeY, liner - 0.12f);

        var eye = new GameObject("Driver Eye").transform;
        eye.SetParent(root, false);
        eye.localPosition = new Vector3(driverX, eyeY, eyeZ);
        vehicle.EyeAnchor = eye;

        // Head-up display: projected 2.3 m ahead, a little under the line of sight.
        var hud = new GameObject("Head Up Display Anchor").transform;
        hud.SetParent(root, false);
        hud.localPosition = eye.localPosition + new Vector3(0f, -0.2f, 2.3f);
        hud.localRotation = Quaternion.LookRotation(hud.localPosition - eye.localPosition, Vector3.up);
        vehicle.HudAnchor = hud;
    }

    // ------------------------------------------------------------------ door

    private static void BuildDoor(Transform root, Transform model, TestDriveVehicle vehicle, TestDriveCarProfile profile,
        List<MeshRenderer> bodyRenderers, SideProfile side, float frontZ, float rearZ, float radius, float height)
    {
        float doorFront, doorRear, outer, beltY = height * 0.6f;
        Transform hinge = null;

        // A model with its own driver's door node (the Tesla): hinge it at the node, which sits at the front edge.
        Transform node = FindNode(model, n =>
        {
            string l = n.ToLowerInvariant();
            return l.Contains("door") && (l.Contains("lf") || l.Contains("fl") || l.Contains("left_front") || l.Contains("front_left") || l.Contains("doorl"))
                   && !l.Contains("handle") && !l.Contains("glass");
        });
        if (node != null)
        {
            Bounds b = new Bounds();
            bool found = false;
            Matrix4x4 toCar = root.worldToLocalMatrix;
            foreach (MeshFilter filter in node.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Bounds fb = TestDriveWheelRig.CarBox(toCar * filter.transform.localToWorldMatrix, filter.sharedMesh.bounds);
                if (!found) { b = fb; found = true; }
                else b.Encapsulate(fb);
            }
            if (found && b.center.x < 0f && b.size.z > 0.6f)
            {
                var pivot = new GameObject("Driver Door Hinge").transform;
                pivot.SetParent(root, false);
                Vector3 at = root.InverseTransformPoint(node.position);
                pivot.localPosition = new Vector3(Mathf.Min(at.x, b.min.x + 0.05f), beltY, Mathf.Max(at.z, b.max.z - 0.05f));
                node.SetParent(pivot, true);
                hinge = pivot;
                doorFront = b.max.z;
                doorRear = b.min.z;
                outer = -b.min.x;
                FinishDoor(root, vehicle, hinge, doorFront, doorRear, outer, beltY);
                return;
            }
        }

        // A body modelled with the door as its own pieces (the M4): take those pieces whole, with the model's own edges.
        if (BuildIslandDoor(root, vehicle, bodyRenderers, frontZ, rearZ, radius, height, beltY)) return;

        // One-piece body: cut the door's skin, glass and trim out of the body meshes.
        doorFront = frontZ - radius - 0.2f;
        doorRear = Mathf.Max(doorFront - profile.DoorLength, rearZ + radius + 0.3f);
        float sill = Mathf.Max(radius * 0.85f, 0.2f), top = height - 0.07f;
        outer = side.At(beltY);
        if (outer < 0.4f || doorFront - doorRear < 0.6f)
        {
            FinishDoor(root, vehicle, null, doorFront, doorRear, Mathf.Max(outer, vehicle.HalfWidth), beltY);
            return;
        }
        Vector3 hingePosition = new Vector3(-outer + 0.03f, beltY, doorFront - 0.03f);
        // The door's outer skin, glass and inner trim: faces turned sideways within DoorDepth of the body's side.
        TestDriveMeshCut.TrianglePredicate band = (a, b, c, n) =>
        {
            Vector3 m = (a + b + c) / 3f;
            return m.x < -0.05f && Mathf.Abs(n.x) >= 0.3f && -m.x > side.At(m.y) - DoorDepth;
        };
        // Cut along straight lines: the door's front and rear edges, the sill and just under the roof.
        Vector4[] planes =
        {
            new Vector4(0f, 0f, -1f, doorFront), new Vector4(0f, 0f, 1f, -doorRear),
            new Vector4(0f, 1f, 0f, -sill), new Vector4(0f, -1f, 0f, top)
        };
        Matrix4x4 rootFromWorld = root.worldToLocalMatrix;
        var doors = new List<Mesh>();
        var remainders = new List<Mesh>();
        var owners = new List<MeshRenderer>();
        int total = 0;
        for (int r = 0; r < bodyRenderers.Count; r++)
        {
            Mesh mesh = bodyRenderers[r].GetComponent<MeshFilter>().sharedMesh;
            Matrix4x4 toCar = rootFromWorld * bodyRenderers[r].transform.localToWorldMatrix;
            // Only meshes that reach the door's box are worth cutting.
            Bounds reach = TestDriveWheelRig.CarBox(toCar, mesh.bounds);
            if (reach.min.x > -0.05f || reach.max.z < doorRear || reach.min.z > doorFront || reach.max.y < sill || reach.min.y > top) continue;
            Mesh door, rest;
            int count;
            if (!TestDriveMeshCut.CutRegion(mesh, toCar, band, planes, out door, out rest, out count)) continue;
            doors.Add(door);
            remainders.Add(rest);
            owners.Add(bodyRenderers[r]);
            total += count;
        }
        if (total < 150)
        {
            FinishDoor(root, vehicle, null, doorFront, doorRear, outer, beltY);
            return;
        }
        var hingeObject = new GameObject("Driver Door Hinge");
        hinge = hingeObject.transform;
        hinge.SetParent(root, false);
        hinge.localPosition = hingePosition;
        for (int i = 0; i < owners.Count; i++)
        {
            MeshRenderer renderer = owners[i];
            // The door part sits exactly where its body mesh sits, then hangs on the hinge.
            var part = new GameObject(renderer.name + " Driver Door") { layer = renderer.gameObject.layer };
            part.transform.SetParent(renderer.transform, false);
            part.transform.SetParent(hinge, true);
            part.AddComponent<MeshFilter>().sharedMesh = doors[i];
            MeshRenderer copy = part.AddComponent<MeshRenderer>();
            copy.sharedMaterials = renderer.sharedMaterials;
            copy.shadowCastingMode = renderer.shadowCastingMode;
            copy.receiveShadows = renderer.receiveShadows;
            copy.lightProbeUsage = renderer.lightProbeUsage;
            copy.reflectionProbeUsage = renderer.reflectionProbeUsage;
            renderer.GetComponent<MeshFilter>().sharedMesh = remainders[i];
        }
        FinishDoor(root, vehicle, hinge, doorFront, doorRear, outer, beltY);
    }

    private static bool BuildIslandDoor(Transform root, TestDriveVehicle vehicle, List<MeshRenderer> bodyRenderers,
        float frontZ, float rearZ, float radius, float height, float beltY)
    {
        Matrix4x4 rootFromWorld = root.worldToLocalMatrix;
        var maps = new List<TestDriveMeshCut.IslandMap>();
        var owners = new List<MeshRenderer>();
        var matrices = new List<Matrix4x4>();
        Bounds panel = new Bounds();
        int panelTriangles = 0;
        for (int r = 0; r < bodyRenderers.Count; r++)
        {
            MeshFilter filter = bodyRenderers[r].GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
            Matrix4x4 toCar = rootFromWorld * bodyRenderers[r].transform.localToWorldMatrix;
            Bounds reach = TestDriveWheelRig.CarBox(toCar, filter.sharedMesh.bounds);
            if (reach.min.x > -0.3f || reach.max.z < rearZ + radius || reach.min.z > frontZ - radius) continue;
            TestDriveMeshCut.IslandMap map = TestDriveMeshCut.Islands(filter.sharedMesh, toCar);
            maps.Add(map);
            owners.Add(bodyRenderers[r]);
            matrices.Add(toCar);
            // The door's outer panel: a large, thin, door-sized piece low on the driver's side between the wheels.
            for (int i = 0; i < map.bounds.Count; i++)
            {
                Bounds b = map.bounds[i];
                Vector3 size = b.size, centre = b.center;
                if (centre.x < -0.45f && size.z > 0.8f && size.z < 1.7f && size.y > 0.4f && size.y < 1.1f && size.x < 0.4f &&
                    centre.y < beltY && centre.z < frontZ - radius && centre.z > rearZ + radius &&
                    map.triangles[i] > 60 && map.triangles[i] > panelTriangles)
                {
                    panel = b;
                    panelTriangles = map.triangles[i];
                }
            }
        }
        if (panelTriangles == 0) return false;

        // Every piece that lies wholly within the panel's box (reaching inwards for the door card) moves with the door.
        Vector3 lo = panel.min - new Vector3(0.12f, 0.12f, 0.12f);
        Vector3 hi = new Vector3(panel.max.x + 0.32f, Mathf.Max(panel.max.y, height - 0.04f), panel.max.z + 0.12f);
        Vector3 hingePosition = new Vector3(panel.min.x + 0.04f, beltY, panel.max.z - 0.02f);
        var hingeObject = new GameObject("Driver Door Hinge");
        Transform hinge = hingeObject.transform;
        hinge.SetParent(root, false);
        hinge.localPosition = hingePosition;
        int total = 0;
        for (int m = 0; m < maps.Count; m++)
        {
            TestDriveMeshCut.IslandMap map = maps[m];
            var door = new HashSet<int>();
            for (int i = 0; i < map.bounds.Count; i++)
            {
                Bounds b = map.bounds[i];
                if (b.min.x >= lo.x && b.min.y >= lo.y && b.min.z >= lo.z && b.max.x <= hi.x && b.max.y <= hi.y && b.max.z <= hi.z)
                    door.Add(i);
            }
            if (door.Count == 0 || map.Pick(door) == 0) continue;
            MeshRenderer renderer = owners[m];
            Mesh source = renderer.GetComponent<MeshFilter>().sharedMesh;
            Mesh doorMesh = TestDriveMeshCut.Extract(source, matrices[m], map.selection, hingePosition, source.name + " Driver Door");
            Mesh rest = TestDriveMeshCut.Remainder(source, map.selection, source.name + " Without Door");
            total += map.selection.count;
            var part = new GameObject(renderer.name + " Driver Door") { layer = renderer.gameObject.layer };
            part.transform.SetParent(hinge, false);
            part.AddComponent<MeshFilter>().sharedMesh = doorMesh;
            MeshRenderer copy = part.AddComponent<MeshRenderer>();
            copy.sharedMaterials = renderer.sharedMaterials;
            copy.shadowCastingMode = renderer.shadowCastingMode;
            copy.receiveShadows = renderer.receiveShadows;
            copy.lightProbeUsage = renderer.lightProbeUsage;
            copy.reflectionProbeUsage = renderer.reflectionProbeUsage;
            renderer.GetComponent<MeshFilter>().sharedMesh = rest;
        }
        if (total == 0)
        {
            Object.Destroy(hingeObject);
            return false;
        }
        FinishDoor(root, vehicle, hinge, panel.max.z, panel.min.z, -panel.min.x, beltY);
        return true;
    }

    private static void FinishDoor(Transform root, TestDriveVehicle vehicle, Transform hinge, float doorFront, float doorRear,
        float outer, float beltY)
    {
        vehicle.DoorHinge = hinge;
        // The door's rear edge swings out to −x when the hinge turns positively about +y.
        vehicle.DoorOpenAngle = 64f;

        var handle = new GameObject("Driver Door Handle").transform;
        handle.SetParent(root, false);
        handle.localPosition = new Vector3(-outer - 0.04f, beltY - 0.06f, doorRear + 0.2f);
        vehicle.HandlePoint = handle;

        var exit = new GameObject("Driver Exit Point").transform;
        exit.SetParent(root, false);
        exit.localPosition = new Vector3(-(Mathf.Max(outer, vehicle.HalfWidth) + 0.85f), 0f, (doorFront + doorRear) * 0.5f - 0.15f);
        exit.localRotation = Quaternion.Euler(0f, 90f, 0f);
        vehicle.ExitPoint = exit;
    }

    // ------------------------------------------------------------------ glass

    /// <summary>
    /// Showroom glass is tinted almost black so the cabin reads as a dark volume from outside; from the driver's seat
    /// that hides the city. The test car gets clear, lightly tinted, reflective glass instead (transparent surface,
    /// alpha 0.14), so the street is plainly visible through the side windows and the windscreen.
    /// </summary>
    private static void ClearGlass(Transform root)
    {
        var copies = new Dictionary<Material, Material>();
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material m = materials[i];
                if (m == null || !IsGlass(m)) continue;
                Material clear;
                if (!copies.TryGetValue(m, out clear))
                {
                    clear = new Material(m) { name = m.name + " (clear)" };
                    MakeClear(clear);
                    copies.Add(m, clear);
                }
                materials[i] = clear;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = materials;
        }
    }

    /// <summary>Glass by name (M4 "windows", Tesla "glass"), or any other blended material that is not a lamp lens
    /// (the E30's window material has a generic palette name).</summary>
    private static bool IsGlass(Material m)
    {
        string n = m.name.ToLowerInvariant();
        if (n.Contains("head") || n.Contains("tail") || n.Contains("lamp") || n.Contains("light") || n.Contains("lens") ||
            n.Contains("mirror") || n.Contains("indicator") || n.Contains("clear)")) return false;
        if (n.Contains("glass") || n.Contains("window") || n.Contains("windshield") || n.Contains("windscreen")) return true;
        return m.renderQueue >= 2900 && m.renderQueue < 4000;
    }

    private static void MakeClear(Material m)
    {
        Color tint = new Color(0.62f, 0.68f, 0.7f, 0.14f);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
        if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.95f);
        if (m.HasProperty("_Surface"))
        {
            // URP Lit transparent, premultiplied so reflections stay bright on clear glass.
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 1f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        m.SetShaderPassEnabled("ShadowCaster", false);
    }

    // ------------------------------------------------------------------ physics

    private static void BuildPhysics(Transform root, TestDriveVehicle vehicle, TestDriveCarProfile profile,
        List<TestDriveWheelRig.Wheel> rig, bool rigged, float frontZ, float rearZ, float radius, float track, Bounds body)
    {
        float height = body.max.y, halfWidth = vehicle.HalfWidth, length = body.size.z;
        float beltY = height * 0.6f;
        var material = new PhysicsMaterial("Test Drive Body")
        {
            dynamicFriction = 0.25f,
            staticFriction = 0.3f,
            bounciness = 0.05f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };

        var lower = new GameObject("Body Collider").AddComponent<BoxCollider>();
        lower.transform.SetParent(root, false);
        float lowY = radius * 0.62f;
        lower.center = new Vector3(0f, (lowY + beltY) * 0.5f, body.center.z);
        lower.size = new Vector3(halfWidth * 2f * 0.97f, beltY - lowY, length * 0.97f);
        lower.sharedMaterial = material;

        var upper = new GameObject("Cabin Collider").AddComponent<BoxCollider>();
        upper.transform.SetParent(root, false);
        float cabinFront = frontZ - radius - 0.15f, cabinRear = rearZ + radius * 0.2f;
        upper.center = new Vector3(0f, (beltY + height * 0.97f) * 0.5f, (cabinFront + cabinRear) * 0.5f);
        upper.size = new Vector3(halfWidth * 2f * 0.8f, height * 0.97f - beltY, Mathf.Max(0.8f, cabinFront - cabinRear));
        upper.sharedMaterial = material;

        Rigidbody rb = root.gameObject.AddComponent<Rigidbody>();
        rb.mass = profile.Mass;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.08f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.maxAngularVelocity = 18f;
        float wheelBase = frontZ - rearZ;
        rb.centerOfMass = new Vector3(0f, profile.CentreOfMassHeight, rearZ + wheelBase * profile.WeightFront);
        float w = halfWidth * 2f, h = 1.1f, l = length;
        rb.inertiaTensor = new Vector3(profile.Mass / 12f * (h * h + l * l), profile.Mass / 12f * (w * w + l * l) * 1.05f,
            profile.Mass / 12f * (w * w + h * h));
        rb.inertiaTensorRotation = Quaternion.identity;
        vehicle.Body = rb;

        // Wheel colliders at the hubs, raised so the springs settle the body at its modelled ride height.
        float suspension = vehicle.SuspensionDistance;
        var hubs = new List<KeyValuePair<Vector3, TestDriveWheelRig.Wheel>>();
        if (rigged)
            for (int i = 0; i < rig.Count; i++) hubs.Add(new KeyValuePair<Vector3, TestDriveWheelRig.Wheel>(rig[i].hub, rig[i]));
        else
        {
            float x = track * 0.5f;
            hubs.Add(new KeyValuePair<Vector3, TestDriveWheelRig.Wheel>(new Vector3(-x, radius, frontZ), null));
            hubs.Add(new KeyValuePair<Vector3, TestDriveWheelRig.Wheel>(new Vector3(x, radius, frontZ), null));
            hubs.Add(new KeyValuePair<Vector3, TestDriveWheelRig.Wheel>(new Vector3(-x, radius, rearZ), null));
            hubs.Add(new KeyValuePair<Vector3, TestDriveWheelRig.Wheel>(new Vector3(x, radius, rearZ), null));
        }
        for (int i = 0; i < hubs.Count; i++)
        {
            Vector3 hub = hubs[i].Key;
            TestDriveWheelRig.Wheel r = hubs[i].Value;
            bool front = r != null ? r.front : hub.z > (frontZ + rearZ) * 0.5f;
            bool left = r != null ? r.left : hub.x < 0f;
            var go = new GameObject((front ? "Front " : "Rear ") + (left ? "Left" : "Right") + " Wheel Collider");
            go.transform.SetParent(root, false);
            go.transform.localPosition = hub + Vector3.up * (suspension * SuspensionTarget);
            WheelCollider wc = go.AddComponent<WheelCollider>();
            wc.radius = r != null ? r.radius : radius;
            wc.mass = 22f;
            wc.wheelDampingRate = 0.3f;
            wc.suspensionDistance = suspension;
            wc.forceAppPointDistance = 0.18f;
            float share = (front ? profile.WeightFront : 1f - profile.WeightFront) * 0.5f;
            float sprung = profile.Mass * share;
            float omega = 2f * Mathf.PI * profile.RideFrequency;
            float k = sprung * omega * omega;
            wc.suspensionSpring = new JointSpring
            {
                spring = k,
                damper = 2f * 0.42f * Mathf.Sqrt(k * sprung),
                targetPosition = SuspensionTarget
            };
            wc.forwardFriction = Curve(0.4f, 1f, 1.0f, 0.72f, front ? 1.7f : 2.1f);
            wc.sidewaysFriction = Curve(0.22f, 1f, 0.6f, 0.78f, front ? 1.6f : 1.8f);
            vehicle.Wheels.Add(new TestDriveVehicle.WheelState
            {
                collider = wc,
                rig = r,
                front = front,
                left = left,
                driven = !front
            });
        }
    }

    private static WheelFrictionCurve Curve(float extremumSlip, float extremumValue, float asymptoteSlip, float asymptoteValue, float stiffness)
    {
        return new WheelFrictionCurve
        {
            extremumSlip = extremumSlip,
            extremumValue = extremumValue,
            asymptoteSlip = asymptoteSlip,
            asymptoteValue = asymptoteValue,
            stiffness = stiffness
        };
    }

    private static void BuildLights(Transform root, TestDriveVehicle vehicle, float radius)
    {
        var lights = new Light[2];
        for (int i = 0; i < 2; i++)
        {
            float x = (i == 0 ? -1f : 1f) * Mathf.Max(0.3f, vehicle.HalfWidth - 0.28f);
            var go = new GameObject(i == 0 ? "Left Headlight" : "Right Headlight");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(x, radius * 1.9f, vehicle.HalfLength - 0.12f);
            go.transform.localRotation = Quaternion.Euler(3f, 0f, 0f);
            Light light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(1f, 0.96f, 0.88f);
            // About 1.2 (a bright overcast day) on the road 10 m ahead, falling off with distance like real lamps.
            light.intensity = 120f;
            light.range = 70f;
            light.spotAngle = 56f;
            light.innerSpotAngle = 22f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.enabled = false;
            lights[i] = light;
        }
        vehicle.Headlights = lights;

        // Cabin fill: the cabin is in the roof's shadow, so dark trim reads as a black hole without it. A soft
        // shadowless light above the centre console stands in for light bounced in through the glass.
        if (vehicle.EyeAnchor != null)
        {
            var fill = new GameObject("Cabin Fill Light");
            fill.transform.SetParent(root, false);
            fill.transform.localPosition = new Vector3(vehicle.EyeAnchor.localPosition.x * 0.4f, vehicle.EyeAnchor.localPosition.y + 0.05f,
                vehicle.EyeAnchor.localPosition.z + 0.35f);
            Light cabin = fill.AddComponent<Light>();
            cabin.type = LightType.Point;
            cabin.color = new Color(1f, 0.95f, 0.9f);
            cabin.intensity = 0.7f;
            cabin.range = 1.9f;
            cabin.shadows = LightShadows.None;
            cabin.renderMode = LightRenderMode.ForcePixel;
            vehicle.CabinLight = cabin;
        }
    }
}
