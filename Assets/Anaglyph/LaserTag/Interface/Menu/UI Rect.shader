// RetroOutlineUI.shader
// UnityEngine.UI.Image-compatible shader: white outline + transparent gap + dark fill.
//
// Differences from the SpriteRenderer version:
//  - Built on the built-in UI-Default lineage (UnityUI.cginc) so it works as an
//    Image material: stencil masking, clip rect, and CanvasRenderer tint all behave.
//  - The Source Image field is unused (we never sample _MainTex). The shader IS
//    the final visual.
//  - Aspect ratio + units come from the Image's RectTransform size, which the
//    companion RectSizeToUV component bakes into TEXCOORD1 per vertex. Because we
//    measure in those rect units, the stroke is uniform thickness on all sides of
//    a non-square Image, and scales with the Image rather than the screen.
//  - AA is screen-space via fwidth: crisp up close, smooth when small. No MSAA.
//
// Setup:
//  1. Create an Image (Image Type = Simple). Source Image can be left empty.
//  2. Assign a material using this shader.
//  3. Add the RectSizeToUV component to the same GameObject.

Shader "Anaglyph/UI/Rect"
{
	Properties
	{
		[PerRendererData] _MainTex ("Sprite Texture (unused)", 2D) = "white" {}
		_Color ("Tint", Color) = (1,1,1,1)

		_StrokeColor ("Stroke Color", Color) = (1,1,1,1)
		_FillColor ("Fill Color", Color) = (0.05,0.06,0.10,1)
		_StrokeWidth ("Stroke Width (rect units)", Float) = 2
		_Gap ("Gap (rect units)", Float) = 1
		_EdgePadding ("Edge Padding (rect units)", Float) = 1
		_CornerRadius ("Corner Radius (rect units)", Float)= 0
		_EdgeSoftness ("Edge Softness (x AA)", Float) = 1

		// --- standard UI plumbing ---
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
		// Separate alpha blend (One OneMinusSrcAlpha) does a Porter-Duff "over"
		// on the alpha channel: dst.a = src.a + dst.a*(1 - src.a). This never
		// lowers the destination alpha, so the AA transition strips in the gap
		// don't punch holes that the Quest passthrough compositor reads as
		// transparency. The plain "Blend SrcAlpha OneMinusSrcAlpha" used by the
		// stock UI shader squares src.a on the alpha channel and corrupts it.
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
				float4 color : COLOR;
				float2 texcoord : TEXCOORD0;
				float2 rectSize : TEXCOORD1; // baked by RectSizeToUV
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				fixed4 color : COLOR;
				float2 texcoord : TEXCOORD0;
				float4 worldPosition : TEXCOORD1;
				float2 rectSize : TEXCOORD2;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			fixed4 _Color;
			fixed4 _TextureSampleAdd;
			float4 _ClipRect;
			float4 _MainTex_ST;

			float4 _StrokeColor;
			float4 _FillColor;
			float _StrokeWidth;
			float _Gap;
			float _EdgePadding;
			float _CornerRadius;
			float _EdgeSoftness;

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
				OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
				OUT.rectSize = v.rectSize;
				OUT.color = v.color * _Color;
				return OUT;
			}

			fixed4 frag(v2f IN) : SV_Target
			{
				// rect dimensions in canvas units (baked into TEXCOORD1)
				float2 texSize = max(IN.rectSize, 1e-3);
				float2 halfSize = texSize * 0.5;
				// centered position in rect units (isotropic -> uniform stroke)
				float2 p = (IN.texcoord - 0.5) * texSize;

				// nested rounded-rect bands
				float2 hOuter = max(halfSize - _EdgePadding, 1e-3);
				float2 hMid = max(hOuter - _StrokeWidth, 1e-3);
				float2 hInner = max(hMid - _Gap, 1e-3);

				float rOuter = clamp(_CornerRadius, 0.0, min(hOuter.x, hOuter.y));
				float rMid = max(rOuter - _StrokeWidth, 0.0);
				float rInner = max(rMid - _Gap, 0.0);

				float dOuter = sdRoundBox(p, hOuter, rOuter);
				float dMid = sdRoundBox(p, hMid, rMid);
				float dInner = sdRoundBox(p, hInner, rInner);

				// screen-space AA windows
				float aaO = fwidth(dOuter) * _EdgeSoftness;
				float aaM = fwidth(dMid) * _EdgeSoftness;
				float aaI = fwidth(dInner) * _EdgeSoftness;

				float covOuter = 1.0 - smoothstep(-aaO, aaO, dOuter);
				float covMid = 1.0 - smoothstep(-aaM, aaM, dMid);
				float covInner = 1.0 - smoothstep(-aaI, aaI, dInner);

				float aWhite = saturate(covOuter - covMid); // outline ring
				float aFill = covInner; // fill interior
				// (covMid - covInner) is the transparent gap -> contributes nothing

				float aW = aWhite * _StrokeColor.a;
				float aF = aFill * _FillColor.a;
				float alpha = aW + aF;

				float3 rgb = alpha > 1e-4
	                     ? (_StrokeColor.rgb * aW + _FillColor.rgb * aF) / alpha
	                     : float3(0, 0, 0);

				fixed4 col = fixed4(rgb, alpha);
				col *= IN.color; // CanvasRenderer tint / CanvasGroup alpha / fades

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