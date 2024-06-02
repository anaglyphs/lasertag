using UnityEngine;
using UnityEngine.EventSystems;

namespace GlassUI
{
	[ExecuteAlways]
	[RequireComponent(typeof(MeshRenderer), typeof(RectTransform))]
	public class RectMesh : UIBehaviour
	{
		[SerializeField] private Mesh originalMesh;
		[SerializeField] private float padding = 0.01f;

		private RectTransform rectTransform;
		private MeshFilter meshFilter;
		private Vector3[] vertsOriginal;
		private Mesh modifiedMesh;

		private bool initializedMesh;

		protected override void Awake()
		{
			rectTransform = GetComponent<RectTransform>();
			meshFilter = GetComponent<MeshFilter>();

			if (modifiedMesh != null)
				DestroyImmediate(modifiedMesh);

			modifiedMesh = new Mesh();

			var originalMeshData = Mesh.AcquireReadOnlyMeshData(originalMesh);
			Mesh.ApplyAndDisposeWritableMeshData(originalMeshData, modifiedMesh);

			vertsOriginal = originalMesh.vertices;

			initializedMesh = true;
		}

		protected override void Start()
		{
			SetSize();
		}

		protected override void OnRectTransformDimensionsChange()
		{
			SetSize();
		}

		private void SetSize()
		{
			if (!initializedMesh)
				return;

			SetSize(rectTransform.rect.size, rectTransform.rect.center, padding);
		}

		public void SetSize(Vector2 size, Vector2 center, float padding = 0)
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