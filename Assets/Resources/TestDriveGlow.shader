// Additive light glow for the night city: the pool a headlight or street lantern throws on the asphalt, the faint
// beam through the air in front of it and the bright lens itself. Unlit and additive (it only ever adds light),
// depth tested against the scene but never writing depth, pulled slightly towards the camera so a pool lying on the
// road does not flicker against it. _Edge > 0 fades a beam cone at its silhouette (by the angle between surface
// and view), so it reads as lit haze rather than a solid cone; _NearFade fades anything right in front of the eye.
// Single-pass instanced and multiview variants for Meta Quest.
Shader "Hidden/Aurelle/Test Drive Glow"
{
    Properties
    {
        _MainTex ("Mask (R)", 2D) = "white" {}
        [HDR] _Color ("Colour", Color) = (1,1,1,1)
        _Edge ("Silhouette fade power", Float) = 0
        _NearFade ("Near fade distance", Float) = 1.5
    }

    SubShader
    {
        Tags { "Queue"="Transparent+20" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Lighting Off Cull Off ZWrite Off ZTest LEqual
        Blend One One
        Offset -1, -1

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            uniform float4 _MainTex_ST;
            uniform float4 _Color;
            uniform float _Edge;
            uniform float _NearFade;

            v2f vert (appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float mask = tex2D(_MainTex, i.texcoord).r;
                float3 toEye = _WorldSpaceCameraPos - i.worldPos;
                float distance = length(toEye);
                float edge = 1.0;
                if (_Edge > 0.001)
                    edge = pow(saturate(abs(dot(normalize(i.worldNormal), toEye / max(distance, 1e-4)))), _Edge);
                float near = _NearFade > 0.001 ? saturate((distance - 0.3) / _NearFade) : 1.0;
                float3 colour = _Color.rgb * (_Color.a * mask * edge * near);
                return fixed4(colour, 0);
            }
            ENDCG
        }
    }
}
