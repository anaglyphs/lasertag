using System;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
	[Serializable]
	public struct Segment
	{
		public Vector2[] points;

		public float thickness;
		public Color32 color;

		public bool closed;

		public float firstAngle;
		public bool overrideFirstAngle;

		public float lastAngle;
		public bool overrideLastAngle;
	}

	[ExecuteAlways]
	public class MultiLineGraphic : Graphic
	{
		public Segment[] segments;

		public static Vector2 FromAngle(float a)
		{
			return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
		}

		protected override void OnPopulateMesh(VertexHelper vh)
		{
			vh.Clear();

			foreach (Segment seg in segments)
			{
				Vector2[] points = seg.points;

				if (points == null || points.Length < 2)
					return;

				int count = points.Length;
				float halfThickness = seg.thickness * 0.5f;
				Color32 c = seg.color;

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

					Vector2 offset = n * halfThickness;

					vh.AddVert(p + offset, c, Vector2.zero);
					vh.AddVert(p - offset, c, Vector2.zero);
				}

				// Build triangles
				for (int i = 0; i < count - 1; i++)
				{
					int v = i * 2;

					vh.AddTriangle(v + 2, v + 1, v + 0);
					vh.AddTriangle(v + 2, v + 3, v + 1);
				}

				if (seg.closed)
				{
					vh.AddTriangle(0, count - 2, count - 1);
					vh.AddTriangle(0, 1, count - 1);
				}
			}
		}
	}
}