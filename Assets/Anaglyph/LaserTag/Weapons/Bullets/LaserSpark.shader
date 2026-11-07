Shader "Anaglyph/Lasertag/Laser Spark"
{
	Properties
	{
		_CoreRadius ("Core Radius (fraction of quad radius)", Range(0, 1)) = 0.15
		_GlowRadius ("Glow Radius (fraction of quad radius)", Range(0, 0.95)) = 0.9
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent"
		}

		Pass
		{
			Tags
			{
				"LightMode" = "UniversalForward"
			}

			Cull Off
			ZTest LEqual
			ZWrite Off
			Blend One OneMinusSrcAlpha

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include_with_pragmas "Assets/Anaglyph/XR/DepthKit/Occlusion/DepthKitEnvironmentOcclusion.hlsl"
			#include "LaserExposure.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION;
				half4 color : COLOR;
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				float2 quadPos : TEXCOORD1; // -1..1 across the quad
				half4 color : COLOR;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
				float _CoreRadius;
				float _GlowRadius;
			CBUFFER_END

			Varyings vert(Attributes IN)
			{
				Varyings OUT;

				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

				OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
				OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
				OUT.quadPos = IN.uv * 2 - 1;
				OUT.color = IN.color;

				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

				// measured in the quad, so it follows particle size and stretch
				const GlowShape shape = MakeGlowShape(_CoreRadius, _GlowRadius, 1, PointGlowProfilePower);
				const float r = length(IN.quadPos);
				float g = GlowProfile(r, shape);

				g *= GlowBoundFade(r, shape) * IN.color.a;

				float occlusion;
				agEnvOcclusion_float(IN.positionWS, occlusion);
				g *= occlusion;

				return half4(LaserExposure(g, shape, IN.color.rgb), 0);
			}

			ENDHLSL
		}
	}
}
