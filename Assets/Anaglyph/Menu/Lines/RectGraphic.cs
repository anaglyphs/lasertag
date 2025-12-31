using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
	[ExecuteAlways]
	public class RectGrahpic : MaskableGraphic
	{
		[Min(0)] public float thickness = 1f;

		protected override void OnPopulateMesh(VertexHelper vh)
		{
			vh.Clear();

			Rect r = rectTransform.rect;

			Vector2 tr = new(r.xMax, r.yMax);
			Vector2 tl = new(r.xMin, r.yMax);
			Vector2 bl = new(r.xMin, r.yMin);
			Vector2 br = new(r.xMax, r.yMin);

			float t = thickness;
			Color32 c = color;

			// Vertices (outer, inner) per corner
			AddPair(vh, tr, tr + new Vector2(-t, -t), c); // 0,1
			AddPair(vh, tl, tl + new Vector2(t, -t), c); // 2,3
			AddPair(vh, bl, bl + new Vector2(t, t), c); // 4,5
			AddPair(vh, br, br + new Vector2(-t, t), c); // 6,7

			// Triangles (same topology as your original mesh)
			for (int i = 0; i < 4; i++)
			{
				int v = i * 2;

				vh.AddTriangle((v + 2) % 8, (v + 1) % 8, (v + 0) % 8);
				vh.AddTriangle((v + 2) % 8, (v + 3) % 8, (v + 1) % 8);
			}
		}

		private static void AddPair(VertexHelper vh, Vector2 outer, Vector2 inner, Color32 c)
		{
			vh.AddVert(outer, c, Vector2.zero);
			vh.AddVert(inner, c, Vector2.zero);
		}

#if UNITY_EDITOR
		protected override void OnValidate()
		{
			base.OnValidate();
			SetVerticesDirty();
		}
#endif
	}
}