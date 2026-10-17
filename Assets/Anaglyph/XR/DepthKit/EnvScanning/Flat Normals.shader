// Triangle wireframe over a flat-normal fill, for the debug environment mesh. Draws
// meshes built by WireframeMesh: each triangle's corners carry (1,0,0), (0,1,0) and
// (0,0,1) in the vertex color, so a pixel's distance to the nearest edge is the
// smallest barycentric over its screen-space rate of change. No geometry shader,
// so it stays cheap on Quest's tiled GPU.
Shader "Anaglyph/Debug/Flat Normals"
{
	Properties
	{
		_WireWidth ("Wire Width (px)", Range(0.5, 4)) = 1.25
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue"="Geometry"
		}

		Pass
		{
			Tags
			{
				"LightMode" = "UniversalForward"
			}

			Cull Back
			ZWrite True

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Assets/Anaglyph/XR/DepthKit/EnvScanning/Wireframe.hlsl"

			CBUFFER_START(UnityPerMaterial)
				half _WireWidth;
			CBUFFER_END

			// Passthrough compositor alpha of the fill (premultiplied), set globally by
			// EnvMesher.DebugMeshOpacity. Wires are always solid. The pass stays opaque:
			// no blending, ZWrite on, so hidden wires stay hidden.
			half agDebugMeshOpacity;

			struct Attributes
			{
				float4 positionOS : POSITION;
				half4 barycentric : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				half3 barycentric : TEXCOORD1;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;

				UNITY_SETUP_INSTANCE_ID(IN);
				// writes the SV_RenderTargetArrayIndex that UNITY_VERTEX_OUTPUT_STEREO
				// declares; d3d11 rejects the shader if it is left unwritten
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

				OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
				OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
				OUT.barycentric = IN.barycentric.rgb;

				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

				float3 dp1 = ddx(IN.positionWS);
				float3 dp2 = ddy(IN.positionWS);
				half3 color = normalize(cross(dp2, dp1)) * 0.5 + 0.5;

				return WireframeComposite(color, color, IN.barycentric, _WireWidth, agDebugMeshOpacity);
			}
			ENDHLSL
		}
	}
}
