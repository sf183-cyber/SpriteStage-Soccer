// Things that give light: lamp heads, sparks of fireworks.  Added to the picture; brighter than white, so they bloom.
// A quad with uv -1..1; the colour (linear, may exceed 1) comes with the vertex, uv2.x = how tight the core is,
// uv2.y = 1: no wide skirt round it (lamps seen by a camera: a small hot core, a thin halo, thin spikes).
Shader "SpriteStage/Glow"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One One
            Cull Off ZWrite Off ZTest LEqual ColorMask RGB
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : TEXCOORD1; float2 tight : TEXCOORD2; };
            v2f vert(float4 vertex : POSITION, float2 uv : TEXCOORD0, float4 col : TEXCOORD1, float2 par : TEXCOORD2)
            {
                v2f o; o.pos = UnityObjectToClipPos(vertex); o.uv = uv; o.col = col; o.tight = par; return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float r = length(i.uv);
                float a = pow(saturate(1.0 - r), i.tight.x) + 0.15 * (1.0 - i.tight.y) * pow(saturate(1.0 - r), 1.5);
                return float4(i.col.rgb * a, 1);
            }
            ENDCG
        }
    }
}
