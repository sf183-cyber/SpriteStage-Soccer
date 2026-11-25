// Small real-time lit things (goal frames, corner flags, the Monument): one sun + sky/ground ambient,
// the same directions as the baked scenery, and a little haze with distance.
Shader "SpriteStage/Lit"
{
    Properties { _Color ("Colour", Color) = (1, 1, 1, 1) _Spec ("Shine: strength, tightness", Vector) = (0, 30, 0, 0) }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color, _Spec, _G3Court; float4 _G3SunDir, _G3SunColor, _G3Sky, _G3Bounce, _G3Haze, _G3FxLight, _G3FxLightCol; float4 _G3Lamps[4]; float4 _G3LampColor;
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wpos : TEXCOORD1; };
            v2f vert(float4 vertex : POSITION, float3 normal : NORMAL)
            {
                v2f o; o.pos = UnityObjectToClipPos(vertex); o.n = UnityObjectToWorldNormal(normal); o.wpos = mul(unity_ObjectToWorld, vertex).xyz; return o;
            }
            float4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                float3 n = normalize(i.n) * (facing > 0 ? 1 : -1);
                float3 light = _G3SunColor.rgb * saturate(dot(n, _G3SunDir.xyz)) + lerp(_G3Bounce.rgb, _G3Sky.rgb, n.y * 0.5 + 0.5);
                float3 shine = 0;
                if (_G3LampColor.w > 0.0)
                {
                    float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                    // night: the four floodlights (w = how strong they are on things standing on the court)
                    for (int k = 0; k < 4; k++)
                    {
                        float3 Lk = _G3Lamps[k].xyz - i.wpos;
                        float att = _G3LampColor.w * 520.0 / (120.0 + dot(Lk, Lk)); float3 Ln = normalize(Lk);
                        light += _G3LampColor.rgb * (att * saturate(0.15 + 0.85 * dot(n, Ln)));
                        // painted steel, vinyl, aluminium: the lamps show in them as soft streaks (_Spec.x = how much, .y = how tight)
                        shine += _G3LampColor.rgb * (att * _Spec.x * step(0.0, dot(n, Ln)) * pow(saturate(dot(n, normalize(Ln + V))), _Spec.y));
                    }
                    // the lit floor of the court throws its warm light back up at what stands on it (_G3Court: xy = half size of the slab, z = strength)
                    float2 off = max(abs(i.wpos.xz) - _G3Court.xy, 0.0);
                    light += float3(0.30, 0.225, 0.135) * (_G3Court.z * saturate(1.0 - length(off) / 5.0) * exp(-max(i.wpos.y, 0.0) / 3.2) * saturate(0.62 - 0.38 * n.y));
                }
                if (_G3FxLight.w > 0.0)
                {
                    float3 L = _G3FxLight.xyz - i.wpos;
                    float att = saturate(1.0 - dot(L, L) / (_G3FxLight.w * _G3FxLight.w));
                    light += _G3FxLightCol.rgb * (att * att * saturate(0.3 + 0.7 * dot(n, normalize(L))));
                }
                float3 c = _Color.rgb * light + shine + _Color.rgb * max(_Color.a - 1.0, 0.0);      // alpha above 1 = it gives light itself (the edge lights of the backboards)
                float d = distance(_WorldSpaceCameraPos, i.wpos);
                c = lerp(c, _G3Haze.rgb, saturate(d / 2600.0) * _G3Haze.a);
                return float4(c, 1);
            }
            ENDCG
        }
    }
}
