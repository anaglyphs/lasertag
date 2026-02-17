Shader "Custom/EnvDebug"
{
    Properties
    {
        [MainTexture] _Volume("Base Map", 3D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "EnvMapper.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            TEXTURE3D(_Volume);

            CBUFFER_START(UnityPerMaterial)
                half4 _Volume_ST;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 uvw = envWorldToVoxelUVW(IN.positionWS);
                float val = envVolume.Sample(envPointClampSampler, uvw);
                float r = -min(val, 0);
                float g = val;
                float b = val < 0;
                half4 color = half4(r, g, b, 1.0);
                return color;
            }
            ENDHLSL
        }
    }
}