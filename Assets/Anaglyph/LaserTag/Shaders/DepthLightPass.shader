Shader "Anaglyph/Lasertag/DepthLight"
{
	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" }

		// Pass 0: lights one depth-map slice at depth resolution. Driven by an explicit
		// light index list, so it needs neither the cluster tiles nor the camera's matrices
		Pass
		{
			Name "DepthLightIrradiance"
			ZWrite Off
			ZTest Always
			Cull Off

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex Vert
			#pragma fragment Frag

			#define _RECEIVE_SHADOWS_OFF 1
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Assets/Anaglyph/XR/DepthKit/DepthKit.hlsl"

			float4x4 _DepthClipToWorld[2];
			float2 _DepthLightTexelSize;
			float _DepthLightEye;
			float _DepthLightCount;
			// additional-light array indices of the point and spot lights to evaluate
			float _DepthLightIndices[MAX_VISIBLE_LIGHTS];

			float4 Vert(uint vertexID : SV_VertexID) : SV_POSITION
			{
				return GetFullScreenTriangleVertexPosition(vertexID);
			}

			half4 Frag(float4 positionCS : SV_POSITION) : SV_Target
			{
				int eye = (int)_DepthLightEye;
				int2 texel = int2(positionCS.xy);

				float depth = agDepthTex.Load(int4(texel, eye, 0));
				if (!agDepthIsKnown(depth)) return 0;

				float3 normalWS = agDepthNormalTex.Load(int4(texel, eye, 0)).xyz;
				if (!(dot(normalWS, normalWS) > 0)) return 0;
				normalWS = normalize(normalWS);

				float2 depthUV = (texel + 0.5) * _DepthLightTexelSize;
				float4 worldH = mul(_DepthClipToWorld[eye], agDepthNDCtoHCS(float3(depthUV, depth)));
				float3 positionWS = worldH.xyz / worldH.w;

				half3 lighting = 0;
				uint lightCount = (uint)_DepthLightCount;
				[loop] for (uint i = 0; i < lightCount; i++)
				{
					Light light = GetAdditionalPerObjectLight((int)_DepthLightIndices[i], positionWS);
					lighting += LightingLambert(light.color * light.distanceAttenuation, light.direction, normalWS);
				}
				return half4(lighting, 1);
			}
			ENDHLSL
		}

		// Pass 1: adds the irradiance under everything already drawn, weighted by how much
		// passthrough each pixel still shows (1 - eye alpha). Coverage alpha already encodes
		// virtual objects in front, occluded ones (alpha = occlusion) and transparents, so
		// no depth test is needed
		Pass
		{
			Name "DepthLightComposite"
			ZWrite Off
			ZTest Always
			Cull Off
			Blend OneMinusDstAlpha One
			ColorMask RGB

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma multi_compile_instancing
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Assets/Anaglyph/XR/DepthKit/DepthKit.hlsl"
			#include "Assets/Anaglyph/XR/DepthKit/ViewQuad.hlsl"

			Texture2DArray<half4> _DepthLightIrradiance;
			float4x4 _WorldToDepthClip[2];

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

			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				float3 positionWS = agViewQuadWorld(input.vertexID);
				output.positionCS = TransformWorldToHClip(positionWS);
				output.viewRayWS = positionWS - GetCameraPositionWS();
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

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

				float4 depthClip = mul(_WorldToDepthClip[eye], float4(cameraWS + normalize(viewRayWS), 1));
				if (!(depthClip.w > 0)) discard;
				float2 depthUV = agDepthHCStoNDC(depthClip).xy;
				if (!all(depthUV >= 0) || !all(depthUV <= 1)) discard;

				half3 lighting = _DepthLightIrradiance.SampleLevel(agBilinearClampSampler, float3(depthUV, eye), 0).rgb;
				if (!any(lighting > 0)) discard;
				return half4(lighting, 0);
			}
			ENDHLSL
		}
	}
}
