Shader "Anaglyph/Solid Vector Sprite"
{
	Properties
	{
		_Color ("Tint", Color) = (1,1,1,1)
	}
	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
		Pass
		{
			Tags { "LightMode" = "SRPDefaultUnlit" }
			Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
			ZWrite Off
			Cull Off
			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma multi_compile_instancing
			#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

			CBUFFER_START(UnityPerMaterial)
				half4 _Color;
			CBUFFER_END

			struct Attributes
			{
				float3 positionOS : POSITION;
				half4 color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				half4 color : COLOR;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				SetUpSpriteInstanceProperties();
				input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
				output.positionCS = TransformObjectToHClip(input.positionOS);
				output.color = input.color * _Color * unity_SpriteColor;
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				return input.color;
			}
			ENDHLSL
		}
	}
}
