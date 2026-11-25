// Photographed sky panorama (equirectangular) as the skybox.
Shader "SpriteStage/Sky"
{
    Properties { _MainTex ("Panorama", 2D) = "white" {} _Tint ("Tint", Color) = (1, 1, 1, 1) _Ground ("Below horizon", Color) = (0.2, 0.3, 0.12, 1) }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; fixed4 _Tint, _Ground;
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert(float4 vertex : POSITION) { v2f o; o.pos = UnityObjectToClipPos(vertex); o.dir = vertex.xyz; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float u = 0.5 - atan2(d.z, d.x) / 6.2831853;
                float v = 1.0 - acos(clamp(d.y, -1, 1)) / 3.14159265;
                fixed3 c = tex2Dlod(_MainTex, float4(u, max(v, 0.502), 0, 0)).rgb * _Tint.rgb;
                c = lerp(c, _Ground.rgb, smoothstep(0.0, -0.06, d.y));
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
}
