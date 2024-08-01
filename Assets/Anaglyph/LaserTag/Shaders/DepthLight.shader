Shader "Lasertag/DepthLight"
{
	Properties
	{
		_Color ("Color", Color) = (1,1,1,1)

	}

	SubShader
	{
		Tags { "RenderType"="Opaque" }
		LOD 200
		// Blend One One // Additive
		// ZTest Always

		Pass {
			HLSLPROGRAM 
			#pragma vertex vert
			#pragma fragment frag

			#include "Assets/Anaglyph/XRTemplate/DepthCast/DepthKit.hlsl"

			CBUFFER_START(UnityPerMaterial)
				half4 _Color;            
			CBUFFER_END 

			struct Attributes
			{
				float4 positionOS   : POSITION;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS  : SV_POSITION;
				float3 positionWS  : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;
				OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
				OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);

				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target 
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
			   
				const float4 depthSpace =
					mul(EnvironmentDepth3DOFReprojectionMatrices[unity_StereoEyeIndex], 
					float4(IN.positionWS, 1.0));
				
				const float2 uv = (depthSpace.xy / depthSpace.w + 1.0f) * 0.5f;
				float deviceDepth = SampleEnvDepthDK(uv, unity_StereoEyeIndex);

				float3 lightPos = unity_ObjectToWorld._m30_m31_m32;
				float3 worldPos = ComputeWorldSpacePositionDK(uv, deviceDepth, unity_StereoEyeIndex);
				float3 worldNorm = ComputeWorldSpaceNormalDK(uv, worldPos, unity_StereoEyeIndex);

				float3 lightDir = normalize(lightPos - worldPos);
				half4 diffuse = max(dot(worldNorm, lightDir), 0.0) * _Color;

				return half4(worldNorm, 1);
			}

			ENDHLSL
		}
	}
}