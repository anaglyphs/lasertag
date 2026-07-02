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
			#include "../DepthKit.hlsl"

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
				// clip xy emitted with w=1, so the interpolated value IS this
				// fragment's camera NDC — no texture size or y-flip handling needed
				float2 ndc : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float2 pos = float2((input.vertexID << 1) & 2, input.vertexID & 2);
				output.positionCS = float4(pos * 2.0 - 1.0, 0.5, 1.0);
				output.ndc = output.positionCS.xy;
				return output;
			}

			float frag(Varyings input) : SV_Depth
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				int eye = unity_StereoEyeIndex;

				// world-space ray through this texel
				float4 nearH = mul(UNITY_MATRIX_I_VP, float4(input.ndc, UNITY_NEAR_CLIP_VALUE, 1.0));
				float3 nearWorld = nearH.xyz / nearH.w;
				float3 camPos = UNITY_MATRIX_I_V._m03_m13_m23;
				float3 dir = normalize(nearWorld - camPos);

				// the render and capture poses differ only by capture latency, so a
				// rotation-only mapping of the ray direction is a near-exact first
				// guess for the sensor uv; one refinement at the sampled distance
				// makes the residual translation (parallax) error second-order
				float3 ndcD = agDepthWorldToNDC(camPos + dir, eye);
				float3 hit = agDepthNDCtoWorld(
					float3(ndcD.xy, agDepthSample(ndcD.xy, eye, agBilinearClampSampler)), eye);

				float t = dot(hit - camPos, dir);
				ndcD = agDepthWorldToNDC(camPos + dir * max(t, 0.05), eye);

				// outside the sensor frustum: leave the cleared far value
				if (any(ndcD.xy != saturate(ndcD.xy)))
					discard;

				hit = agDepthNDCtoWorld(
					float3(ndcD.xy, agDepthSample(ndcD.xy, eye, agBilinearClampSampler)), eye);
				t = dot(hit - camPos, dir);

				// beyond the cutoff (including sensor far plane / no data): don't composite
				if (t > _MaxDistance || t <= 0.0)
					discard;

				float4 clip = TransformWorldToHClip(camPos + dir * t);
				float depth = clip.z / clip.w;

				#if !UNITY_REVERSED_Z
				depth = depth * 0.5 + 0.5; // OpenGL clip z [-1,1] -> depth [0,1]
				#endif

				return depth;
			}
			ENDHLSL
		}
	}
}
