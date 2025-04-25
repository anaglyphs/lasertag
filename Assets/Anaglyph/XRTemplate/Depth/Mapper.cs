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

			var depthTex = Shader.GetGlobalTexture(depthTexID);
			if (depthTex == null) return;

			Matrix4x4 view = Shader.GetGlobalMatrixArray(viewID)[0];
			Matrix4x4 proj = Shader.GetGlobalMatrixArray(projID)[0];
			GeometryUtility.CalculateFrustumPlanes(proj * view, frustum);

			foreach (var chunk in chunks)
			{
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
		while(enabled)
		{
			foreach (var mesher in meshers)
			{
				if (mesher.ShouldUpdate)
				{
					mesher.BuildMesh();
					await Awaitable.WaitForSecondsAsync(meshEverySeconds);
				}
			}

			await Awaitable.NextFrameAsync();
		}
	}
}