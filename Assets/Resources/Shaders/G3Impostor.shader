// Trees: photographs of the scanned trees taken from all round under the scene's sun (see Pipeline/u_trees.py).
// Each tree shows the two pictures nearest to the viewing direction, cross-faded.
Shader "SpriteStage/Impostor"
{
    Properties { _MainTex ("Atlas", 2D) = "white" {} _Tint ("Tint", Color) = (1, 1, 1, 1) _Cutoff ("Cutoff", Float) = 0.4 }
    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off ZWrite On AlphaToMask On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; fixed4 _Tint; float _Cutoff; float4 _G3Haze;
            struct v2f { float4 pos : SV_POSITION; float4 uv : TEXCOORD0; float blend : TEXCOORD1; float dist : TEXCOORD2; fixed3 light : TEXCOORD3; };
            v2f vert(float4 vertex : POSITION, float2 uv : TEXCOORD0, float2 uv2 : TEXCOORD1, fixed4 color : COLOR)
            {
                v2f o; o.pos = UnityObjectToClipPos(vertex); o.uv = float4(uv, uv2); o.blend = color.a; o.light = color.rgb;      // rgb: white by day; by night what light reaches that corner of the tree (World.UpdateTrees)
                o.dist = distance(_WorldSpaceCameraPos, mul(unity_ObjectToWorld, vertex).xyz);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 a = tex2D(_MainTex, i.uv.xy), b = tex2D(_MainTex, i.uv.zw);
                fixed4 c = lerp(a, b, i.blend);
                c.rgb *= _Tint.rgb * i.light;
                c.rgb = lerp(c.rgb, _G3Haze.rgb, saturate(i.dist / 900.0) * _G3Haze.a);
                c.a = saturate((c.a - _Cutoff) / max(fwidth(c.a), 1e-4) + 0.5);
                clip(c.a - 0.004);
                return c;
            }
            ENDCG
        }
    }
}
