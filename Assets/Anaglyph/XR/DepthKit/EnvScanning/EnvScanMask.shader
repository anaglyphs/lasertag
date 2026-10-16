Shader "Hidden/Anaglyph/EnvScanMask"
{
	// renders invisible mask renderers from the depth sensor's viewpoint.
	// writes each texel's nearest distance from the depth eye, so the scanner
	// can refuse to integrate anything at a greater distance
	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" }

		Pass
		{
			Name "EnvScanMask"

			ZTest LEqual
			ZWrite On
			Cull Back

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			// set by EnvScanner. the view projection is passed explicitly rather than
			// through unity's camera matrices, which belong to whichever camera last rendered
			float4x4 _EnvMaskViewProj; // GPU-adjusted depth sensor view projection
			float3 _EnvMaskEyePos;

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float3 worldPos : TEXCOORD0;
			};

			Varyings vert(float3 positionOS : POSITION)
			{
				Varyings output;
				output.worldPos = TransformObjectToWorld(positionOS);
				output.positionCS = mul(_EnvMaskViewProj, float4(output.worldPos, 1));
				return output;
			}

			float frag(Varyings input) : SV_Target
			{
				return distance(input.worldPos, _EnvMaskEyePos);
			}
			ENDHLSL
		}
	}
}
