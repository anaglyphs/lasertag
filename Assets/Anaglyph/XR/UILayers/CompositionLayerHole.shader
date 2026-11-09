// Clears color and alpha to zero where nothing nearer has been drawn, so an
// underlay composition layer shows through the eye buffer.
Shader "Anaglyph/XR/Composition Layer Hole"
{
	SubShader
	{
		Tags { "RenderType"="Opaque" "Queue"="AlphaTest+1" "RenderPipeline"="UniversalPipeline" }

		Pass
		{
			Name "Hole"
			ZWrite On
			ZTest LEqual
			Cull Off
			Blend One Zero

			HLSLPROGRAM
			#pragma vertex Vertex
			#pragma fragment Fragment
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings Vertex(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				return output;
			}

			half4 Fragment(Varyings input) : SV_Target
			{
				return 0;
			}
			ENDHLSL
		}
	}
}
