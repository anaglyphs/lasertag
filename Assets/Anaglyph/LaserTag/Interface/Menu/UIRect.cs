using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// A procedural UI rectangle: bright outline + transparent gap + dark fill,
	/// drawn entirely by the UI/UIRect shader. Replaces an Image + RectSizeToUV
	/// mesh effect.
	///
	/// All per-instance parameters (rect size, stroke width, gap, edge padding,
	/// corner radius, edge softness, and both colors) are baked into the vertex
	/// stream, so rects with different styles still share ONE material and batch
	/// together. The shader exposes no color/geometry properties; everything is
	/// driven from this component.
	///
	/// Notes:
	///  - Put this on a bare UI GameObject (it IS a Graphic, so no Image alongside).
	///  - Assign a material using the "UI/UIRect" shader to the Material field.
	///  - Units for stroke/gap/padding/radius are the RectTransform's own units.
	///  - The Source Texture is unused; mainTexture defaults to white.
	///  - CanvasGroup alpha is applied by the CanvasRenderer to the COLOR channel
	///    only, which here carries the stroke color. The fill color rides in
	///    TEXCOORD3 and will NOT fade with a CanvasGroup. For uniform fades, animate
	///    this component's `color` (the master tint, folded into both colors at bake
	///    time) which re-tessellates, or keep fill in a material property instead.
	/// </summary>
	[RequireComponent(typeof(CanvasRenderer))]
	public class UIRect : MaskableGraphic
	{
		[Header("Colors")] [SerializeField] private Color _strokeColor = Color.white;
		[SerializeField] private Color _fillColor = new(0.05f, 0.06f, 0.10f, 1f);

		[Header("Geometry (rect units)")] [SerializeField] [Min(0f)]
		private float _strokeWidth = 2f;

		[SerializeField] [Min(0f)] private float _gap = 1f;
		[SerializeField] [Min(0f)] private float _edgePadding = 1f;
		[SerializeField] [Min(0f)] private float _cornerRadius = 0f;
		[SerializeField] [Min(0.01f)] private float _edgeSoftness = 1f;

		// Public accessors that re-tessellate on change (use these from gameplay
		// code for hover/press state changes, etc.).
		public Color StrokeColor
		{
			get => _strokeColor;
			set
			{
				_strokeColor = value;
				SetVerticesDirty();
			}
		}

		public Color FillColor
		{
			get => _fillColor;
			set
			{
				_fillColor = value;
				SetVerticesDirty();
			}
		}

		public float StrokeWidth
		{
			get => _strokeWidth;
			set
			{
				_strokeWidth = Mathf.Max(0f, value);
				SetVerticesDirty();
			}
		}

		public float Gap
		{
			get => _gap;
			set
			{
				_gap = Mathf.Max(0f, value);
				SetVerticesDirty();
			}
		}

		public float EdgePadding
		{
			get => _edgePadding;
			set
			{
				_edgePadding = Mathf.Max(0f, value);
				SetVerticesDirty();
			}
		}

		public float CornerRadius
		{
			get => _cornerRadius;
			set
			{
				_cornerRadius = Mathf.Max(0f, value);
				SetVerticesDirty();
			}
		}

		public float EdgeSoftness
		{
			get => _edgeSoftness;
			set
			{
				_edgeSoftness = Mathf.Max(0.01f, value);
				SetVerticesDirty();
			}
		}

		protected override void OnEnable()
		{
			base.OnEnable();
			EnsureCanvasChannels();
		}

		protected override void OnPopulateMesh(VertexHelper vh)
		{
			vh.Clear();

			Rect r = rectTransform.rect;
			Vector2 size = r.size;

			// master tint (Graphic.color) folded into both colors
			Color stroke = _strokeColor * color;
			Color fill = _fillColor * color;

			// packed per-instance parameters
			Vector4 sizeStrokeGap = new(size.x, size.y, _strokeWidth, _gap);
			Vector4 padRadSoft = new(_edgePadding, _cornerRadius, _edgeSoftness, 0f);

			float xMin = r.xMin, xMax = r.xMax, yMin = r.yMin, yMax = r.yMax;

			UIVertex v = UIVertex.simpleVert;
			v.color = stroke; // COLOR  -> stroke color (gets CanvasGroup alpha)
			v.uv1 = sizeStrokeGap; // TEXCOORD1 -> (w, h, strokeWidth, gap)
			v.uv2 = padRadSoft; // TEXCOORD2 -> (edgePadding, cornerRadius, edgeSoftness, _)
			v.uv3 = fill; // TEXCOORD3 -> fill color

			v.position = new Vector3(xMin, yMin);
			v.uv0 = new Vector4(0f, 0f, 0f, 0f);
			vh.AddVert(v);
			v.position = new Vector3(xMin, yMax);
			v.uv0 = new Vector4(0f, 1f, 0f, 0f);
			vh.AddVert(v);
			v.position = new Vector3(xMax, yMax);
			v.uv0 = new Vector4(1f, 1f, 0f, 0f);
			vh.AddVert(v);
			v.position = new Vector3(xMax, yMin);
			v.uv0 = new Vector4(1f, 0f, 0f, 0f);
			vh.AddVert(v);

			vh.AddTriangle(0, 1, 2);
			vh.AddTriangle(2, 3, 0);
		}

		/// <summary>
		/// The Canvas only emits the vertex streams listed in Additional Shader
		/// Channels. This graphic uses TexCoord1/2/3, so make sure they're enabled
		/// or the baked data is stripped at batch time.
		/// </summary>
		private void EnsureCanvasChannels()
		{
			Canvas c = canvas;
			if (c == null)
				return;

			const AdditionalCanvasShaderChannels need =
				AdditionalCanvasShaderChannels.TexCoord1 |
				AdditionalCanvasShaderChannels.TexCoord2 |
				AdditionalCanvasShaderChannels.TexCoord3;

			if ((c.additionalShaderChannels & need) != need)
				c.additionalShaderChannels |= need;
		}

#if UNITY_EDITOR
		protected override void OnValidate()
		{
			base.OnValidate();
			_strokeWidth = Mathf.Max(0f, _strokeWidth);
			_gap = Mathf.Max(0f, _gap);
			_edgePadding = Mathf.Max(0f, _edgePadding);
			_cornerRadius = Mathf.Max(0f, _cornerRadius);
			_edgeSoftness = Mathf.Max(0.01f, _edgeSoftness);
			EnsureCanvasChannels();
			SetVerticesDirty();
		}
#endif
	}
}