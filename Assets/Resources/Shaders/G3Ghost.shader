// After-images of the ball: the same sprite tiles, drawn as pure light in the colour of the shot.
Shader "SpriteStage/Ghost"
{
    Properties { _MainTex ("Atlas", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue" = "Transparent+5" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual
            Blend One One
            ColorMask RGB
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_TexelSize;
            struct v2f { float4 pos : SV_POSITION; float4 col : COLOR; float2 uv : TEXCOORD0; float4 cell : TEXCOORD1; };
            v2f vert(float4 vertex : POSITION, float4 color : COLOR, float2 uv : TEXCOORD0, float4 cell : TEXCOORD1)
            {
                v2f o; o.pos = UnityObjectToClipPos(vertex); o.col = color; o.uv = uv; o.cell = cell;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 px = i.uv * _MainTex_TexelSize.zw - 0.5;
                float2 w = max(fwidth(px), 1e-5);
                float2 fl = floor(px), fr = px - fl;
                float2 t = saturate((fr - 0.5) / w + 0.5);
                float2 uv = clamp((fl + 0.5 + t) * _MainTex_TexelSize.xy, i.cell.xy, i.cell.zw);
                fixed4 c = tex2D(_MainTex, uv);
                float a = saturate((c.a - 0.5) / max(fwidth(c.a), 1e-4) + 0.5);
                float3 art = c.a > 0.004 ? c.rgb / c.a : c.rgb;
                float lum = dot(art, float3(0.3, 0.6, 0.1));
                return fixed4(lerp(i.col.rgb, art, 0.3) * (0.45 + 0.55 * lum) * i.col.a * a, 0);
            }
            ENDCG
        }
    }
}
