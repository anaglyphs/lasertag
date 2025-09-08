Shader "Lasertag/Boundary"
{
	Properties
	{
		_Color ("Color", Color) = (1, 1, 1, 1)
		_FadeStart ("Fade Start Distance", Float) = 1.0
		_FadeEnd ("Fade End Distance", Float) = 0.1
		_Scale ("Stripe Scale", Float) = 100
		_Slope ("Stripe Slope", Float) = 1
		_XROrigin ("XR Origin", Vector) = (0, 0, 0)
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

			// #define PI 3.1415926538

			struct Attributes
			{
				float4 positionOS : POSITION;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD1;

				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO 
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _Color;
			float _FadeStart;
			float _FadeEnd;
			float _Scale;
			float _Slope;
			float3 _XROrigin;
			CBUFFER_END

			Varyings vert (Attributes v)
			{
				Varyings o;

				UNITY_SETUP_INSTANCE_ID(v);	
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				float4 positionWS = mul(GetObjectToWorldMatrix(), v.positionOS);
				o.positionWS = positionWS.xyz;
				o.positionCS = TransformWorldToHClip(o.positionWS);

				return o;
			}

			float4 frag (Varyings i) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

				float3 camPos = GetCameraPositionWS();
				float dist = distance(camPos, i.positionWS);
				
				float alpha = saturate((dist - _FadeStart) / (_FadeEnd - _FadeStart));

				float2 uv = float2(0, 0);
				float angle = atan2(i.positionWS.x - _XROrigin.x, i.positionWS.z - _XROrigin.z);

				uv.x = 1 + (angle / (PI * 2));
				uv.y = i.positionWS.y;

				alpha *= (((uv.x * _Slope + uv.y) * _Scale) % 1.0) > 0.5;

				float4 color = _Color.rgba * alpha;
				META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY_WORLDPOS(i.positionWS, color, 0);
				return color;
			}
			ENDHLSL
		}
	}
}