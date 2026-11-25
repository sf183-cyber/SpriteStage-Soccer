// Fine parts of the White House (balusters, railings, glazing bars, the flag): their lighting was baked
// into vertex colours by the path tracer, so they are drawn unlit like the rest of the house.
Shader "SpriteStage/VertexColor"
{
    Properties { _Tint ("Tint", Color) = (1, 1, 1, 1) }
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
            fixed4 _Tint; float4 _G3Haze;
            struct v2f { float4 pos : SV_POSITION; fixed3 col : COLOR0; };
            v2f vert(float4 vertex : POSITION, fixed4 color : COLOR)
            {
                v2f o; o.pos = UnityObjectToClipPos(vertex);
                #ifdef UNITY_COLORSPACE_GAMMA
                o.col = color.rgb;
                #else
                o.col = GammaToLinearSpace(color.rgb);        // the colours were stored display-referred
                #endif
                o.col *= _Tint.rgb;
                float dist = distance(_WorldSpaceCameraPos, mul(unity_ObjectToWorld, vertex).xyz);
                o.col = lerp(o.col, _G3Haze.rgb, saturate(dist / 900.0) * _G3Haze.a);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target { return fixed4(i.col, 1); }
            ENDCG
        }
    }
}
