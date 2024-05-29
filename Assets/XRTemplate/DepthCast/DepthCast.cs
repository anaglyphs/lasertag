using Meta.XR.Depth;
using Unity.XR.Oculus;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Diagnostics;


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

	[SerializeField] private ComputeShader computeShader;
	[SerializeField] private EnvironmentDepthTextureProvider envDepthTextureProvider;

	private static readonly Vector2Int DefaultEnvironmentDepthTextureSize = new Vector2Int(2000, 2000);

	public Vector2Int environmentDepthTextureSize = DefaultEnvironmentDepthTextureSize;

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
		Instance.RaycastBlocking(ray, out result, maxLength, verticalThreshold, handRejection, ignoreNearOrigin);

	/// <summary>
	/// 
	/// </summary>
	/// <param name="ray"></param>
	/// <param name="result"></param>
	/// <param name="maxLength"></param>
	/// <param name="verticalThreshold"></param>
	/// <returns></returns>
	public bool RaycastBlocking(Ray ray, out DepthCastResult result,
		float maxLength = 10f, float verticalThreshold = 0.9f, bool handRejection = false, float ignoreNearOrigin = 0.2f)
	{
		result = default;

		if (!depthEnabled) return false;

		float start = 0, end = maxLength;

		// Ignore steps along the ray outside of the camera bounds
		Matrix4x4 projMat = Camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
		Matrix4x4 viewMat = Camera.worldToCameraMatrix;
		Plane[] planes = GeometryUtility.CalculateFrustumPlanes(projMat * viewMat);
		// Ordering: [0] = Left, [1] = Right, [2] = Down, [3] = Up, [4] = Near, [5] = Far
		// need to nudge Right plane left a bit
		//planes[1].distance -= 0.1f;
		Vector3 tolerance = Vector3.zero;
		bool rayInView = GetFrustumLineIntersection(planes, ray, tolerance, out start, out end);

		if (!rayInView)
			return false;

		start = Mathf.Max(start, 0);
		end = Mathf.Min(end, maxLength);

		Vector3 worldStart = ray.GetPoint(start);
		Vector3 worldEnd = ray.GetPoint(end);

		// number of samples along ray (pixel distance)
		Vector2 screenStart = Camera.WorldToScreenPoint(worldStart, Left);
		Vector2 screenEnd = Camera.WorldToScreenPoint(worldEnd, Left);
		int numDepthTextureSamples = (int)(Vector2.Distance(screenStart, screenEnd) / 2f);

		if (numDepthTextureSamples == 0)
			numDepthTextureSamples = 1;

		float distStep = (end - start) / numDepthTextureSamples;

		List<Vector3> worldSamples = new List<Vector3>(numDepthTextureSamples);

		for (int i = 0; i < numDepthTextureSamples; i++)
		{
			worldSamples.Add(ray.GetPoint(start + distStep * i));
		}

		if (worldSamples.Count == 0)
			return false;

		DepthCastResult[] results = DispatchCompute(worldSamples);

		for (int i = 0; i < results.Length; i++)
		{
			result = results[i];

			bool intersectsDepth = result.ZDepthDiff < 0;

			if (!intersectsDepth)
				continue;

			if (handRejection)
			{
				Plane rayPlane = new();
				rayPlane.SetNormalAndPosition(ray.direction, ray.origin);
				bool pointBeforeRayOrigin = rayPlane.GetDistanceToPoint(result.Position) > 0;

				if (!pointBeforeRayOrigin)
					continue;

				if (ignoreNearOrigin > 0 && Vector3.Distance(result.Position, ray.origin) < ignoreNearOrigin)
					continue;
			}

			if (Mathf.Abs(Vector3.Dot(result.Normal, Vector3.up)) > verticalThreshold)
				result.Normal = Vector3.up * Mathf.Sign(result.Normal.y);

			return true;
		}

		return false;
	}

	private ComputeBuffer requestsCB;
	private ComputeBuffer resultsCB;

	public static Camera Camera { get; private set; }

	public static DepthCast Instance { get; private set; }

	private void Awake() {

		Instance = this;

		requestsCB?.Release();
		requestsCB = null;
		resultsCB?.Release();
		resultsCB = null;

		if(envDepthTextureProvider == null)
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

		if (!depthEnabled)
			return;

		int depthTextureId = EnvironmentDepthTextureProvider.DepthTextureID;


		computeShader.SetTextureFromGlobal(0, EnvDepthTextureCS, depthTextureId);
		computeShader.SetInts(EnvDepthTextureSize, environmentDepthTextureSize.x, environmentDepthTextureSize.y);
	}

	private DepthCastResult[] DispatchCompute(List<Vector3> requestedPositions)
	{
		int count = requestedPositions.Count;
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
		if(environmentDepthTextureSize == default)
		{
			environmentDepthTextureSize = DefaultEnvironmentDepthTextureSize;
		}
	}
}