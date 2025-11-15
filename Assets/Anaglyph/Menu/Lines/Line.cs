using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
	[RequireComponent(typeof(CanvasRenderer))]
	public class Line : Graphic
	{
		public float thickness;

		public List<Vector2> points;

		public float firstAngle = 0;
		public bool overrideFirstAngle = false;

		public static Vector2 FromAngle(float a)
		{
			return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
		}

		protected override void OnPopulateMesh(VertexHelper vh)
		{
			vh.Clear();

			if (points.Count < 2) return;

			var vertex = UIVertex.simpleVert;
			vertex.color = color;

			var end = points.Count - 1;

			for (var i = 0; i < points.Count; i++)
			{
				var p = points[i];

				var quarter = Mathf.PI / 2;
				var nNext = Vector2.zero;
				var nPrev = Vector2.zero;
				var n = Vector2.zero;

				if (i < end)
				{
					var pNext = points[i + 1];
					var a = Mathf.Atan2(pNext.y - p.y, pNext.x - p.x) + quarter;
					nNext = FromAngle(a);
					n += nNext;
				}

				if (i > 0)
				{
					var pPrev = points[i - 1];
					var a = Mathf.Atan2(pPrev.y - p.y, pPrev.x - p.x) - quarter;
					nPrev = FromAngle(a);
					n += nPrev;
				}

				n.Normalize();

				if (i > 0 && i < end)
				{
					var l = Mathf.Sqrt(2) / Mathf.Sqrt(1 + Vector2.Dot(nNext, nPrev));
					n *= l;
				}

				var halfThickness = thickness / 2f;

				vertex.position = p + n * halfThickness;
				vh.AddVert(vertex);

				vertex.position = p - n * halfThickness;
				vh.AddVert(vertex);

				if (i < end)
				{
					var triIndex = i * 2;
					vh.AddTriangle(triIndex + 2, triIndex + 1, triIndex + 0);
					vh.AddTriangle(triIndex + 2, triIndex + 3, triIndex + 1);
				}
			}
		}
	}
}