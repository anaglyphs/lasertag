Shader "Debug/ChunkDebug"
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
                float3 worldPos : TEXCOORD0;
                float size : PSIZE;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, float4(v.vertex, 1)).xyz;
                o.size = _PointSize;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 dx = ddx(i.worldPos);
                float3 dy = ddy(i.worldPos);
                float3 faceNormal = normalize(cross(dx, dy));

                half3 rgb = faceNormal * 0.5 + 0.5;
                return half4(rgb, 1);
            }
            ENDCG
        }
    }
}