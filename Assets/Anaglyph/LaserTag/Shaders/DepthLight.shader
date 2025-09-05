Shader "Lasertag/DepthLight"
{
	Properties
	{
		[PerObjectData] _ColorPerObject ("Color", Color) = (1,1,1,1)
		[PerObjectData] _IntensityPerObject ("Intensity", Float) = 1
	}

	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue"="Geometry-1" }
		LOD 200
		ZWrite Off
		ZTest LEqual
		Cull Front
		Blend One OneMinusSrcAlpha

		Pass {
			HLSLPROGRAM 
			#pragma vertex vert
			#pragma fragment frag

			#include "Assets/Anaglyph/XRTemplate/Depth/DepthKit.hlsl" 
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			half4 _ColorPerObject;
			half _IntensityPerObject;

			float sqr(float x)
			{
				return x * x;
			}

			// float attenuate_cusp(float distance, float radius,
			// 	float max_IntensityPerObject, float falloff)
			// {
			// 	float s = distance / radius;

			// 	if (s >= 1.0)
			// 		return 0.0;

			// 	float s2 = sqr(s);

			// 	return max_IntensityPerObject * sqr(1 - s2) / (1 + falloff * s);
			// }

			struct Attributes
			{
				float4 positionOS   : POSITION;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float4 positionHCSTexCoord : TEXCOORD0;
				float3 positionWS : TEXCOORD1;

				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;

				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

				OUT.positionHCS = TransformObjectToHClip(IN.positionOS);
				OUT.positionHCSTexCoord = OUT.positionHCS;
				OUT.positionWS = TransformObjectToWorld(IN.positionOS);

				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target 
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

				const int eye = unity_StereoEyeIndex;

				const float3 ndc = agDepthWorldToNDC(IN.positionWS, eye); 
				
				const float depthNDC = agDepthSample(ndc.xy, eye, bilinearClampSampler);

				float2 uv = ndc.xy;
				float3 lightPos = mul(unity_ObjectToWorld, float4(0,0,0,1)).xyz;
				float3 depthWorld = agDepthNDCtoWorld(float3(uv, depthNDC), eye);
	
				float3 worldNorm = agDepthNormalSample(uv, eye, bilinearClampSampler);

				float3 diff = lightPos - depthWorld;
				
				float3 lightDir = normalize(diff);

				float dist = length(diff);
				// float radius = length(mul(unity_ObjectToWorld, float4(1,0,0,0))) / 2;

				float facingSurface = max(dot(worldNorm, lightDir), 0.0);
				
				float intensity = facingSurface * max(0, 1.0 / (dist * dist)) * _IntensityPerObject;

				float luminance = dot(_ColorPerObject, float3(0.2126, 0.7152, 0.0722));
				float brightness = luminance * intensity;

				float threshold = 0.1;
				float maxBrightness = 1.0;
				 
				float t = clamp((brightness - threshold) / (maxBrightness - threshold), 0.0, 1.0);
				
				float3 saturatedColor = _ColorPerObject.rgb * intensity;
				float3 finalColor = lerp(saturatedColor, float3(1.0, 1.0, 1.0), t);
				return float4(finalColor, 0);

				// return float4(worldNorm, 0);
			}

			ENDHLSL
		}
	}
}