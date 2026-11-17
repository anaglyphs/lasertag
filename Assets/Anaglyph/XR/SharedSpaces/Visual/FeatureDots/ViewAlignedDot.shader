Shader "Anaglyph/XR/View Aligned Dot"
{
	Properties
	{
		_MainTex ("Dot", 2D) = "white" {}
		_Color ("Color", Color) = (1, 1, 1, 1)
		_AngularSize ("Angular Radius (radians)", Float) = 0.01
		_MinSize ("Min Radius (meters)", Float) = 0.008
		_SurfacePull ("Pull Toward Viewer (meters, per meter of distance)", Vector) = (0.03, 0.03, 0, 0)
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
			ZTest LEqual
			ZWrite Off
			Blend One OneMinusSrcAlpha

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION; // the dot's center
				half4 color : COLOR;
				float2 uv : TEXCOORD0; // the quad corner, 0..1
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				half4 color : COLOR;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			TEXTURE2D(_MainTex);
			SAMPLER(sampler_MainTex);

			CBUFFER_START(UnityPerMaterial)
				float4 _MainTex_ST;
				half4 _Color;
				float _AngularSize;
				float _MinSize;
				float4 _SurfacePull;
			CBUFFER_END

			Varyings vert(Attributes IN)
			{
				Varyings OUT;
				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

				float3 centerVS = TransformWorldToView(TransformObjectToWorld(IN.positionOS.xyz));
				const float distance = -centerVS.z;
				const float size = max(_MinSize, _AngularSize * distance);
				// dots sit on the surface they mark; pulled in front so it does not hide them
				centerVS.z += min(_SurfacePull.x + _SurfacePull.y * distance, 0.5 * distance);
				centerVS.xy += (IN.uv * 2 - 1) * size;

				// a hidden dot collapses to nothing
				OUT.positionHCS = IN.color.a > 0 ? TransformWViewToHClip(centerVS) : 0;
				OUT.uv = IN.uv;
				OUT.color = IN.color;
				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
				const half4 dot = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
				const half alpha = dot.a * _Color.a * IN.color.a;
				return half4(dot.rgb * _Color.rgb * IN.color.rgb * alpha, alpha);
			}

			ENDHLSL
		}
	}
}
