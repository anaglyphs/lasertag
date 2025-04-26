using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.DepthKit;
using System.Collections.Generic;
using UnityEngine;

public class Mapper : MonoBehaviour
{
	public static List<Chunk> chunks = new();
	public static List<ChunkMesher> meshers = new();
	public float frequency = 30f;
	public float meshEverySeconds = 0.01f;

	private int viewID => DepthKitDriver.agDepthView_ID;
	private int projID => DepthKitDriver.agDepthProj_ID;
	private int depthTexID => DepthKitDriver.agDepthTex_ID;

	private Camera mainCam;
	private Plane[] frustum = new Plane[6];

	private void Awake()
	{
		mainCam = Camera.main;
	}

	private void OnEnable()
	{
		IntegrateLoop();
		MeshLoop();
	}

	private async void IntegrateLoop()
	{
		while (enabled)
		{
			await Awaitable.WaitForSecondsAsync(1f / frequency);

			var depthTex = Shader.GetGlobalTexture(DepthKitDriver.Meta_EnvironmentDepthTexture_ID);
			if (depthTex == null) continue;

			Matrix4x4 view = Shader.GetGlobalMatrixArray(viewID)[0];
			Matrix4x4 proj = Shader.GetGlobalMatrixArray(projID)[0];
			GeometryUtility.CalculateFrustumPlanes(proj * view, frustum);

			for (int i = 0; i < chunks.Count; i++)
			{
				Chunk chunk = chunks[i];
				var headPos = mainCam.transform.position;
				var chunkCenter = chunk.transform.position;
				var maxDist = chunk.MaxEyeDist + chunk.Bounds.extents.x;
				bool withinDist = Vector3.Distance(headPos, chunkCenter) < maxDist;

				if (GeometryUtility.TestPlanesAABB(frustum, chunk.Bounds) && withinDist)
					chunk.Integrate(depthTex, view, proj);
			}
		}
	}

	private async void MeshLoop()
	{
		await Awaitable.WaitForSecondsAsync(3);

		while(enabled)
		{
			for (int i = 0; i < meshers.Count; i++)
			{
				ChunkMesher mesher = meshers[i];
				if (mesher == null)
					break;

				if (mesher.ShouldUpdate)
				{
					mesher.BuildMesh();
					await Awaitable.WaitForSecondsAsync(meshEverySeconds);
				}
			}

			await Awaitable.NextFrameAsync();
		}
	}

	private static bool IsBoundsInsideFrustum(Plane[] frustumPlanes, Bounds bounds)
	{
		Vector3 center = bounds.center;
		Vector3 extents = bounds.extents;

		foreach (var plane in frustumPlanes)
		{
			// Compute the projection interval radius of b onto L(t) = b.c + t * p.n
			Vector3 normal = plane.normal;
			float r = extents.x * Mathf.Abs(normal.x) +
					  extents.y * Mathf.Abs(normal.y) +
					  extents.z * Mathf.Abs(normal.z);

			// Distance from box center to plane
			float s = Vector3.Dot(normal, center) + plane.distance;

			// If outside, box is completely outside this plane
			if (s + r < 0)
				return false;
		}

		return true; // Fully or partially inside
	}
}