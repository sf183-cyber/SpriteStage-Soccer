// Plain baked texture (the White House carries its lighting in its texture).
Shader "SpriteStage/Unlit"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} _Tint ("Tint", Color) = (1, 1, 1, 1) }
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
            sampler2D _MainTex; fixed4 _Tint; float4 _G3Haze;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; };
            v2f vert(float4 vertex : POSITION, float2 uv : TEXCOORD0)
            {
                v2f o; o.pos = UnityObjectToClipPos(vertex); o.uv = uv;
                o.wpos = mul(unity_ObjectToWorld, vertex).xyz;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 c = tex2D(_MainTex, i.uv).rgb * _Tint.rgb;
                c = lerp(c, _G3Haze.rgb, saturate(distance(_WorldSpaceCameraPos, i.wpos) / 900.0) * _G3Haze.a);      // air between the camera and distant things
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
}
