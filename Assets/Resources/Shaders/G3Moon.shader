// The moon: a photograph (NASA's map of the near side) hung in the sky, behind everything else.
Shader "SpriteStage/Moon"
{
    Properties { _MainTex ("Moon", 2D) = "white" {} _Tint ("Tint", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "Queue" = "Transparent-20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off ZWrite Off ZTest LEqual ColorMask RGB
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _Tint;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(float4 vertex : POSITION, float2 uv : TEXCOORD0) { v2f o; o.pos = UnityObjectToClipPos(vertex); o.uv = uv; return o; }
            float4 frag(v2f i) : SV_Target { float4 c = tex2D(_MainTex, i.uv); c.rgb *= _Tint.rgb; return c; }
            ENDCG
        }
    }
}
