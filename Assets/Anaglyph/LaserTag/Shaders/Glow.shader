Shader "Anaglyph/Lasertag/Glow"
{
	Properties
	{
		_Color ("Color", Color) = (1, 0, 0, 1)
		_Intensity ("Intensity", Float) = 1
		_Whiteness ("Whiteness", Range(0, 1)) = 0.1
		_Falloff ("Falloff", Range(0.1, 8)) = 1
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent"
		}

		Pass
		{
			Tags
			{
				"LightMode" = "UniversalForward"
			}

			Cull Off
			ZWrite Off
			Blend One OneMinusSrcAlpha

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include_with_pragmas "Assets/Anaglyph/XR/DepthKit/Occlusion/DepthKitEnvironmentOcclusion.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				float2 uv : TEXCOORD1;
				
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;

				// also sets unity_StereoEyeIndex under single-pass instanced, which
				// _WorldSpaceCameraPos indexes, so it must run before reading O
				UNITY_SETUP_INSTANCE_ID(IN);
				// writes the SV_RenderTargetArrayIndex that UNITY_VERTEX_OUTPUT_STEREO
				// declares; d3d11 rejects the shader if it is left unwritten
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
				
				OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
				OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
				OUT.uv = IN.uv;
				
				return OUT;
			}

			CBUFFER_START(UnityPerMaterial)
				half4 _Color;
				float _Intensity;
				half _Whiteness;
				float _Falloff;
			CBUFFER_END

			half4 frag(Varyings IN) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

				// glow gradient along the streak: full at uv.x = 0, fully
				// fallen off at uv.x = 1
				const float fall = pow(saturate(1 - IN.uv.x), _Falloff);
				float g = fall * _Intensity;

				// occlude against the environment scan so the streak doesn't
				// shine through walls
				float occlusion;
				agEnvOcclusion_float(IN.positionWS, occlusion);
				g *= occlusion;

				// exposure tonemap: the emission leaks a little into every
				// channel, so once the dominant channels clip the leak keeps
				// rising and the bright end burns to white while the dim tail
				// stays saturated
				const half3 spectrum = lerp(_Color.rgb, 1, _Whiteness);
				const half3 col = 1 - exp(-g * spectrum);

				return half4(col, 0);
			}
			
			ENDHLSL
		}
	}
}
