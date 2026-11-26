// Shop windows with a room behind them (interior mapping): the glazing box's front face casts the view ray into a
// virtual room the width and height of the pane and _Depth metres deep, and shows its back wall, side walls, floor
// and ceiling from a shop atlas (2048 x 1024: back wall in the left half; side wall, floor and ceiling in the right
// half), so a flat pane reads as a lit shop with real perspective and parallax as you drive past. The glass adds a
// fresnel reflection of the sky. The room is self-lit (shop lighting) and brighter at night (_TestDriveShopLight).
Shader "Hidden/Aurelle/Test Drive Shop Window"
{
    Properties
    {
        _MainTex ("Shop atlas", 2D) = "white" {}
        _Depth ("Room depth (m)", Float) = 4
        _Span ("Back wall share shown", Float) = 0.6
        _Tint ("Glass tint", Color) = (0.86, 0.92, 0.95, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Depth, _Span;
            float4 _Tint;
            float _TestDriveShopLight;
            float4 _TestDriveSkyTop, _TestDriveSkyHorizon;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 objPos : TEXCOORD0;
                float3 objCam : TEXCOORD1;
                float3 objNormal : TEXCOORD2;
                float3 worldNormal : TEXCOORD3;
                float3 worldPos : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.objPos = v.vertex.xyz;
                o.objCam = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz;
                o.objNormal = v.normal;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 scale = float3(length(unity_ObjectToWorld._m00_m10_m20), length(unity_ObjectToWorld._m01_m11_m21), length(unity_ObjectToWorld._m02_m12_m22));
                float3 n = normalize(i.worldNormal);
                float3 toEye = normalize(_WorldSpaceCameraPos - i.worldPos);
                float fres = 0.04 + 0.96 * pow(1 - saturate(dot(n, toEye)), 5);
                float3 sky = lerp(_TestDriveSkyHorizon.rgb, _TestDriveSkyTop.rgb, saturate(reflect(-toEye, n).y));
                if (dot(sky, sky) < 1e-5) sky = float3(0.55, 0.62, 0.7);
                if (i.objNormal.z < 0.5)
                    return fixed4(_Tint.rgb * 0.15 + sky * 0.2, 1); // sides and back of the pane

                float3 p = i.objPos * scale;
                float3 c = i.objCam * scale;
                float3 d = normalize(p - c);
                float w = scale.x * 0.5, h = scale.y * 0.5, zf = p.z, zb = zf - _Depth;
                float tx = ((d.x > 0 ? w : -w) - p.x) / (abs(d.x) > 1e-5 ? d.x : 1e-5);
                float ty = ((d.y > 0 ? h : -h) - p.y) / (abs(d.y) > 1e-5 ? d.y : 1e-5);
                float tz = (zb - p.z) / min(d.z, -1e-5);
                float t = min(tx, min(ty, tz));
                float3 hit = p + d * t;
                float uu = saturate((hit.x + w) / (2 * w)), vv = saturate((hit.y + h) / (2 * h)), dz = saturate((hit.z - zb) / _Depth);
                float2 uv;
                float shade = 1;
                // Each pane shows its own part of the back wall (a hash of where it stands), so a row of panes reads as one shop.
                float offset = frac(sin(dot(unity_ObjectToWorld._m03_m23, float2(12.9898, 78.233))) * 43758.5453) * (1 - _Span);
                if (t == tz) { uv = float2((offset + uu * _Span) * 0.5, vv); }
                else if (t == tx) { uv = float2(0.5 + 0.25 * (d.x > 0 ? dz : 1 - dz), 0.5 + 0.5 * vv); shade = 0.85; }
                else if (d.y < 0) { uv = float2(0.75 + 0.25 * uu, 0.5 + 0.5 * (1 - dz)); shade = 0.9; }
                else { uv = float2(0.5 + 0.25 * uu, 0.5 * (1 - dz)); shade = 0.95; }
                float3 room = tex2D(_MainTex, uv).rgb * shade;
                room *= lerp(0.75, 1.0, dz);                         // a little darker deep inside
                float light = _TestDriveShopLight > 0 ? _TestDriveShopLight : 0.8;
                float3 col = room * _Tint.rgb * light * (1 - fres * 0.85) + sky * fres;
                // Never brighter than white: a lit shop at night must not bloom into a blank white pane.
                col = min(col, 0.9);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
