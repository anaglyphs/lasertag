Shader "Custom/URPSimpleLit"
{
	Properties
	{
		_Color ("Color", Color) = (1,0,0,1)
	}
	SubShader
	{
		Tags { 
			"RenderPipeline" = "UniversalPipeline" 
			"IgnoreProjector" = "True" 
			"Queue" = "Transparent" 
			"RenderType" = "Transparent"
		}
		LOD 100
		Blend SrcAlpha OneMinusSrcAlpha

		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }


			HLSLPROGRAM
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
			#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
			#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile _ _SHADOWS_SOFT
			#pragma multi_compile _ _MIXED_LIGHTING_SUBTRACTIVE

			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
			#pragma multi_compile _ LIGHTMAP_ON

			#pragma multi_compile_instancing
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl" 
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.meta.xr.depthapi.urp/Shaders/EnvironmentOcclusionURP.hlsl"

			#pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION 

			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
			}; 

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float3 normal : TEXCOORD0;
				float3 worldPos : TEXCOORD1;

				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			v2f vert (appdata v)
			{
				v2f o;

				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				o.vertex = TransformObjectToHClip(v.vertex);
				o.worldPos = TransformObjectToWorld(v.vertex);
				o.normal = v.normal;
				return o;
			}

			float4 _Color;

			float3 Lambert(float3 lightColor, float3 lightDir, float3 normal)
			{
				float NdotL = saturate(dot(normal, lightDir));
				return lightColor * NdotL;
			}

			float4 frag (v2f i) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

				float4 color = _Color;

				float3 lightPos = _MainLightPosition.xyz;
				float3 lightCol = Lambert(_MainLightColor * unity_LightData.z, lightPos, i.normal);

				uint lightsCount = GetAdditionalLightsCount();
				for (int j = 0; j < lightsCount; j++)
				{
					Light light = GetAdditionalLight(j, i.worldPos);
					lightCol += Lambert(light.color * (light.distanceAttenuation * light.shadowAttenuation), light.direction, i.normal);
				}

				color.rgb += lightCol;

				float occlusionValue = META_DEPTH_GET_OCCLUSION_VALUE_WORLDPOS(i.worldPos, 0);
				
				color.a *= (occlusionValue > 0.01);

				return color;
			}
			ENDHLSL
		}
	}
}