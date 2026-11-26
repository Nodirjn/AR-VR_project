using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Errands: now and then a car in the traffic indicates, slows and pulls in to the kerb near a shop, the driver gets
/// out, walks to the shop and goes in; a while later they come out, walk back, get in, and the car pulls out into the
/// traffic again. Breaks up the column of cars and makes the street look used. While a car is out of the agent's
/// hands it is registered with <see cref="TestDriveTraffic"/>, so the traffic behind slows for it as it pulls in and out.
/// </summary>
public sealed class TestDriveCurbStops : MonoBehaviour
{
    private enum Phase { Driving, PullingIn, Parked, PullingOut }

    private sealed class Car
    {
        public ExteriorTrafficCarAgent agent;
        public Phase phase;
        public float nextTry, t, duration, cruise;
        public Vector3 p0, p1, p2, p3;
        public TestDriveTraffic.Mover mover;
        public GameObject driver;
        public TestDriveCity.ShopDoor shop;
        public int errand; // 0 walking there, 1 inside, 2 walking back
        public float timer;
        public Vector3 door;
        public Transform[] path;
    }

    private static readonly FieldInfo RouteField = typeof(ExteriorTrafficCarAgent).GetField("route", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo CruiseField = typeof(ExteriorTrafficCarAgent).GetField("cruisingSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo IndexField = typeof(ExteriorPedestrianAgent).GetField("waypointIndex", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly List<Car> cars = new List<Car>();
    private GameObject walkerTemplate;
    private int maxStopped;

    public static void Install(Transform parent, IEnumerable<ExteriorTrafficCarAgent> agents)
    {
        var go = new GameObject("Kerbside Errands");
        go.transform.SetParent(parent, false);
        TestDriveCurbStops c = go.AddComponent<TestDriveCurbStops>();
        bool quest = QualitySettings.GetQualityLevel() <= 1 || Application.platform == RuntimePlatform.Android;
        c.maxStopped = quest ? 2 : 6;
        var random = new System.Random(4);
        foreach (ExteriorTrafficCarAgent a in agents)
            c.cars.Add(new Car { agent = a, nextTry = 6f + (float)random.NextDouble() * 40f });
        foreach (ExteriorPedestrianAgent w in Object.FindObjectsByType<ExteriorPedestrianAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (w.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) { c.walkerTemplate = w.gameObject; break; }
    }

    private void Update()
    {
        float now = Time.time, dt = Time.deltaTime;
        int stopped = 0;
        foreach (Car c in cars) if (c.phase != Phase.Driving) stopped++;
        foreach (Car c in cars)
        {
            if (c.agent == null) continue;
            switch (c.phase)
            {
                case Phase.Driving:
                    if (now >= c.nextTry && stopped < maxStopped && c.agent.isActiveAndEnabled && TryPullIn(c)) stopped++;
                    else if (now >= c.nextTry) c.nextTry = now + 3f;
                    break;
                case Phase.PullingIn: Move(c, dt, true); break;
                case Phase.Parked: Errand(c, dt); break;
                case Phase.PullingOut: Move(c, dt, false); break;
            }
        }
    }

    // ------------------------------------------------------------------ pulling in

    private bool TryPullIn(Car c)
    {
        Transform car = c.agent.transform;
        Vector3 heading = Vector3.ProjectOnPlane(car.forward, Vector3.up).normalized;
        float speed = c.agent.Speed;
        if (speed < 1.5f || speed > 14f) return false;
        TestDriveCity.Street street;
        float lateralSign;
        if (!OnStreet(car.position, heading, out street, out lateralSign)) return false;
        // A spot 18 m ahead, on the kerb side, not at a junction, near a shop, with nobody there.
        Vector3 ahead = car.position + heading * 18f;
        float along = street.eastWest ? ahead.x : ahead.z;
        if (along < street.from + 15f || along > street.to - 15f) return false;
        foreach (Vector2 j in TestDriveCity.Junctions)
        {
            Vector2 p = street.eastWest ? new Vector2(along, street.centre) : new Vector2(street.centre, along);
            if ((p - j).sqrMagnitude < 20f * 20f) return false;
        }
        Vector3 spot = street.eastWest ? new Vector3(along, car.position.y, street.centre + lateralSign * 5.0f)
                                       : new Vector3(street.centre + lateralSign * 5.0f, car.position.y, along);
        // The nearest shop on that side, within a short walk.
        float best = 60f * 60f;
        bool found = false;
        foreach (TestDriveCity.ShopDoor d in TestDriveCity.ShopDoors)
        {
            Vector3 toDoor = d.position - spot;
            float lateralToDoor = street.eastWest ? toDoor.z : toDoor.x;
            if (lateralToDoor * lateralSign < 0f) continue; // the other side of the street
            float dist = toDoor.sqrMagnitude;
            if (dist < best) { best = dist; c.shop = d; found = true; }
        }
        if (!found) return false;
        foreach (ExteriorTrafficCarAgent other in ExteriorTrafficCarAgent.Active)
            if (other != c.agent && (other.transform.position - spot).sqrMagnitude < 9f * 9f) return false;
        foreach (Car other in cars)
            if (other != c && other.phase != Phase.Driving && (other.p3 - spot).sqrMagnitude < 14f * 14f) return false;

        // Hand the car over: a smooth curve from the lane to the kerb, braking to a stop.
        c.cruise = CruiseField != null ? (float)CruiseField.GetValue(c.agent) : 7f;
        c.agent.enabled = false;
        c.p0 = car.position;
        c.p3 = spot;
        c.p1 = c.p0 + heading * 6f;
        c.p2 = c.p3 - heading * 6f;
        c.duration = Mathf.Clamp(2f * 19f / Mathf.Max(speed, 3f), 3f, 8f);
        c.t = 0f;
        c.phase = Phase.PullingIn;
        c.mover = new TestDriveTraffic.Mover { Root = car, HalfLength = 2.4f, HalfWidth = 1f };
        TestDriveTraffic.Register(c.mover);
        return true;
    }

    private static bool OnStreet(Vector3 p, Vector3 heading, out TestDriveCity.Street street, out float lateralSign)
    {
        street = default(TestDriveCity.Street);
        lateralSign = 0f;
        foreach (TestDriveCity.Street s in TestDriveCity.Streets)
        {
            float lateral = s.eastWest ? p.z - s.centre : p.x - s.centre;
            float along = s.eastWest ? p.x : p.z;
            if (Mathf.Abs(lateral) > 6f || along < s.from || along > s.to) continue;
            // Along the street, and on the side the car drives on (right-hand traffic: the kerb is on the right).
            Vector3 axis = s.eastWest ? Vector3.right : Vector3.forward;
            if (Mathf.Abs(Vector3.Dot(heading, axis)) < 0.95f) continue;
            Vector3 right = Vector3.Cross(Vector3.up, heading);
            float rightSign = s.eastWest ? Mathf.Sign(right.z) : Mathf.Sign(right.x);
            if (lateral * rightSign < 0.2f) continue;
            street = s;
            lateralSign = rightSign;
            return true;
        }
        return false;
    }

    private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
    {
        float u = 1f - t;
        return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
    }

    private void Move(Car c, float dt, bool pullingIn)
    {
        c.t += dt / c.duration;
        float s = Mathf.Clamp01(c.t);
        // Pulling in: decelerating (fast at first). Pulling out: accelerating.
        float eased = pullingIn ? 1f - (1f - s) * (1f - s) : s * s;
        Transform car = c.agent.transform;
        Vector3 before = car.position;
        Vector3 p = Bezier(c.p0, c.p1, c.p2, c.p3, eased);
        Vector3 q = Bezier(c.p0, c.p1, c.p2, c.p3, Mathf.Min(1f, eased + 0.02f));
        car.position = p;
        Vector3 dir = Vector3.ProjectOnPlane(q - p, Vector3.up);
        if (dir.sqrMagnitude > 1e-6f) car.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        if (dt > 0f) c.mover.ManualVelocity = (p - before) / dt;
        if (s < 1f) return;
        c.mover.ManualVelocity = Vector3.zero;
        if (pullingIn)
        {
            c.phase = Phase.Parked;
            StartErrand(c);
        }
        else
        {
            // Back to the agent: on its lane, heading on along its route at a moderate speed.
            TestDriveTraffic.Unregister(c.mover);
            var route = RouteField != null ? RouteField.GetValue(c.agent) as Transform[] : null;
            if (route != null && route.Length > 1)
                c.agent.SetStartIndex(ExteriorTrafficCarAgent.NearestTargetIndex(route, car.position, car.forward));
            c.agent.enabled = true;
            c.phase = Phase.Driving;
            c.nextTry = Time.time + 40f + Random.value * 60f;
        }
    }

    // ------------------------------------------------------------------ the errand

    private void StartErrand(Car c)
    {
        Transform car = c.agent.transform;
        // The driver's door is on the left of the car (the street side of a kerb on the right... so they walk round
        // the front to the sidewalk): step out at the front corner on the kerb side.
        Vector3 kerbSide = car.right;
        if (Vector3.Dot(kerbSide, c.shop.position - car.position) < 0f) kerbSide = -kerbSide;
        c.door = car.position + kerbSide * 1.4f + car.forward * 0.6f;
        c.errand = 0;
        c.timer = 0f;
        if (walkerTemplate == null) { c.errand = 1; c.timer = 20f; return; }
        // Waypoints: the car, the sidewalk in front of the shop, the door; the walker's own logic does the walking.
        var holder = new GameObject("Errand Path").transform;
        holder.SetParent(transform, false);
        Vector3 doorStep = c.shop.position;
        var a = new GameObject("From Car").transform; a.SetParent(holder, false); a.position = c.door;
        var b = new GameObject("Shop Door").transform; b.SetParent(holder, false); b.position = doorStep;
        c.path = new[] { a, b };
        var staging = new GameObject("Driver Staging");
        staging.SetActive(false);
        GameObject person = Instantiate(walkerTemplate, staging.transform);
        person.name = "Driver on an Errand";
        ExteriorPedestrianAgent walker = person.GetComponent<ExteriorPedestrianAgent>();
        walker.Configure(c.path, 1.25f, walkerTemplate.transform.localScale.x, Random.Range(1, 999));
        if (IndexField != null) IndexField.SetValue(walker, 1);
        person.transform.SetParent(transform, true);
        person.transform.position = new Vector3(c.door.x, walkerTemplate.transform.position.y, c.door.z);
        person.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(doorStep - c.door, Vector3.up).normalized + Vector3.forward * 1e-4f, Vector3.up);
        Destroy(staging);
        c.driver = person;
    }

    private void Errand(Car c, float dt)
    {
        c.timer += dt;
        switch (c.errand)
        {
            case 0: // walking to the shop
                if (c.driver == null || Flat(c.driver.transform.position, c.shop.position) < 0.8f || c.timer > 40f)
                {
                    if (c.driver != null) c.driver.SetActive(false); // in through the door
                    c.errand = 1;
                    c.timer = 0f;
                }
                break;
            case 1: // inside
                if (c.timer > 18f + (c.agent.GetInstanceID() & 15))
                {
                    if (c.driver != null)
                    {
                        c.driver.transform.position = new Vector3(c.shop.position.x, c.driver.transform.position.y, c.shop.position.z);
                        ExteriorPedestrianAgent w = c.driver.GetComponent<ExteriorPedestrianAgent>();
                        if (w != null && IndexField != null) IndexField.SetValue(w, 0); // back to the car
                        c.driver.SetActive(true);
                    }
                    c.errand = 2;
                    c.timer = 0f;
                }
                break;
            default: // walking back
                if (c.driver == null || Flat(c.driver.transform.position, c.door) < 0.8f || c.timer > 40f)
                {
                    if (c.driver != null) Destroy(c.driver);
                    if (c.path != null && c.path.Length > 0 && c.path[0] != null) Destroy(c.path[0].parent.gameObject);
                    c.driver = null;
                    PullOut(c);
                }
                break;
        }
    }

    private void PullOut(Car c)
    {
        Transform car = c.agent.transform;
        Vector3 heading = Vector3.ProjectOnPlane(car.forward, Vector3.up).normalized;
        // Wait for a gap in the lane beside and behind.
        foreach (ExteriorTrafficCarAgent other in ExteriorTrafficCarAgent.Active)
        {
            Vector3 d = other.transform.position - car.position;
            if (d.magnitude < 22f && Vector3.Dot(d, heading) < 8f) { c.timer = 0f; c.errand = 2; return; }
        }
        Vector3 lanePoint = car.position + heading * 20f;
        Vector3 right = Vector3.Cross(Vector3.up, heading);
        // The lane is 1.6 m from the centre line; the car stands 5 m from it.
        lanePoint -= right * 3.4f;
        c.p0 = car.position;
        c.p1 = c.p0 + heading * 6f;
        c.p2 = lanePoint - heading * 7f;
        c.p3 = lanePoint;
        c.t = 0f;
        c.duration = 4.5f;
        c.phase = Phase.PullingOut;
    }

    private static float Flat(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
