Shader "Anaglyph/Lasertag/Sighting Crosshair"
{
	Properties
	{
		_Color ("Color", Color) = (1, 1, 0, 1)
		_Eye ("Eye (0 left, 1 right)", Float) = 1
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay"
		}

		Pass
		{
			Tags
			{
				"LightMode" = "UniversalForward"
			}

			Cull Off
			ZWrite Off
			ZTest Always
			Blend One Zero

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
				float4 _Color;
				float _Eye;
			CBUFFER_END

			Varyings vert(Attributes IN)
			{
				Varyings OUT;
				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
				OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);

				// one eye only: seen with both, it has no one direction to line a far spot up in
				#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
				if ((float)unity_StereoEyeIndex != _Eye) OUT.positionHCS = float4(2, 2, 2, 1);
				#endif

				return OUT;
			}

			float4 frag(Varyings IN) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
				return _Color;
			}
			ENDHLSL
		}
	}
}
