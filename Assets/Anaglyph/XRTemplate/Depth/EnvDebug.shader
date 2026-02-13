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

            SamplerState pointClampSampler;

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
                // eturn half4(voxCount.xyz / 2000.0, 1.0);
                float3 uvw = envWorldToVoxelUVW(IN.positionWS);
                // return half4(uvw.xyz, 1.0);
                float val = envVolume.Sample(pointClampSampler, uvw);
                float r = -min(val, 0);
                float g = val;
                float b = val < 0;
                half4 color = half4(r, g, b, 1.0);
                return color;
                // float val = SAMPLE_TEXTURE3D(_Volume, linearClampSampler, uvw);
                // return half4(val, val, val, 1.0);
            }
            ENDHLSL
        }
    }
}