using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Shoppers: people walk along the sidewalk to a shop and go in through its door, and others come out of shops and
/// walk off down the street. They appear and disappear only at a shop door or out of the viewer's sight, so the
/// street simply looks busy. Uses copies of the scene's walkers and their own walking.
/// </summary>
public sealed class TestDriveShoppers : MonoBehaviour
{
    private sealed class Shopper
    {
        public GameObject person;
        public Transform[] path;
        public bool goingIn;
        public float age, still;
        public Vector3 last;
        public TestDriveCity.ShopDoor door;
    }

    private sealed class Pair
    {
        public GameObject[] people;
        public Vector3 at;
    }

    private readonly List<Pair> pairs = new List<Pair>();
    private int pairTarget;
    private float nextPair;

    private static readonly FieldInfo IndexField = typeof(ExteriorPedestrianAgent).GetField("waypointIndex", BindingFlags.Instance | BindingFlags.NonPublic);
    private readonly List<Shopper> shoppers = new List<Shopper>();
    private readonly List<GameObject> templates = new List<GameObject>();
    private int target;
    private float nextSpawn;
    private System.Random random = new System.Random(17);

    public static void Install(Transform parent)
    {
        if (TestDriveCity.ShopDoors.Count == 0) return;
        var go = new GameObject("Shoppers");
        go.transform.SetParent(parent, false);
        TestDriveShoppers s = go.AddComponent<TestDriveShoppers>();
        bool quest = QualitySettings.GetQualityLevel() <= 1 || Application.platform == RuntimePlatform.Android;
        s.target = quest ? 3 : 8;
        s.pairTarget = quest ? 1 : 2;
        foreach (ExteriorPedestrianAgent w in Object.FindObjectsByType<ExteriorPedestrianAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (w.GetComponentInChildren<SkinnedMeshRenderer>(true) != null && s.templates.Count < 8) s.templates.Add(w.gameObject);
    }

    private Transform Viewer => Camera.main != null ? Camera.main.transform : null;

    private bool Unseen(Vector3 p)
    {
        Transform v = Viewer;
        if (v == null) return true;
        Vector3 d = p - v.position;
        return d.magnitude > 45f || Vector3.Dot(d.normalized, v.forward) < 0.2f;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        for (int i = shoppers.Count - 1; i >= 0; i--)
        {
            Shopper s = shoppers[i];
            s.age += dt;
            bool done = s.person == null;
            if (!done)
            {
                Vector3 p = s.person.transform.position, end = s.path[1].position;
                p.y = end.y = 0f;
                // Nobody stands frozen: someone who has not moved for a few seconds has gone in (near the door) or
                // goes on their way out of sight; failing that they are taken away after a little longer.
                if ((p - s.last).sqrMagnitude > 0.04f) { s.last = p; s.still = 0f; }
                else s.still += dt;
                float toDoor = Vector3.Distance(p, new Vector3(s.door.position.x, 0f, s.door.position.z));
                if (s.still > 3f && (toDoor < 3f || Unseen(s.person.transform.position))) { done = true; goto Finish; }
                if (s.still > 8f) { done = true; goto Finish; }
                bool arrived = Vector3.Distance(p, end) < 0.8f;
                // In through the door; or, walking away, gone once out of sight at the end of the walk.
                if (s.goingIn && arrived) done = true;
                else if (!s.goingIn && Unseen(s.person.transform.position) && (arrived || s.age > 20f)) done = true;
                else if (!s.goingIn && arrived)
                {
                    // Still in view at the end of the walk: keep walking on down the sidewalk rather than stop and
                    // turn back.
                    Vector3 on = s.path[1].position - s.path[0].position;
                    on.y = 0f;
                    Vector3 next = s.path[1].position + on.normalized * 15f;
                    if (TestDriveCity.Free(next, 0.4f))
                    {
                        s.path[0].position = s.path[1].position;
                        s.path[1].position = next;
                        ExteriorPedestrianAgent w = s.person.GetComponent<ExteriorPedestrianAgent>();
                        if (w != null && IndexField != null) IndexField.SetValue(w, 1);
                    }
                    else done = true;
                }
                else if (s.age > 90f) done = true;
            }
            Finish:
            if (!done) continue;
            if (s.person != null) Destroy(s.person);
            if (s.path != null && s.path[0] != null) Destroy(s.path[0].parent.gameObject);
            shoppers.RemoveAt(i);
        }
        if (shoppers.Count < target && Time.time >= nextSpawn && templates.Count > 0) Spawn();
        UpdatePairs();
    }

    /// <summary>Now and then two people stopped for a chat by a shop window, out of the walkers' way; there are only
    /// ever one or two such pairs, near the viewer, and they come and go out of sight.</summary>
    private void UpdatePairs()
    {
        Transform v = Viewer;
        if (v == null || templates.Count == 0) return;
        for (int i = pairs.Count - 1; i >= 0; i--)
        {
            Pair pr = pairs[i];
            if ((pr.at - v.position).sqrMagnitude > 80f * 80f && Unseen(pr.at))
            {
                foreach (GameObject g in pr.people) if (g != null) Destroy(g);
                pairs.RemoveAt(i);
            }
        }
        if (pairs.Count >= pairTarget || Time.time < nextPair) return;
        nextPair = Time.time + 3f;
        TestDriveCity.ShopDoor door = TestDriveCity.ShopDoors[random.Next(TestDriveCity.ShopDoors.Count)];
        float dist = Vector3.Distance(new Vector3(door.position.x, 0f, door.position.z), new Vector3(v.position.x, 0f, v.position.z));
        if (dist < 20f || dist > 60f) return;
        Vector3 along = Vector3.Cross(Vector3.up, door.inward).normalized * (random.NextDouble() < 0.5 ? 1f : -1f);
        Vector3 at = door.position + along * (2.6f + (float)random.NextDouble() * 1.5f) + door.inward * 0.4f;
        if (!Unseen(at)) return;
        foreach (Pair other in pairs) if ((other.at - at).sqrMagnitude < 30f * 30f) return;
        foreach (Shopper s in shoppers) if ((s.door.position - door.position).sqrMagnitude < 0.01f) return;
        // Face to face across a short gap, turned a little towards the street.
        Vector3 axis = (along + (-door.inward) * 0.5f).normalized;
        Vector3 a = at - axis * 0.5f, b = at + axis * 0.5f;
        if (TestDriveCity.OnWalkLine(a, 0.7f) || TestDriveCity.OnWalkLine(b, 0.7f)) return;
        if (!TestDriveCity.Free(a, 0.35f) || !TestDriveCity.Free(b, 0.35f)) return;
        var people = new List<TestDriveIdlePerson>();
        foreach (Vector3 p in new[] { a, b })
        {
            GameObject template = templates[random.Next(templates.Count)];
            Vector3 face = Vector3.ProjectOnPlane(at - p, Vector3.up).normalized;
            TestDriveIdlePerson person = TestDriveIdlePerson.Create(template, transform, new Vector3(p.x, template.transform.position.y, p.z),
                Quaternion.LookRotation(face, Vector3.up), TestDriveIdlePerson.Mode.Stand, (float)random.NextDouble());
            if (person == null) continue;
            person.Stand();
            people.Add(person);
        }
        var heads = new Transform[people.Count];
        var gos = new GameObject[people.Count];
        for (int i = 0; i < people.Count; i++) { heads[i] = people[i].Head; gos[i] = people[i].gameObject; }
        for (int i = 0; i < people.Count; i++) people[i].SetGroup(heads, i);
        pairs.Add(new Pair { people = gos, at = at });
    }

    private void Spawn()
    {
        nextSpawn = Time.time + 1.5f;
        Transform v = Viewer;
        // A shop near the viewer, so the life is where they are.
        var near = new List<TestDriveCity.ShopDoor>();
        foreach (TestDriveCity.ShopDoor d in TestDriveCity.ShopDoors)
            if (v == null || (d.position - v.position).sqrMagnitude < 90f * 90f) near.Add(d);
        if (near.Count == 0) return;
        TestDriveCity.ShopDoor door = near[random.Next(near.Count)];
        // One shopper at a door at a time, and never right on top of another.
        foreach (Shopper other in shoppers) if ((other.door.position - door.position).sqrMagnitude < 0.01f) return;
        Vector3 along = Vector3.Cross(Vector3.up, door.inward).normalized * (random.NextDouble() < 0.5 ? 1f : -1f);
        Vector3 far = door.position + along * (14f + (float)random.NextDouble() * 10f);
        bool goingIn = random.NextDouble() < 0.5;
        Vector3 start = goingIn ? far : door.position, end = goingIn ? door.position : far;
        if (goingIn && !Unseen(start)) return;           // nobody pops up in plain view
        if (!TestDriveCity.Free(start, 0.4f) || !TestDriveCity.Free(end, 0.4f)) return;
        foreach (Shopper other in shoppers)
            if (other.person != null && (other.person.transform.position - start).sqrMagnitude < 4f * 4f) return;

        var holder = new GameObject("Shopper Path").transform;
        holder.SetParent(transform, false);
        var a = new GameObject("Start").transform; a.SetParent(holder, false); a.position = start;
        var b = new GameObject("End").transform; b.SetParent(holder, false); b.position = end;
        var path = new[] { a, b };
        GameObject template = templates[random.Next(templates.Count)];
        var staging = new GameObject("Shopper Staging");
        staging.SetActive(false);
        GameObject person = Instantiate(template, staging.transform);
        person.name = goingIn ? "Shopper" : "Shopper Leaving";
        ExteriorPedestrianAgent walker = person.GetComponent<ExteriorPedestrianAgent>();
        walker.Configure(path, 1.0f + (float)random.NextDouble() * 0.35f, template.transform.localScale.x, random.Next(1, 999));
        if (IndexField != null) IndexField.SetValue(walker, 1);
        person.transform.SetParent(transform, true);
        person.transform.SetPositionAndRotation(new Vector3(start.x, template.transform.position.y, start.z),
            Quaternion.LookRotation(Vector3.ProjectOnPlane(end - start, Vector3.up).normalized + Vector3.forward * 1e-4f, Vector3.up));
        Destroy(staging);
        shoppers.Add(new Shopper { person = person, path = path, goingIn = goingIn, door = door, last = new Vector3(start.x, 0f, start.z) });
    }
}
