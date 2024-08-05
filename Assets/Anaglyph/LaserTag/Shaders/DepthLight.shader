Shader "Lasertag/DepthLight"
{
	Properties
	{
		_Color ("Color", Color) = (1,1,1,1)
		_Intensity ("Intensity", Float) = 1
	}

	SubShader
	{
		Tags { "RenderType"="Opaque" }
		LOD 200
		ZTest Always
		ZWrite Off
		Cull Front
		Blend One OneMinusSrcAlpha

		Pass {
			HLSLPROGRAM 
			#pragma vertex vert
			#pragma fragment frag 

			#include "Assets/Anaglyph/XRTemplate/Depth/DepthKit.hlsl" 
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
				half4 _Color;
				half _Intensity;
			CBUFFER_END 

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

			struct Attributes
			{
				float4 positionOS   : POSITION;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float4 positionHCSTexCoord : TEXCOORD0;
				float3 positionWS : TEXCOORD1;

				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;

				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

				OUT.positionHCS = TransformObjectToHClip(IN.positionOS);
				OUT.positionHCSTexCoord = OUT.positionHCS;
				OUT.positionWS = TransformObjectToWorld(IN.positionOS);

				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target 
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
				
				const int slice = unity_StereoEyeIndex;

				const float2 uv = (IN.positionHCSTexCoord.xy / IN.positionHCSTexCoord.w) * 0.5 + float2(0.5, 0.5);
				
				const float deviceDepth = SampleDepthDK(uv, slice);

				float3 lightPos = mul(unity_ObjectToWorld, float4(0,0,0,1)).xyz;
				float3 worldPos = ComputeWorldSpacePositionDK(uv, deviceDepth, slice);
				float3 worldNorm = ComputeWorldSpaceNormalDK(uv, worldPos, slice);

				float3 diff = lightPos - worldPos;
				
				float3 lightDir = normalize(diff);

				float dist = length(diff);
				float rad = length(mul(unity_ObjectToWorld, float4(1,0,0,0))) / 2;
				
				float intensity = max(dot(worldNorm, lightDir), 0.0) * sqr(max(0, 1 - dist / rad)) * _Intensity; 

				return float4(_Color.rgb * intensity, 0);
			}

			ENDHLSL
		}
	}
}