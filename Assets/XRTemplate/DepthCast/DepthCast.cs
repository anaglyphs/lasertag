using Meta.XR.Depth;
using System;
using System.Runtime.InteropServices;
using UnityEngine;

/**
 * Based on code by Jude Tudor (? or Tudor Jude ?)
 * https://github.com/oculus-samples/Unity-DepthAPI/issues/16
 */

public struct DepthCastResult
{
	public float ZDepthDiff;
	public Vector3 Position;
	public Vector3 Normal;
}

[DefaultExecutionOrder(-30000)]
public class DepthCast : MonoBehaviour
{
	private const Camera.MonoOrStereoscopicEye Left = Camera.MonoOrStereoscopicEye.Left;
	private static readonly int raycastResultsId = Shader.PropertyToID("RaycastResults");
	private static readonly int raycastRequestsId = Shader.PropertyToID("RaycastRequests");
	private static readonly int EnvDepthTextureCS = Shader.PropertyToID("EnvDepthTextureCS");
	private static readonly int EnvDepthTextureSize = Shader.PropertyToID("EnvDepthTextureSize");
	private static readonly Vector2Int DefaultEnvironmentDepthTextureSize = new Vector2Int(2000, 2000);

	[SerializeField] private ComputeShader computeShader;
	[SerializeField] private EnvironmentDepthTextureProvider envDepthTextureProvider;
	public Vector2Int environmentDepthTextureSize = DefaultEnvironmentDepthTextureSize;

	public static Camera Camera;
	public static DepthCast Instance { get; private set; }

	private ComputeBuffer requestsCB;
	private ComputeBuffer resultsCB;

	private bool depthEnabled = false;

	/// <summary>
	/// 
	/// </summary>
	/// <param name="ray"></param>
	/// <param name="result"></param>
	/// <param name="maxLength"></param>
	/// <param name="minDotForVertical"></param>
	/// <returns></returns>
	public static bool Raycast(Ray ray, out DepthCastResult result, float maxLength = 30f, bool handRejection = false, float verticalThreshold = 0.9f, float ignoreNearOrigin = 0.2f) =>
		Instance.Raycast(ray, out result, maxLength, verticalThreshold, handRejection, ignoreNearOrigin);

