Shader "Anaglyph/DepthKit/Sim/R32toD16Blit"
{
	SubShader
	{
		Tags
		{
			"RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
		}

		HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		#include "DepthKit.hlsl"

		struct Varyings
		{
			float4 positionHCS : SV_POSITION;
			float2 uv : TEXCOORD0;
		};

		Varyings vert(uint vertexID : SV_VertexID)
		{
			Varyings OUT;
			OUT.positionHCS = GetFullScreenTriangleVertexPosition(vertexID);
			OUT.uv = GetFullScreenTriangleTexCoord(vertexID);
			return OUT;
		}
		ENDHLSL

		// mono linear depth (phone / AR Foundation simulator) -> one D16 eye slice
		Pass
		{
			ZWrite On
			ZTest Always
			Cull Off

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			Texture2D<float> rawDepth;
			// meters per sampled unit (ARCore's 16-bit millimeters sample as fractions of 65535)
			float _RawDepthScale;
			// a phone's depth image counts rows from the top
			float _FlipRows;

			float frag(Varyings IN) : SV_Depth
			{
				float2 uv = IN.uv;
				if (_FlipRows > 0.5) uv.y = 1.0 - uv.y;
				const float z = rawDepth.SampleLevel(agPointClampSampler, uv, 0).r * _RawDepthScale;

				// no measurement: 0 / negative / NaN / inf would map to the near plane or NaN
				if (!(z > 0.0 && z < 1e30)) return AG_DEPTH_UNKNOWN;

				const float near = agDepthZParams.x;
				const float far = agDepthZParams.y;
				float depth = (far / (far - near)) - (far * near) / ((far - near) * z);
				return min(depth, AG_DEPTH_UNKNOWN);
			}
			ENDHLSL
		}

		// stereo D16 headset depth -> one D16 eye slice, unknown (0) rewritten
		Pass
		{
			ZWrite On
			ZTest Always
			Cull Off

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			Texture2DArray<float> rawDepthArray;
			int _Slice;

			float frag(Varyings IN) : SV_Depth
			{
				// same size source and target: pixel coords need no uv flip handling
				const float d = rawDepthArray.Load(int4(IN.positionHCS.xy, _Slice, 0));
				return d > 0.0 ? d : AG_DEPTH_UNKNOWN;
			}
			ENDHLSL
		}
	}
}
