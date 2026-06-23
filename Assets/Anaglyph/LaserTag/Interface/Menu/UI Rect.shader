// UIRect.shader
// Companion shader for the UIRect MaskableGraphic. Reads every per-instance
// parameter from the vertex stream, so the material carries no color/geometry
// properties and rects of any style batch under one material.
//
// Vertex channel layout (written by UIRect.OnPopulateMesh):
//   COLOR      -> stroke color (also receives CanvasGroup inherited alpha)
//   TEXCOORD0  -> uv (0..1) in .xy
//   TEXCOORD1  -> (rectWidth, rectHeight, strokeWidth, gap)
//   TEXCOORD2  -> (edgePadding, cornerRadius, edgeSoftness, unused)
//   TEXCOORD3  -> fill color
//
// Requires the Canvas Additional Shader Channels to include TexCoord1/2/3
// (UIRect enables these automatically).

Shader "Anaglyph/UI/UIRect"
{
	Properties
	{
		// standard UI plumbing only; no style properties (those are per-vertex)
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
		// Separate alpha blend (Porter-Duff "over" on alpha) so partial-coverage
		// AA strips never lower the destination alpha and punch passthrough holes
		// in the Quest compositor.
		Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
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
				float4 color : COLOR; // stroke color
				float4 uv0 : TEXCOORD0; // .xy = uv
				float4 uv1 : TEXCOORD1; // size.xy, strokeWidth, gap
				float4 uv2 : TEXCOORD2; // edgePadding, cornerRadius, edgeSoftness, _
				float4 fillCol : TEXCOORD3; // fill color
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				fixed4 strokeColor : COLOR;
				float2 uv : TEXCOORD0;
				float4 sizeStrokeGap : TEXCOORD1;
				float4 padRadSoft : TEXCOORD2;
				float4 fillColor : TEXCOORD3;
				float4 worldPosition : TEXCOORD4;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			float4 _ClipRect;

			// Signed distance to a rounded box. Negative inside, 0 on the edge.
			float sdRoundBox(float2 p, float2 halfSize, float r)
			{
				float2 q = abs(p) - (halfSize - r);
				return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
			}

			v2f vert(appdata_t v)
			{
				v2f OUT;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
				OUT.worldPosition = v.vertex;
				OUT.vertex = UnityObjectToClipPos(v.vertex);
				OUT.uv = v.uv0.xy;
				OUT.sizeStrokeGap = v.uv1;
				OUT.padRadSoft = v.uv2;
				OUT.strokeColor = v.color;
				OUT.fillColor = v.fillCol;
				return OUT;
			}

			fixed4 frag(v2f IN) : SV_Target
			{
				float2 texSize = max(IN.sizeStrokeGap.xy, 1e-3);
				float strokeW = IN.sizeStrokeGap.z;
				float gap = IN.sizeStrokeGap.w;
				float pad = IN.padRadSoft.x;
				float radius = IN.padRadSoft.y;
				float soft = max(IN.padRadSoft.z, 1e-3);

				float2 halfSize = texSize * 0.5;
				float2 p = (IN.uv - 0.5) * texSize;

				float2 hOuter = max(halfSize - pad, 1e-3);
				float2 hMid = max(hOuter - strokeW, 1e-3);
				float2 hInner = max(hMid - gap, 1e-3);

				float rOuter = clamp(radius, 0.0, min(hOuter.x, hOuter.y));
				float rMid = max(rOuter - strokeW, 0.0);
				float rInner = max(rMid - gap, 0.0);

				float dOuter = sdRoundBox(p, hOuter, rOuter);
				float dMid = sdRoundBox(p, hMid, rMid);
				float dInner = sdRoundBox(p, hInner, rInner);

				float aaO = fwidth(dOuter) * soft;
				float aaM = fwidth(dMid) * soft;
				float aaI = fwidth(dInner) * soft;

				float covOuter = 1.0 - smoothstep(-aaO, aaO, dOuter);
				float covMid = 1.0 - smoothstep(-aaM, aaM, dMid);
				float covInner = 1.0 - smoothstep(-aaI, aaI, dInner);

				float aWhite = saturate(covOuter - covMid);
				float aFill = covInner;

				float aW = aWhite * IN.strokeColor.a;
				float aF = aFill * IN.fillColor.a;
				float alpha = aW + aF;

				float3 rgb = alpha > 1e-4
	                                      ? (IN.strokeColor.rgb * aW + IN.fillColor.rgb * aF) / alpha
	                                      : float3(0, 0, 0);

				fixed4 col = fixed4(rgb, alpha);

				#ifdef UNITY_UI_CLIP_RECT
				col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
				#endif

				#ifdef UNITY_UI_ALPHACLIP
				clip(col.a - 0.001);
				#endif

				return col;
			}
			ENDCG
		}
	}
}