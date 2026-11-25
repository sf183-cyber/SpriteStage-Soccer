// 东京塔夜场的远景全景带（Pipeline/tokyo/t_pano.py 渲、t_compose.py 冲洗好的 RGBA，非预乘）。
// 画在天空盒之后、近处的透明物之前；不写深度，被近处的东西（草坪、树、人）按深度挡住。
Shader "SpriteStage/Band"
{
    Properties { _MainTex ("Texture", 2D) = "black" {} _Tint ("Tint", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "Queue" = "Transparent-15" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off ZWrite Off ZTest LEqual ColorMask RGB
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; fixed4 _Tint;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(float4 vertex : POSITION, float2 uv : TEXCOORD0) { v2f o; o.pos = UnityObjectToClipPos(vertex); o.uv = uv; return o; }
            fixed4 frag(v2f i) : SV_Target { fixed4 c = tex2D(_MainTex, i.uv); c.rgb *= _Tint.rgb; return c; }
            ENDCG
        }
    }
}
