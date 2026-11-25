// The figures' shadows: the same sprite tiles laid flat on the turf along the sun's direction, drawn as a
// multiply.  The stencil makes overlapping tiles (and overlapping players) darken the ground only once.
Shader "SpriteStage/SpriteShadow"
{
    Properties { _MainTex ("Atlas", 2D) = "white" {} _ShadowColor ("Shadow", Color) = (0.45, 0.52, 0.5, 1) }
    SubShader
    {
        Tags { "Queue" = "Geometry+20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual
            Blend DstColor Zero
            Offset -1, -1
            Stencil { Ref 1 Comp NotEqual Pass Replace }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_TexelSize; fixed4 _ShadowColor;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 cell : TEXCOORD1; };
            v2f vert(float4 vertex : POSITION, float2 uv : TEXCOORD0, float4 cell : TEXCOORD1)
            {
                v2f o; o.pos = UnityObjectToClipPos(vertex); o.uv = uv; o.cell = cell; return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = clamp(i.uv, i.cell.xy, i.cell.zw);
                clip(tex2D(_MainTex, uv).a - 0.5);
                return fixed4(_ShadowColor.rgb, 1);
            }
            ENDCG
        }
    }
}
