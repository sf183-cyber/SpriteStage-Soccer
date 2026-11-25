// Everything the effects layer draws: glows, trails, sparks, dust, soft shadows, leaves, rain, lightning.
// One material for all of it.  Colours come premultiplied, so light that adds (a glow) and things that cover
// (a leaf, a puff of dust, a shadow) can sit in the same mesh and be drawn in one go, back to front as listed.
//   uv0 = position inside the quad, -1..1        uv1.x = shape        uv1.y = how much of it is pure light (1 = adds only)
//   uv2 = for the pixel-art shapes: art pixels per half quad, animation step, a parameter of the shape, a turn
Shader "SpriteStage/Fx"
{
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual
            Blend One OneMinusSrcAlpha
            ColorMask RGB                       // alpha keeps the "cartoon" mark of the figures (see G3Sprite)
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float4 pos : SV_POSITION; float4 col : COLOR; float2 uv : TEXCOORD0; float2 kind : TEXCOORD1; float4 px : TEXCOORD2; };
            v2f vert(float4 vertex : POSITION, float4 color : COLOR, float2 uv : TEXCOORD0, float2 kind : TEXCOORD1, float4 px : TEXCOORD2)
            {
                v2f o; o.pos = UnityObjectToClipPos(vertex); o.col = color; o.uv = uv; o.kind = kind; o.px = px;
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            // The pixel-art shapes (8 and up).  They are drawn on a grid of art pixels the size of the figures' own
            // (px.x = how many of them fit into half the quad), in three flat tones, and they move in steps
            // (px.y = the step we are in) - so that they belong to the cartoon and not to the photograph.
            // Returns the tone: 0 nothing, 1 rim, 2 body, 3 white-hot heart.
            float Tone(float2 q, int shape, float4 px)
            {
                float N = max(px.x, 1.0);
                float2 cell = floor(q * N);
                float2 p = (cell + 0.5) / N;
                float ragged = hash(cell + px.y * 17.0);
                if (shape == 8)
                {
                    // a comet of fire: the head sits left of the middle, tongues trail off to the right and flicker at every step
                    float2 h = float2(-0.45, 0.0);
                    float r = length(p - h) / 0.50;
                    float x = p.x - h.x;
                    float t = saturate(x / (0.30 + 1.15 * px.z));
                    float wob = (hash(float2(floor(cell.x * 0.5), px.y)) - 0.5) * 0.30 * t;
                    float w = 0.50 * pow(1.0 - t, 0.75) * (0.80 + 0.40 * hash(float2(floor(cell.x * 0.5) + 31.0, px.y)));
                    float d = x > 0.0 ? abs(p.y - wob) / max(w, 1e-3) : 9.0;
                    float e = min(r, d) * (0.90 + 0.22 * ragged);
                    return e < 0.40 ? 3.0 : e < 0.72 ? 2.0 : e < 1.0 ? 1.0 : 0.0;
                }
                if (shape == 9)
                {
                    // a burst: eight spikes flying apart, hollow in the middle as it grows  (px.z = how far along, 0..1)
                    float a = atan2(p.y, p.x), r = length(p), t = px.z;
                    float spike = pow(abs(cos(a * 4.0 + px.w)), 4.0);
                    float R = (0.38 + 0.62 * spike) * (0.40 + 0.60 * t) * (0.92 + 0.16 * ragged);
                    float hole = R * saturate(t * 1.7 - 0.25);
                    if (r > R || r < hole) return 0.0;
                    float k = (r - hole) / max(R - hole, 1e-3);
                    return k < 0.45 ? 3.0 : k < 0.80 ? 2.0 : 1.0;
                }
                if (shape == 10 || shape == 14)
                {
                    // a ring  (px.z = its thickness)
                    float r = length(p), k = (1.0 - r) / max(px.z, 1e-3);
                    if (k < 0.0 || k > 1.0) return 0.0;
                    return abs(k - 0.5) < 0.2 ? 3.0 : 2.0;
                }
                if (shape == 13)
                {
                    // a puff of dust: a ragged blob that is eaten away as it ages  (px.z = age, 0..1)
                    float r = length(p) * (0.80 + 0.40 * ragged), R = 1.0 - px.z * 0.6;
                    if (r > R) return 0.0;
                    return r > R - 0.30 ? 1.0 : 2.0;
                }
                if (shape == 15)
                {
                    // a leaf lying along the diagonal, turned in quarter turns  (px.w = which)
                    float2 l = p; int turn = (int)fmod(px.w, 4.0);
                    if (turn == 1) l = float2(-p.y, p.x); else if (turn == 2) l = -p; else if (turn == 3) l = float2(p.y, -p.x);
                    float u = (l.x + l.y) * 0.7071, v = (l.x - l.y) * 0.7071;
                    if (abs(u) > 0.95 || abs(v) > 0.46 * (1.0 - u * u) + 0.04) return 0.0;
                    return abs(v) < 0.5 / N ? 1.0 : (u > 0.3 ? 3.0 : 2.0);
                }
                if (shape == 16)
                {
                    // a glint: a four-pointed star, upright or (px.w > 0.5) on its corner; it shrinks to nothing  (px.z = how far along, 0..1)
                    float2 g = px.w > 0.5 ? float2(p.x + p.y, p.x - p.y) * 0.7071 : p;
                    float2 ag = abs(g); float hi = max(ag.x, ag.y), lo = min(ag.x, ag.y), R = 1.0 - 0.6 * px.z;
                    if (hi > R || lo > 0.20 * (R - hi) + 0.6 / N) return 0.0;
                    return hi < 0.30 * R ? 3.0 : hi < 0.65 * R ? 2.0 : 1.0;
                }
                return px.z;                                    // 11, 12: a plain block in the tone given
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 q = i.uv; float r = length(q);
                float m;
                int shape = (int)(i.kind.x + 0.5);
                if (shape >= 8)
                {
                    // four samples round the pixel: the steps of the art stay hard, but their edges do not crawl
                    float2 dx = ddx(q) * 0.25, dy = ddy(q) * 0.25;
                    float3 body = i.col.rgb, sum = 0; float cover = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        float2 o = (k == 0 ? dx + dy * 0.5 : k == 1 ? -dx - dy * 0.5 : k == 2 ? dy - dx * 0.5 : dx * 0.5 - dy);
                        float tone = Tone(q + o, shape, i.px);
                        if (tone > 0.5)
                        {
                            // heart: nearly white and far brighter than white (it is this that the bloom picks up); rim: deep colour
                            // (shapes 12 and up are things, not light: dust, paper, leaves, rain - plain colours in three shades)
                            float3 c = shape >= 12 && shape <= 15 ? (tone > 2.5 ? body * 1.25 + 0.06 : tone > 1.5 ? body : body * 0.62)
                                     : (tone > 2.5 ? lerp(body, 1.0, 0.80) * 2.6 : tone > 1.5 ? (body + 0.15) * 1.7 : body * body * 1.2);
                            sum += c; cover += 1.0;
                        }
                    }
                    float a = cover * 0.25 * i.col.a;
                    return fixed4(sum * 0.25 * i.col.a, a * (1.0 - i.kind.y));
                }
                if (shape == 0) { m = saturate(1.0 - r); m = m * m * (3.0 - 2.0 * m); }                     // soft disc
                else if (shape == 1) { m = smoothstep(1.0, 0.86, r) * smoothstep(0.52, 0.78, r); }          // ring
                else if (shape == 2)                                                                         // leaf
                {
                    float w = 0.46 * (1.0 - q.x * q.x) * (1.0 + 0.35 * q.x);
                    float e = fwidth(q.y) * 1.5 + 0.02;
                    m = smoothstep(w, w - e, abs(q.y)) * step(abs(q.x), 1.0);
                    m *= 0.72 + 0.28 * smoothstep(0.0, 0.10, abs(q.y));                                      // the midrib, a little darker
                }
                else if (shape == 3) { float t = saturate(1.0 - abs(q.y)); m = t * t * saturate(1.0 - abs(q.x)); }    // streak (soft across, fading along)
                else if (shape == 4) { m = pow(saturate(1.0 - r), 2.5) * 1.6; }                              // spark: a hot core
                else if (shape == 5) { m = smoothstep(1.0, 0.35, r); }                                       // soft shadow
                else if (shape == 6)                                                                         // star: four rays and a hot heart
                {
                    float2 a2 = abs(q);
                    float rays = max(exp(-a2.y * 26.0) * pow(saturate(1.0 - a2.x), 1.5), exp(-a2.x * 26.0) * pow(saturate(1.0 - a2.y), 1.5));
                    float heart = pow(saturate(1.0 - r), 7.0) * 0.7;
                    m = saturate(rays + heart) * 1.3;
                }
                else { m = smoothstep(1.0, 0.82, max(abs(q.x), abs(q.y))) * (0.82 + 0.18 * q.y); }           // 7: a scrap of paper
                float a = i.col.a * m;
                return fixed4(i.col.rgb * a, a * (1.0 - i.kind.y));
            }
            ENDCG
        }
    }
}
