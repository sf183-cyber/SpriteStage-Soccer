// Flat see-through boxes for the start screen and the Esc menu (vertex colours, drawn over everything by the HUD camera).
Shader "Hidden/G3Panel"
{
    SubShader
    {
        Tags { "Queue" = "Overlay+1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct a2v { float4 vertex : POSITION; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; };
            v2f vert(a2v i) { v2f o; o.pos = UnityObjectToClipPos(i.vertex); o.color = i.color; return o; }
            float4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
