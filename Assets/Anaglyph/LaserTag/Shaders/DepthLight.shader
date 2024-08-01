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
		ZTest Always
		ZWrite Off
		Cull Front

		Pass {
			HLSLPROGRAM 
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
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
				float4 positionHCS : SV_POSITION;
				float4 positionHCSTexCoord : TEXCOORD0;

				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;

				UNITY_SETUP_INSTANCE_ID(IN);
				// UNITY_INITIALIZE_OUTPUT(IN, OUT);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

				OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
				OUT.positionHCSTexCoord = OUT.positionHCS;

				return OUT;
			}

			float sqr(float x)
			{
				return x * x;
			}


			float attenuate_cusp(float distance, float radius,
				float max_intensity, float falloff)
			{
				float s = distance / radius;

				if (s >= 1.0)
					return 0.0;

				float s2 = sqr(s);

				return max_intensity * sqr(1 - s2) / (1 + falloff * s);
			}

			half4 frag(Varyings IN) : SV_Target 
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
			   
				const float2 uv = (IN.positionHCSTexCoord.xy / IN.positionHCSTexCoord.w) * 0.5 + float2(0.5, 0.5);
				
				// const float2 uv = (depthSpace.xy / depthSpace.w + 1.0f) * 0.5f;
				float deviceDepth = SampleEnvDepthDK(uv, 0);

				float3 lightPos = mul(unity_ObjectToWorld, float4(0,0,0,1)).xyz;
				float3 worldPos = ComputeWorldSpacePositionDK(uv, deviceDepth, 0);
				float3 worldNorm = ComputeWorldSpaceNormalDK(uv, worldPos, 0);

				float3 diff = lightPos - worldPos;
				
				float3 lightDir = normalize(diff);

				half4 diffuse = _Color;
				diffuse *= max(dot(worldNorm, lightDir), 0.0);

				float dist = length(diff);
				float rad = length(mul(unity_ObjectToWorld, float4(1,0,0,0))) / 2;
				diffuse *= sqr(max(0, 1 - dist / rad));
				return diffuse;

				return half4(lightPos, 1);
			}

			ENDHLSL
		}
	}
}