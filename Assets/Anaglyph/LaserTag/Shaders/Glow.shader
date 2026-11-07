Shader "Anaglyph/Lasertag/Glow"
{
	Properties
	{
		_Color ("Color", Color) = (1, 0, 0, 1)
		_CoreRadius ("Core Radius (fraction of streak)", Range(0, 1)) = 0.05
		_GlowRadius ("Glow Radius (fraction of streak)", Range(0, 0.95)) = 0.9
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
			#include "Assets/Anaglyph/LaserTag/Weapons/Bullets/LaserExposure.hlsl"

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
				float _CoreRadius;
				float _GlowRadius;
			CBUFFER_END

			half4 frag(Varyings IN) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

				// glow gradient along the streak: brightest at uv.x = 0, fading
				// to zero by uv.x = 1 so the quad's end never shows
				const GlowShape shape = MakeGlowShape(_CoreRadius, _GlowRadius, 1, LineGlowProfilePower);
				float g = GlowProfile(IN.uv.x, shape) * GlowBoundFade(IN.uv.x, shape);

				// occlude against the environment scan so the streak doesn't
				// shine through walls
				float occlusion;
				agEnvOcclusion_float(IN.positionWS, occlusion);
				g *= occlusion;

				return half4(LaserExposure(g, shape, _Color.rgb), 0);
			}
			
			ENDHLSL
		}
	}
}
