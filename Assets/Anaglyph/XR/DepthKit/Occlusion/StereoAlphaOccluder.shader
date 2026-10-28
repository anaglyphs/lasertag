Shader "Anaglyph/DepthKit/StereoAlphaOccluder"
{
	Properties
	{
		_OcclusionColorMask ("Color Mask", Float) = 15
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "Opaque" "Queue"="Geometry-1" "RenderPipeline" = "UniversalPipeline"
		}
		ZWrite On
		ZTest LEqual
		Cull Off
		// does not work in simulation but does on AR headsets
		// with pre-multiplied compositing (i.e. meta quest) 
		ColorMask [_OcclusionColorMask]

		Pass
		{
			Tags
			{
				"LightMode" = "UniversalForward"
			}

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			// set on DepthKitEnvironmentOcclusionFeature's override material, which draws into agOcclusionTex
			#pragma multi_compile_local _ AG_OCCLUSION_MATRICES

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#if AG_OCCLUSION_MATRICES
			#include "DepthKitEnvironmentOcclusion.hlsl"
			#endif

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

			Varyings vert(Attributes IN)
			{
				Varyings OUT;

				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

				#if AG_OCCLUSION_MATRICES
				OUT.positionHCS = mul(agOcclusionVP[unity_StereoEyeIndex], float4(TransformObjectToWorld(IN.positionOS.xyz), 1));
				#else
				OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
				#endif

				return OUT;
			}

			half4 frag() : SV_Target
			{
				return 0;
			}
			ENDHLSL
		}
	}
}