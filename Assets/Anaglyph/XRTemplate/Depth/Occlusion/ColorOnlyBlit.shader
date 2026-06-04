// this exists because ARFoundation's simulator automatically occludes ALL 
// objects (even those without a depth occlusion shader) with the simulated
// environment depth buffer. This makes testing occlusion annoying, because
// ARF makes it unclear if things are correctly occluded by *my* shaders
// or ARF...
// this fixes by using ARCameraBackground's custom material field
// to strip the depth buffer and only blit the background color
Shader "DepthKit/Sim/ColorOnlyBlit"
{
    Properties
    {
        // ⚠️ Provider-specific — see note below. Simulation may NOT bind "_MainTex".
        _TextureSingle ("Camera Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "ForceNoShadowCasting" = "True"
        }

        Pass
        {
            Name "AR Background (Color Only)"

            Cull Off
            ZTest Always
            ZWrite Off // <-- the whole point: never touch the depth buffer
            Lighting Off
            // ColorMask RGB

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Supplied by AR Foundation each frame to orient the camera image correctly.
            float4x4 _UnityDisplayTransform;

            TEXTURE2D(_TextureSingle);
            SAMPLER(sampler_TextureSingle);

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS);
                OUT.uv = mul(float3(IN.uv, 1.0f), (float3x3)_UnityDisplayTransform).xy;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_TextureSingle, sampler_TextureSingle, IN.uv);
            }
            ENDHLSL
        }
    }
}