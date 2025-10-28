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
		
		public static Vector2 FromAngle(float a) => new (Mathf.Cos(a), Mathf.Sin(a));

		protected override void OnPopulateMesh(VertexHelper vh)
		{
			vh.Clear();

			if (points.Count < 2) return;
			
			UIVertex vertex = UIVertex.simpleVert;
			vertex.color = color;

			for (var i = 0; i < points.Count; i++)
			{
				bool onFirst = i == 0;
				bool onLast = i == points.Count - 1;
				
				var v = points[i];

				var quarter = Mathf.PI / 2;
				Vector2 na = Vector2.zero;
				Vector2 nb = Vector2.zero;
				Vector2 vec = Vector2.zero;

				if (!onLast)
				{
					var p = points[i + 1];
					var a = Mathf.Atan2(p.y - v.y, p.x - v.x) + quarter;
					na = FromAngle(a);
					vec += na;
				}

				if (!onFirst)
				{
					var p = points[i - 1];
					var a = Mathf.Atan2(p.y - v.y, p.x - v.x) - quarter;
					nb = FromAngle(a);
					vec += nb;
				}
				
				vec.Normalize();

				if (!onFirst && !onLast)
				{
					var l = Mathf.Sqrt(2) / Mathf.Sqrt(1 + Vector2.Dot(na, nb));
					vec *= l;
				}

				float halfThickness = thickness / 2f;

				vertex.position = v + vec * halfThickness;
				vh.AddVert(vertex);

				vertex.position = v - vec * halfThickness;
				vh.AddVert(vertex);

				if (!onLast)
				{
					int triIndex = i * 2;
					vh.AddTriangle(triIndex + 2, triIndex + 1, triIndex + 0);
					vh.AddTriangle(triIndex + 2, triIndex + 3, triIndex + 1);
				}
			}
		}
	}
}