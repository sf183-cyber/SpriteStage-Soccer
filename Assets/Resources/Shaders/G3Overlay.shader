// The game's own picture (menus, cut-scenes, the status bar) drawn on top of everything, pixel-exact.
Shader "SpriteStage/Overlay"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(float4 vertex : POSITION, float2 uv : TEXCOORD0) { v2f o; o.pos = UnityObjectToClipPos(vertex); o.uv = uv; return o; }
            fixed4 frag(v2f i) : SV_Target { return fixed4(tex2D(_MainTex, i.uv).rgb, 1); }
            ENDCG
        }
    }
}
