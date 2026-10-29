#ifndef AURELLE_PLANAR_REFLECTION_INCLUDED
#define AURELLE_PLANAR_REFLECTION_INCLUDED

// Planar reflection for "Aurelle/Planar Reflective Lit" (the podium deck). AurellePlanarReflection renders the hero car
// mirrored in the deck plane into _AurellePlanarReflectionTex (half resolution, mip chain, alpha = coverage, the image
// flipped in x so the mirrored camera keeps its triangle winding) and sets the globals below. Where the texture has
// coverage, the environment probe's specular term is replaced by the planar one with URP's own environment BRDF, so
// the Fresnel and the occlusion are exactly URP Lit's. Globals only: the material stays SRP Batcher compatible.

TEXTURE2D(_AurellePlanarReflectionTex);
SAMPLER(sampler_AurellePlanarReflectionTex);
// x: strength (0 = off), y: base mip (a slight blur), z: extra mips per unit of perceptual roughness,
// w: screen-space offset per unit of the normal's tilt (polishing ripples)
float4 _AurellePlanarParams;

half3 AurellePlanarSpecularDelta(InputData inputData, SurfaceData surfaceData)
{
    UNITY_BRANCH
    if (_AurellePlanarParams.x <= 0.0)
        return half3(0.0, 0.0, 0.0);

    BRDFData brdfData;
    half alpha = surfaceData.alpha;
    InitializeBRDFData(surfaceData.albedo, surfaceData.metallic, surfaceData.specular, surfaceData.smoothness, alpha, brdfData);

    half3 reflectVector = reflect(-inputData.viewDirectionWS, inputData.normalWS);
    half NoV = saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
    half fresnelTerm = Pow4(1.0 - NoV);

    float2 uv = inputData.normalizedScreenSpaceUV;
    uv.x = 1.0 - uv.x;
    uv += inputData.normalWS.xz * _AurellePlanarParams.w;
    half mip = _AurellePlanarParams.y + brdfData.perceptualRoughness * _AurellePlanarParams.z;
    half4 planar = SAMPLE_TEXTURE2D_LOD(_AurellePlanarReflectionTex, sampler_AurellePlanarReflectionTex, uv, mip);
    half coverage = saturate(planar.a) * _AurellePlanarParams.x;

    UNITY_BRANCH
    if (coverage <= 0.001)
        return half3(0.0, 0.0, 0.0);

    // Premultiplied coverage: un-premultiply the filtered colour before swapping it for the probe's.
    half3 planarColor = planar.rgb / max(planar.a, 0.001);
    half3 probe = GlossyEnvironmentReflection(reflectVector, inputData.positionWS, brdfData.perceptualRoughness, 1.0h, inputData.normalizedScreenSpaceUV);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
    return (planarColor - probe) * coverage * EnvironmentBRDFSpecular(brdfData, fresnelTerm) * aoFactor.indirectAmbientOcclusion;
}

// LitPassFragment (URP 17.3 LitForwardPass.hlsl) with the planar term added before fog.
void AurellePlanarLitPassFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#if defined(_PARALLAXMAP)
#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirTS = input.viewDirTS;
#else
    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    half3 viewDirTS = GetViewDirectionTangentSpace(input.tangentWS, input.normalWS, viewDirWS);
#endif
    ApplyPerPixelDisplacement(viewDirTS, input.uv);
#endif

    SurfaceData surfaceData;
    InitializeStandardLitSurfaceData(input.uv, surfaceData);

#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));

#if defined(_DBUFFER)
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif

    InitializeBakedGIData(input, inputData);

    half4 color = UniversalFragmentPBR(inputData, surfaceData);
    color.rgb += AurellePlanarSpecularDelta(inputData, surfaceData);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));

    outColor = color;

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif
