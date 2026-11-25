// Player / ball sprites: crisp pixel art on a quad that faces the camera.
// "Sharp bilinear" sampling keeps every art pixel a clean square at any angle or zoom without shimmer;
// alpha-to-coverage gives the silhouettes anti-aliased edges (with MSAA).
Shader "SpriteStage/Sprite"
{
    Properties { _MainTex ("Atlas", 2D) = "white" {} _Silhouette ("Silhouette (0..1)", Float) = 0 }
    SubShader
    {
        Tags { "Queue" = "AlphaTest+10" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex; float4 _MainTex_TexelSize; float4 _G3FxLight, _G3FxLightCol; float _Silhouette;
        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 cell : TEXCOORD1; float4 wpos : TEXCOORD2; };
        v2f vert(float4 vertex : POSITION, float2 uv : TEXCOORD0, float4 cell : TEXCOORD1, float2 layer : TEXCOORD2)
        {
            v2f o; o.pos = UnityObjectToClipPos(vertex); o.uv = uv; o.cell = cell; o.wpos = float4(mul(unity_ObjectToWorld, vertex).xyz, layer.y);
            // the tiles of one figure lie in one plane; later tiles win by a hair of depth (not of position, which would open seams)
            #if defined(UNITY_REVERSED_Z)
            o.pos.z += layer.x * 3e-6 * o.pos.w;
            #else
            o.pos.z -= layer.x * 3e-6 * o.pos.w;
            #endif
            return o;
        }
        fixed4 art(v2f i)
        {
            float2 px = i.uv * _MainTex_TexelSize.zw - 0.5;
            float2 w = max(fwidth(px), 1e-5);
            float2 fl = floor(px), fr = px - fl;
            float2 t = saturate((fr - 0.5) / w + 0.5);
            float2 uv = clamp((fl + 0.5 + t) * _MainTex_TexelSize.xy, i.cell.xy, i.cell.zw);
            fixed4 c = tex2D(_MainTex, uv);
            c.rgb = c.a > 0.004 ? c.rgb / c.a : c.rgb;          // the atlas keeps transparent texels black
            c.a = saturate((c.a - 0.5) / max(fwidth(c.a), 1e-4) + 0.5);
            return c;
        }
        ENDCG
        Pass
        {
            Cull Off ZWrite On AlphaToMask On
            Offset -1, -1
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = art(i);
                clip(c.a - 0.004);
                c.rgb = lerp(c.rgb, fixed3(0.02, 0.02, 0.025), _Silhouette);      // 场边的人：原版闪电时小人全变黑剪影，他们也跟着变（Crowd.cs）
                if (_G3FxLight.w > 0.0)
                {
                    // a figure near a glowing ball catches its light (flat figures: by distance only)
                    float3 L = _G3FxLight.xyz - i.wpos.xyz;
                    float att = saturate(1.0 - dot(L, L) / (_G3FxLight.w * _G3FxLight.w));
                    c.rgb += c.rgb * _G3FxLightCol.rgb * (att * att * 0.8);
                }
                return c;
            }
            ENDCG
        }
        // Marks the figure in the picture's alpha channel (0 = cartoon).  The photographic finish (G3Post) leaves
        // marked pixels out of its glow, so white shorts and the ball stay as crisp as the rest of the pixel art.
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual ColorMask A
            Offset -1, -1
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target
            {
                clip(art(i).a - 0.5);
                return 0;
            }
            ENDCG
        }
        // Marks the ball and the shooter of a special shot in the stencil (layer.y = 1, see SpriteField): when the world
        // stands still for the shot, the darkness that falls over the picture (G3Night) leaves the marked pixels out.
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual ColorMask 0 AlphaToMask On
            Offset -1, -1
            Stencil { Ref 2 ReadMask 2 WriteMask 2 Comp Always Pass Replace }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag(v2f i) : SV_Target
            {
                clip(i.wpos.w - 0.5);
                fixed4 c = art(i);
                clip(c.a - 0.004);
                return c;
            }
            ENDCG
        }
    }
}
