using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lets the city react to the visitor's test-drive car: the traffic agents read <see cref="Movers"/> and yield to it
/// like to a rider, and <see cref="EnsureTrafficColliders"/> gives the traffic and parked cars a solid body, so the
/// driven car cannot pass through them.
/// </summary>
public static class TestDriveTraffic
{
    public sealed class Mover
    {
        public Rigidbody Body;
        /// <summary>For a car moved by script rather than physics (a traffic car pulling in to the kerb).</summary>
        public Transform Root;
        public Vector3 ManualVelocity;
        public float HalfLength = 2.3f;
        public float HalfWidth = 0.95f;

        private Transform Frame => Body != null ? Body.transform : Root;
        public bool IsAlive => Frame != null && Frame.gameObject.activeInHierarchy;
        public Vector3 Position => Body != null ? Body.position : Root.position;

        public Vector3 Velocity
        {
            get
            {
                Vector3 v = Body != null ? Body.linearVelocity : ManualVelocity;
                return new Vector3(v.x, 0f, v.z);
            }
        }

        public Vector3 Heading
        {
            get
            {
                Vector3 f = Frame.forward;
                f.y = 0f;
                return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
            }
        }
    }

    public static readonly List<Mover> Movers = new List<Mover>();

    private const string ColliderName = "Test Drive Solid Body";
    private static bool collidersAdded;

    public static void Register(Mover mover)
    {
        if (mover != null && !Movers.Contains(mover)) Movers.Add(mover);
    }

    public static void Unregister(Mover mover)
    {
        Movers.Remove(mover);
    }

    /// <summary>One box per moving or parked city car (sized to its renderers), with a kinematic body on the movers
    /// so the physics engine sweeps them correctly. Runs once.</summary>
    public static void EnsureTrafficColliders()
    {
        if (collidersAdded) return;
        collidersAdded = true;
        ExteriorTrafficCarAgent[] agents = Object.FindObjectsByType<ExteriorTrafficCarAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < agents.Length; i++) AddBox(agents[i].transform, true);

        GameObject exterior = GameObject.Find("Exterior Environment");
        if (exterior == null) return;
        foreach (Transform t in exterior.GetComponentsInChildren<Transform>(true))
            if (t.name == "Parked Customer Vehicle") AddBox(t, false);
    }

    private static void AddBox(Transform car, bool moving)
    {
        if (car == null || car.Find(ColliderName) != null) return;
        Bounds local = new Bounds();
        bool found = false;
        foreach (MeshFilter filter in car.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Matrix4x4 toCar = car.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Bounds b = filter.sharedMesh.bounds;
            for (int k = 0; k < 8; k++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                Vector3 p = toCar.MultiplyPoint3x4(corner);
                if (!found) { local = new Bounds(p, Vector3.zero); found = true; }
                else local.Encapsulate(p);
            }
        }
        if (!found || local.size.z < 2f) return;
        var go = new GameObject(ColliderName);
        go.transform.SetParent(car, false);
        BoxCollider box = go.AddComponent<BoxCollider>();
        // Keep clear of the ground so the box never snags the road.
        float bottom = local.min.y + 0.18f;
        box.center = new Vector3(local.center.x, (bottom + local.max.y) * 0.5f, local.center.z);
        box.size = new Vector3(local.size.x * 0.96f, Mathf.Max(0.3f, local.max.y - bottom), local.size.z * 0.97f);
        if (!moving || car.GetComponent<Rigidbody>() != null) return;
        Rigidbody body = car.gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.None;
    }
}
