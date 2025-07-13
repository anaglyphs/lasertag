Shader "Custom/OpaqueAlphaPassthrough"
{
	Properties
	{
		_Color ("Color", Color) = (1, 1, 1, 1)
		_FadeStart ("Fade Start Distance", Float) = 1.0
		_FadeEnd ("Fade End Distance", Float) = 0.1
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Geometry-100" }
		ZWrite Off
		ZTest Always
		Cull Off

		Pass
		{
			Name "UnlitAlphaPass"
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"

			#pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv         : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv         : TEXCOORD0; 
				float3 positionWS : TEXCOORD1;


				UNITY_VERTEX_OUTPUT_STEREO 
			};

			float4 _Color;
			float _FadeStart;
			float _FadeEnd;

			Varyings vert (Attributes v)
			{
				Varyings o;
				float4 positionWS = mul(GetObjectToWorldMatrix(), v.positionOS);
				o.positionWS = positionWS.xyz;
				o.positionCS = TransformWorldToHClip(o.positionWS);
				o.uv = v.uv;

				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				return o;
			}

			float4 frag (Varyings i) : SV_Target
			{
				float3 camPos = GetCameraPositionWS();
				float dist = distance(camPos, i.positionWS);
				
				float alpha = saturate((dist - _FadeStart) / (_FadeEnd - _FadeStart));

				alpha *= (((i.uv.x + i.uv.y) * 100) % 1) > 0.5;

				float4 color = _Color.rgba * alpha;
				META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY_WORLDPOS(i.positionWS, color, 0);
				return color;
			}
			ENDHLSL
		}
	}
}