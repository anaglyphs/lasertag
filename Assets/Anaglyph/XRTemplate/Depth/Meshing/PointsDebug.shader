Shader "Debug/PointVisualizer"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _PointSize ("Point Size", Float) = 5.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            // Needed on most platforms to allow programmable point size
            #pragma geometry geom
            #include "UnityCG.cginc"

            float4 _Color;
            float _PointSize;

            struct appdata
            {
                float3 vertex : POSITION;
            };

            struct v2g
            {
                float4 pos : POSITION;
            };

            struct g2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float size : PSIZE;
            };

            v2g vert (appdata v)
            {
                v2g o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            [maxvertexcount(1)]
            void geom(point v2g input[1], inout PointStream<g2f> stream)
            {
                g2f o;
                o.pos = input[0].pos;
                o.color = _Color;
                o.size = _PointSize;
                stream.Append(o);
            }

            fixed4 frag (g2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}