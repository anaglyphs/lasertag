Shader "Anaglyph/Env Scan Grid"
{
	Properties
	{
		_LineColor ("Line Color", Color) = (0.3, 0.9, 1, 1)
		_FillColor ("Fill Color", Color) = (0.3, 0.9, 1, 1)
		_FillOpacity ("Fill Opacity", Range(0, 1)) = 0.05
		_GridSpacing ("Grid Spacing (m)", Float) = 0.1
		_LineWidthPixels ("Line Width (px)", Range(0.5, 4)) = 1.25
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

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
				half4 _LineColor;
				half4 _FillColor;
				half _FillOpacity;
				float _GridSpacing;
				half _LineWidthPixels;
			CBUFFER_END

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
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
				OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
				OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
				return OUT;
			}

			// 1 on a world-axis grid line, 0 between, antialiased over a pixel. Lines of the
			// axis a surface faces are dropped: a floor shows x and z lines, not y contours.
			half GridLines(float3 positionWS)
			{
				float3 faceNormal = normalize(cross(ddy(positionWS), ddx(positionWS)));
				float3 cell = positionWS / _GridSpacing;
				float3 cellsPerPixel = max(fwidth(cell), 1e-5);
				float3 pixelsToLine = abs(frac(cell + 0.5) - 0.5) / cellsPerPixel;
				float3 lines = 1.0 - saturate(pixelsToLine - _LineWidthPixels + 0.5);
				float3 axisAcrossSurface = saturate((1.0 - abs(faceNormal)) * 4.0);
				// lines denser than a few pixels apart shimmer, so they fade out with distance
				float3 resolvable = saturate(2.0 - cellsPerPixel * 8.0);
				return max(lines.x * axisAcrossSurface.x * resolvable.x,
					max(lines.y * axisAcrossSurface.y * resolvable.y,
						lines.z * axisAcrossSurface.z * resolvable.z));
			}

			half4 frag(Varyings IN) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

				half gridLine = GridLines(IN.positionWS) * _LineColor.a;

				// premultiplied for the passthrough compositor: faint fill, solid lines over it
				half3 rgb = lerp(_FillColor.rgb * _FillOpacity, _LineColor.rgb, gridLine);
				half alpha = lerp(_FillOpacity, 1.0, gridLine);
				return half4(rgb, alpha);
			}
			ENDHLSL
		}
	}
}
