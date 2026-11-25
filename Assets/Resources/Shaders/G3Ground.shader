// Ground with baked lighting (path-traced in Blender).  Unlit: the texture already is the photograph.
// _Feather > 0 fades the edges out (the detailed near lawn lies on top of the wide far lawn);
// _Detail adds fine grain to the far lawn when the camera gets close.
Shader "SpriteStage/Ground"
{
    Properties
    {
        _MainTex ("Baked", 2D) = "white" {}
        _Detail ("Detail (grey = neutral)", 2D) = "gray" {}
        _DetailScale ("Detail tiles per metre", Float) = 0
        _Size ("Size in metres (x, z)", Vector) = (1, 1, 0, 0)
        _Feather ("Edge feather (m)", Float) = 0
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _Detail; float4 _Size; float _Feather, _DetailScale; fixed4 _Tint; float4 _G3Haze, _G3FxLight, _G3FxLightCol;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; };
            v2f vert(float4 vertex : POSITION, float2 uv : TEXCOORD0)
            {
                v2f o; o.pos = UnityObjectToClipPos(vertex); o.uv = uv; o.wpos = mul(unity_ObjectToWorld, vertex).xyz; return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                if (_DetailScale > 0)
                {
                    fixed3 d = tex2D(_Detail, i.wpos.xz * _DetailScale).rgb;
                    float dist = distance(_WorldSpaceCameraPos, i.wpos);
                    c.rgb *= lerp(d * 2.0, 1.0, saturate(dist / 70.0));
                }
                c.rgb *= _Tint.rgb;
                if (_G3FxLight.w > 0.0)
                {
                    // the turf lit by a glowing ball (the only light in the scene that is not baked)
                    float3 L = _G3FxLight.xyz - i.wpos;
                    float att = saturate(1.0 - dot(L, L) / (_G3FxLight.w * _G3FxLight.w));
                    c.rgb += c.rgb * _G3FxLightCol.rgb * (att * att * saturate(0.35 + 0.65 * L.y / max(length(L), 1e-3)));
                }
                c.rgb = lerp(c.rgb, _G3Haze.rgb, saturate(distance(_WorldSpaceCameraPos, i.wpos) / 900.0) * _G3Haze.a);
                c.a = 1;
                if (_Feather > 0)
                {
                    float2 e = min(i.uv, 1 - i.uv) * _Size.xy;
                    c.a = smoothstep(0, _Feather, min(e.x, e.y));
                }
                return c;
            }
            ENDCG
        }
    }
}
