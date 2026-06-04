Shader "DepthKit/Sim/R32toD16Blit"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            ZWrite On
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "DepthKit.hlsl"

            Texture2D<float> rawDepth;

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings OUT;
                OUT.positionHCS = GetFullScreenTriangleVertexPosition(vertexID);
                OUT.uv = GetFullScreenTriangleTexCoord(vertexID);
                return OUT;
            }

            float frag(Varyings IN) : SV_Depth
            {
                const float z = rawDepth.SampleLevel(agPointClampSampler, float3(IN.uv, 0), 0);
                const float near = agDepthZParams.x;
                const float far = agDepthZParams.y;
                float depth = (far / (far - near)) - (far * near) / ((far - near) * z);
                return depth;
            }
            ENDHLSL

        }
    }
}