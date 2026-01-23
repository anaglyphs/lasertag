using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.LaserTag
{
	[ExecuteAlways]
	[RequireComponent(typeof(RectTransform))]
	public class Trapezoid : MaskableGraphic
	{
		[SerializeField] public float slope = 0f;

		protected override void OnPopulateMesh(VertexHelper vh)
		{
			vh.Clear();

			Rect rect = rectTransform.rect;

			float topOff = rect.yMax * slope;
			float bottomOff = rect.yMin * slope;

			Vector3 v0 = new(rect.xMin - topOff, rect.yMax);
			Vector3 v1 = new(rect.xMax + topOff, rect.yMax);
			Vector3 v2 = new(rect.xMax + bottomOff, rect.yMin);
			Vector3 v3 = new(rect.xMin - bottomOff, rect.yMin);

			Color32 col = color;

			vh.AddVert(v0, col, new Vector2(0f, 1f));
			vh.AddVert(v1, col, new Vector2(1f, 1f));
			vh.AddVert(v2, col, new Vector2(1f, 0f));
			vh.AddVert(v3, col, new Vector2(0f, 0f));

			vh.AddTriangle(0, 1, 2);
			vh.AddTriangle(2, 3, 0);
		}

		protected override void OnRectTransformDimensionsChange()
		{
			base.OnRectTransformDimensionsChange();
			SetVerticesDirty();
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