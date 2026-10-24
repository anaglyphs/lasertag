Shader "Anaglyph/Debug/Flat Normals"
{
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

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			// Passthrough compositor alpha only (premultiplied), set globally by
			// EnvMesher.DebugMeshOpacity. The pass stays opaque: no blending, ZWrite on.
			// This does not make the surface see-through in-scene.
			half agDebugMeshOpacity;

			struct Attributes
			{
				float4 positionOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
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

				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

				float3 dp1 = ddx(IN.positionWS);
				float3 dp2 = ddy(IN.positionWS);
				float3 normalOS = normalize(cross(dp2, dp1));

				// Premultiplied alpha for the passthrough compositor
				return float4((normalOS * 0.5 + 0.5) * agDebugMeshOpacity, agDebugMeshOpacity);
			}
			ENDHLSL
		}
	}
}
