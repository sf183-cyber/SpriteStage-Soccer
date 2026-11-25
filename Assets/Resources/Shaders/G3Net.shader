// Goal netting: a procedural mesh of thin cords.  A shot can make it bulge, ripple and tear:
//   _G3NetHit   xyz = where the ball struck (world), w = seconds since then (negative: nothing happening)
//   _G3NetPush  xyz = direction and depth of the bulge (metres), w = radius of the hole torn into it (0: none)
// A ball lying in the goal keeps the netting pushed out round itself:
//   _G3NetBall  xyz = the ball as drawn (world), w = its radius as the netting feels it (0: no ball in the goal)
//   _G3NetGrip  x = how much of that push is on (0..1, eases in and out), y = how far the middle of a panel can give (metres)
//   _G3NetBox   xyz = the middle of the box of netting (x: distance from the centre line), for telling outward from inward
// The second UV set says how free each point is (0 along the frame, 1 in the middle of a panel).
Shader "SpriteStage/Net"
{
    Properties { _Color ("Colour", Color) = (0.95, 0.95, 0.95, 1) _Cell ("Mesh size (uv units)", Float) = 0.12 }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color; float _Cell; float4 _G3NetHit, _G3NetPush, _G3NetBall, _G3NetGrip, _G3NetBox;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 rest : TEXCOORD1; float stretch : TEXCOORD2; };
            float Tear(float3 q) { float ang = atan2(q.y, q.z + q.x * 0.37); return _G3NetPush.w * saturate(_G3NetHit.w / 0.10) * (1.0 + 0.28 * sin(ang * 5.0 + 1.3) + 0.17 * sin(ang * 11.0) + 0.10 * sin(ang * 23.0 + 2.0)); }
            v2f vert(float4 vertex : POSITION, float3 normal : NORMAL, float2 uv : TEXCOORD0, float2 slack : TEXCOORD1)
            {
                v2f o;
                float3 w = mul(unity_ObjectToWorld, vertex).xyz;
                o.rest = w; o.stretch = 0;
                // the ball in the goal: the netting lies over it like a cloth over a fist, and slopes away from there
                float3 held = 0;
                if (_G3NetBall.w > 0.0 && _G3NetGrip.x > 0.0)
                {
                    float3 n = normalize(mul((float3x3)unity_ObjectToWorld, normal));
                    float3 mid = float3(w.x >= 0.0 ? _G3NetBox.x : -_G3NetBox.x, _G3NetBox.y, _G3NetBox.z);
                    if (dot(n, w - mid) < 0.0) n = -n;                       // outward
                    float3 rel = _G3NetBall.xyz - w;
                    float along = dot(rel, n), r = _G3NetBall.w;
                    float depth = r + along;                                 // how far the front of the ball is past this panel
                    if (depth > 0.0)
                    {
                        float side = length(rel - n * along);
                        float onBall = side < r ? depth - (r - sqrt(r * r - side * side)) : 0.0;
                        float reach = 0.30 + 0.50 * depth;
                        float want = max(onBall, depth * exp(-side * side / (2.0 * reach * reach)));
                        float limit = _G3NetGrip.y * slack.x + 1e-4;         // tied along the frame, loose in the middle
                        float x = want / limit;
                        float give = want / sqrt(sqrt(1.0 + x * x * x * x)) * _G3NetGrip.x;      // follows the ball, up to what the panel can give there
                        held = n * give;
                        o.stretch = saturate(give * 0.9);
                    }
                }
                float t = _G3NetHit.w;
                if (t >= 0.0)
                {
                    float d = distance(w, _G3NetHit.xyz);
                    float depth = length(_G3NetPush.xyz);
                    // the punch: out in an instant, then swinging back and forth while it dies down
                    float swing = (1.0 - exp(-t / 0.035)) * exp(-t / 0.85) * (0.5 + 0.5 * cos(t * 10.0));
                    if (_G3NetPush.w > 0.0) swing += 0.16 * (1.0 - exp(-t / 0.25));       // torn netting stays pushed out and sagging
                    float bulge = exp(-d * d / (2.0 * 0.95 * 0.95)) * swing;
                    // and the wave that runs out through the rest of the netting
                    float wave = sin(d * 4.5 - t * 16.0) * exp(-d / 3.0) * exp(-t / 0.9) * saturate(t * 8.0) * 0.16;
                    float k = (bulge + wave) * slack.x;
                    w += _G3NetPush.xyz * k;
                    o.stretch = saturate(o.stretch + abs(k) * 1.2);
                }
                w += held;
                o.pos = mul(UNITY_MATRIX_VP, float4(w, 1.0));
                o.uv = uv;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                // the tear: a ragged hole round the spot where the ball went through
                float frayed = 0.0;
                if (_G3NetPush.w > 0.0)
                {
                    float3 q = i.rest - _G3NetHit.xyz;
                    float edge = Tear(q), len = length(q);
                    clip(len - edge);
                    frayed = saturate(1.0 - (len - edge) / 0.22);           // the cords round the hole: bunched up, thicker and whiter
                }
                float2 g = i.uv / _Cell;
                float2 w = max(fwidth(g), 1e-5);
                float2 d = abs(frac(g + 0.5) - 0.5);                 // distance to the nearest cord, in cells
                float cord = 0.045 * (1.0 + 2.2 * frayed);           // cord half-width, in cells
                float2 a = saturate((cord - d) / w + 0.5) * saturate(cord * 2 / w);     // fades to an even veil when the mesh gets too fine to resolve
                float veil = saturate(cord * 4);
                float2 far = saturate(w * 0.8);
                float alpha = max(max(a.x, a.y), max(far.x, far.y) * veil * 1.4);
                return fixed4(_Color.rgb * (1.0 + 0.6 * i.stretch + 0.5 * frayed), saturate(alpha * (1.0 + 0.8 * i.stretch + 1.5 * frayed)) * _Color.a);
            }
            ENDCG
        }
    }
}
