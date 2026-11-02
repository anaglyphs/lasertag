using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Utilities.XR;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
	/// <summary>
	/// GameObject for environment scan mesh chunk
	/// </summary>
	public class Chunk : MonoBehaviour
	{
		public int chunkIndex;
		/// <summary>The mesh drawn and collided with. Replaced, not edited, on each remesh.</summary>
		public Mesh mesh;
		// written and cooked while mesh stays in use, then swapped in by Present
		internal Mesh backMesh;
		public bool dirty;
		public uint lastMeshingChangeSum;
		internal uint pendingMeshingChangeSum;
		// unscaled time this chunk was last queued for a readback
		internal float lastQueuedTime = float.NegativeInfinity;
		// workers take the queued chunk with the highest first
		internal uint meshPriority;
		// the latest odometer visibility reported, and whether EnvMesher is holding the
		// chunk until that change is checked
		internal uint seenChangeSum;
		internal bool awaitingRecheck;
		public bool meshIsPopulated;
		
		public NativeArray<uint> voxelSignBits;

		// the empty components reaching this chunk's faces, from voxelSignBits; see ChunkPortals
		public uint[] portalMasks = ChunkPortals.Open;
		public int portalComponents = 1;
		public int portalRevision;

		public MeshFilter meshFilter;
		public MeshRenderer meshRenderer;
		public MeshCollider meshCollider;

		// the collider cook EnvMesher runs on a job thread. It reads backMesh, so the mesh
		// outlives it
		internal JobHandle colliderBake;

		private Vector3 worldCenter;

		private void Awake()
		{
			TryGetComponent(out meshFilter);
			TryGetComponent(out meshRenderer);
			TryGetComponent(out meshCollider);
			meshCollider.enabled = false;
			// nothing to draw or cull until the first mesh lands
			meshRenderer.enabled = false;

			mesh = new Mesh();
			backMesh = new Mesh();
			meshFilter.sharedMesh = mesh;
		}

		/// <summary>
		/// Swaps <see cref="backMesh"/> in. A populated one must already be cooked
		/// (<see cref="Physics.BakeMesh(EntityId, bool, MeshColliderCookingOptions)"/> with the
		/// collider's options), so the collider takes the cooked data instead of cooking here
		/// </summary>
		internal void Present(bool populated)
		{
			(mesh, backMesh) = (backMesh, mesh);
			meshFilter.sharedMesh = mesh;

			meshIsPopulated = populated;
			meshRenderer.enabled = populated;
			// an empty mesh is never handed to the collider, which would try to cook it
			meshCollider.sharedMesh = populated ? mesh : null;
			meshCollider.enabled = populated;
		}

		private void Start()
		{
			worldCenter = transform.position + EnvMesher.ChunkWorldSizeHalf;
		}

		private void OnDestroy()
		{
			colliderBake.Complete();
			Destroy(mesh);
			Destroy(backMesh);

			if (voxelSignBits.IsCreated)
				voxelSignBits.Dispose();
		}

		/// <summary>Drawn by <see cref="EnvMesher"/> in debug mode, so idle chunks cost no Update.</summary>
		internal void DrawDebug()
		{
			if (dirty)
				DrawChunkFrame(Color.white);
		}

		private void DrawChunkFrame(Color color)
		{
			XRGizmos.DrawWireCube(worldCenter, Quaternion.identity, EnvMesher.ChunkWorldSize,
				color);
		}

#if UNITY_EDITOR

		private void OnDrawGizmosSelected()
		{
			DrawChunkFrame(Color.aliceBlue);
		}

#endif
	}
}
