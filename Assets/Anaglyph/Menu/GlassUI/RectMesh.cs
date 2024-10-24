using Anaglyph;
using UnityEngine;

namespace GlassUI
{
	[ExecuteAlways]
	[RequireComponent(typeof(RectTransform))]
	public class RectMesh : ProgrammaticMesh
	{
		[SerializeField] private float padding = 0.01f;

		[SerializeField] private RectTransform rectTransform;

		protected override void OnValidate()
		{
			base.OnValidate();

			this.SetComponent(ref rectTransform);
		}

		//protected override void Awake()
		//{
		//	base.Awake();
		//}

		private void Start()
		{
			UpdateMesh();
		}

		private void OnRectTransformDimensionsChange()
		{
			UpdateMesh();
		}

		public void UpdateMesh()
		{
			UpdateMesh(rectTransform.rect.size, rectTransform.rect.center, padding);
		}

		public void UpdateMesh(Vector2 size, Vector2 center, float padding = 0)
		{
			if (!initializedMesh)
				return;

			Vector3[] vertsNew = new Vector3[modifiedMesh.vertices.Length];

			Vector3 s = transform.lossyScale;
			Vector2 globalSize = size * (Vector2)s;

			float xOffset = globalSize.x / 2 - 1 + padding;
			float yOffset = globalSize.y / 2 - 1 + padding;

			for (int i = 0; i < modifiedMesh.vertices.Length; i++)
			{
				Vector3 vert = vertsOriginal[i];

				vert.x += xOffset * Mathf.Sign(vert.x);
				vert.y += yOffset * Mathf.Sign(vert.y);

				vert = new Vector3(vert.x / s.x, vert.y / s.y, vert.z / s.z);

				vert += (Vector3)(rectTransform.rect.center);

				vertsNew[i] = vert;
			}

			modifiedMesh.SetVertices(vertsNew);
			modifiedMesh.RecalculateBounds();
			meshFilter.mesh = modifiedMesh;
		}

		protected override void OnDestroy() => DestroyImmediate(modifiedMesh);
	}
}