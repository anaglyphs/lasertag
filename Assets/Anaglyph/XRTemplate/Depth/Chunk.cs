using Anaglyph.XRTemplate.DepthKit;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Anaglyph.XRTemplate
{
	public class Chunk : MonoBehaviour
	{
		[SerializeField] private ComputeShader shader = null;
		[SerializeField] private float metersPerVoxel = 0.1f;
		[SerializeField] private int size = 128;

		[SerializeField] private RenderTexture volume;
		public Bounds Bounds { get; private set; }
		
		[SerializeField] private float maxEyeDist = 7f;
		public float MaxEyeDist => maxEyeDist;

		private ComputeKernel clearKernel;
		private ComputeKernel integrateKernel;

		private int viewID => DepthKitDriver.agDepthView_ID;
		private int projID => DepthKitDriver.agDepthProj_ID;

		private int viewInvID => DepthKitDriver.agDepthViewInv_ID;
		private int projInvID => DepthKitDriver.agDepthProjInv_ID;

		private int depthTexID => DepthKitDriver.agDepthTex_ID;

		private int chunkPosID = Shader.PropertyToID("chunkPos");

		//private int numPlayersID = Shader.PropertyToID("numPlayers");		
		//private int playerHeadsWorldID = Shader.PropertyToID("playerHeadsWorld");

		//public List<Transform> PlayerHeads = new();
		//private Vector4[] headPositions = new Vector4[512];

		private void Awake()
		{
			RenderTextureDescriptor desc = new RenderTextureDescriptor()
			{
				dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
				width = size,
				height = size,
				volumeDepth = size,
				mipCount = 1,
				msaaSamples = 1,
				graphicsFormat = GraphicsFormat.R8_SNorm,
				enableRandomWrite = true,
			};

			volume = new RenderTexture(desc);

			Mapper.chunks.Add(this);
		}

		private void OnDestroy()
		{
			Mapper.chunks.Remove(this);
		}

		private void Start()
		{
			float sizeMeters = size * metersPerVoxel;
			Bounds = new(transform.position, Vector3.one * sizeMeters);

			clearKernel = new(shader, "Clear");
			integrateKernel = new(shader, "Integrate");

			//raycastKernel = new(shader, "Raycast");
			//raycastKernel.Set("rcVolume", volume);

			Clear();
		}

		public void Clear()
		{
			clearKernel.Set(nameof(volume), volume);
			clearKernel.DispatchGroups(volume);
		}

		public void Integrate()
		{
			var depthTex = Shader.GetGlobalTexture(depthTexID);
			if (depthTex == null) return;

			Matrix4x4 view = Shader.GetGlobalMatrixArray(viewID)[0];
			Matrix4x4 proj = Shader.GetGlobalMatrixArray(projID)[0];

			var planes = GeometryUtility.CalculateFrustumPlanes(view * proj);

			if(GeometryUtility.TestPlanesAABB(planes, Bounds))
				ApplyScan(depthTex, view, proj);
		}

		public void ApplyScan(Texture depthTex, Matrix4x4 view, Matrix4x4 proj)//, bool useDepthFrame)
		{
			shader.SetInts("volumeSize", size, size, size);
			shader.SetFloat(nameof(metersPerVoxel), metersPerVoxel);

			shader.SetMatrixArray(viewID, new[]{ view, Matrix4x4.zero });
			shader.SetMatrixArray(projID, new[]{ proj, Matrix4x4.zero });

			shader.SetMatrixArray(viewInvID, new[] { view.inverse, Matrix4x4.zero });
			shader.SetMatrixArray(projInvID, new[] { proj.inverse, Matrix4x4.zero });

			//for(int i = 0; i < PlayerHeads.Count; i++)
			//{
			//	Vector3 playerHead = PlayerHeads[i].position;
			//	headPositions[i] = playerHead;
			//}

			//shader.SetInt(numPlayersID, PlayerHeads.Count);
			//shader.SetVectorArray(playerHeadsWorldID, headPositions);

			transform.rotation = Quaternion.identity;
			transform.localScale = Vector3.one;

			shader.SetVector(chunkPosID, transform.position);

			integrateKernel.Set(depthTexID, depthTex);
			integrateKernel.Set(nameof(volume), volume);
			integrateKernel.DispatchGroups(size, size, size);
		}

		//private const float RaycastScaleFactor = 1000f;

		//public struct RayResult
		//{
		//	public Vector3 point;
		//	public float distance;
		//	public bool didHit;

		//	public RayResult(Vector3 hitPoint, float distance)
		//	{
		//		this.point = hitPoint;
		//		this.distance = distance;
		//		this.didHit = false;
		//	}
		//}

		//private bool RaycastInternal(Ray ray, float maxDist, out RayResult result, bool fallback)
		//{
		//	result = new(ray.origin, 0);
		//	if (maxDist == 0)
		//		return false;

		//	if (!DepthKitDriver.DepthAvailable && fallback)
		//	{
		//		// floor cast if depth isn't available

		//		var orig = ray.origin;
		//		var dir = ray.direction;
		//		Vector2 slope = new Vector2(dir.x, dir.z) / dir.y;

		//		result.point = new Vector3(slope.x * -orig.y + orig.x, 0, slope.y * -orig.y + orig.z);
		//		result.distance = Vector3.Distance(orig, result.point);

		//		return true;
		//	}

		//	shader.SetVector("rcOrig", ray.origin);
		//	shader.SetVector("rcDir", ray.direction);
		//	shader.SetFloat("rcIntScale", RaycastScaleFactor);

		//	uint lengthInt = (uint)(maxDist * RaycastScaleFactor);

		//	ComputeBuffer resultBuffer = new ComputeBuffer(1, sizeof(uint));
		//	resultBuffer.SetData(new uint[] { lengthInt });
		//	raycastKernel.Set("rcResult", resultBuffer);

		//	int totalNumSteps = Mathf.RoundToInt(maxDist / metersPerVoxel);

		//	if (totalNumSteps == 0)
		//		return false;

		//	raycastKernel.DispatchGroups(totalNumSteps, 1, 1);

		//	//var request = await AsyncGPUReadback.Request(resultBuffer);
		//	//if (request.hasError)
		//	//	return hit;
		//	//var result = request.GetData<uint>();
		//	uint[] d = new uint[1];
		//	resultBuffer.GetData(d);
		//	uint hitDistInt = d[0];
		//	//result.Dispose();
		//	resultBuffer.Release();

		//	if (hitDistInt >= lengthInt)
		//		return false;

		//	float hitDist = hitDistInt / RaycastScaleFactor;

		//	if (hitDist >= maxDist)
		//		return false;

		//	result = new(ray.GetPoint(hitDist), hitDist);
		//	result.didHit = true;
		//	return true;
		//}
	}
}
