Shader "Anaglyph/Indicator Line"
{
	Properties
	{
		_BaseColor ("Color", Color) = (1,1,1,1)
		_LineWidth ("Width in pixels", Float) = 2
		[Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
	}
	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" "RenderType" = "Transparent" }
		Pass
		{
			Tags { "LightMode" = "SRPDefaultUnlit" }
			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off
			ZTest [_ZTest]
			Cull Off
			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma multi_compile_instancing
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			CBUFFER_START(UnityPerMaterial)
				half4 _BaseColor;
				float _LineWidth;
				float4 _ViewportSize;
			CBUFFER_END
			struct Attributes
			{
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float edge : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
			};
			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				float4 start = TransformObjectToHClip(float3(-.5, 0, 0));
				float4 end = TransformObjectToHClip(float3(.5, 0, 0));
				float2 direction = (end.xy / end.w - start.xy / start.w) * _ViewportSize.xy;
				float2 perpendicular = float2(-direction.y, direction.x) * rsqrt(max(dot(direction, direction), 1e-10));
				output.positionCS = lerp(start, end, input.uv.x);
				output.edge = input.uv.y * 2 - 1;
				output.positionCS.xy += perpendicular * output.edge * (_LineWidth + 1) / _ViewportSize.xy * output.positionCS.w;
				return output;
			}
			half4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				half coverage = saturate((1 - abs(input.edge)) * (_LineWidth + 1) * .5);
				return half4(_BaseColor.rgb, _BaseColor.a * coverage);
			}
			ENDHLSL
		}
	}
}
