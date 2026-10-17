// Tinted triangle wireframe for ghost copies of environment chunks, drawn from meshes
// built by WireframeMesh (see Wireframe.hlsl). Written and depth-tested like any
// opaque surface; the fill's premultiplied alpha (EnvMesher.DebugMeshOpacity) lets the
// passthrough composited beneath show through, like the debug chunk mesh. The scan
// meshes carry no normals, so a flat normal is derived per pixel for a little shading.
// URP, single-pass instanced stereo.
Shader "Anaglyph/Scan Registration Ghost"
{
	Properties
	{
		_Color ("Color", Color) = (1, 0.6, 0.2, 1)
		_WireWidth ("Wire Width (px)", Range(0.5, 4)) = 1.25
	}
	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "RenderType" = "Opaque" }
		Pass
		{
			Tags { "LightMode" = "UniversalForward" }
			Blend Off
			ZWrite On
			ZTest LEqual
			Cull Off
			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma multi_compile_instancing
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Assets/Anaglyph/XR/DepthKit/EnvScanning/Wireframe.hlsl"

			CBUFFER_START(UnityPerMaterial)
			float4 _Color;
			half _WireWidth;
			CBUFFER_END

			half agDebugMeshOpacity;

			struct Attributes
			{
				float3 positionOS : POSITION;
				half4 barycentric : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				half3 barycentric : TEXCOORD1;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				output.positionWS = TransformObjectToWorld(input.positionOS);
				output.positionCS = TransformWorldToHClip(output.positionWS);
				output.barycentric = input.barycentric.rgb;
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				float3 n = normalize(cross(ddy(input.positionWS), ddx(input.positionWS)));
				half shade = 0.65 + 0.35 * saturate(n.y * 0.5 + 0.5);
				half3 tint = _Color.rgb * shade;
				return WireframeComposite(tint, _Color.rgb, input.barycentric, _WireWidth, agDebugMeshOpacity);
			}
			ENDHLSL
		}
	}
}
