using UnityEngine;

/// <summary>
/// Soft ambient-occlusion shadow under the hero car: a transparent quad on the podium deck carrying a texture the
/// render setup tool generates from the car's footprint (Assets/Materials/Realism/Rendering). It works at every
/// quality level (URP Unlit, alpha blended). The car's parts move, the footprint does not, so the shadow fades out
/// while the Car Explorer has anything out of the car or exploded, stays out while those parts travel back, and fades
/// back in once the car is whole and at rest. The X-ray view keeps it (the car is still there). Reads
/// <see cref="CarExplorerModels.Current"/> only; allocation-free per frame.
/// </summary>
[DisallowMultipleComponent]
public sealed class AurelleHeroContactShadow : MonoBehaviour
{
    public Renderer shadowQuad;
    [Tooltip("Opacity of the shadow while the car is whole and at rest.")]
    [Range(0f, 1f)] public float opacity = 1f;
    [Tooltip("Seconds for a full fade in or out.")]
    public float fadeSeconds = 0.45f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock block;
    private float current = -1f;
    private bool partsWereOut;

    private void OnEnable()
    {
        if (shadowQuad == null) shadowQuad = GetComponentInChildren<Renderer>(true);
        current = -1f;
    }

    private void LateUpdate()
    {
        if (shadowQuad == null) return;
        float target = TargetOpacity();
        float next = current < 0f ? target : Mathf.MoveTowards(current, target, Time.deltaTime / Mathf.Max(0.01f, fadeSeconds));
        if (Mathf.Approximately(next, current)) return;
        current = next;
        Apply(current);
    }

    private float TargetOpacity()
    {
        ICarExplorerModel model = CarExplorerModels.Current;
        if (model == null) { partsWereOut = false; return opacity; }
        bool partsOut = model.View == CarExplorerView.Exploded || model.TakenOut != null;
        if (partsOut) { partsWereOut = true; return 0f; }
        // Parts that are still travelling back keep the shadow off until the car is whole again.
        if (model.IsMoving && partsWereOut) return 0f;
        if (!model.IsMoving) partsWereOut = false;
        return opacity;
    }

    private void Apply(float alpha)
    {
        bool visible = alpha > 0.002f;
        if (shadowQuad.enabled != visible) shadowQuad.enabled = visible;
        if (!visible) return;
        if (block == null) block = new MaterialPropertyBlock();
        if (alpha >= opacity - 0.002f && Mathf.Approximately(opacity, 1f))
        {
            // At rest the material's own colour applies: no block, so the quad stays SRP Batcher compatible.
            shadowQuad.SetPropertyBlock(null);
            return;
        }
        block.SetColor(BaseColorId, new Color(0f, 0f, 0f, alpha));
        shadowQuad.SetPropertyBlock(block);
    }
}
