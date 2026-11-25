// A light photographic finish for the 3D view: a soft glow round the brightest things (the white house in
// the sun, the sky), a gentle vignette and a touch of contrast.  The picture itself is not blurred, and the
// cartoon figures are left out of the glow.
Shader "Hidden/SpriteStage/Post"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _Bloom; float4 _MainTex_TexelSize;
        float _Threshold, _Intensity, _Vignette, _Contrast, _Flash, _Rain;
        float4 _Wave, _Heat, _Zoom;         // set by the effects layer (see PostFx.cs); all zero = nothing
        float _Gloom, _Dim;                 // storm light (0..1); the hush while a special shot is in the air (0..1)
        float4 _Punch, _Lines;              // a mighty kick: the colour of its flash (rgb) and how much (a); impact lines: centre x, y, strength, which set
        float _Aspect, _Clock;
        float4 _Push;                       // the standstill of a special shot: the picture drawn in towards a point (x, y, -, how much)
        float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
        // alpha 0 marks the cartoon figures (see G3Sprite): they give no glow
        // (the picture is HDR: light sources are brighter than white, and it is those that glow most)
        float3 glowSrc(float2 uv) { float4 s = tex2D(_MainTex, uv); return min(s.rgb, 6.0) * saturate(s.a * 3.0); }
        fixed4 bright(v2f i) : SV_Target
        {
            float2 d = _MainTex_TexelSize.xy;
            float3 c = (glowSrc(i.uv + d * float2(-1, -1)) + glowSrc(i.uv + d * float2(1, -1)) + glowSrc(i.uv + d * float2(-1, 1)) + glowSrc(i.uv + d * float2(1, 1))) * 0.25;
            float l = max(c.r, max(c.g, c.b));
            return fixed4(c * saturate((l - _Threshold) / max(1.0 - _Threshold, 1e-3)), 1);
        }
        fixed4 blur(v2f i, float2 dir)
        {
            float2 d = dir * _MainTex_TexelSize.xy;
            float3 c = tex2D(_MainTex, i.uv).rgb * 0.227 +
                (tex2D(_MainTex, i.uv + d * 1.384).rgb + tex2D(_MainTex, i.uv - d * 1.384).rgb) * 0.316 +
                (tex2D(_MainTex, i.uv + d * 3.230).rgb + tex2D(_MainTex, i.uv - d * 3.230).rgb) * 0.070;
            return fixed4(c, 1);
        }
        fixed4 blurH(v2f i) : SV_Target { return blur(i, float2(1, 0)); }
        fixed4 blurV(v2f i) : SV_Target { return blur(i, float2(0, 1)); }

        // Where to look in the picture for this pixel: pushed about by a shock wave and by hot air.
        float2 Bent(float2 uv)
        {
            float2 asp = float2(_Aspect, 1.0);
            if (_Push.w > 0.0) uv = _Push.xy + (uv - _Push.xy) / (1.0 + _Push.w);
            if (_Wave.w > 0.0)
            {
                // a ring running outwards: the picture is squeezed towards it on both sides
                float2 p = (uv - _Wave.xy) * asp; float d = length(p);
                float k = (d - _Wave.z) / max(_Wave.z * 0.22 + 0.01, 1e-3);
                uv += p / max(d, 1e-4) / asp * (k * exp(-k * k) * _Wave.w * 0.05);
            }
            if (_Heat.w > 0.0)
            {
                float2 p = (uv - _Heat.xy) * asp; float d = length(p) / max(_Heat.z, 1e-3);
                float f = saturate(1.0 - d); f *= f;
                uv += float2(sin(uv.y * 190.0 + _Clock * 23.0), cos(uv.x * 170.0 - _Clock * 19.0)) / asp * (f * _Heat.w * 0.004);
            }
            return uv;
        }

        fixed4 final(v2f i) : SV_Target
        {
            float2 uv = Bent(i.uv);
            float4 s = tex2D(_MainTex, uv);
            if (_Zoom.w > 0.0)
            {
                // the instant of a mighty kick: the picture rushes towards the ball
                // (twelve looks along the way, each a little further in: a smooth smear, not a row of ghosts)
                float2 to = (uv - _Zoom.xy) * (_Zoom.w * 0.0065);
                float jit = hash(i.uv * 913.0 + _Clock) - 0.5;
                for (int k = 1; k < 12; k++) s += tex2D(_MainTex, uv - to * (k + jit));
                s /= 12.0;
            }
            float3 c = s.rgb;
            float world = saturate(s.a * 3.0);                      // 0 on the cartoon figures
            // rain: the light goes flat and a little cold (the figures keep most of their colour)
            float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
            float3 wet = lerp(c, lum.xxx, 0.16) * float3(0.88, 0.92, 0.97);
            c = lerp(c, wet, _Rain * (0.15 + 0.85 * world));
            // What is brighter than white is a light (fire, lightning, sparks): gloom and hush leave those burning.
            float hot = saturate((max(c.r, max(c.g, c.b)) - 0.95) / 0.8);
            if (_Gloom > 0.0)
            {
                // storm light: under heavy cloud the world loses its light and most of its colour; the figures only a little
                float l2 = dot(c, float3(0.2126, 0.7152, 0.0722));
                float3 dark = lerp(c, l2.xxx, 0.35) * float3(0.40, 0.47, 0.60);
                float3 fig = c * float3(0.78, 0.82, 0.92);
                c = lerp(c, lerp(lerp(fig, dark, world), c, hot), _Gloom);
            }
            if (_Dim > 0.0)
                c = lerp(c, lerp(c * float3(0.42, 0.42, 0.52), c, max(hot, 1.0 - world)), _Dim);      // the hush: the world dims round a special shot
            // lightning: for an instant everything but the figures is burnt out
            c = lerp(c, c * (1.0 + 4.0 * _Flash) * float3(0.92, 0.96, 1.0) + float3(0.16, 0.19, 0.26) * _Flash, world);    // over-exposed, not fogged: the shadows stay
            // a mighty kick: a flash in the colour of the shot
            c = lerp(c, c * (1.0 + 1.8 * _Punch.a) + _Punch.rgb * (0.22 * _Punch.a), world);
            c += tex2D(_Bloom, uv).rgb * _Intensity * (0.25 + 0.75 * world);       // little of the glow on the figures: they stay crisp
            float2 q = i.uv - 0.5;
            c *= 1.0 - (_Vignette + 0.30 * _Gloom + 0.22 * _Dim) * dot(q, q) * 2.0;
            if (_Lines.z > 0.0)
            {
                // Impact lines, as a comic draws them: hard white wedges from the edges of the picture towards the ball,
                // in big pixels like the rest of the cartoon, a new set at every step.
                float2 grid = float2(360.0 * _Aspect, 360.0);
                float2 pq = ((floor(i.uv * grid) + 0.5) / grid - _Lines.xy) * float2(_Aspect, 1.0);
                float d = length(pq), turn = atan2(pq.y, pq.x) / 6.2832 + 0.5;
                float cell = floor(turn * 72.0), f = abs(frac(turn * 72.0) - 0.5);
                float h1 = hash(float2(cell, _Lines.w)), h2 = hash(float2(cell + 7.0, _Lines.w * 1.3 + 2.0));
                float from = 0.27 + 0.36 * h2;                                  // each line starts at its own distance from the ball
                float wide = 0.42 * h1 * saturate((d - from) / 0.50);           // and widens outwards
                float on = step(0.42, h1) * step(from, d) * step(f, wide);
                float3 ink = h2 > 0.82 ? float3(0.02, 0.02, 0.05) : float3(1.25, 1.25, 1.25);
                c = lerp(c, ink, on * saturate(_Lines.z));
            }
            // a touch of contrast about middle grey, done on display-referred values
            float3 g = pow(saturate(c), 1.0 / 2.2);
            g = saturate((g - 0.5) * _Contrast + 0.5);
            return fixed4(pow(g, 2.2), 1);
        }
        ENDCG
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment bright
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment blurH
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment blurV
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment final
            ENDCG }
    }
}
