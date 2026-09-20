Shader "Anaglyph/Lasertag/DepthLight"
{
	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" }

		Pass
		{
			Name "DepthLighting"
			ZWrite Off
			ZTest LEqual
			Cull Off
			Blend One One
			ColorMask RGB

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma multi_compile_instancing
			#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
			#pragma multi_compile_fragment _ _LIGHT_COOKIES
			#pragma multi_compile _ _LIGHT_LAYERS
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"

			#define _RECEIVE_SHADOWS_OFF 1
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Assets/Anaglyph/XR/DepthKit/DepthKit.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float4x4 _WorldToDepthClip[2];
				float4x4 _DepthClipToWorld[2];
				uint _EnvironmentRenderingLayers;
			CBUFFER_END

			struct Attributes
			{
				uint vertexID : SV_VertexID;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float3 viewRayWS : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			struct FragmentOutput
			{
				half4 color : SV_Target;
				float depth : SV_Depth;
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
				float4 nearWS = mul(UNITY_MATRIX_I_VP, output.positionCS);
				output.viewRayWS = nearWS.xyz - GetCameraPositionWS() * nearWS.w;
				return output;
			}

			bool SampleSurface(float3 rayPositionWS, int eye, out float2 depthUV, out float3 positionWS)
			{
				depthUV = 0;
				positionWS = 0;
				float4 depthClip = mul(_WorldToDepthClip[eye], float4(rayPositionWS, 1));
				if (!(depthClip.w > 0)) return false;

				depthUV = agDepthHCStoNDC(depthClip).xy;
				if (!all(depthUV >= 0) || !all(depthUV <= 1)) return false;

				float depth = agDepthSample(depthUV, eye, agBilinearClampSampler);
				if (!(depth > 0 && depth < 1)) return false;

				float4 worldH = mul(_DepthClipToWorld[eye], agDepthNDCtoHCS(float3(depthUV, depth)));
				positionWS = worldH.xyz / worldH.w;
				return true;
			}

			half3 EvaluateLight(Light light, half3 normalWS)
			{
				#if defined(_LIGHT_LAYERS)
				if (!IsMatchingLightLayer(light.layerMask, _EnvironmentRenderingLayers)) return 0;
				#endif

				return LightingLambert(light.color * (light.distanceAttenuation * light.shadowAttenuation),
					light.direction, normalWS);
			}

			FragmentOutput Frag(Varyings input)
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				FragmentOutput output = (FragmentOutput)0;

				#if !USE_CLUSTER_LIGHT_LOOP
				discard;
				#else
				int eye = unity_StereoEyeIndex;
				float3 cameraWS = GetCameraPositionWS();
				float3 viewRayWS = input.viewRayWS;
				#if defined(SUPPORTS_FOVEATED_RENDERING_NON_UNIFORM_RASTER)
				UNITY_BRANCH if (_FOVEATED_RENDERING_NON_UNIFORM_RASTER)
				{
					float2 renderUV = input.positionCS.xy / GetScaledScreenParams().xy;
					float2 linearUV = FoveatedRemapNonUniformToLinear(renderUV);
					viewRayWS = ComputeWorldSpacePosition(linearUV, UNITY_NEAR_CLIP_VALUE, UNITY_MATRIX_I_VP) - cameraWS;
				}
				#endif
				float3 rayDirectionWS = normalize(viewRayWS);

				float2 depthUV;
				float3 surfaceWS;
				if (!SampleSurface(cameraWS + rayDirectionWS, eye, depthUV, surfaceWS)) discard;

				float rayDistance = dot(surfaceWS - cameraWS, rayDirectionWS);
				surfaceWS = cameraWS + rayDirectionWS * rayDistance;
				float viewDepth = dot(GetViewForwardDir(), surfaceWS - cameraWS);
				if (!(viewDepth >= _ProjectionParams.y && viewDepth < _ProjectionParams.z)) discard;

				InputData inputData = (InputData)0;
				inputData.positionWS = surfaceWS;
				float3 normalWS = agDepthNormalSample(depthUV, eye, agBilinearClampSampler).xyz;
				if (!(dot(normalWS, normalWS) > 0)) discard;
				inputData.normalWS = NormalizeNormalPerPixel(normalWS);
				inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

				half3 lighting = 0;
				half4 shadowMask = half4(1, 1, 1, 1);
				uint pixelLightCount = GetAdditionalLightsCount();
				LIGHT_LOOP_BEGIN(pixelLightCount)
					Light light = GetAdditionalLight(lightIndex, surfaceWS, shadowMask);
					lighting += EvaluateLight(light, inputData.normalWS);
				LIGHT_LOOP_END

				float4 surfaceCS = TransformWorldToHClip(surfaceWS);
				output.depth = surfaceCS.z / surfaceCS.w;
				#if !UNITY_REVERSED_Z
				output.depth = output.depth * 0.5 + 0.5;
				#endif
				
				output.color = half4(lighting, 0);
				#endif
				return output;
			}
			ENDHLSL
		}
	}
}
