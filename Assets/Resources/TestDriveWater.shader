// The sea: a large plane whose normal is the sum of several travelling waves (no textures), shaded with a fresnel
// mix of the deep water colour and the sky (the time of day's sky colours, _TestDriveSkyTop / _TestDriveSkyHorizon),
// a sun glitter highlight (_TestDriveSunDir / _TestDriveSunColor) and a lighter, greener band in the shallows near
// the beach. Wave detail fades with distance so the horizon stays calm.
Shader "Hidden/Aurelle/Test Drive Water"
{
    Properties
    {
        _Deep ("Deep colour", Color) = (0.02, 0.12, 0.2, 1)
        _Shallow ("Shallow colour", Color) = (0.1, 0.42, 0.45, 1)
        _ShoreZ ("Shoreline z", Float) = -150
        _Strength ("Wave normal strength", Float) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #include "UnityCG.cginc"

            float4 _Deep, _Shallow;
            float _ShoreZ, _Strength;
            float4 _TestDriveSkyTop, _TestDriveSkyHorizon, _TestDriveSunDir, _TestDriveSunColor;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 worldPos : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float2 wave(float2 p, float2 dir, float k, float speed, float amp)
            {
                float ph = dot(p, dir) * k + _Time.y * speed;
                return dir * (cos(ph) * k * amp);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = i.worldPos.xz;
                float dist = length(_WorldSpaceCameraPos - i.worldPos);
                float2 g = wave(p, normalize(float2(0.3, -1)), 0.55, 1.6, 0.18)
                         + wave(p, normalize(float2(-0.6, -1)), 0.9, 2.1, 0.09)
                         + wave(p, normalize(float2(1, -0.4)), 1.7, 2.9, 0.04)
                         + wave(p, normalize(float2(-1, -0.2)), 3.1, 3.7, 0.018)
                         + wave(p, normalize(float2(0.2, 1)), 5.3, 4.6, 0.008);
                g *= _Strength * saturate(1 - dist / 900);
                float3 n = normalize(float3(-g.x, 1, -g.y));
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float fres = 0.02 + 0.98 * pow(1 - saturate(dot(n, v)), 5);
                float3 r = reflect(-v, n);
                float3 top = dot(_TestDriveSkyTop.rgb, 1) > 0.001 ? _TestDriveSkyTop.rgb : float3(0.45, 0.6, 0.8);
                float3 hor = dot(_TestDriveSkyHorizon.rgb, 1) > 0.001 ? _TestDriveSkyHorizon.rgb : float3(0.7, 0.75, 0.8);
                float3 sky = lerp(hor, top, saturate(r.y * 1.5));
                float shallow = saturate((i.worldPos.z - (_ShoreZ - 22)) / 22);
                float3 water = lerp(_Deep.rgb, _Shallow.rgb, shallow) * (0.35 + 0.65 * saturate(dot(top, float3(0.3, 0.5, 0.2)) * 2));
                float3 sunDir = dot(_TestDriveSunDir.xyz, _TestDriveSunDir.xyz) > 0.01 ? normalize(_TestDriveSunDir.xyz) : normalize(float3(0.3, 0.6, -0.7));
                float3 sunCol = dot(_TestDriveSunColor.rgb, 1) > 0.001 ? _TestDriveSunColor.rgb : float3(1, 0.95, 0.85);
                float spec = pow(saturate(dot(r, sunDir)), 380) * 6 + pow(saturate(dot(r, sunDir)), 40) * 0.25;
                float3 col = lerp(water, sky, fres) + sunCol * spec;
                // A thin line of surf where the water meets the sand.
                float foam = saturate(1 - abs(i.worldPos.z - _ShoreZ - sin(p.x * 0.15 + _Time.y * 0.8) * 0.8) / 1.6) * 0.5;
                col = lerp(col, float3(0.9, 0.93, 0.92) * (0.3 + 0.7 * saturate(dot(top, 1))), foam);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
