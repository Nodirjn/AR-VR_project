using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

/// <summary>
/// Planar reflection of the hero car in the polished podium deck (PC, highest quality level, desktop only).
/// Before each game camera renders, a hidden camera mirrored in the deck plane renders only the car (its renderers are
/// moved to <see cref="mirrorLayer"/> at start) into a half-resolution HDR texture with a mip chain, through a
/// renderer without SSAO or post-processing, without shadows or MSAA. The deck's shader (Aurelle/Planar Reflective
/// Lit) swaps its probe reflection for this texture where the car covers it. Off at lower quality levels, in XR, in the
/// explorer's X-ray view, for cameras below the deck and for scene/preview cameras: the deck then renders exactly
/// like URP Lit.
/// Allocation-free per frame; the texture is reallocated only when the camera's size changes.
/// </summary>
[DisallowMultipleComponent]
public sealed class AurellePlanarReflection : MonoBehaviour
{
    [Tooltip("The polished deck (its renderer's bounds decide whether a camera sees it).")]
    public Renderer deck;
    [Tooltip("World height of the deck's top surface: the mirror plane.")]
    public float planeHeight = 0.018f;
    [Tooltip("Car root; found by name when empty.")]
    public Transform car;
    public string carName = "CarConcept";
    [Tooltip("Free layer the car's renderers move to so that only they are drawn into the reflection.")]
    public int mirrorLayer = 30;
    [Tooltip("Index of the PC pipeline asset's renderer without SSAO or post (set by the render setup tool); -1 = default.")]
    public int mirrorRendererIndex = -1;
    [Tooltip("Lowest quality level that draws the reflection (Ultra = 5).")]
    public int minQualityLevel = 5;
    [Range(0.25f, 1f)] public float resolutionScale = 0.5f;
    [Range(0f, 1f)] public float strength = 1f;
    [Tooltip("Base blur in mips, extra mips per unit of perceptual roughness, ripple offset per unit of normal tilt.")]
    public float baseMip = 0.6f, mipsPerRoughness = 7f, rippleOffset = 0.02f;

    private static readonly int TexId = Shader.PropertyToID("_AurellePlanarReflectionTex");
    private static readonly int ParamsId = Shader.PropertyToID("_AurellePlanarParams");
    private static readonly Vector4 Off = Vector4.zero;

    private Camera mirror;
    private RenderTexture target;
    private CommandBuffer mips;
    private Renderer[] carRenderers;
    private int[] carLayers;
    private readonly Plane[] frustum = new Plane[6];
    private Bounds deckBounds;
    private bool layersMoved;

    private void OnEnable()
    {
        Shader.SetGlobalVector(ParamsId, Off);
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        Shader.SetGlobalVector(ParamsId, Off);
        RestoreLayers();
        ReleaseTarget();
        if (mirror != null) Destroy(mirror.gameObject);
        mirror = null;
        mips?.Release();
        mips = null;
    }

    private bool QualityAllows => QualitySettings.GetQualityLevel() >= minQualityLevel && !XRSettings.isDeviceActive;

    private void Update()
    {
        // Give the texture back and the car its layers as soon as the quality level drops or a headset starts.
        if (!QualityAllows)
        {
            if (target != null) ReleaseTarget();
            RestoreLayers();
        }
    }

