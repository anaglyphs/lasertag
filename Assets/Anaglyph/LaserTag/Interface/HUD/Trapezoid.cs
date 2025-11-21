using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.LaserTag
{
	[ExecuteAlways]
	[RequireComponent(typeof(CanvasRenderer), typeof(RectTransform))]
	public class Trapezoid : MonoBehaviour
	{
		public float slope = 0;

		private Mesh mesh;
		private CanvasRenderer cr;
		private RectTransform rt;

		// Cache data so nothing must be reallocated
		private Vector3[] verts = new Vector3[4];
		private Vector2[] uvs = new Vector2[4];
		private int[] tris = { 0, 1, 2, 2, 3, 0 };

		private Color[] colors =
		{
			Color.white,
			Color.white,
			Color.white,
			Color.white
		};

		private bool initialized = false;

		private void Init()
		{
			cr = GetComponent<CanvasRenderer>();
			rt = GetComponent<RectTransform>();

			// UVs once
			uvs[0] = new Vector2(0, 1);
			uvs[1] = new Vector2(1, 1);
			uvs[2] = new Vector2(1, 0);
			uvs[3] = new Vector2(0, 0);

			mesh = new Mesh
			{
				vertices = verts,
				uv = uvs,
				triangles = tris,
				colors = colors
			};

			initialized = true;
		}

		private void Awake()
		{
			Init();
			UpdateShape();
		}

		private void OnRectTransformDimensionsChange()
		{
			if (!initialized)
				Init();
			UpdateShape();
		}

		private void UpdateShape()
		{
			var rect = rt.rect;

			// top slope offset
			var topOff = rect.yMax * slope;
			var bottomOff = rect.yMin * slope;

			verts[0] = new Vector3(rect.xMin - topOff, rect.yMax);
			verts[1] = new Vector3(rect.xMax + topOff, rect.yMax);
			verts[2] = new Vector3(rect.xMax + bottomOff, rect.yMin);
			verts[3] = new Vector3(rect.xMin - bottomOff, rect.yMin);

			mesh.vertices = verts;
			mesh.RecalculateBounds();

			cr.SetMesh(mesh);
			cr.materialCount = 1;
			cr.SetMaterial(Graphic.defaultGraphicMaterial, 0);
		}
	}
}