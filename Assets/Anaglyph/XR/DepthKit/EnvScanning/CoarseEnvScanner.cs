using System;
using Anaglyph.GPU;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
	/// <summary>
	/// Keeps a <see cref="CoarseEnvMap"/> of the playspace from the local depth sensor: a
	/// sparse grid of its rays a few times a second, as far as the sensor is trusted. Cells
	/// players stand in are kept empty
	/// </summary>
	public class CoarseEnvScanner : MonoBehaviour
	{
		public static CoarseEnvScanner Instance { get; private set; }

		// a player's bounds; anything larger is not a body
		private const float LargestPlayerMeters = 3f;

		[SerializeField] private ComputeShader compute;
		[SerializeField] private float cellMeters = 1f;
		[SerializeField] private int raysPerSide = 32;
		[SerializeField] private float secondsBetweenLooks = 0.25f;
		[Tooltip("Readings nearer than x are the player's own hands and say nothing. A reading past y only says nothing is nearer than y")]
		[SerializeField] private Vector2 trustedMeters = new(1f, 6f);

		private static readonly int raysPerSideID = Shader.PropertyToID("raysPerSide");
		private static readonly int rayEndsID = Shader.PropertyToID("rayEnds");

		private ComputeKernel captureKernel;
		private ComputeBuffer rayEnds;
		private NativeArray<float4> latestRayEnds;
		private bool hasLatestRayEnds;
		private bool looking;
		private float nextLookTime;
		private int clearCount;

		public CoarseEnvMap Map { get; private set; }

		/// <summary>Looks added to the map since it was last cleared.</summary>
		public int LookCount { get; private set; }

		/// <summary>Nearest and farthest sensor readings that count as surfaces, meters.</summary>
		public Vector2 TrustedMeters => trustedMeters;

		/// <summary>World positions the sensor's rays ended at in the latest look, w = 0 where it had no reading. Empty until the first look since the map was cleared.</summary>
		public ReadOnlySpan<float4> LatestRayEnds => hasLatestRayEnds ? latestRayEnds.AsReadOnlySpan() : default;

		/// <summary>The headset's pose is in the live map's frame, as the fine scan requires: only then do rays belong in the map.</summary>
		public bool PoseIsInMapFrame
		{
			get
			{
				EnvScanner scanner = EnvScanner.Instance;
				return scanner != null && !scanner.ScanIntoScratch && scanner.LocalSensorTrusted;
			}
		}

		private void Awake() => Instance = this;

		private void Start()
		{
			EnvScanner scanner = EnvScanner.Instance;
			if (compute == null || scanner == null)
			{
				Debug.LogError($"[{nameof(CoarseEnvScanner)}] needs its compute shader and an {nameof(EnvScanner)}", this);
				enabled = false;
				return;
			}

			float3 playspaceMeters = (float3)scanner.ChunkTableDims * scanner.ChunkWorldSizeDim;
			Map = new CoarseEnvMap(scanner.ChunkCoordToCornerWorldPos(int3.zero), playspaceMeters, cellMeters);
			scanner.Cleared += OnScanCleared;

			captureKernel = new ComputeKernel(compute, "CaptureRayEnds");
			rayEnds = new ComputeBuffer(raysPerSide * raysPerSide, sizeof(float) * 4);
			latestRayEnds = new NativeArray<float4>(raysPerSide * raysPerSide, Allocator.Persistent);
		}

		private void OnDestroy()
		{
			if (Instance == this) Instance = null;
			if (EnvScanner.Instance != null) EnvScanner.Instance.Cleared -= OnScanCleared;
			rayEnds?.Release();
			if (latestRayEnds.IsCreated) latestRayEnds.Dispose();
			Map?.Dispose();
			Map = null;
		}

		private void OnScanCleared()
		{
			Map.Clear();
			LookCount = 0;
			hasLatestRayEnds = false;
			clearCount++;
		}

		private void Update()
		{
			if (looking || Time.unscaledTime < nextLookTime) return;

			if (!DepthKitDriver.DepthAvailable || !PoseIsInMapFrame) return;

			nextLookTime = Time.unscaledTime + secondsBetweenLooks;
			Look();
		}

		private async void Look()
		{
			looking = true;
			try
			{
				DepthKitDriver depth = DepthKitDriver.Instance;
				float3 eye = (Vector3)depth.ViewInv[0].GetColumn(3);
				int clearCountAtCapture = clearCount;

				compute.SetMatrixArray(DepthKitDriver.projInvID, depth.ProjInv);
				compute.SetMatrixArray(DepthKitDriver.viewInvID, depth.ViewInv);
				compute.SetInt(raysPerSideID, raysPerSide);
				captureKernel.Bind(DepthKitDriver.depthTexID, depth.DepthTex);
				captureKernel.Bind(rayEndsID, rayEnds);
				captureKernel.DispatchFit(raysPerSide, raysPerSide);

				AsyncGPUReadbackRequest readback = await AsyncGPUReadback.RequestAsync(rayEnds);
				if (!this || readback.hasError || clearCountAtCapture != clearCount) return;

				readback.GetData<float4>().CopyTo(latestRayEnds);
				hasLatestRayEnds = true;
				Map.AddRays(eye, latestRayEnds, trustedMeters.x, trustedMeters.y);
				KeepPlayersEmpty();
				LookCount++;
			}
			catch (OperationCanceledException)
			{
			}
			finally
			{
				looking = false;
			}
		}

		// the sensor sees other players as surfaces; the masks that hide them from the fine
		// scan say where they are
		private void KeepPlayersEmpty()
		{
			foreach (EnvScanMask mask in EnvScanMask.All)
			{
				Bounds bounds = mask.Renderer.bounds;
				if (math.cmax(bounds.size) > LargestPlayerMeters) continue;
				Map.MarkEmpty(bounds.min, bounds.max);
			}
		}
	}
}
