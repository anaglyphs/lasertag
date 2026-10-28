Shader "Anaglyph/AR Occlusion/Raw Depth Occlusion Prime"
{
	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" }

		Pass
		{
			Name "RawDepthPrime"

			ZTest Always
			ZWrite On
			Cull Off
			ColorMask 0

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "DepthKitEnvironmentOcclusion.hlsl"
			#include "../ViewQuad.hlsl"

			// sensor depth is composited out to this distance in meters
			float _MaxDistance;

			struct Attributes
			{
				uint vertexID : SV_VertexID;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				// rasterized through agOcclusionVP like the meshes drawn into this texture
				output.positionWS = agViewQuadWorld(input.vertexID);
				output.positionCS = mul(agOcclusionVP[unity_StereoEyeIndex], float4(output.positionWS, 1));
				return output;
			}

			float frag(Varyings input) : SV_Depth
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				int eye = unity_StereoEyeIndex;

				// world-space ray through this texel
				float3 camPos = UNITY_MATRIX_I_V._m03_m13_m23;
				float3 dir = normalize(input.positionWS - camPos);

				// the depth map comes with its own capture pose, which the system does
				// not warp to the render pose, so the surface on this ray has to be found
				// in the depth view. The poses differ only by head motion over the capture
				// latency, so guessing the surface 1 m out lands near the right texel;
				// one refinement at the sampled distance leaves a second-order error
				float3 ndcD = agDepthWorldToNDC(camPos + dir, eye);
				float3 hit = agDepthNDCtoWorld(
					float3(ndcD.xy, agDepthSample(ndcD.xy, eye, agBilinearClampSampler)), eye);

				float t = dot(hit - camPos, dir);
				ndcD = agDepthWorldToNDC(camPos + dir * max(t, 0.05), eye);

				// outside the depth frustum: leave the cleared far value
				if (any(ndcD.xy != saturate(ndcD.xy)))
					discard;

				hit = agDepthNDCtoWorld(
					float3(ndcD.xy, agDepthSample(ndcD.xy, eye, agBilinearClampSampler)), eye);
				t = dot(hit - camPos, dir);

				// beyond the cutoff (including unknown depth, stored far): don't composite
				if (t > _MaxDistance || t <= 0.0)
					discard;

				return agOcclusionUVDepth(camPos + dir * t, eye).z;
			}
			ENDHLSL
		}

		// Pass 1: copies agOcclusionTex into the camera depth, pushed back by _PrimeMargin,
		// so opaques fully behind real surfaces fail the early depth test instead of being
		// shaded to black. Takes the farthest of the 4 texels the occluded shaders' bilinear
		// compare reads, so it only rejects fragments that compare would zero anyway
		Pass
		{
			Name "CameraDepthPrime"

			ZTest LEqual
			ZWrite On
			Cull Off
			ColorMask 0

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "DepthKitEnvironmentOcclusion.hlsl"
			#include "../ViewQuad.hlsl"

			// meters added to the environment's eye depth before it is written
			float _PrimeMargin;

			struct Attributes
			{
				uint vertexID : SV_VertexID;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				output.positionWS = agViewQuadWorld(input.vertexID);
				output.positionCS = TransformWorldToHClip(output.positionWS);
				return output;
			}

			float frag(Varyings input) : SV_Depth
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				// the same uv OccludedLitForwardPass projects any point on this pixel's ray to
				float2 uv = agOcclusionUVDepth(input.positionWS, unity_StereoEyeIndex).xy;
				float4 texels = agOcclusionTex.GatherRed(sampler_PointClamp, float3(uv, unity_StereoEyeIndex));

				#if UNITY_REVERSED_Z
				float device = min(min(texels.x, texels.y), min(texels.z, texels.w));
				#else
				float device = max(max(texels.x, texels.y), max(texels.z, texels.w));
				#endif

				float eyeDepth = LinearEyeDepth(device, _ZBufferParams) + _PrimeMargin;
				return saturate((rcp(eyeDepth) - _ZBufferParams.w) / _ZBufferParams.z);
			}
			ENDHLSL
		}
	}
}
