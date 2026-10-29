using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Quest (quality levels 0 and 1): at rest the car is drawn as merged meshes, one per material, instead of one draw per
// part. Two proxies, each shown only while nothing can differ from the parts themselves:
// - the normal view: the car's own parts (95 renderers);
// - the X-ray overview: the inner content (the V6 and the internals, 738 renderers at Low) and the car's solid parts
//   (the glass shell stays as it is).
// Both need the view at rest with nothing out, apart, moving, gliding back or grabbed, and no part of theirs selected,
// hovered, dimmed or ghosted (no property block). In every other state, and in the frame a change starts, the parts
// draw themselves as before. The parts stay enabled behind a proxy (forceRenderingOff only), so picking treats them as
// visible, and their transforms and materials are never touched. A part whose mesh has no CPU access draws itself.
public partial class CarExplorerController
{
    private const int LowProxyMaxQualityLevel = 1;
    private static readonly HashSet<Renderer> lowProxyHidden = new HashSet<Renderer>();

    private sealed class LowProxy
    {
        public string Name;
        public bool XRay;
        public GameObject Root;
        public readonly List<MeshRenderer> Renderers = new List<MeshRenderer>();
        public readonly List<Mesh> Meshes = new List<Mesh>();
        public readonly List<Renderer> Sources = new List<Renderer>();
        public readonly List<Part> Parts = new List<Part>();
        public bool Built, Shown;
    }

    private readonly LowProxy lowNormalProxy = new LowProxy { Name = "Car Explorer Low Car Proxy", XRay = false };
    private readonly LowProxy lowXRayProxy = new LowProxy { Name = "Car Explorer Low X-Ray Proxy", XRay = true };

    // For tools: draws of the shown proxy (0 when none shows) and whether one shows.
    public int LowProxyDrawCount => lowNormalProxy.Shown ? lowNormalProxy.Renderers.Count : lowXRayProxy.Shown ? lowXRayProxy.Renderers.Count : 0;
    public bool LowProxyShown => lowNormalProxy.Shown || lowXRayProxy.Shown;

    private static bool IsHiddenByLowProxy(Renderer renderer) => lowProxyHidden.Contains(renderer);

    private void LateUpdate()
    {
        if (!initialized || QualitySettings.GetQualityLevel() > LowProxyMaxQualityLevel)
            return;
        bool atRest = takenOut == null && openPath.Count == 0 && transitionT >= 1f && returningParts.Count == 0 && !ghostAnimating;
        bool normal = atRest && view == CarExplorerView.Normal;
        bool xray = atRest && view == CarExplorerView.XRay;
        if (xray)
            foreach (Node assembly in assemblies)
                if (assembly.Inner && !assembly.InnerShown) { xray = false; break; }
        bool wantNormal = normal && IsLowProxyClean(lowNormalProxy);
        bool wantXRay = xray && IsLowProxyClean(lowXRayProxy);
        // Hide before show, so a renderer shared by both proxies (the car's solid parts) ends up in the right state.
        if (!wantNormal) SetLowProxyShown(lowNormalProxy, false);
        if (!wantXRay) SetLowProxyShown(lowXRayProxy, false);
        if (wantNormal) ShowLowProxy(lowNormalProxy);
        if (wantXRay) ShowLowProxy(lowXRayProxy);
    }

    private void ShowLowProxy(LowProxy proxy)
    {
        if (!proxy.Built) BuildLowProxy(proxy);
        SetLowProxyShown(proxy, proxy.Renderers.Count > 0);
    }

    private bool IsInLowProxy(LowProxy proxy, Part part) => proxy.XRay ? part.IsInner || !part.IsShell : !part.IsInner;

    private bool IsLowProxyClean(LowProxy proxy)
    {
        List<Part> candidates = proxy.Built ? proxy.Parts : parts;
        foreach (Part part in candidates)
        {
            if (!proxy.Built && !IsInLowProxy(proxy, part)) continue;
            if (part.Tint != 0 || (part.GrabInteractable != null && part.GrabInteractable.isSelected))
                return false;
            foreach (Renderer renderer in part.Renderers)
                if (renderer != null && renderer.HasPropertyBlock())
                    return false;
        }
        return true;
    }

    private void BuildLowProxy(LowProxy proxy)
    {
        proxy.Built = true;
        var byMaterial = new Dictionary<Material, List<CombineInstance>>();
        var order = new List<Material>();
        foreach (Part part in parts)
        {
            if (!IsInLowProxy(proxy, part)) continue;
            proxy.Parts.Add(part);
            foreach (Renderer renderer in part.Renderers)
            {
                if (!(renderer is MeshRenderer) || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                Material[] materials = renderer.sharedMaterials;
                if (mesh == null || !mesh.isReadable || materials.Length < mesh.subMeshCount) continue;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    Material material = materials[s];
                    if (material == null) continue;
                    if (!byMaterial.TryGetValue(material, out List<CombineInstance> list))
                    {
                        byMaterial[material] = list = new List<CombineInstance>();
                        order.Add(material);
                    }
                    list.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = renderer.localToWorldMatrix });
                }
                proxy.Sources.Add(renderer);
            }
        }
        if (proxy.Sources.Count == 0)
            return;
        // World space: the car does not move while a proxy can show (nothing out, nothing moving).
        proxy.Root = new GameObject(proxy.Name);
        foreach (Material material in order)
        {
            var mesh = new Mesh { name = proxy.Name + " " + material.name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(byMaterial[material].ToArray(), true, true, false);
            mesh.UploadMeshData(true);
            proxy.Meshes.Add(mesh);
            var child = new GameObject(mesh.name);
            child.transform.SetParent(proxy.Root.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.enabled = false;
            proxy.Renderers.Add(renderer);
        }
    }

    private static void SetLowProxyShown(LowProxy proxy, bool shown)
    {
        if (shown == proxy.Shown)
            return;
        proxy.Shown = shown;
        foreach (MeshRenderer renderer in proxy.Renderers)
            if (renderer != null) renderer.enabled = shown;
        foreach (Renderer source in proxy.Sources)
        {
            if (source == null) continue;
            source.forceRenderingOff = shown;
            if (shown) lowProxyHidden.Add(source);
            else lowProxyHidden.Remove(source);
        }
    }

    private void SetLowProxyShown(bool shown)
    {
        SetLowProxyShown(lowNormalProxy, shown);
        SetLowProxyShown(lowXRayProxy, shown);
    }

    private void DestroyLowProxy()
    {
        foreach (LowProxy proxy in new[] { lowNormalProxy, lowXRayProxy })
        {
            SetLowProxyShown(proxy, false);
            foreach (Mesh mesh in proxy.Meshes)
                if (mesh != null) Destroy(mesh);
            proxy.Meshes.Clear();
            proxy.Renderers.Clear();
            proxy.Sources.Clear();
            proxy.Parts.Clear();
            if (proxy.Root != null) Destroy(proxy.Root);
            proxy.Root = null;
            proxy.Built = false;
        }
    }
}
