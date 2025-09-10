Shader "Lasertag/DepthLight"
{
	Properties
	{
		_Color ("Color", Color) = (1,1,1,1)
		_Intensity ("Intensity", Float) = 1
	}

	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue"="Geometry-1" }
		LOD 200
		ZWrite Off
		ZTest LEqual
		Cull Front

		Pass {
			HLSLPROGRAM 
			#pragma vertex vert
			#pragma fragment frag

			#include "Assets/Anaglyph/XRTemplate/Depth/DepthKit.hlsl" 
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Light {
				float3 position;
				float3 color;
				float intensity;
				float padding;
			};

			StructuredBuffer<Light> _Lights;
			int _LightCount = 0;

			struct Attributes
			{
				float4 positionOS   : POSITION;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float3 positionWS : TEXCOORD1;

				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;

				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

				OUT.positionHCS = TransformObjectToHClip(IN.positionOS);
				OUT.positionWS = TransformObjectToWorld(IN.positionOS);

				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target 
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

				const int eye = unity_StereoEyeIndex;

				const float3 ndc = agDepthWorldToNDC(IN.positionWS, eye); 
				const float2 uv = ndc.xy;
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

				float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
				float brightness = luminance;

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