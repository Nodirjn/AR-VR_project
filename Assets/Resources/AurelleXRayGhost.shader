// X-ray shell for the car explorer (CarExplorerController): the body panels turn into tinted glass with bright
// fresnel edges, so the opaque parts inside the car (engine, seats, dashboard, wheels) show through.
// Loaded with Resources.Load("AurelleXRayGhost") so it is always included in builds.
//
// _Fade blends between a solid stand-in for the panel's own material (0: base colour / texture of the original,
// lit by the main light, the realtime additional lights, ambient probe and reflection probes, with a clear coat
// when the original has one) and the ghost (1). The controller fades the shell
// through it, so the switch from the original material is barely visible. Per renderer it sets _Fade, _Highlight
// and the stand-in values with MaterialPropertyBlocks.
//
// _GlassOpacity scales the glass per renderer (the inner trims and panels behind the skin are fainter). The shadow
// pass keeps the car's shadow while the shell is still mostly solid (_Fade < 0.5).
//
// Two material set-ups use this shader (render state from the material, see CarExplorerController):
//   while fading:  ZWrite On,  Cull Back - the nearest panel hides what is behind it, as the solid car does;
//   steady ghost:  ZWrite Off, Cull Off  - the far side of the body shows faintly through the near side.
// With a low alpha and a mostly additive rim, the order in which overlapping glass panels are drawn makes no
// visible difference, so they do not flicker as the camera moves.
Shader "Aurelle/X-Ray Ghost"
{
    Properties
    {
        [MainColor] _BaseColor ("Glass Tint", Color) = (0.62, 0.80, 0.98, 1)
        _GhostAlpha ("Glass Opacity", Range(0, 1)) = 0.06
        _GlassOpacity ("Glass Opacity Scale (per renderer)", Range(0, 1)) = 1
        _RimColor ("Rim Colour", Color) = (0.78, 0.92, 1.0, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3.0
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.9
        _EdgeStrength ("Crease Lines", Range(0, 2)) = 0.45
        _ApproxColor ("Solid Colour", Color) = (0.5, 0.5, 0.5, 1)
        _ApproxMap ("Solid Texture", 2D) = "white" {}
        _ApproxSmoothness ("Solid Smoothness", Range(0, 1)) = 0.6
        _ApproxMetallic ("Solid Metallic", Range(0, 1)) = 0
        _ApproxClearCoat ("Solid Clear Coat", Range(0, 1)) = 0
        _ApproxClearCoatSmoothness ("Solid Clear Coat Smoothness", Range(0, 1)) = 1
        _Fade ("Fade (0 solid, 1 glass)", Range(0, 1)) = 1
        _Highlight ("Highlight", Range(0, 1)) = 0
        _HighlightColor ("Highlight Colour", Color) = (0.95, 0.61, 0.2, 1)
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half _GhostAlpha;
        half _GlassOpacity;
        half4 _RimColor;
        half _RimPower;
        half _RimStrength;
        half _EdgeStrength;
        half4 _ApproxColor;
        float4 _ApproxMap_ST;
        half _ApproxSmoothness;
        half _ApproxMetallic;
        half _ApproxClearCoat;
        half _ApproxClearCoatSmoothness;
        half _Fade;
        half _Highlight;
        half4 _HighlightColor;
        half _ZWrite;
        half _Cull;
    CBUFFER_END
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "XRayGhost"
            Tags { "LightMode" = "UniversalForward" }

            // Premultiplied: the rim adds light without hiding what is behind it.
            Blend One OneMinusSrcAlpha
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GhostVertex
            #pragma fragment GhostFragment

            // Single-pass instanced (D3D11, Metal) and multiview (Vulkan / Quest) stereo, as the URP shaders.
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            // As URP Lit: the Forward+ light loop (also gives the main light its attenuation there) and the
            // reflection probes as Forward+ binds them (blended, box projected), so the stand-in is lit like the paint.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_ApproxMap);
            SAMPLER(sampler_ApproxMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // One realtime light on the stand-in: Lambert plus the same highlight as the main light (no shadows).
            half3 StandInLight(Light light, half3 normal, half3 view, half3 diffuseColor, half3 specColor,
                               half specPower)
            {
                half3 radiance = light.color * (light.distanceAttenuation * light.shadowAttenuation);
                half highlight = pow(saturate(dot(normal, SafeNormalize(light.direction + view))), specPower);
                return radiance * (diffuseColor * saturate(dot(normal, light.direction)) + specColor * highlight);
            }

            // The showroom spots and any other realtime light that lights the original material.
            half3 StandInAdditionalLights(float3 positionWS, float2 screenUV, half3 normal, half3 view,
                                          half3 diffuseColor, half3 specColor, half specPower)
            {
                half3 sum = half3(0, 0, 0);
                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalizedScreenSpaceUV = screenUV;
            #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint dirIndex = 0; dirIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); dirIndex++)
                    sum += StandInLight(GetAdditionalLight(dirIndex, positionWS), normal, view, diffuseColor, specColor,
                                        specPower);
            #endif
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    sum += StandInLight(GetAdditionalLight(lightIndex, positionWS), normal, view, diffuseColor, specColor,
                                        specPower);
                LIGHT_LOOP_END
                return sum;
            }

            Varyings GhostVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _ApproxMap);
                return output;
            }

            half4 GhostFragment(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normal = normalize(input.normalWS);
                // Back faces (Cull Off) are lit and rimmed from their own side.
                normal = IS_FRONT_VFACE(face, normal, -normal);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                half facing = saturate(dot(normal, view));
                Light mainLight = GetMainLight();
                half diffuseTerm = saturate(dot(normal, mainLight.direction));
                half3 ambient = SampleSHPixel(half3(0, 0, 0), normal);

                // Solid stand-in for the original material.
                half4 texel = SAMPLE_TEXTURE2D(_ApproxMap, sampler_ApproxMap, input.uv);
                half3 albedo = _ApproxColor.rgb * texel.rgb;
                albedo = lerp(albedo, _HighlightColor.rgb, _Highlight * 0.34);
                half perceptualRoughness = 1.0 - _ApproxSmoothness;
                half3 reflectVector = reflect(-view, normal);
                half3 reflection = GlossyEnvironmentReflection(reflectVector, input.positionWS, perceptualRoughness, 1.0,
                                                               screenUV);
                half3 f0 = lerp(half3(0.04, 0.04, 0.04), albedo, _ApproxMetallic);
                half grazing = Pow4(1.0 - facing);
                half3 specular = reflection * lerp(f0, max(f0, _ApproxSmoothness.xxx), grazing) * _ApproxSmoothness;
                half3 diffuseColor = albedo * (1.0 - _ApproxMetallic);
                half3 specColor = f0 * _ApproxSmoothness;
                half specPower = exp2(10.0 * _ApproxSmoothness + 1.0);
                half3 solid = diffuseColor * ambient + specular +
                              StandInLight(mainLight, normal, view, diffuseColor, specColor, specPower) +
                              StandInAdditionalLights(input.positionWS, screenUV, normal, view, diffuseColor, specColor,
                                                      specPower);
                // Clear coat (car paint): a sharp dielectric reflection over the base, as URP's clear coat.
                half coatFresnel = (0.04 + 0.96 * grazing) * _ApproxClearCoat;
                half3 coat = GlossyEnvironmentReflection(reflectVector, input.positionWS, 1.0 - _ApproxClearCoatSmoothness,
                                                         1.0, screenUV);
                solid = solid * (1.0 - coatFresnel) + coat * coatFresnel;
                half solidAlpha = _ApproxColor.a * texel.a;

                // Glass: a faint tinted film, brighter toward the silhouette and along creases and panel edges.
                half3 tint = lerp(_BaseColor.rgb, _HighlightColor.rgb, _Highlight * 0.55);
                half rim = pow(1.0 - facing, _RimPower) * _RimStrength;
                float curvature = length(fwidth(normal)) / max(length(fwidth(input.positionWS)), 1e-4);
                half crease = saturate((curvature - 6.0) * 0.04) * _EdgeStrength;
                half glassAlpha = saturate(_GhostAlpha * (1.0 + 1.2 * _Highlight) + rim * 0.45 + crease * 0.3) *
                                  _GlassOpacity;
                half3 glassLight = tint * (0.45 + 0.35 * diffuseTerm) + reflection * 0.08;
                half3 rimColor = lerp(_RimColor.rgb, _HighlightColor.rgb, _Highlight * 0.6);
                half3 glass = glassLight * glassAlpha + rimColor * (rim * 0.35 + crease * 0.25) * _GlassOpacity;

                half fade = smoothstep(0.0, 1.0, _Fade);
                half alpha = lerp(solidAlpha, glassAlpha, fade);
                half3 color = lerp(solid * solidAlpha, glass, fade);
                return half4(color, alpha);
            }
            ENDHLSL
        }

        // The shell keeps casting its shadow while it is still mostly solid, so the car's shadow and the light in
        // the cabin do not jump when the fade starts; the glass casts none.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVertex(ShadowAttributes input)
            {
                ShadowVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                output.positionCS = ApplyShadowClamping(
                    TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                return output;
            }

            half4 ShadowFragment(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(0.5 - _Fade);
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
