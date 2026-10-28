using UnityEngine;

/// <summary>
/// Runtime walls, prop colliders and fallback ground shared by the desktop body, the XR rig and Remy.
/// Safe to apply repeatedly.
/// </summary>
public static class ShowroomCollisionSetup
{
    /// <summary>Players below this height have left the walkable world and are returned to the spawn.</summary>
    public const float RespawnBelowHeight = -25f;

    public static void Apply()
    {
        AddFloorSolid();
        AddArchitectureCollision();
        AddDisplayCarCollision();
        AddFurnitureCollision();
        AddExteriorGround();
    }

    /// <summary>Highest non-trigger surface below <paramref name="origin"/>, ignoring character capsules.</summary>
    public static bool TryGetFloorHeight(Vector3 origin, float maxDistance, out float floorY)
    {
        floorY = float.NegativeInfinity;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, maxDistance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
            if (hit.collider != null && hit.collider.GetComponent<CharacterController>() == null)
                floorY = Mathf.Max(floorY, hit.point.y);
        return !float.IsNegativeInfinity(floorY);
    }

    private static void AddFloorSolid()
    {
        // The showroom floor is a zero-thickness Plane with a one-sided MeshCollider. A character capsule that
        // ends up even slightly below it (a frame hitch, an overlap resolved downwards) falls straight through,
        // so back it with a 1 m deep box whose top is the floor surface.
        GameObject floor = GameObject.Find("Showroom Floor");
        if (floor == null || floor.GetComponent<BoxCollider>() != null) return;
        MeshFilter filter = floor.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;
        Bounds mesh = filter.sharedMesh.bounds;
        float depth = 1f / Mathf.Max(Mathf.Abs(floor.transform.lossyScale.y), 0.0001f);
        BoxCollider solid = floor.AddComponent<BoxCollider>();
        solid.center = new Vector3(mesh.center.x, mesh.max.y - depth * 0.5f, mesh.center.z);
        solid.size = new Vector3(mesh.size.x, depth, mesh.size.z);
    }

    private static void AddArchitectureCollision()
    {
        GameObject architecture = GameObject.Find("Showroom Architecture");
        if (architecture == null) return;
        MeshFilter[] meshes = architecture.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter filter in meshes)
        {
            if (filter == null || filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
            Renderer renderer = filter.GetComponent<Renderer>();
            if (renderer == null || !renderer.enabled) continue;
            Bounds b = renderer.bounds;
            if (b.size.y < 1.15f || (b.size.x < 1.4f && b.size.z < 1.4f)) continue;
            MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = false;
        }
    }

    private static void AddDisplayCarCollision()
    {
        // glTFast imports the premium cars without colliders. One box per display around all of its meshes,
        // measured in the display's own space, so it follows however the display is turned or re-centred.
        GameObject collection = GameObject.Find("Showroom Premium Collection");
        if (collection == null) return;
        foreach (Transform display in collection.transform)
        {
            if (display.GetComponentInChildren<Collider>(true) != null) continue;
            Matrix4x4 worldToDisplay = display.worldToLocalMatrix;
            Bounds local = default;
            bool hasBounds = false;
            foreach (Renderer renderer in display.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    Encapsulate(ref local, ref hasBounds, worldToDisplay * renderer.transform.localToWorldMatrix,
                        filter.sharedMesh.bounds);
                else
                    Encapsulate(ref local, ref hasBounds, worldToDisplay, renderer.bounds);
            }
            if (!hasBounds) continue;
            BoxCollider collider = display.gameObject.AddComponent<BoxCollider>();
            collider.center = local.center;
            collider.size = local.size;
        }
    }

    private static void AddFurnitureCollision()
    {
        GameObject zones = GameObject.Find("Showroom Interior Zones");
        if (zones == null) return;
        GameObject floor = GameObject.Find("Showroom Floor");
        Renderer floorRenderer = floor != null ? floor.GetComponent<Renderer>() : null;
        float floorY = floorRenderer != null ? floorRenderer.bounds.max.y : zones.transform.position.y;
        foreach (MeshFilter filter in zones.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
            Renderer renderer = filter.GetComponent<Renderer>();
            if (renderer == null || !renderer.enabled) continue;
            Bounds b = renderer.bounds;
            // Rugs, plinths and feet stay below step height. Seats, tables, planters and the counter front start
            // below hip height, and the counter worktop is a thin slab below chest height. The wall battens, the
            // plaque and the wall displays start higher up; the 3D text has no MeshFilter.
            if (b.max.y < floorY + 0.25f) continue;
            if (b.min.y > floorY + 0.6f && (b.size.y > 0.3f || b.min.y > floorY + 1.3f)) continue;
            BoxCollider collider = filter.gameObject.AddComponent<BoxCollider>();
            Bounds mesh = filter.sharedMesh.bounds;
            collider.center = mesh.center;
            collider.size = mesh.size;
        }
    }

    private static void Encapsulate(ref Bounds bounds, ref bool hasBounds, Matrix4x4 toSpace, Bounds box)
    {
        Vector3 min = box.min;
        Vector3 max = box.max;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = toSpace.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? min.x : max.x,
                (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z));
            if (hasBounds)
            {
                bounds.Encapsulate(corner);
            }
            else
            {
                bounds = new Bounds(corner, Vector3.zero);
                hasBounds = true;
            }
        }
    }

    private static void AddExteriorGround()
    {
        if (GameObject.Find("Desktop Walkable Exterior Ground") != null) return;
        // Safety net under the lawns around the showroom; the Rolling City Ground collider stays the walking
        // surface. Its top (-0.10) is flush with or below the grass, and below the -0.05 showroom floor.
        GameObject ground = new GameObject("Desktop Walkable Exterior Ground");
        ground.transform.position = new Vector3(1f, -0.21f, 0f);
        BoxCollider collider = ground.AddComponent<BoxCollider>();
        collider.size = new Vector3(100f, 0.22f, 100f);
    }
}
