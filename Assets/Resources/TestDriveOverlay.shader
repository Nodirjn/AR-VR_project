// Head-up display material for the test drive: drawn after everything else with no depth test, like a real HUD
// projected on the windscreen, so the hood, the pillars and the cabin never cover the speed readout.
// _Sdf = 0: UI sprites (RGBA texture times vertex colour). _Sdf = 1: TextMeshPro signed-distance-field glyphs.
// Single-pass instanced and multiview variants, so it renders in both eyes on Meta Quest.
Shader "Hidden/Aurelle/Test Drive Overlay"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Sdf ("Signed distance field", Float) = 0
        _Softness ("SDF edge softness", Range(0.25, 2)) = 0.8
    }

    SubShader
    {
        Tags
        {
            "Queue"="Overlay"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }
        Lighting Off Cull Off ZTest Always ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

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
                fixed4 color : COLOR;
                float4 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            uniform float4 _MainTex_ST;
            uniform fixed4 _Color;
            uniform float _Sdf;
            uniform float _Softness;

            v2f vert (appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.texcoord = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.texcoord);
                if (_Sdf > 0.5)
                {
                    float d = tex.a;
                    float w = max(fwidth(d) * _Softness, 0.0001);
                    fixed4 glyph = i.color;
                    glyph.a *= smoothstep(0.5 - w, 0.5 + w, d);
                    return glyph;
                }
                return tex * i.color;
            }
            ENDCG
        }
    }
}
