// The football in the round: a sphere painted on the quad the ball's drawing used to be on, so it keeps its place among the
// flat figures.  It carries the black pentagons of a real ball (a truncated icosahedron) and turns as the ball rolls.
//   _BallSpin    ball space from world (the ball's turning so far)
//   _BallRight / _BallUp / _BallBack   the camera's axes in the world: to the right, up the picture, towards the eye
//   _BallInk / _BallBody / _BallShade  the three colours of the original drawing: outline and patches, the white, the shaded white
//   _BallStyle   x = art pixels across the ball, two to each pixel of the original as for the figures (0: smooth), y = half-width of the seams between the white panels (0: none),
//                z = size of the black patches (1.0267 = true to a real ball; less = bigger), w = where the shade begins (-1..1)
Shader "SpriteStage/Ball"
{
    Properties { _MainTex ("(unused)", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue" = "AlphaTest+10" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        CGINCLUDE
        #include "UnityCG.cginc"
        float4x4 _BallSpin;
        float4 _BallRight, _BallUp, _BallBack, _BallInk, _BallBody, _BallShade, _BallStyle;
        float4 _G3SunDir, _G3FxLight, _G3FxLightCol, _G3BallLight;      // _G3BallLight.w > 0：夜里按主泛光灯的方向分明暗（东京塔夜场），否则按太阳
        struct v2f { float4 pos : SV_POSITION; float2 p : TEXCOORD0; float3 wpos : TEXCOORD1; };
        v2f vert(float4 vertex : POSITION, float2 uv : TEXCOORD0)
        {
            v2f o; o.pos = UnityObjectToClipPos(vertex); o.p = uv; o.wpos = mul(unity_ObjectToWorld, vertex).xyz;
            return o;
        }
        // the middles of the twenty white panels (ten, with their opposites), not yet of length one
        static const float3 Panels[10] =
        {
            float3(1, 1, 1), float3(1, 1, -1), float3(1, -1, 1), float3(-1, 1, 1),
            float3(0, 1.618034, 0.618034), float3(0, 1.618034, -0.618034),
            float3(0.618034, 0, 1.618034), float3(-0.618034, 0, 1.618034),
            float3(1.618034, 0.618034, 0), float3(1.618034, -0.618034, 0)
        };
        // colour (rgb) and whether there is ball here at all (a), at p: -1..1 across the ball
        float4 Paint(float2 p)
        {
            float px = _BallStyle.x;
            if (px > 0.0) p = (floor(p * px * 0.5) + 0.5) / (px * 0.5);      // one answer per art pixel
            float r2 = dot(p, p);
            if (r2 >= 1.0) return 0;
            float edge = px > 0.0 ? 4.2 / px : 0.12;                       // the black outline: as heavy as round the figures (two art pixels)
            if (r2 > (1.0 - edge) * (1.0 - edge)) return float4(_BallInk.rgb, 1);
            float3 n = float3(p, sqrt(1.0 - r2));
            float3 wn = _BallRight.xyz * n.x + _BallUp.xyz * n.y + _BallBack.xyz * n.z;
            float3 d = mul((float3x3)_BallSpin, wn), a = abs(d);
            // nearest of the twelve black patches, and the two nearest white panels
            float patch = max(a.y + 1.618034 * a.z, max(a.x + 1.618034 * a.y, 1.618034 * a.x + a.z)) * 0.525731;
            float f1 = 0.0, f2 = 0.0;
            for (int k = 0; k < 10; k++)
            {
                float f = abs(dot(d, Panels[k])) * 0.577350;
                if (f > f1) { f2 = f1; f1 = f; } else if (f > f2) f2 = f;
            }
            bool black = patch > f1 * _BallStyle.z;
            float3 col = black ? _BallInk.rgb : _BallBody.rgb;
            float lit = dot(wn, normalize(_G3BallLight.w > 0.0 ? _G3BallLight.xyz : _G3SunDir.xyz));
            if (!black)
            {
                if (lit < _BallStyle.w) col = _BallShade.rgb;
                if (_BallStyle.y > 0.0 && f1 - f2 < _BallStyle.y) col = lerp(col, _BallInk.rgb, 0.55);       // the stitching
            }
            return float4(col, 1);
        }
        float4 Ball(v2f i)
        {
            // four looks per screen pixel: clean edges on the outline, the patches and the art pixels alike
            float2 dx = ddx(i.p), dy = ddy(i.p);
            float4 c = Paint(i.p + dx * 0.125 + dy * 0.375) + Paint(i.p - dx * 0.375 + dy * 0.125)
                     + Paint(i.p + dx * 0.375 - dy * 0.125) + Paint(i.p - dx * 0.125 - dy * 0.375);
            c.rgb /= max(c.a, 1.0); c.a *= 0.25;
            return c;
        }
        ENDCG
        Pass
        {
            Cull Off ZWrite On AlphaToMask On
            Stencil { Ref 2 ReadMask 2 WriteMask 2 Comp Always Pass Replace }      // (the ball stays lit when the world goes dark for a special shot: see G3Night)
            Offset -1, -1
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target
            {
                float4 c = Ball(i);
                clip(c.a - 0.2);
                if (_G3FxLight.w > 0.0)
                {
                    float3 L = _G3FxLight.xyz - i.wpos;
                    float att = saturate(1.0 - dot(L, L) / (_G3FxLight.w * _G3FxLight.w));
                    c.rgb += c.rgb * _G3FxLightCol.rgb * (att * att * 0.8);
                }
                return c;
            }
            ENDCG
        }
        // the cartoon mark, as for the figures (see G3Sprite): keeps the white of the ball out of the photographic glow
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual ColorMask A
            Offset -1, -1
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target
            {
                clip(Ball(i).a - 0.5);
                return 0;
            }
            ENDCG
        }
    }
}