	/// <summary>
	/// 
	/// </summary>
	/// <param name="ray"></param>
	/// <param name="result"></param>
	/// <param name="maxLength"></param>
	/// <param name="verticalThreshold"></param>
	/// <returns></returns>
	public bool Raycast(Ray ray, out DepthCastResult result,
		float maxLength = 10f, float verticalThreshold = 0.9f, bool handRejection = false, float ignoreNearOrigin = 0.2f)
	{
		result = default;

		if (!depthEnabled) return false;

		float rayStartDist = 0, rayEndDist = maxLength;

		// 'Crop' ray to camera frustum
		// TODO: Check if depth texture frustum may extend beyond camera frustum a bit... 
		Matrix4x4 projMat = Camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
		Matrix4x4 viewMat = Camera.worldToCameraMatrix;
		Plane[] planes = GeometryUtility.CalculateFrustumPlanes(projMat * viewMat);
		// Ordering: [0] = Left, [1] = Right, [2] = Down, [3] = Up, [4] = Near, [5] = Far
		//planes[1].distance -= 0.1f;
		Vector3 tolerance = Vector3.zero;
		bool rayInView = GetFrustumLineIntersection(planes, ray, tolerance, out rayStartDist, out rayEndDist);

		if (!rayInView) return false;

		rayStartDist = Mathf.Max(rayStartDist, 0);
		rayEndDist = Mathf.Min(rayEndDist, maxLength);

		Vector3 worldStart = ray.GetPoint(rayStartDist);
		Vector3 worldEnd = ray.GetPoint(rayEndDist);

		// number of samples along ray (pixel distance)
		Vector2 screenStart = Camera.WorldToScreenPoint(worldStart, Left);
		Vector2 screenEnd = Camera.WorldToScreenPoint(worldEnd, Left);
		int numDepthTextureSamples = (int)(Vector2.Distance(screenStart, screenEnd) / 2f);

		if (numDepthTextureSamples == 0)
			numDepthTextureSamples = 1;

		// Array of world positions along ray to send to GPU
		float worldRayStepDist = (rayEndDist - rayStartDist) / numDepthTextureSamples;
		Vector3[] worldRaySamples = new Vector3[numDepthTextureSamples];

		for (int i = 0; i < numDepthTextureSamples; i++)
			worldRaySamples[i] = ray.GetPoint(rayStartDist + worldRayStepDist * i);

		// Dispatch to GPU
		DepthCastResult[] depthResults = DispatchCompute(worldRaySamples);

		// Binary search for result where ray zdepth first exceeds environment zdepth
		// !!!!! this is not used because there may be multiple intersection points 
		// !!!!! but I always want to find the __first__!!!!!

		//int a = 1, b = depthResults.Length - 1;
		//int maxIterations = Mathf.CeilToInt(Mathf.Log(depthResults.Length, 2));

		//for (int i = 0; i < maxIterations; i++)
		//{
		//	int index = Mathf.CeilToInt((b + a) / 2f);

		//	result = depthResults[index];
		//	var prevResult = depthResults[index - 1];

		//	// ZDepthDiff = env zdepth - ray point zdepth
		//	// so if negative, the ray point is 'behind' the physical environment
		//	bool rayExceedsEnvDepth = result.ZDepthDiff < 0;

		//	// Check if this result is the first to intersect 
		//	if (rayExceedsEnvDepth && prevResult.ZDepthDiff > 0)
		//		return true;

		//	if (a >= b)
		//		return false;

		//	if (rayExceedsEnvDepth)
		//		b = Math.Max(index - 1, 1);
		//	else
		//		a = Math.Min(index + 1, depthResults.Length - 1);
		//}

		// Linear search
		for (int i = 0; i < depthResults.Length; i++)
		{
			result = depthResults[i];

			// ZDepthDiff = env zdepth - ray point zdepth
			// so if negative, the ray point is 'behind' the physical environment
			bool rayFurtherThanEnvDepth = result.ZDepthDiff < 0;

			if (!rayFurtherThanEnvDepth)
				continue;

			if (handRejection)
			{
				// Check that the hit poit isn't behind the ray origin along its normal
				Plane rayPlane = new();
				rayPlane.SetNormalAndPosition(ray.direction, ray.origin);
				bool pointBeforeRayOrigin = rayPlane.GetDistanceToPoint(result.Position) > 0;

				if (!pointBeforeRayOrigin)
					continue;

				// Check that the hit point isn't too close to the ray origin
				if (ignoreNearOrigin > 0 && Vector3.Distance(result.Position, ray.origin) < ignoreNearOrigin)
					continue;
			}

			// Straighten almost vertical hit normal for nice floor placement
			if (Mathf.Abs(Vector3.Dot(result.Normal, Vector3.up)) > verticalThreshold)
				result.Normal = Vector3.up * Mathf.Sign(result.Normal.y);

			return true;
		}

		return false;
	}

	private void Awake()
	{
		Instance = this;

#if UNITY_EDITOR
		requestsCB?.Release();
		requestsCB = null;
		resultsCB?.Release();
		resultsCB = null;
#endif

		if (envDepthTextureProvider == null)
		{
			envDepthTextureProvider = FindObjectOfType<EnvironmentDepthTextureProvider>(true);
		}
	}

	private void OnEnable()
	{
		Camera = Camera.main;
	}

	private void Update()
	{
		UpdateCurrentRenderingState();
	}

	private void OnDestroy()
	{
		requestsCB?.Release();
		resultsCB?.Release();
	}

	private void UpdateCurrentRenderingState()
	{
		depthEnabled = Unity.XR.Oculus.Utils.GetEnvironmentDepthSupported() &&
			envDepthTextureProvider != null &&
			envDepthTextureProvider.GetEnvironmentDepthEnabled();

		if (!depthEnabled) return;

		int depthTextureId = EnvironmentDepthTextureProvider.DepthTextureID;


		computeShader.SetTextureFromGlobal(0, EnvDepthTextureCS, depthTextureId);
		computeShader.SetInts(EnvDepthTextureSize, environmentDepthTextureSize.x, environmentDepthTextureSize.y);
	}

