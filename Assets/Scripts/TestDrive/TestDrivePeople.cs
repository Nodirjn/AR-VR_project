using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// A livelier street: more people walking the sidewalks (copies of the scene's own walkers, spread along their
/// routes in other clothes), small groups standing and talking on the forecourt and along the avenue, and people
/// sitting on the benches. Spots are checked before anyone is put there (nothing solid in the way, ground below),
/// and the talkers stand clear of the walkers' paths so nobody is blocked.
/// </summary>
public static class TestDrivePeople
{
    private static readonly FieldInfo WaypointsField = typeof(ExteriorPedestrianAgent).GetField("waypoints", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo IndexField = typeof(ExteriorPedestrianAgent).GetField("waypointIndex", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo SpeedField = typeof(ExteriorPedestrianAgent).GetField("walkSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo ScaleField = typeof(ExteriorPedestrianAgent).GetField("scale", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo SeedField = typeof(ExteriorPedestrianAgent).GetField("appearanceSeed", BindingFlags.Instance | BindingFlags.NonPublic);
    private static bool installed;

    public static void Install()
    {
        if (installed) return;
        installed = true;
        bool quest = QualitySettings.GetQualityLevel() <= 1 || Application.platform == RuntimePlatform.Android;
        var walkers = new List<ExteriorPedestrianAgent>(Object.FindObjectsByType<ExteriorPedestrianAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        walkers.RemoveAll(w => w.GetComponentInChildren<SkinnedMeshRenderer>(true) == null);
        if (walkers.Count == 0) return;
        var root = new GameObject("Street Life").transform;
        // (Copies of the walkers on their own short loops, and standing groups, made people queue behind one another and
        // stand in the road; the street life now comes from the promenade, the shoppers and the benches.)
        int added = 0;
        int idle = AddBenchSitters(walkers, root);
        if (TestDriveCity.Streets.Exists(st => Mathf.Approximately(st.centre, TestDriveCity.OceanZ)))
        {
            added += AddPromenade(walkers, root, quest);
            idle += AddBeachPeople(walkers, root, quest);
        }
        TestDriveShoppers.Install(root);
        Debug.Log("[Test Drive] Street life: " + added + " more walkers, " + idle + " people standing and sitting.");
    }

    /// <summary>Walkers up and down Ocean Drive: the shop side and the promenade above the beach.</summary>
    private static int AddPromenade(List<ExteriorPedestrianAgent> walkers, Transform root, bool quest)
    {
        if (IndexField == null) return 0;
        float z0 = TestDriveCity.OceanZ, x = TestDriveCity.OuterX - 20f;
        var loops = new[]
        {
            Path(root, "Ocean Drive Shops Walk", new[] { new Vector3(-x, 0f, z0 + TestDriveCity.WalkInner), new Vector3(x, 0f, z0 + TestDriveCity.WalkInner), new Vector3(x, 0f, z0 + TestDriveCity.WalkOuter), new Vector3(-x, 0f, z0 + TestDriveCity.WalkOuter) }),
            Path(root, "Promenade Walk", new[] { new Vector3(-x, 0f, z0 - TestDriveCity.WalkInner), new Vector3(x, 0f, z0 - TestDriveCity.WalkInner), new Vector3(x, 0f, z0 - TestDriveCity.WalkOuter), new Vector3(-x, 0f, z0 - TestDriveCity.WalkOuter) }),
        };
        var holderObject = new GameObject("Promenade Staging");
        holderObject.SetActive(false);
        var random = new System.Random(12);
        int made = 0, per = quest ? 4 : 10;
        foreach (Transform[] loop in loops)
            for (int k = 0; k < per; k++)
            {
                ExteriorPedestrianAgent source = walkers[random.Next(walkers.Count)];
                int at = (k * loop.Length / per + random.Next(3)) % loop.Length;
                Transform a = loop[at], b = loop[(at + 1) % loop.Length];
                GameObject copy = Object.Instantiate(source.gameObject, holderObject.transform);
                copy.name = "Promenade Walker " + made;
                ExteriorPedestrianAgent agent = copy.GetComponent<ExteriorPedestrianAgent>();
                float scale = ScaleField != null ? (float)ScaleField.GetValue(source) : source.transform.localScale.x;
                agent.Configure(loop, 0.9f + 0.35f * (float)random.NextDouble(), scale, 300 + made);
                IndexField.SetValue(agent, (at + 1) % loop.Length);
                Vector3 p = Vector3.Lerp(a.position, b.position, (float)random.NextDouble());
                copy.transform.SetParent(root, false);
                copy.transform.SetPositionAndRotation(new Vector3(p.x, source.transform.position.y, p.z),
                    Quaternion.LookRotation(Vector3.ProjectOnPlane(b.position - a.position, Vector3.up).normalized + Vector3.forward * 1e-4f, Vector3.up));
                copy.SetActive(true);
                made++;
            }
        Object.Destroy(holderObject);
        return made;
    }

    private static Transform[] Path(Transform root, string name, Vector3[] corners)
    {
        var parent = new GameObject(name).transform;
        parent.SetParent(root, false);
        var list = new List<Transform>();
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 a = corners[i], b = corners[(i + 1) % corners.Length];
            int steps = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / 15f));
            for (int j = 0; j < steps; j++)
            {
                var t = new GameObject("Waypoint " + list.Count).transform;
                t.SetParent(parent, false);
                t.position = Vector3.Lerp(a, b, j / (float)steps);
                list.Add(t);
            }
        }
        return list.ToArray();
    }

    /// <summary>People on the loungers under the umbrellas, and small groups chatting at the water's edge.</summary>
    private static int AddBeachPeople(List<ExteriorPedestrianAgent> walkers, Transform root, bool quest)
    {
        int made = 0, look = 5, budget = quest ? 8 : 22;
        var random = new System.Random(21);
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (made >= budget) break;
            if (t.name != "Sun Lounger" || random.NextDouble() > 0.45) continue;
            Vector3 seat = t.position + Vector3.up * 0.12f + t.forward * 0.15f;
            Quaternion facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized, Vector3.up);
            GameObject template = walkers[look++ % walkers.Count].gameObject;
            TestDriveIdlePerson p = TestDriveIdlePerson.Create(template, root, seat - Vector3.up * 0.9f, facing, TestDriveIdlePerson.Mode.Sit, (float)random.NextDouble());
            if (p == null) continue;
            p.Sit(seat, t.position.y - 0.3f);
            Cull(p.gameObject, quest);
            made++;
        }
        return made;
    }

    private static int AddBenchSitters(List<ExteriorPedestrianAgent> walkers, Transform root)
    {
        int made = 0, look = 1;
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (t.name != "Street Bench") continue;
            if (made >= (QualitySettings.GetQualityLevel() <= 1 ? 6 : 16) || (made > 0 && ((int)(t.position.x * 13f + t.position.z * 7f) & 1) == 1)) continue;
            var people = new List<TestDriveIdlePerson>();
            // Two people on each bench, side by side, facing out from its back rest (+Z).
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 seat = t.TransformPoint(new Vector3(side * 0.42f, 0.56f, 0.02f));
                Quaternion facing = Quaternion.LookRotation(t.forward, Vector3.up);
                GameObject template = walkers[look++ % walkers.Count].gameObject;
                TestDriveIdlePerson person = TestDriveIdlePerson.Create(template, root, seat - Vector3.up * 0.9f, facing, TestDriveIdlePerson.Mode.Sit, side > 0 ? 0.8f : 0.2f);
                if (person == null) continue;
                person.Sit(seat + Vector3.up * 0.06f, t.position.y);
                people.Add(person);
            }
            var heads = new Transform[people.Count];
            for (int i = 0; i < people.Count; i++) heads[i] = people[i].Head;
            for (int i = 0; i < people.Count; i++) people[i].SetGroup(heads, i);
            made += people.Count;
        }
        return made;
    }

    /// <summary>Ground under the spot and nothing solid around it (buildings, lamp posts, trees, signs).</summary>
    private static bool Clear(Vector3 spot, float radius, out Vector3 grounded)
    {
        grounded = spot;
        RaycastHit hit;
        if (!Physics.Raycast(spot + Vector3.up * 3f, Vector3.down, out hit, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;
        grounded = hit.point;
        Collider[] around = Physics.OverlapCapsule(grounded + Vector3.up * (radius + 0.15f), grounded + Vector3.up * 1.8f, radius,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach (Collider c in around)
            if (c != hit.collider && !(c is CharacterController)) return false;
        return true;
    }

    private static void Cull(GameObject person, bool quest)
    {
        if (!quest) return;
        LODGroup lod = person.AddComponent<LODGroup>();
        lod.SetLODs(new[] { new LOD(0.02f, person.GetComponentsInChildren<Renderer>(true)) });
        lod.RecalculateBounds();
    }
}
