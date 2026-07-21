using System;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
	[Serializable]
	public struct Segment
	{
		public Vector2[] points;

		[Min(0f)]
		public float thickness;
		public Color32 color;

		public bool closed;

		public float firstAngle;
		public bool overrideFirstAngle;

		public float lastAngle;
		public bool overrideLastAngle;
	}

	/// <summary>
	/// Multiple polylines drawn as anti-aliased SDF strokes by the
	/// Anaglyph/UI/UILine shader. Each segment supplies its own stroke width and
	/// color while padding and softness are shared by the graphic. Assign a
	/// material using the "Anaglyph/UI/UILine" shader to the Material field.
	/// </summary>
	[RequireComponent(typeof(CanvasRenderer))]
	[ExecuteAlways]
	public class MultiLineGraphic : MaskableGraphic
	{
		[Tooltip("Extra stroke-weight padding added to each ribbon so the AA edge " +
		         "isn't clipped by the geometry.")]
		[Min(0f)] public float padding = 2f;

		[Tooltip("AA edge softness multiplier on top of the screen-space derivative.")]
		[Min(0.01f)] public float softness = 1f;

		public Segment[] segments;

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

			if (segments == null)
				return;

			foreach (Segment seg in segments)
			{
				Vector2[] points = seg.points;

				if (points == null || points.Length < 2)
					continue;

				int count = points.Length;
				int firstVertex = vh.currentVertCount;
				float halfStroke = seg.thickness * 0.5f;
				float edge = halfStroke + padding;

				UIVertex vert = UIVertex.simpleVert;
				vert.color = seg.color;
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

					if (i == 0 && seg.overrideFirstAngle)
						n = FromAngle(seg.firstAngle);
					else if (i == count - 1 && seg.overrideLastAngle)
						n = FromAngle(seg.lastAngle);

					float dot = Vector2.Dot(d, n);
					if (!Mathf.Approximately(dot, 0f))
						n /= dot;

					Vector2 offset = n * edge;

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
					int v = firstVertex + i * 2;

					vh.AddTriangle(v + 2, v + 1, v + 0);
					vh.AddTriangle(v + 2, v + 3, v + 1);
				}

				if (seg.closed)
				{
					int firstPlus = firstVertex;
					int firstMinus = firstVertex + 1;
					int lastPlus = firstVertex + (count - 1) * 2;
					int lastMinus = lastPlus + 1;

					vh.AddTriangle(firstPlus, lastMinus, lastPlus);
					vh.AddTriangle(firstPlus, firstMinus, lastMinus);
				}
			}
		}

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
			padding = Mathf.Max(0f, padding);
			softness = Mathf.Max(0.01f, softness);

			if (segments != null)
				for (int i = 0; i < segments.Length; i++)
				{
					Segment segment = segments[i];
					segment.thickness = Mathf.Max(0f, segment.thickness);
					segments[i] = segment;
				}

			EnsureCanvasChannels();
			SetVerticesDirty();
		}
#endif
	}
}
