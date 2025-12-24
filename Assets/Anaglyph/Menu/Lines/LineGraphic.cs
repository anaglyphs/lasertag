using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
	[ExecuteAlways]
	public class LineGraphic : MaskableGraphic
	{
		[Min(0)] public float thickness = 1f;

		private int prevPointsLength = 0;
		public Vector2[] points;

		public float firstAngle = 0f;
		public bool overrideFirstAngle = false;

		public float lastAngle = 0f;
		public bool overrideLastAngle = false;

		public static Vector2 FromAngle(float a)
		{
			return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
		}

		protected override void OnPopulateMesh(VertexHelper vh)
		{
			vh.Clear();

			if (points == null || points.Length < 2)
				return;

			int count = points.Length;
			float halfThickness = thickness * 0.5f;
			Color32 c = color;

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
		}
	}
}