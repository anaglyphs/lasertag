using UnityEngine;

namespace Anaglyph
{
	[RequireComponent(typeof(MeshFilter))]
	public abstract class ProgrammaticMesh : MonoBehaviour
	{
		[SerializeField] protected Mesh originalMesh;

		[SerializeField] protected MeshFilter meshFilter;
		protected Vector3[] vertsOriginal { get; private set; }
		protected Mesh modifiedMesh;
		public bool initializedMesh { get; private set; }

		protected virtual void OnValidate()
		{
			this.SetDefaultComponent(ref meshFilter);
		}

		protected virtual void Awake()
		{
			if (modifiedMesh != null)
				DestroyImmediate(modifiedMesh);

			modifiedMesh = new Mesh();

			var originalMeshData = Mesh.AcquireReadOnlyMeshData(originalMesh);
			Mesh.ApplyAndDisposeWritableMeshData(originalMeshData, modifiedMesh);

			vertsOriginal = originalMesh.vertices;

			initializedMesh = true;
		}

		protected virtual void OnDestroy() => DestroyImmediate(modifiedMesh);
	}
}
