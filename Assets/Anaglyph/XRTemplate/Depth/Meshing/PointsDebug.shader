Shader "Debug/PointVisualizer"
{
    Properties
    {
        _PointSize ("Point Size", Float) = 5
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"

            float4 _Color;
            float _PointSize;

            struct appdata
            {
                float3 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 norm : TEXCOORD;
                float size : PSIZE;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.norm = v.normal;
                o.size = _PointSize;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half3 rgb = i.norm.xyz / 2 + 0.5f;
                return half4(rgb, 1);
            }
            ENDCG
        }
    }
}