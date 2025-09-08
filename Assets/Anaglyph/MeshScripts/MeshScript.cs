using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph
{
	[RequireComponent(typeof(MeshFilter))]
	public abstract class MeshScript : MonoBehaviour
	{
		[SerializeField] protected Mesh originalMesh;

		[SerializeField] protected MeshFilter meshFilter;
		protected List<Vector3> vertsOriginal { get; private set; } = new();
		protected Mesh modifiedMesh;
		public bool initializedMesh { get; private set; }

		protected virtual void OnValidate()
		{
			TryGetComponent(out meshFilter);
		}

		protected virtual void Awake()
		{
			if (modifiedMesh != null)
				DestroyImmediate(modifiedMesh);

			modifiedMesh = new Mesh();

			var originalMeshData = Mesh.AcquireReadOnlyMeshData(originalMesh);
			Mesh.ApplyAndDisposeWritableMeshData(originalMeshData, modifiedMesh);

			originalMesh.GetVertices(vertsOriginal);

			initializedMesh = true;
		}

		protected virtual void OnDestroy() => DestroyImmediate(modifiedMesh);
	}
}