    private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (cam == mirror) return;
        if (!Prepare(cam))
        {
            Shader.SetGlobalVector(ParamsId, Off);
            return;
        }
        RenderMirror(context, cam);
        Shader.SetGlobalTexture(TexId, target);
        Shader.SetGlobalVector(ParamsId, new Vector4(strength, baseMip, mipsPerRoughness, rippleOffset));
    }

    private bool Prepare(Camera cam)
    {
        if (strength <= 0f || cam.cameraType != CameraType.Game || cam.stereoEnabled || !QualityAllows) return false;
        if (cam.orthographic || cam.transform.position.y <= PlaneY + 0.02f) return false;
        // The X-ray view draws the shell as glass plus the V6 and the internals: their reflection in the stone adds
        // little and would double the X-ray view's draws, so the deck shows its probe reflection there.
        ICarExplorerModel model = CarExplorerModels.Current;
        if (model != null && model.View == CarExplorerView.XRay) return false;
        if (!EnsureCar()) return false;
        GeometryUtility.CalculateFrustumPlanes(cam, frustum);
        if (!GeometryUtility.TestPlanesAABB(frustum, deckBounds)) return false;
        EnsureMirror();
        // The texture only grows (cameras of other sizes share it: the deck samples it by normalised screen position),
        // so cameras of different sizes in one frame never free and reallocate it.
        int w = Mathf.Max(16, Mathf.RoundToInt(cam.pixelWidth * resolutionScale));
        int h = Mathf.Max(16, Mathf.RoundToInt(cam.pixelHeight * resolutionScale));
        if (target == null || target.width < w || target.height < h)
        {
            if (target != null) { w = Mathf.Max(w, target.width); h = Mathf.Max(h, target.height); }
            ReleaseTarget();
            target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = "Aurelle Planar Reflection",
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
            };
            target.Create();
        }
        return true;
    }

    private float PlaneY => planeHeight;

    private bool EnsureCar()
    {
        if (carRenderers != null && layersMoved) return true;
        if (car == null)
        {
            GameObject found = GameObject.Find(carName);
            if (found == null) return false;
            car = found.transform;
        }
        deckBounds = deck != null ? deck.bounds : new Bounds(car.position, new Vector3(6f, 0.1f, 6f));
        carRenderers = car.GetComponentsInChildren<Renderer>(true);
        carLayers = new int[carRenderers.Length];
        for (int i = 0; i < carRenderers.Length; i++)
        {
            GameObject go = carRenderers[i].gameObject;
            carLayers[i] = go.layer;
            go.layer = mirrorLayer;
        }
        layersMoved = true;
        return true;
    }

    private void RestoreLayers()
    {
        if (!layersMoved || carRenderers == null) return;
        for (int i = 0; i < carRenderers.Length; i++)
            if (carRenderers[i] != null) carRenderers[i].gameObject.layer = carLayers[i];
        layersMoved = false;
    }

    private void EnsureMirror()
    {
        if (mirror != null) return;
        var go = new GameObject("Aurelle Planar Reflection Camera") { hideFlags = HideFlags.HideAndDontSave };
        mirror = go.AddComponent<Camera>();
        mirror.enabled = false;
        mirror.cullingMask = 1 << mirrorLayer;
        mirror.clearFlags = CameraClearFlags.SolidColor;
        mirror.backgroundColor = new Color(0f, 0f, 0f, 0f);
        mirror.allowHDR = true;
        mirror.allowMSAA = false;
        mirror.useOcclusionCulling = false;
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.antialiasing = AntialiasingMode.None;
        data.requiresDepthOption = CameraOverrideOption.Off;
        data.requiresColorOption = CameraOverrideOption.Off;
        data.stopNaN = false;
        data.dithering = false;
        if (mirrorRendererIndex >= 0) data.SetRenderer(mirrorRendererIndex);
        mips = new CommandBuffer { name = "Aurelle Planar Reflection Mips" };
    }

    private void RenderMirror(ScriptableRenderContext context, Camera cam)
    {
        float y = PlaneY;
        // Reflection across the horizontal plane y = PlaneY.
        Matrix4x4 reflect = Matrix4x4.identity;
        reflect.m11 = -1f;
        reflect.m13 = 2f * y;
        Matrix4x4 view = cam.worldToCameraMatrix * reflect;

        Vector3 eye = cam.transform.position;
        mirror.transform.SetPositionAndRotation(new Vector3(eye.x, 2f * y - eye.y, eye.z),
            Quaternion.LookRotation(reflect.MultiplyVector(cam.transform.forward), reflect.MultiplyVector(cam.transform.up)));
        mirror.fieldOfView = cam.fieldOfView;
        mirror.aspect = cam.aspect;
        mirror.nearClipPlane = cam.nearClipPlane;
        mirror.farClipPlane = cam.farClipPlane;
        mirror.worldToCameraMatrix = view;

        // Oblique near plane on the deck, then a horizontal flip that undoes the mirror's winding change (the deck's
        // shader samples the texture with u flipped).
        Vector3 cp = view.MultiplyPoint(new Vector3(0f, y + 0.002f, 0f));
        Vector3 cn = view.MultiplyVector(Vector3.up).normalized;
        Vector4 clip = new Vector4(cn.x, cn.y, cn.z, -Vector3.Dot(cp, cn));
        Matrix4x4 proj = Oblique(cam.projectionMatrix, clip);
        Matrix4x4 flip = Matrix4x4.identity;
        flip.m00 = -1f;
        mirror.projectionMatrix = flip * proj;
        mirror.targetTexture = target;

        // The reflection has no shadow maps, so the sun would light the car straight through the roof: it is left
        // out of this one render (indoors the roof shadows it anyway).
        Light sun = RenderSettings.sun;
        bool sunOn = sun != null && sun.enabled && sun.type == LightType.Directional;
        if (sunOn) sun.enabled = false;
#pragma warning disable 0618 // SubmitRenderRequest cannot be issued while a camera renders; this is URP's nested path
        UniversalRenderPipeline.RenderSingleCamera(context, mirror);
#pragma warning restore 0618
        if (sunOn) sun.enabled = true;

        mips.Clear();
        mips.GenerateMips(target);
        context.ExecuteCommandBuffer(mips);
    }

    // Eric Lengyel's oblique near-plane projection (as in Unity's Camera.CalculateObliqueMatrix).
    private static Matrix4x4 Oblique(Matrix4x4 projection, Vector4 clipPlane)
    {
        Vector4 q = projection.inverse * new Vector4(Mathf.Sign(clipPlane.x), Mathf.Sign(clipPlane.y), 1f, 1f);
        Vector4 c = clipPlane * (2f / Vector4.Dot(clipPlane, q));
        projection.m20 = c.x - projection.m30;
        projection.m21 = c.y - projection.m31;
        projection.m22 = c.z - projection.m32;
        projection.m23 = c.w - projection.m33;
        return projection;
    }

    private void ReleaseTarget()
    {
        if (target == null) return;
        if (mirror != null) mirror.targetTexture = null;
        target.Release();
        Destroy(target);
        target = null;
    }
}
