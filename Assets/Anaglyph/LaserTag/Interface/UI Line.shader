// UI Line.shader
// Companion shader for the LineGraphic MaskableGraphic. The line is drawn as an
// anti-aliased SDF stroke: the ribbon geometry is padded past the requested
// thickness, the signed cross-stroke distance is written to TEXCOORD0, and the
// fragment derives coverage from |u| relative to the stroke half-width. Stroke
// params ride in the vertex stream so lines of any thickness batch under one
// material.
//
// Vertex channel layout (written by LineGraphic.OnPopulateMesh):
//   COLOR      -> line color (also receives CanvasGroup inherited alpha)
//   TEXCOORD0  -> .x = signed cross-stroke distance from the centerline (local units)
//   TEXCOORD1  -> .x = halfStroke (coverage hits 0.5 here), .y = softness
//
// Requires the Canvas Additional Shader Channels to include TexCoord1
// (LineGraphic enables this automatically).

Shader "Anaglyph/UI/UILine"
{
	Properties
	{
		// standard UI plumbing only; stroke style is per-vertex
		_StencilComp ("Stencil Comparison", Float) = 8
		_Stencil ("Stencil ID", Float) = 0
		_StencilOp ("Stencil Operation", Float) = 0
		_StencilWriteMask ("Stencil Write Mask", Float) = 255
		_StencilReadMask ("Stencil Read Mask", Float) = 255
		_ColorMask ("Color Mask", Float) = 15
		[Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
	}

	SubShader
	{
		Tags
		{
			"Queue" = "Transparent"
			"IgnoreProjector" = "True"
			"RenderType" = "Transparent"
			"PreviewType" = "Plane"
			"CanUseSpriteAtlas" = "True"
		}

		Stencil
		{
			Ref [_Stencil]
			Comp [_StencilComp]
			Pass [_StencilOp]
			ReadMask [_StencilReadMask]
			WriteMask [_StencilWriteMask]
		}

		Cull Off
		Lighting Off
		ZWrite Off
		ZTest [unity_GUIZTestMode]
		// Premultiplied-alpha "over": the fragment outputs color already scaled by
		// coverage, so color and alpha composite identically and partial-coverage
		// AA edges never drop destination alpha below the covered amount -> no
		// passthrough holes in the Quest mixed-reality compositor.
		Blend One OneMinusSrcAlpha
		ColorMask [_ColorMask]

		Pass
		{
			Name "Default"
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 2.0

			#include "UnityCG.cginc"
			#include "UnityUI.cginc"

			#pragma multi_compile_local _ UNITY_UI_CLIP_RECT
			#pragma multi_compile_local _ UNITY_UI_ALPHACLIP

			struct appdata_t
			{
				float4 vertex : POSITION;
				float4 color : COLOR;   // line color
				float4 uv0 : TEXCOORD0; // .x = signed cross-stroke distance
				float4 uv1 : TEXCOORD1; // .x = halfStroke, .y = softness
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				fixed4 color : COLOR;
				float4 uv0 : TEXCOORD0;
				float4 uv1 : TEXCOORD1;
				float4 worldPosition : TEXCOORD2;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			float4 _ClipRect;

			v2f vert(appdata_t v)
			{
				v2f OUT;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
				OUT.worldPosition = v.vertex;
				OUT.vertex = UnityObjectToClipPos(v.vertex);
				OUT.uv0 = v.uv0;
				OUT.uv1 = v.uv1;
				OUT.color = v.color;
				return OUT;
			}

			fixed4 frag(v2f IN) : SV_Target
			{
				float u = IN.uv0.x;          // signed distance from the stroke center
				float halfStroke = IN.uv1.x; // coverage reaches 0.5 here
				float soft = max(IN.uv1.y, 1e-3);

				// Signed distance to the stroke edge: negative inside, 0 on the edge.
				float d = abs(u) - halfStroke;

				// Screen-space AA width from the derivative of the cross coord, so a
				// constant 'soft' looks the same at any canvas scale / view distance.
				float aa = fwidth(u) * soft;
				float coverage = 1.0 - smoothstep(-aa, aa, d);

				float alpha = coverage * IN.color.a;

				#ifdef UNITY_UI_CLIP_RECT
				alpha *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
				#endif

				#ifdef UNITY_UI_ALPHACLIP
				clip(alpha - 0.001);
				#endif

				// Premultiplied output to pair with Blend One OneMinusSrcAlpha.
				return fixed4(IN.color.rgb * alpha, alpha);
			}
			ENDCG
		}
	}
}