	private DepthCastResult[] DispatchCompute(Vector3[] requestedPositions)
	{
		int count = requestedPositions.Length;
		int threads = Mathf.CeilToInt(count / 32f);

		var (requestsCB, resultsCB) = GetComputeBuffers(count);
		requestsCB.SetData(requestedPositions);

		computeShader.SetBuffer(0, raycastRequestsId, requestsCB);
		computeShader.SetBuffer(0, raycastResultsId, resultsCB);

		computeShader.Dispatch(0, threads, 1, 1);

		var raycastResults = new DepthCastResult[count];
		resultsCB.GetData(raycastResults);

		return raycastResults;
	}

	private (ComputeBuffer, ComputeBuffer) GetComputeBuffers(int size)
	{
		if (requestsCB != null && resultsCB != null && requestsCB.count != size)
		{
			requestsCB.Release();
			requestsCB = null;
			resultsCB.Release();
			resultsCB = null;
		}

		if (requestsCB == null || resultsCB == null)
		{
			requestsCB = new ComputeBuffer(size, Marshal.SizeOf<Vector3>(),
				ComputeBufferType.Structured);
			resultsCB = new ComputeBuffer(size, Marshal.SizeOf<DepthCastResult>(),
				ComputeBufferType.Structured);
		}

		return (requestsCB, resultsCB);
	}

	// This function by Salvatore Previti 
	// https://gist.github.com/SalvatorePreviti/0ec6a73cb14cd33f12350ae27468f2e7
	public static bool GetFrustumLineIntersection(Plane[] frustum, Ray ray, Vector3 tolerance, out float d1, out float d2)
	{
		d1 = 0f;
		d2 = 0f;

		float d1Angle = 0f, d2Angle = 0f;
		bool d1Valid = false, d2Valid = false;

		for (int i = 0; i < frustum.Length; ++i)
		{

			// Find the angle between a frustum plane and the ray.
			var angle = Mathf.Abs(Vector3.Angle(frustum[i].normal, ray.direction) - 90f);
			if (angle < 2f)
				continue; // Ray almost parallel to the plane, skip the plane.

			if (angle < d1Angle && angle < d2Angle)
				continue; // The angle is smaller than a previous angle that was better, skip the plane.

			// Cast a ray onto the plane to find the distance from ray origin where it happens.
			// Compute also the direction the ray hits the plane, backward or forward (dir) ignoring the ray direction.
			float d;
			var dir = frustum[i].Raycast(ray, out d) ^ (frustum[i].GetDistanceToPoint(ray.origin) >= 0);

			// Update d1 or d2, depending on the direction.
			if (dir)
			{
				d1Angle = angle;
				if (!d1Valid || d > d1)
				{ // Choose the maximum value
					d1 = d;
					d1Valid = true;
				}
			}
			else
			{
				d2Angle = angle;
				if (!d2Valid || d < d2)
				{ // Choose the minimum value
					d2 = d;
					d2Valid = true;
				}
			}
		}

		if (!d1Valid || !d2Valid)
			return false; // Points are not valid.

		// Sort points

		if (d1 > d2)
		{
			var t = d1;
			d1 = d2;
			d2 = t;
		}

		// Check whether points are visible in the frustum.

		var p1 = ray.GetPoint(d1);
		var p2 = ray.GetPoint(d2);

		var bb = new Bounds();
		bb.SetMinMax(Vector3.Min(p1, p2) - tolerance, Vector3.Max(p1, p2) + tolerance);

		return GeometryUtility.TestPlanesAABB(frustum, bb);
	}

	private void OnValidate()
	{
		if (environmentDepthTextureSize == default)
		{
			environmentDepthTextureSize = DefaultEnvironmentDepthTextureSize;
		}
	}
}