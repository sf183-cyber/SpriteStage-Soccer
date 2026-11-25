// The darkness that falls when the world stands still for a special shot: one sheet over the whole picture that
// takes the light out of everything drawn before it - pitch, house, sky, crowd, the other players, the weather.
// Left out: the ball and the shooter (marked in the stencil by G3Sprite / G3Ball).  What is drawn after it - the
// effects of the standstill itself - stays as bright as it is.  Round the shooter a little of the light is left.
Shader "SpriteStage/Night"
{
    SubShader
    {
        Tags { "Queue" = "Transparent+15" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            Blend DstColor Zero
            ColorMask RGB
            Stencil { Ref 2 ReadMask 2 Comp NotEqual }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _G3Night;        // the shooter (a point in the world) and how dark it is (0..1)
            struct v2f { float4 pos : SV_POSITION; float2 ndc : TEXCOORD0; float3 spot : TEXCOORD1; };
            v2f vert(float4 vertex : POSITION)
            {
                v2f o; o.pos = float4(vertex.xy, 0.5, 1.0); o.ndc = vertex.xy;
                float4 s = mul(UNITY_MATRIX_VP, float4(_G3Night.xyz, 1.0));
                o.spot = float3(s.xy / max(s.w, 1e-3), _ScreenParams.x / _ScreenParams.y);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 d = (i.ndc - i.spot.xy) * float2(i.spot.z, 1.0);
                float near = exp(-dot(d, d) / 0.10);
                float3 night = float3(0.012, 0.014, 0.034) * (1.0 + 4.0 * near) * (1.0 - 0.35 * saturate(dot(i.ndc, i.ndc) * 0.5));
                return fixed4(lerp(float3(1.0, 1.0, 1.0), night, _G3Night.w), 1.0);
            }
            ENDCG
        }
    }
}
