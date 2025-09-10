Shader "DepthLightEffect"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" "Queue"="Geometry-1" }
        LOD 100
        ZWrite Off Cull Off
		Blend One OneMinusSrcAlpha
        Pass
        {
			Name "DepthLightEffectPass"

			HLSLPROGRAM

			#include "Assets/Anaglyph/XRTemplate/Depth/DepthKit.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			// The Blit.hlsl file provides the vertex shader (Vert),
			// input structure (Attributes) and output strucutre (Varyings)
			#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

			struct Light {
				float3 position;
				float3 color;
				float intensity;
				float padding;
			};

			StructuredBuffer<Light> _Lights;
			int _LightCount = 0;

			#pragma vertex Vert
			#pragma fragment frag

			TEXTURE2D_X(_CameraOpaqueTexture);
            SAMPLER(sampler_CameraOpaqueTexture);

			half4 frag (Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				float2 uv = input.texcoord.xy;

				#if defined(SHADER_API_MOBILE)
					uv.y = 1.0 - uv.y;
				#endif

				const int eye = unity_StereoEyeIndex;
				const float depthNDC = agDepthSample(uv, eye, bilinearClampSampler);

				float3 depthWorld = agDepthNDCtoWorld(float3(uv, depthNDC), eye);
				float3 worldNorm = agDepthNormalSample(uv, eye, bilinearClampSampler).xyz;

				float3 color = float3(0, 0, 0);
				float intens = 0;

				for(int i = 0; i < _LightCount; i++) {

					Light light = _Lights[i];

					float3 diff = light.position - depthWorld;
				
					float3 lightDir = normalize(diff);
					float dist = length(diff);
					float facingSurface = max(dot(worldNorm, lightDir), 0.0);
				
					float intensity = facingSurface * max(0, 1.0 / (dist * dist)) * light.intensity;

					color += light.color * intensity;
					intens += intensity;
				}

				// return half4(color.rgb, 0);

				float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
				float brightness = luminance;// * intens;

				float threshold = 0.1;
				float maxBrightness = 1.0;
				 
				float t = clamp((brightness - threshold) / (maxBrightness - threshold), 0.0, 1.0);
				
				color = lerp(color, float3(1.0, 1.0, 1.0), t);
				return half4(color.rgb, 0);
			}
			ENDHLSL
		}
	}
}