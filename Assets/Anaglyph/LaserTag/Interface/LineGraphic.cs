using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
	/// <summary>
	/// A polyline drawn as an anti-aliased SDF stroke by the Anaglyph/UI/UILine
	/// shader. The ribbon geometry is padded past the requested thickness so the
	/// AA falloff has room to fade out inside the mesh; the signed cross-stroke
	/// distance is written to TEXCOORD0 and the shader derives coverage from it.
	///
	/// Per-instance stroke params (halfStroke, softness) ride in TEXCOORD1, so
	/// lines of different thickness still share ONE material and batch together.
	/// Assign a material using the "Anaglyph/UI/UILine" shader to the Material
	/// field.
	/// </summary>
	[RequireComponent(typeof(CanvasRenderer))]
	[ExecuteAlways]
	public class LineGraphic : MaskableGraphic
	{
		[Min(0)] public float thickness = 1f;

		[Tooltip("Extra stroke-weight padding added to the ribbon so the AA edge " +
		         "isn't clipped by the geometry. Keep it at least a pixel or two in " +
		         "local units; increase if thin or scaled-down lines look cut off.")]
		[Min(0f)] public float padding = 2f;

		[Tooltip("AA edge softness multiplier on top of the screen-space derivative.")]
		[Min(0.01f)] public float softness = 1f;

		public Vector2[] points;

		public float firstAngle = 0f;
		public bool overrideFirstAngle = false;

		public float lastAngle = 0f;
		public bool overrideLastAngle = false;

		public static Vector2 FromAngle(float a)
		{
			return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
		}

		protected override void OnEnable()
		{
			base.OnEnable();
			EnsureCanvasChannels();
		}

		protected override void OnPopulateMesh(VertexHelper vh)
		{
			vh.Clear();

			if (points == null || points.Length < 2)
				return;

			int count = points.Length;
			float halfStroke = thickness * 0.5f;
			float edge = halfStroke + padding; // padded rail: extra room for AA

			UIVertex vert = UIVertex.simpleVert;
			vert.color = color;
			// TEXCOORD1 -> per-instance stroke params, constant along the line.
			vert.uv1 = new Vector4(halfStroke, softness, 0f, 0f);

			// Build vertices
			for (int i = 0; i < count; i++)
			{
				Vector2 p = points[i];

				Vector2 nNext = Vector2.zero;
				if (i < count - 1)
				{
					Vector2 pNext = p;
					for (int s = i + 1; s < count; s++)
						if (points[s] != p)
						{
							pNext = points[s];
							break;
						}

					Vector2 dNext = (pNext - p).normalized;
					nNext = new Vector2(dNext.y, -dNext.x);
				}

				Vector2 nPrev = Vector2.zero;
				if (i > 0)
				{
					Vector2 pPrev = p;
					for (int s = i - 1; s >= 0; s--)
						if (points[s] != p)
						{
							pPrev = points[s];
							break;
						}

					Vector2 dPrev = (pPrev - p).normalized;
					nPrev = new Vector2(-dPrev.y, dPrev.x);
				}

				Vector2 n = (nNext + nPrev).normalized;
				Vector2 d = i < count - 1 ? nNext : nPrev;

				if (i == 0 && overrideFirstAngle)
					n = FromAngle(firstAngle);
				else if (i == count - 1 && overrideLastAngle)
					n = FromAngle(lastAngle);

				float dot = Vector2.Dot(d, n);
				if (!Mathf.Approximately(dot, 0f))
					n /= dot;

				Vector2 offset = n * edge;

				// TEXCOORD0.x -> signed cross-stroke distance from the centerline.
				// The miter scaling above keeps each rail's perpendicular distance
				// to its segment at exactly +/- edge, so |u| reads as that distance.
				vert.position = p + offset;
				vert.uv0 = new Vector4(edge, 0f, 0f, 0f);
				vh.AddVert(vert);

				vert.position = p - offset;
				vert.uv0 = new Vector4(-edge, 0f, 0f, 0f);
				vh.AddVert(vert);
			}

			// Build triangles
			for (int i = 0; i < count - 1; i++)
			{
				int v = i * 2;

				vh.AddTriangle(v + 2, v + 1, v + 0);
				vh.AddTriangle(v + 2, v + 3, v + 1);
			}
		}

		/// <summary>
		/// The Canvas only emits the vertex streams listed in Additional Shader
		/// Channels. This graphic packs stroke params into TexCoord1, so make sure
		/// it's enabled or that data is stripped at batch time.
		/// </summary>
		private void EnsureCanvasChannels()
		{
			Canvas c = canvas;
			if (c == null)
				return;

			const AdditionalCanvasShaderChannels need =
				AdditionalCanvasShaderChannels.TexCoord1;

			if ((c.additionalShaderChannels & need) != need)
				c.additionalShaderChannels |= need;
		}

#if UNITY_EDITOR
		protected override void OnValidate()
		{
			base.OnValidate();
			thickness = Mathf.Max(0f, thickness);
			padding = Mathf.Max(0f, padding);
			softness = Mathf.Max(0.01f, softness);
			EnsureCanvasChannels();
			SetVerticesDirty();
		}
#endif
	}
}
