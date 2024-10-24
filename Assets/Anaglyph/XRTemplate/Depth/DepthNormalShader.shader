Shader "CustomRenderTexture/DepthNormals"
{
	Properties
	{
	}

	SubShader
	{
		Blend One Zero
		Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        ZWrite Off Cull Off

		Pass
		{
			Name "DepthNormals"

			HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
			#include "DepthKit.hlsl"
			#pragma vertex Vert
            #pragma fragment frag
			#pragma target 3.0

			int eye = 0;

			float4 frag(Varyings IN) : SV_Target
			{
				float2 uv = IN.texcoord.xy;

				float3 depthWorld = agDepthNDCtoWorld(float3(uv, agDepthSample(uv, eye)), eye);

				uv = IN.texcoord.xy + float2(0.005, 0.0);
				float3 depthWorldH = agDepthNDCtoWorld(float3(uv, agDepthSample(uv, eye)), eye);

				uv = IN.texcoord.xy + float2(0.0, 0.005);
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