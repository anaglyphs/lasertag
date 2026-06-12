using Unity.Collections;
using UnityEngine;

namespace Anaglyph.DepthKit.EnvScanning
{
	/// <summary>
	/// GameObject for environment scan mesh chunk
	/// </summary>
	public class Chunk : MonoBehaviour
	{
		public int chunkIndex;
		public Mesh mesh;
		public bool dirty;
		public uint lastMeshingChangeSum;

		public MeshFilter meshFilter;
		public MeshCollider meshCollider;

		// mesh synced from other players, kept alongside the local
		// scan so drift correction can compare the two
		public Mesh RemoteMesh { get; private set; }
		public bool HasRemoteMesh => RemoteMesh != null;

		private GameObject remoteMeshObject;
		private MeshCollider remoteMeshCollider;

		private Vector3 worldCenter;

		private void Awake()
		{
			TryGetComponent(out meshFilter);
			TryGetComponent(out meshCollider);
			meshCollider.enabled = false;

			mesh = new Mesh();
			mesh.MarkDynamic();
			meshFilter.sharedMesh = mesh;
			meshCollider.sharedMesh = mesh;
		}

		private void Start()
		{
			worldCenter = transform.position + ChunkManager.ChunkWorldSizeHalf;
		}

		private void OnDestroy()
		{
			Destroy(mesh);

			if (RemoteMesh)
				Destroy(RemoteMesh);
		}

		public void ApplyRemoteMesh(NativeArray<Vector3> positions, NativeArray<int> indices)
		{
			if (RemoteMesh == null)
				CreateRemoteMeshObject();

			RemoteMesh.Clear();

			bool isPopulated = indices.Length > 0;

			if (isPopulated)
			{
				RemoteMesh.SetVertices(positions);
				RemoteMesh.SetIndices(indices, MeshTopology.Triangles, 0);
				RemoteMesh.RecalculateNormals();

				remoteMeshCollider.sharedMesh = RemoteMesh;
			}

			remoteMeshCollider.enabled = isPopulated;
		}

		public void ReleaseRemoteMesh()
		{
			if (RemoteMesh == null) return;

			Destroy(remoteMeshObject);
			Destroy(RemoteMesh);

			remoteMeshObject = null;
			remoteMeshCollider = null;
			RemoteMesh = null;
		}

		private void CreateRemoteMeshObject()
		{
			RemoteMesh = new Mesh();
			RemoteMesh.MarkDynamic();

			remoteMeshObject = new GameObject("Remote Mesh")
			{
				layer = gameObject.layer
			};
			remoteMeshObject.transform.SetParent(transform, false);

			MeshFilter filter = remoteMeshObject.AddComponent<MeshFilter>();
			filter.sharedMesh = RemoteMesh;

			if (TryGetComponent(out MeshRenderer localRenderer))
			{
				MeshRenderer remoteRenderer = remoteMeshObject.AddComponent<MeshRenderer>();
				remoteRenderer.sharedMaterials = localRenderer.sharedMaterials;
			}

			remoteMeshCollider = remoteMeshObject.AddComponent<MeshCollider>();
			remoteMeshCollider.enabled = false;
		}

#if UNITY_EDITOR

		private void OnDrawGizmos()
		{
			if (dirty)
			{
				Gizmos.color = Color.yellow;
				DrawChunkFrame();
			}
		}

		private void OnDrawGizmosSelected()
		{
			if (dirty) return;

			Gizmos.color = Color.aliceBlue;
			DrawChunkFrame();
		}

		private void DrawChunkFrame()
		{
			Gizmos.DrawWireCube(worldCenter, ChunkManager.ChunkWorldSize);
		}

#endif
	}
}
