Shader "Custom/GradientSkyboxCube"
{
	Properties
	{

	}

	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Background" }
		Cull Front

		Pass
		{
			Name "GradientPass"
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			
			#include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "config.hlsl"

			// DepthAPI Environment Occlusion
			#pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION

			struct Attributes
			{

				float4 positionOS : POSITION;

				UNITY_VERTEX_INPUT_INSTANCE_ID 
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float3 worldPos : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert (Attributes IN)
			{
				Varyings OUT;

				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

				float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
				OUT.worldPos = worldPos;
				OUT.positionHCS = TransformWorldToHClip(worldPos);
				return OUT;
			}

			half4 frag (Varyings IN) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

				float3 cameraPos = GetCameraPositionWS();
				float3 viewDir = normalize(IN.worldPos - cameraPos);

				float t = saturate(viewDir.y * 0.5 + 0.5); // [-1,1] to [0,1]
				half4 color = lerp(fogColorBottom, fogColorTop, t);

				META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY_WORLDPOS(IN.worldPos, color, 0.0);

				return color;
			}
			ENDHLSL
		}
	}
	FallBack "Hidden/InternalErrorShader"
}