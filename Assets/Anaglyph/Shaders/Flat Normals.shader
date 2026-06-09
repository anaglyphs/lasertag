Shader "Anaglyph/Debug/Flat Normals"
{
	SubShader
	{
		Tags
		{
			"RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue"="Geometry"
		}

		Pass
		{
			Tags
			{
				"LightMode" = "UniversalForward"
			}

			Cull Off
			ZWrite True

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
				float3 positionWS : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;
				OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
				OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);

				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target
			{
				float3 dp1 = ddx(IN.positionWS);
				float3 dp2 = ddy(IN.positionWS);
				float3 normalOS = normalize(cross(dp2, dp1));

				return float4(normalOS * 0.5 + 0.5, 1.0);
			}
			ENDHLSL
		}
	}
}