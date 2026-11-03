Shader "Anaglyph/Lasertag/Laser Spark"
{
	Properties
	{
		_CoreRadius ("Core Radius (fraction of quad radius)", Float) = 0.05
		_Intensity ("Intensity", Float) = 200
		_Whiteness ("Whiteness", Range(0, 1)) = 0.005
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
				float _Intensity;
				half _Whiteness;
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

				// the laser's point kernel seen from the side: (eps² / (r² + eps²))^3/2.
				// Measured in the quad, so it follows particle size and stretch
				const float r2 = dot(IN.quadPos, IN.quadPos);
				const float eps2 = _CoreRadius * _CoreRadius;
				const float kernel = eps2 / (r2 + eps2);
				float g = kernel * sqrt(kernel);

				// fade to zero before the quad's edge so its outline never shows
				const float fade = saturate(1 - sqrt(r2));
				g *= fade * fade * _Intensity * IN.color.a;

				float occlusion;
				agEnvOcclusion_float(IN.positionWS, occlusion);
				g *= occlusion;

				return half4(LaserExposure(g, IN.color.rgb, _Whiteness), 0);
			}

			ENDHLSL
		}
	}
}
