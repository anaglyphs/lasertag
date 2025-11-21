using System;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
	[ExecuteAlways]
	[RequireComponent(typeof(CanvasRenderer), typeof(RectTransform))]
	public class Line : MonoBehaviour
	{
		public float thickness;
		public Color color = Color.white;
		public Vector2[] points;

		public float firstAngle = 0;
		public bool overrideFirstAngle = false;
		public float lastAngle = 0;
		public bool overrideLastAngle = false;

		private Vector3[] verts;
		private int[] tris;
		private Color[] colors;

		private Mesh mesh;
		private CanvasRenderer cr;
		private RectTransform rt;

		private bool initialized = false;

		public static Vector2 FromAngle(float a)
		{
			return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
		}

		private void Init()
		{
			cr = GetComponent<CanvasRenderer>();
			rt = GetComponent<RectTransform>();

			cr.materialCount = 1;
			cr.SetMaterial(Graphic.defaultGraphicMaterial, 0);

			BuildMesh();
			PositionVertices();

			initialized = true;
		}

		private void Awake()
		{
			Init();
		}

		private void OnValidate()
		{
			if (!initialized)
				Init();

			BuildMesh();
			PositionVertices();
		}

		public void PositionVertices()
		{
			var end = points.Length - 1;
			var halfThickness = thickness * 0.5f;

			for (var i = 0; i < points.Length; i++)
			{
				var p = points[i];

				var nNext = Vector2.zero;
				if (i < end)
				{
					var pNext = points[i + 1];
					var dNext = (pNext - p).normalized;
					nNext = new Vector2(dNext.y, -dNext.x);
				}

				var nPrev = Vector2.zero;
				if (i > 0)
				{
					var pPrev = points[i - 1];
					var dPrev = (pPrev - p).normalized;
					nPrev = new Vector2(-dPrev.y, dPrev.x);
				}

				var n = (nNext + nPrev).normalized;
				var d = i < end ? nNext : nPrev;

				if (i == 0 && overrideFirstAngle)
					n = FromAngle(firstAngle);
				else if (i == end && overrideLastAngle)
					n = FromAngle(lastAngle);

				n /= Vector2.Dot(d, n);

				var v = i * 2;
				verts[v + 0] = p + n * halfThickness;
				verts[v + 1] = p - n * halfThickness;
			}

			mesh.vertices = verts;

			cr.SetMesh(mesh);
		}

		public void SetColor(Color color)
		{
			this.color = color;

			if (!initialized)
				return;

			colors = new Color[points.Length];
			for (var i = 0; i < colors.Length; i++) colors[i] = color;
			mesh.colors = colors;
			cr.SetMesh(mesh);
		}

		public void BuildMesh()
		{
			verts = new Vector3[points.Length * 2];
			tris = new int[points.Length * 6];

			for (var i = 0; i < points.Length - 1; i++)
			{
				var v = i * 2;
				var t = i * 6;

				tris[t + 0] = v + 2;
				tris[t + 1] = v + 1;
				tris[t + 2] = v + 0;

				tris[t + 3] = v + 2;
				tris[t + 4] = v + 3;
				tris[t + 5] = v + 1;
			}

			colors = new Color[verts.Length];
			for (var i = 0; i < colors.Length; i++) colors[i] = color;

			mesh = new Mesh
			{
				vertices = verts,
				triangles = tris,
				colors = colors
			};

			cr.SetMesh(mesh);
		}
	}
}