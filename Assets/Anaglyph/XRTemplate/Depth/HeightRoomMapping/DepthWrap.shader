// This shader draws a texture on the mesh.
Shader "Anaglyph/DepthWrap"
{
	// The _BaseMap variable is visible in the Material's Inspector, as a field
	// called Base Map.
	Properties
	{
		[MainTexture]
		_DepthTex("Base Map", 2DArray) = "white"
		_Factor("Factor", Float) = 1
	}

	SubShader
	{
		Tags { "RenderType" = "Opaque" }

		ZWrite On
		ZTest LEqual
		Cull Back

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS  : SV_POSITION;
				float color : COLOR0;

				UNITY_VERTEX_OUTPUT_STEREO
			};

			TEXTURE2D_ARRAY_FLOAT(_DepthTex);
			SAMPLER(sampler_DepthTex);
			float _Factor;

			Varyings vert(Attributes IN)
			{

				float3 ogWorldPos = TransformObjectToWorld(IN.positionOS.xyz);

				float4 depthSpace = mul(_EnvironmentDepthReprojectionMatrices[0], float4(ogWorldPos, 1.0));
				float2 uvCoords = (depthSpace.xy / depthSpace.w + 1.0f) * 0.5f;
				float linearSceneDepth = (1.0f / ((depthSpace.z / depthSpace.w) + _EnvironmentDepthZBufferParams.y)) * _EnvironmentDepthZBufferParams.x;
				float inputDepthEye = SAMPLE_TEXTURE2D_ARRAY_LOD(_DepthTex, sampler_DepthTex, uvCoords, 0, 0);
				float inputDepthNdc = inputDepthEye * 2.0 - 1.0;
				float linearDepth = (1.0f / (inputDepthNdc + _EnvironmentDepthZBufferParams.y)) * _EnvironmentDepthZBufferParams.x;
				linearDepth = clamp(linearDepth, 0, 5);

				float3 depthPositionOS = IN.positionOS.xyz * linearDepth;

				Varyings OUT;

				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

				OUT.positionHCS = TransformObjectToHClip(depthPositionOS); 
				OUT.color = TransformObjectToWorld(depthPositionOS).y * _Factor;

				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target
			{
				return half4(IN.color, 0, 0, 1);
			}

			ENDHLSL
		}
	}
}