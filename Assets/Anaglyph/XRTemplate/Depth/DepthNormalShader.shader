Shader "CustomRenderTexture/DepthNormals"
{
	Properties
	{
	}

	SubShader
	{
		Blend One Zero

		Pass
		{
			Name "DepthNormals"

			HLSLPROGRAM
			#include "UnityCustomRenderTexture.cginc"
			#include "DepthKit.hlsl"
			#pragma vertex CustomRenderTextureVertexShader
			#pragma fragment frag
			#pragma target 3.0

			float4 frag(v2f_customrendertexture IN) : SV_Target
			{
				float2 uv = IN.globalTexcoord.xy;

				int eye = IN.globalTexcoord.z;

				float3 lightPos = mul(unity_ObjectToWorld, float4(0,0,0,1)).xyz;
				float3 depthWorld = agDepthNDCtoWorld(float3(uv, agDepthSample(uv, eye)), eye);

				uv = IN.globalTexcoord.xy + float2(0.005, 0.0);
				float3 depthWorldH = agDepthNDCtoWorld(float3(uv, agDepthSample(uv, eye)), eye);

				uv = IN.globalTexcoord.xy + float2(0.0, 0.005);
				float3 depthWorldV = agDepthNDCtoWorld(float3(uv, agDepthSample(uv, eye)), eye);
	
				const float3 hDeriv = depthWorldH - depthWorld;
				const float3 vDeriv = depthWorldV - depthWorld;
	
				float3 worldNorm = -normalize(cross(hDeriv, vDeriv));

				return float4(worldNorm, 1);
			}
			ENDHLSL
		}
	}
}