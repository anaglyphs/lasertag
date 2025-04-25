using Anaglyph.XRTemplate.DepthKit;
using System;
using UnityEngine;

namespace Anaglyph.XRTemplate
{
	public class Chunk : MonoBehaviour
	{
		[SerializeField] private ComputeShader shader = null;
		[SerializeField] private float metersPerVoxel = 0.1f;
		public float MetersPerVoxel => metersPerVoxel;
		[SerializeField] private float distTruncate = 0.2f;
		[SerializeField] private int volumeSize = 32;
		public int Size => volumeSize;

		private ComputeBuffer volume;
		public ComputeBuffer Volume => volume;

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

		private int maxEyeDistID = Shader.PropertyToID(nameof(maxEyeDist));
		private int chunkPosID = Shader.PropertyToID("chunkPos");

		private int volumeSizeID = Shader.PropertyToID(nameof(volumeSize));
		private int metersPerVoxelID = Shader.PropertyToID(nameof(metersPerVoxel));
		private int distTruncateID = Shader.PropertyToID(nameof(distTruncate));

		//private int numPlayersID = Shader.PropertyToID("numPlayers");		
		//private int playerHeadsWorldID = Shader.PropertyToID("playerHeadsWorld");

		//public List<Transform> PlayerHeads = new();
		//private Vector4[] headPositions = new Vector4[512];

		public Action OnIntegrate = delegate { };

		private void Awake()
		{
			Mapper.chunks.Add(this);
		}

		private void OnDestroy()
		{
			if(volume != null)
				volume.Dispose();

			Mapper.chunks.Remove(this);
		}

		private void Start()
		{
			float sizeMeters = volumeSize * metersPerVoxel;
			Bounds = new(transform.position, Vector3.one * sizeMeters);

			integrateKernel = new(shader, "Integrate");
			clearKernel = new(shader, "Clear");

			//raycastKernel = new(shader, "Raycast");
			//raycastKernel.Set("rcVolume", volume);

		}

		public void Clear()
		{
			clearKernel.Set(nameof(volume), volume);
			clearKernel.DispatchGroups(volumeSize, volumeSize, volumeSize);
		}

		public void Integrate(Texture depthTex, Matrix4x4 view, Matrix4x4 proj)//, bool useDepthFrame)
		{
			if (volume == null)
			{
				volume = new ComputeBuffer(volumeSize * volumeSize * volumeSize, 4);
				Clear();
			}

			shader.SetInts(volumeSizeID, volumeSize, volumeSize, volumeSize);
			shader.SetFloat(metersPerVoxelID, metersPerVoxel);
			shader.SetFloat(distTruncateID, distTruncate);
			shader.SetFloat(maxEyeDistID, maxEyeDist);

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
			integrateKernel.DispatchGroups(volumeSize, volumeSize, volumeSize);

			OnIntegrate?.Invoke();
		}

		/**
		private const float RaycastScaleFactor = 1000f;

		public struct RayResult
		{
			public Vector3 point;
			public float distance;
			public bool didHit;

			public RayResult(Vector3 hitPoint, float distance)
			{
				this.point = hitPoint;
				this.distance = distance;
				this.didHit = false;
			}
		}

		private bool RaycastInternal(Ray ray, float maxDist, out RayResult result, bool fallback)
		{
			result = new(ray.origin, 0);
			if (maxDist == 0)
				return false;

			if (!DepthKitDriver.DepthAvailable && fallback)
			{
				// floor cast if depth isn't available

				var orig = ray.origin;
				var dir = ray.direction;
				Vector2 slope = new Vector2(dir.x, dir.z) / dir.y;

				result.point = new Vector3(slope.x * -orig.y + orig.x, 0, slope.y * -orig.y + orig.z);
				result.distance = Vector3.Distance(orig, result.point);

				return true;
			}

			shader.SetVector("rcOrig", ray.origin);
			shader.SetVector("rcDir", ray.direction);
			shader.SetFloat("rcIntScale", RaycastScaleFactor);

			uint lengthInt = (uint)(maxDist * RaycastScaleFactor);

			ComputeBuffer resultBuffer = new ComputeBuffer(1, sizeof(uint));
			resultBuffer.SetData(new uint[] { lengthInt });
			raycastKernel.Set("rcResult", resultBuffer);

			int totalNumSteps = Mathf.RoundToInt(maxDist / metersPerVoxel);

			if (totalNumSteps == 0)
				return false;

			raycastKernel.DispatchGroups(totalNumSteps, 1, 1);

			//var request = await AsyncGPUReadback.Request(resultBuffer);
			//if (request.hasError)
			//	return hit;
			//var result = request.GetData<uint>();
			uint[] d = new uint[1];
			resultBuffer.GetData(d);
			uint hitDistInt = d[0];
			//result.Dispose();
			resultBuffer.Release();

			if (hitDistInt >= lengthInt)
				return false;

			float hitDist = hitDistInt / RaycastScaleFactor;

			if (hitDist >= maxDist)
				return false;

			result = new(ray.GetPoint(hitDist), hitDist);
			result.didHit = true;
			return true;
		}
		**/
	}
}
