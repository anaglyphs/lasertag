

Shader "Anaglyph/EnvironmentGrid" {
	Properties {
		_MainTex ("Texture", 2D) = "white" {}
		_Scale("Scale", Float) = 5
		_Darken("Darken", Range(0, 1)) = 0
	}

	SubShader {
		Pass {

			Tags {"Queue"="Opaque" "RenderType"="Opaque"}
			ZWrite On
			ZTest LEqual

			HLSLPROGRAM
			#pragma target 3.0 
			#pragma glsl
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Assets/Anaglyph/XRTemplate/Depth/Environment/Environment.hlsl"

			Texture2D<float2> _MainTex;
			SamplerState bilinearRepeat;

			float _Scale;
			float _Darken;

			struct Attributes
			{
				float4 positionOS   : POSITION;
				float2 uv           : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionHCS  : SV_POSITION;
				float2 uv           : TEXCOORD0;
				float3 positionOBJ     : TEXCOORD1;
			};

			Varyings vert(Attributes IN) 
			{
				Varyings OUT;

				float3 v = IN.positionOS;
				v.y = agdk_EnvHeightMap.SampleLevel(agdk_pointClampSampler, IN.uv, 0).r;

				OUT.positionHCS = TransformObjectToHClip(v);
				OUT.positionOBJ = v; 
				OUT.uv = IN.uv;
				return OUT;
			}

			float4 frag(Varyings IN) : SV_Target
			{
				float3 uvPosScaled = IN.positionOBJ * _Scale;
				uvPosScaled -= float3(0, 0.2, 0);

				float grid = _MainTex.Sample(bilinearRepeat, uvPosScaled.xy)
				           + _MainTex.Sample(bilinearRepeat, uvPosScaled.yz)
				           + _MainTex.Sample(bilinearRepeat, uvPosScaled.xz); 
				
				float2 heightMapVal = agdk_EnvHeightMap.Sample(bilinearRepeat, IN.uv, 0).rg; 

				float4 result;
				result.rgb = grid;
				result.a = 1;
				result *= saturate(grid + _Darken) * heightMapVal.g;

				return result;
			}
			ENDHLSL
		}
	}
}