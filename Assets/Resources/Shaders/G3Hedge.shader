// 公园边的绿篱（Pipeline/tokyo/t_hedge.py 在均匀天光下拍的叶子贴图：近似“反照率 x 遮挡”），按场景的灯打亮：
// 月光 + 天空 + 四根泛光灯（和 G3Lit 一样的算法），顶边按贴图的 alpha 裁出参差的轮廓。
Shader "SpriteStage/Hedge"
{
    Properties { _MainTex ("Leaves", 2D) = "white" {} _Tint ("Tint", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off ZWrite On AlphaToMask On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _Tint;
            float4 _G3SunDir, _G3SunColor, _G3Sky, _G3Bounce, _G3Haze; float4 _G3Lamps[4]; float4 _G3LampColor;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; float3 wpos : TEXCOORD2; };
            v2f vert(float4 vertex : POSITION, float3 normal : NORMAL, float2 uv : TEXCOORD0)
            {
                v2f o; o.pos = UnityObjectToClipPos(vertex); o.uv = uv; o.n = UnityObjectToWorldNormal(normal); o.wpos = mul(unity_ObjectToWorld, vertex).xyz; return o;
            }
            float4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                float4 t = tex2D(_MainTex, i.uv);
                float3 n = normalize(i.n) * (facing > 0 ? 1 : -1);
                float3 light = _G3SunColor.rgb * saturate(0.3 + 0.7 * dot(n, _G3SunDir.xyz)) + lerp(_G3Bounce.rgb, _G3Sky.rgb, n.y * 0.5 + 0.5);
                if (_G3LampColor.w > 0.0)
                    for (int k = 0; k < 4; k++)
                    {
                        float3 Lk = _G3Lamps[k].xyz - i.wpos;
                        float att = _G3LampColor.w * 520.0 / (120.0 + dot(Lk, Lk));
                        light += _G3LampColor.rgb * (att * saturate(0.25 + 0.75 * dot(n, normalize(Lk))));
                    }
                float3 c = t.rgb * _Tint.rgb * light;
                c = lerp(c, _G3Haze.rgb, saturate(distance(_WorldSpaceCameraPos, i.wpos) / 2600.0) * _G3Haze.a);
                return float4(c, saturate((t.a - 0.35) / 0.2));
            }
            ENDCG
        }
    }
}
