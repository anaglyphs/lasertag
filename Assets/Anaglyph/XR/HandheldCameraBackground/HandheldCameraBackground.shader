// A phone's camera feed, styled by PassthroughStylingFeature, drawn by HandheldCameraBackground after the
// opaques and under what they left by their alpha, as a headset's passthrough compositor does.
// ARKit delivers YCbCr planes, ARCore (Vulkan) and XR Simulation an RGB image
Shader "Anaglyph/HandheldCameraBackground"
{
    Properties
    {
        _textureY ("ARKit Y", 2D) = "black" {}
        _textureCbCr ("ARKit CbCr", 2D) = "gray" {}
        _MainTex ("ARCore Image", 2D) = "black" {}
        _TextureSingle ("Simulation Image", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "HandheldCameraBackground"
            Cull Off
            ZTest Always
            ZWrite Off
            // alpha is kept: later passes weigh how much feed a pixel shows by it
            Blend OneMinusDstAlpha One, Zero One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ HANDHELD_ARKIT HANDHELD_ARCORE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "PassthroughStyle.hlsl"

            // image coordinates for screen coordinates, set from each camera frame
            float4x4 _UnityDisplayTransform;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 imageUV : TEXCOORD0;
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                // one triangle over the screen; (0, 0) is its bottom left
                float2 screenUV = float2((vertexID << 1) & 2, vertexID & 2);

                Varyings output;
                output.positionCS = float4(screenUV * 2 - 1, 0.5, 1);
                output.positionCS.y *= _ProjectionParams.x;
                #if HANDHELD_ARKIT
                output.imageUV = mul(float4(screenUV, 1, 1), _UnityDisplayTransform).xy;
                #elif HANDHELD_ARCORE
                output.imageUV = mul(float4(screenUV, 1, 0), _UnityDisplayTransform).xy;
                #else
                output.imageUV = screenUV;
                #endif
                return output;
            }

            #if HANDHELD_ARKIT

            TEXTURE2D(_textureY);
            SAMPLER(sampler_textureY);
            TEXTURE2D(_textureCbCr);
            SAMPLER(sampler_textureCbCr);

            static const float4x4 YCbCrToSrgb = float4x4(
                float4(1.0,  0.0000,  1.4020, -0.7010),
                float4(1.0, -0.3441, -0.7141,  0.5291),
                float4(1.0,  1.7720,  0.0000, -0.8860),
                float4(0.0,  0.0000,  0.0000,  1.0000));

            // Y is already gamma-space luma
            float LumaSrgb(float2 uv) { return SAMPLE_TEXTURE2D(_textureY, sampler_textureY, uv).r; }

            float3 StyledFeedColor(float2 uv, float2 edgeStepX, float2 edgeStepY)
            {
                float4 ycbcr = float4(LumaSrgb(uv), SAMPLE_TEXTURE2D(_textureCbCr, sampler_textureCbCr, uv).rg, 1);
                float3 srgb = saturate(mul(YCbCrToSrgb, ycbcr).rgb);

                if (IsPassthroughStyled())
                {
                    float edgeStrength = 0;
                    if (_PassthroughEdgeColor.a > 0)
                        edgeStrength = PassthroughEdgeStrength(
                            LumaSrgb(uv - edgeStepX), LumaSrgb(uv + edgeStepX),
                            LumaSrgb(uv - edgeStepY), LumaSrgb(uv + edgeStepY));
                    srgb = ApplyPassthroughStyleSrgb(srgb, edgeStrength);
                }

                #if UNITY_COLORSPACE_GAMMA
                return srgb;
                #else
                return FastSRGBToLinear(srgb);
                #endif
            }

            #else

            #if HANDHELD_ARCORE
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float3 FeedColor(float2 uv) { return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb; }
            #else
            TEXTURE2D(_TextureSingle);
            SAMPLER(sampler_TextureSingle);
            float3 FeedColor(float2 uv) { return SAMPLE_TEXTURE2D(_TextureSingle, sampler_TextureSingle, uv).rgb; }
            #endif

            // the image is in the project's color space
            float3 StyledFeedColor(float2 uv, float2 edgeStepX, float2 edgeStepY)
            {
                float3 color = FeedColor(uv);
                if (!IsPassthroughStyled()) return color;

                float edgeStrength = 0;
                if (_PassthroughEdgeColor.a > 0)
                    edgeStrength = PassthroughEdgeStrength(
                        PassthroughLuminanceSrgbOfColorSpaceColor(FeedColor(uv - edgeStepX)),
                        PassthroughLuminanceSrgbOfColorSpaceColor(FeedColor(uv + edgeStepX)),
                        PassthroughLuminanceSrgbOfColorSpaceColor(FeedColor(uv - edgeStepY)),
                        PassthroughLuminanceSrgbOfColorSpaceColor(FeedColor(uv + edgeStepY)));
                return ApplyPassthroughStyleColorSpaceColor(color, edgeStrength);
            }

            #endif

            half4 Frag(Varyings input) : SV_Target
            {
                float2 edgeStepX = ddx(input.imageUV) * PassthroughEdgeSampleSpacingPixels;
                float2 edgeStepY = ddy(input.imageUV) * PassthroughEdgeSampleSpacingPixels;
                return half4(StyledFeedColor(input.imageUV, edgeStepX, edgeStepY), 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
