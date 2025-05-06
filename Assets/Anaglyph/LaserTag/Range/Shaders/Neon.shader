Shader "Lasertag/Range/Wall"
{
	SubShader
	{
		Tags { "Queue" = "Transparent" "RenderPipeline" = "UniversalRenderPipeline"  }

		Blend One One

		Pass
		{


			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"
			#include "conversion.hlsl"
			#include "config.hlsl"

			#pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION

			struct Attributes
			{
				float4 positionOS   : POSITION;                 
				float2 uv           : TEXCOORD0; 

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS  : SV_POSITION;
				float3 positionWS   : TEXCOORD0;
				float2 uv           : TEXCOORD1;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};            

			Varyings vert(Attributes IN)
			{
				Varyings OUT;

				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

				OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
				OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
				OUT.uv = IN.uv;

				return OUT;
			}
			
			half4 frag(Varyings IN) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

				half4 color = half4(1, 1, 1, 1);
				color = 1 - distance(IN.uv.y, 0.5) * 2;
				color *= color;


				float3 cameraPos = GetCameraPositionWS();
				
				// float3 viewDir = normalize(IN.positionWS - cameraPos);

				// float t = saturate(viewDir.y * 0.5 + 0.5); // [-1,1] to [0,1]
				// half4 fogColor = lerp(fogColorBottom, fogColorTop, t); 

				float cameraDist = distance(IN.positionWS, cameraPos);

				// color = lerp(color, fogColor, saturate(cameraDist / fogMaxDist));

				color *= (1 - saturate(cameraDist / fogMaxDist));

				META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY_WORLDPOS(IN.positionWS, color, 0.0);

				return color;
			}
			ENDHLSL
		}
	}
}