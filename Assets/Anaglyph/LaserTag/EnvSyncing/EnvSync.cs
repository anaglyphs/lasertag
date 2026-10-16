using System;
using System.Collections.Generic;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Netcode;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR.DepthKit.EnvScanning.Encoding;
using Unity.Collections;
using UnityEngine;

namespace Anaglyph.LaserTag.EnvSyncing
{
	/// <summary>
	/// Shares the environment scan between headsets as small depth frames. Each headset
	/// integrates its own sensor at full resolution and broadcasts a downsampled frame
	/// of the same view when that scan changed its volume enough that peers need it;
	/// frames held back are folded into the next sent frame's weight. Late joiners pull
	/// the authority's chunks whole as a snapshot.
	/// </summary>
	public class EnvSync : MonoBehaviour
	{
		public static EnvSync Instance { get; private set; }

		// Direct events: frames are fire-and-forget, per-sender ordered, and need no
		// ordering against other synced state
		private readonly SyncEventBytes frameEvent = new("env.frames", EventRoute.Direct);
		private readonly SyncEventBytes chunkEvent = new("env.chunks", EventRoute.Direct);
		private readonly SyncEvent<MapSpaceScanContext> snapshotRequest = new("env.snapshot", EventRoute.ToAuthority);
		private readonly SyncEvent<bool> visibleEvent = new("env.visible", EventRoute.Direct);

		// NGO caps fragmented messages at 64000 bytes; leave headroom for headers.
		private const int MaxPayloadBytes = 60000;

		private const uint FramePayloadMagic = 0x4D415246; // "FRAM"
		private const uint ChunkPayloadMagic = 0x4B4E4843; // "CHNK"

		// [magic][MapSpaceScanContext][frame index] then the codec's bytes
		private const int ContextOffset = sizeof(uint);
		private const int ContextBytes = 48;
		private const int FrameIndexOffset = ContextOffset + ContextBytes;
		private const int FramePayloadHeaderBytes = FrameIndexOffset + sizeof(uint);

		// [magic][MapSpaceScanContext][chunk index] then the RLE bytes
		private const int ChunkIndexOffset = ContextOffset + ContextBytes;
		private const int ChunkPayloadHeaderBytes = ChunkIndexOffset + sizeof(int);

		[Header("Frame sending")]
		// summed quantized voxel change a single frame must cause before it is worth
		// sending. below this the frame is integrated locally only
		[SerializeField] private uint frameChangeThreshold = 64;

		// change accumulated by frames NOT sent. once it crosses this, the next frame
		// goes out regardless, so a slowly changing scene cannot drift away unsent
		[SerializeField] private uint unsentChangeThreshold = 512;

		// floor between sends. each player sends in its own phase of this period so
		// two headsets dry-running the same wall rarely both send
		[SerializeField] private float minSecondsBetweenSends = 0.2f;

		[SerializeField] private DepthFrameCompression compression = DepthFrameCompression.PredictedDeflate;

		[Header("Snapshots")]
		// chunks encoded and sent per Unity frame while serving a joiner
		[SerializeField] private int snapshotChunksPerFrame = 2;

		private uint unsentChange;
		// local frames integrated since the last send, folded into the next sent frame's weight
		private int unsentFrames;
		private long lastSentSlot = long.MinValue;
		private uint sentFrameIndex;

		private NativeArray<byte> receivedCodes;
		private NativeArray<byte> chunkEncoding;
		private NativeArray<EnvScanner.Voxel> receivedVoxels;

		// Invalidates work that outlives a network session or a scan.
		private int syncGeneration;
		private Guid requestedScan;

		// per-second traffic report for development builds: what went out, what came in,
		// and why anything was dropped. silent when nothing happened
		private int statSent, statSentBytes, statReceived, statReceivedBytes, statChunksSent, statChunksReceived, statHeld;
		private readonly Dictionary<string, int> statDrops = new();
		private float nextStatReport;

		private bool snapshotServing;
		private readonly Queue<(ulong client, MapSpaceScanContext context)> snapshotQueue = new();
		private readonly Dictionary<ulong, float> lastSnapshotRequest = new();

		// Scans are shared under every colocation method, system-determined included: the
		// map coordinator's integration gate already decides whether the local frame is
		// trusted. Peers only need to agree on the method family they are aligned with
		private bool CanShareScan => ColocationManager.Instance != null &&
			(ColocationManager.UsesSavedReferences(ColocationManager.Instance.ActiveMethod) && ColocationManager.UsesSavedReferences(ColocationManager.Instance.SelectedMethod) ||
			 ColocationManager.Instance.ActiveMethod == ColocationManager.Instance.SelectedMethod);
		private bool CanSendScan => CanShareScan && LaserTagMapCoordinator.Instance != null && LaserTagMapCoordinator.Instance.CanIntegrateEnvironment;
		private MapSpaceScanContext CurrentFrame => new() { space = LaserTagMapCoordinator.Instance?.SessionSpaceId ?? Guid.Empty,
			frame = LaserTagMapCoordinator.Instance?.CanonicalFrameId ?? Guid.Empty, scan = LaserTagMapCoordinator.Instance?.ScanContext ?? Guid.Empty };
		private bool FrameMatches(MapSpaceScanContext frame) => frame.Matches(CurrentFrame);

		private void Awake()
		{
			Instance = this;

			frameEvent.Register(); frameEvent.Received += OnFrameReceived;
			chunkEvent.Register(); chunkEvent.Received += OnChunkReceived;
			snapshotRequest.Register(); snapshotRequest.Received += OnSnapshotRequested;
			visibleEvent.Register(); visibleEvent.Received += OnVisibleReceived;

			SyncBus.Activated += OnBusActivated;
			SyncBus.Deactivated += OnBusDeactivated;
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			ColocationManager.Colocated += OnColocated;
		}

		private void Start()
		{
			LaserTagMapCoordinator.WorldFrameRebased += OnWorldFrameRebased;

			if (EnvScanner.Instance != null)
			{
				EnvScanner.Instance.Cleared += OnScanCleared;
				EnvScanner.Instance.FrameCaptured += OnFrameCaptured;
				EnvScanner.Instance.IntegrationGate = () => LaserTagMapCoordinator.Instance != null && LaserTagMapCoordinator.Instance.CanIntegrateEnvironment;
			}
		}

		private void OnDestroy()
		{
			LaserTagMapCoordinator.WorldFrameRebased -= OnWorldFrameRebased;

			if (EnvScanner.Instance != null)
			{
				EnvScanner.Instance.Cleared -= OnScanCleared;
				EnvScanner.Instance.FrameCaptured -= OnFrameCaptured;
				EnvScanner.Instance.IntegrationGate = null;
			}

			syncGeneration++;

			if (receivedCodes.IsCreated) receivedCodes.Dispose();
			if (chunkEncoding.IsCreated) chunkEncoding.Dispose();
			if (receivedVoxels.IsCreated) receivedVoxels.Dispose();

			SyncBus.Activated -= OnBusActivated;
			SyncBus.Deactivated -= OnBusDeactivated;
			frameEvent.Received -= OnFrameReceived; frameEvent.Unregister();
			chunkEvent.Received -= OnChunkReceived; chunkEvent.Unregister();
			snapshotRequest.Received -= OnSnapshotRequested; snapshotRequest.Unregister();
			visibleEvent.Received -= OnVisibleReceived; visibleEvent.Unregister();
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			ColocationManager.Colocated -= OnColocated;
		}

		private void ResetSendState()
		{
			unsentChange = 0;
			unsentFrames = 0;
			lastSentSlot = long.MinValue;
			requestedScan = Guid.Empty;
			snapshotQueue.Clear();
			lastSnapshotRequest.Clear();
		}

		/// <summary>A cleared scan is a new volume; nothing remembered about the old one applies.</summary>
		private void OnScanCleared()
		{
			syncGeneration++;
			ResetSendState();
		}

		/// <summary>
		/// The map changed under us, so world space now sits somewhere else and the scan
		/// describes the room in the wrong place. Every peer drops its own — including the
		/// frames it received, which were measured in that same outgoing frame.
		/// </summary>
		private void OnWorldFrameRebased()
		{
			syncGeneration++;
			ResetSendState();

			if (EnvScanner.Instance != null)
				EnvScanner.Instance.Clear();
		}

		private void OnColocated(bool isColocated) => HandleScannerActivity();
		private void OnNetcodeStateChanged(NetcodeState state) => HandleScannerActivity();

		private void HandleScannerActivity()
		{
			// Scanner is only enabled if connected & collocated OR disconnected
			if (EnvMesher.Instance) EnvMesher.Instance.enabled = LaserTagMapCoordinator.Instance != null && LaserTagMapCoordinator.Instance.CanIntegrateEnvironment;
		}

		private void OnBusActivated()
		{
			syncGeneration++;
			ResetSendState();

			// Don't reset if authority, because the authority determines the coordinate system
			// so their scan will be aligned with the environment
			if (!SyncBus.IsAuthority)
				EnvScanner.Instance.Clear();
		}

		private void OnBusDeactivated()
		{
			syncGeneration++;
			ResetSendState();
		}

		public void SetEnvMeshVisibleEveryone(bool visible)
		{
			visibleEvent.Raise(visible);
		}

		private void OnVisibleReceived(ulong sender, bool visible)
		{
			EnvMesher.Instance.SetChunksVisible(visible);
		}

		private void Update()
		{
			HandleScannerActivity();

			var frame = CurrentFrame;
			if (SyncBus.Active && !SyncBus.IsAuthority && frame.space != Guid.Empty && requestedScan != frame.scan &&
			    LaserTagMapCoordinator.Instance.Phase is MapPhase.FollowingSession)
			{
				requestedScan = frame.scan;
				snapshotRequest.Raise(frame);
				Debug.Log($"[{nameof(EnvSync)}] requested a scan snapshot from the authority");
			}

			if (!snapshotServing && snapshotQueue.Count > 0)
				ServeSnapshots(syncGeneration);

			ReportStats();
		}

		private void Drop(string reason)
		{
			statDrops.TryGetValue(reason, out int n);
			statDrops[reason] = n + 1;
		}

		private void ReportStats()
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (Time.unscaledTime < nextStatReport) return;
			nextStatReport = Time.unscaledTime + 1f;

			// quiet off-network; in a session the line always prints so a stalled sync shows
			// its gating state instead of nothing
			if (!SyncBus.Active && statSent == 0 && statReceived == 0 && statChunksSent == 0 && statChunksReceived == 0 && statDrops.Count == 0) return;

			var coordinator = LaserTagMapCoordinator.Instance;
			var sb = new System.Text.StringBuilder($"[{nameof(EnvSync)}] frames out {statSent} ({statSentBytes}B) in {statReceived} ({statReceivedBytes}B), held {statHeld}, chunks out {statChunksSent} in {statChunksReceived}");
			foreach (var drop in statDrops) sb.Append($", dropped {drop.Value} ({drop.Key})");
			sb.Append($" | bus={SyncBus.Active} authority={SyncBus.IsAuthority} phase={coordinator?.Phase} colocated={ColocationManager.IsColocated} share={CanShareScan} send={CanSendScan} integrate={coordinator?.CanIntegrateEnvironment} space={(CurrentFrame.space != Guid.Empty)} snapshotRequested={(requestedScan != Guid.Empty)} snapshotQueue={snapshotQueue.Count}");
			Debug.Log(sb.ToString());

			statSent = statSentBytes = statReceived = statReceivedBytes = statChunksSent = statChunksReceived = statHeld = 0;
			statDrops.Clear();
#endif
		}

		// ---- frames ------------------------------------------------------------

		/// <summary>
		/// The scanner integrated the local sensor frame and measured what it did to the
		/// volume. Send it if peers need it: a big change, or enough small ones held back
		/// </summary>
		private void OnFrameCaptured(EnvScanner.CapturedFrame captured)
		{
			if (!SyncBus.Active) { unsentChange = 0; unsentFrames = 0; return; }
			if (!CanSendScan || !ColocationManager.IsColocated)
			{
				unsentChange = 0;
				unsentFrames = 0;
				Drop("local frame: cannot send (not colocated / not shareable / gate closed)");
				return;
			}

			unsentChange += captured.change;
			unsentFrames++;

			// sends sit in per-player slots of the period, so headsets looking at the same
			// surface tend to take turns rather than both sending the first frame they see
			double period = Math.Max(minSecondsBetweenSends, 0.01f);
			double phase = SyncBus.LocalClientId % 8 / 8.0 * period;
			long slot = (long)Math.Floor((Time.unscaledTimeAsDouble - phase) / period);
			if (slot == lastSentSlot) return;

			if (captured.change < frameChangeThreshold && unsentChange < unsentChangeThreshold) { statHeld++; return; }

			// peers integrate this frame once, with the weight of every local frame it stands
			// for, so their voxels move and settle as far as ours did. capped at the settle
			// count, beyond which extra weight changes nothing
			DepthFrameHeader header = captured.header;
			header.weight = (ushort)Math.Clamp(unsentFrames, 1, MaxFrameWeight);

			byte[] encoded = DepthFrameCodec.Encode(header, captured.codes, compression);
			int payloadSize = FramePayloadHeaderBytes + encoded.Length;

			if (payloadSize > MaxPayloadBytes)
			{
				Debug.LogWarning($"[{nameof(EnvSync)}] Frame payload ({payloadSize}B) exceeds the fragmented message cap");
				return;
			}

			byte[] payload = new byte[payloadSize];
			SyncBytes.Write(payload, 0, FramePayloadMagic);
			WriteContext(payload, CurrentFrame);
			SyncBytes.Write(payload, FrameIndexOffset, ++sentFrameIndex);
			Buffer.BlockCopy(encoded, 0, payload, FramePayloadHeaderBytes, encoded.Length);

			frameEvent.Raise(payload);
			statSent++; statSentBytes += payload.Length;

			lastSentSlot = slot;
			unsentChange = 0;
			unsentFrames = 0;
		}

		// matches maxIntegCountSettle in EnvScanner.compute
		private const int MaxFrameWeight = 16;

		private void OnFrameReceived(ulong sender, byte[] payload)
		{
			// Direct events also invoke locally; the scanner integrated the full-res sensor
			// this frame was captured from
			if (sender == SyncBus.LocalClientId) return;
			if (!CanShareScan) { Drop("frame: colocation method not shareable"); return; }
			if (!EnvScanner.Instance) { Drop("frame: no scanner"); return; }

			if (payload.Length < FramePayloadHeaderBytes || payload.Length > MaxPayloadBytes) { Drop("frame: bad size"); return; }
			if (SyncBytes.Read<uint>(payload, 0) != FramePayloadMagic) { Drop("frame: bad magic"); return; }
			if (!FrameMatches(ReadContext(payload))) { Drop("frame: space/frame/scan context mismatch"); return; }

			if (!DepthFrameCodec.TryReadHeader(payload, FramePayloadHeaderBytes, out DepthFrameHeader header)) { Drop("frame: bad header"); return; }

			if (!receivedCodes.IsCreated || receivedCodes.Length != header.PackedBytes)
			{
				if (receivedCodes.IsCreated) receivedCodes.Dispose();
				receivedCodes = new NativeArray<byte>(header.PackedBytes, Allocator.Persistent);
			}

			if (!DepthFrameCodec.Decode(payload, FramePayloadHeaderBytes, header, receivedCodes))
			{
				Drop("frame: decode failed");
				return;
			}

			if (EnvScanner.Instance.IntegrateFrame(header, receivedCodes)) { statReceived++; statReceivedBytes += payload.Length; }
			else Drop("frame: scanner refused");
		}

		// ---- snapshots ---------------------------------------------------------

		private void OnSnapshotRequested(ulong sender, MapSpaceScanContext context)
		{
			if (sender == SyncBus.LocalClientId) return;
			if (!SyncBus.IsAuthority) { Drop("snapshot request: not authority"); return; }
			if (!CanShareScan) { Drop("snapshot request: not shareable"); return; }
			if (!FrameMatches(context)) { Drop("snapshot request: context mismatch"); return; }
			if (lastSnapshotRequest.TryGetValue(sender, out float time) && Time.unscaledTime - time < 5) return;
			lastSnapshotRequest[sender] = Time.unscaledTime;

			Debug.Log($"[{nameof(EnvSync)}] serving a scan snapshot to client {sender}");
			snapshotQueue.Enqueue((sender, context));
		}

		/// <summary>
		/// Reads back every chunk this headset holds and sends each to the requester,
		/// a few per Unity frame so the GPU and the wire are never saturated
		/// </summary>
		private async void ServeSnapshots(int generation)
		{
			snapshotServing = true;
			EnvScanner scanner = EnvScanner.Instance;
			EnvScanner.ChunkReadbackBuffer readback = scanner.CreateChunkReadbackBuffer();
			NativeList<byte> encoding = new(Allocator.Persistent);

			try
			{
				while (generation == syncGeneration && SyncBus.Active && snapshotQueue.TryDequeue(out var request))
				{
					List<int> chunkIndices = new();
					foreach (Chunk chunk in EnvMesher.Instance.ChunksList) chunkIndices.Add(chunk.chunkIndex);

					int sentThisFrame = 0;

					foreach (int chunkIndex in chunkIndices)
					{
						if (generation != syncGeneration || !SyncBus.Active || !FrameMatches(request.context)) break;

						bool ok = await scanner.ReadbackChunkInto(chunkIndex, readback);
						if (!this || generation != syncGeneration) return;
						if (!ok) continue;

						await VolumeRLE.EncodeChunk(readback.data, encoding);
						if (!this || generation != syncGeneration) return;

						// a fully observed chunk's run-length data is ~64KB of near-identical
						// pairs, over the message cap; deflated it is a few KB
						byte[] rle = encoding.AsArray().ToArray();
						byte[] deflated = DepthFrameCodec.Deflate(rle, 0, rle.Length);

						int payloadSize = ChunkPayloadHeaderBytes + deflated.Length;
						if (payloadSize > MaxPayloadBytes)
						{
							Debug.LogWarning($"[{nameof(EnvSync)}] Chunk {chunkIndex} snapshot ({payloadSize}B) exceeds the fragmented message cap");
							continue;
						}

						byte[] payload = new byte[payloadSize];
						SyncBytes.Write(payload, 0, ChunkPayloadMagic);
						WriteContext(payload, request.context);
						SyncBytes.Write(payload, ChunkIndexOffset, chunkIndex);
						Buffer.BlockCopy(deflated, 0, payload, ChunkPayloadHeaderBytes, deflated.Length);

						chunkEvent.RaiseTo(request.client, payload);
						statChunksSent++;

						if (++sentThisFrame >= snapshotChunksPerFrame)
						{
							sentThisFrame = 0;
							await Awaitable.NextFrameAsync();
						}
					}
				}
			}
			catch (Exception e)
			{
				Debug.LogException(e, this);
			}
			finally
			{
				readback.Dispose();
				encoding.Dispose();
				snapshotServing = false;
			}
		}

		private async void OnChunkReceived(ulong sender, byte[] payload)
		{
			if (sender == SyncBus.LocalClientId) return;
			if (!CanShareScan || !EnvScanner.Instance) { Drop("chunk: not shareable"); return; }

			if (payload.Length < ChunkPayloadHeaderBytes || payload.Length > MaxPayloadBytes) { Drop("chunk: bad size"); return; }
			if (SyncBytes.Read<uint>(payload, 0) != ChunkPayloadMagic) { Drop("chunk: bad magic"); return; }
			MapSpaceScanContext context = ReadContext(payload);
			if (!FrameMatches(context)) { Drop("chunk: context mismatch"); return; }

			int chunkIndex = SyncBytes.Read<int>(payload, ChunkIndexOffset);
			if (chunkIndex < 0 || chunkIndex >= EnvScanner.Instance.ChunkTableLength) { Drop("chunk: bad index"); return; }

			EnvScanner scanner = EnvScanner.Instance;
			int voxelCount = scanner.VoxPerChunkDim * scanner.VoxPerChunkDim * scanner.VoxPerChunkDim;
			if (!receivedVoxels.IsCreated || receivedVoxels.Length != voxelCount)
			{
				if (receivedVoxels.IsCreated) receivedVoxels.Dispose();
				receivedVoxels = new NativeArray<EnvScanner.Voxel>(voxelCount, Allocator.Persistent);
			}

			// worst case RLE: every voxel raw, plus a control byte per run
			int maxRle = voxelCount * EnvScanner.Voxel.stride + voxelCount / 64 + 64;
			byte[] rle = DepthFrameCodec.Inflate(payload, ChunkPayloadHeaderBytes, payload.Length - ChunkPayloadHeaderBytes, maxRle);
			if (rle == null) { Drop("chunk: inflate failed"); return; }

			int encodedLength = rle.Length;
			if (!chunkEncoding.IsCreated || chunkEncoding.Length < encodedLength)
			{
				if (chunkEncoding.IsCreated) chunkEncoding.Dispose();
				chunkEncoding = new NativeArray<byte>(Math.Max(encodedLength, 1), Allocator.Persistent);
			}

			NativeArray<byte>.Copy(rle, 0, chunkEncoding, 0, encodedLength);

			// decode and upload run back to back, so a chunk arriving mid-decode cannot
			// overwrite the buffers under this one
			int generation = syncGeneration;
			await snapshotDecodeGate.WaitAsync();
			try
			{
				if (!this || generation != syncGeneration || !FrameMatches(context)) return;

				if (!VolumeRLE.DecodeChunkNow(chunkEncoding.GetSubArray(0, encodedLength), receivedVoxels))
				{
					Drop("chunk: decode failed");
					return;
				}

				if (scanner.UploadChunk(chunkIndex, receivedVoxels)) statChunksReceived++;
				else Drop("chunk: scanner refused");
			}
			finally
			{
				snapshotDecodeGate.Release();
			}
		}

		private readonly System.Threading.SemaphoreSlim snapshotDecodeGate = new(1, 1);

		// ---- payload helpers ---------------------------------------------------

		private static void WriteContext(byte[] payload, MapSpaceScanContext context)
		{
			SyncBytes.Write(payload, ContextOffset, context.space);
			SyncBytes.Write(payload, ContextOffset + 16, context.frame);
			SyncBytes.Write(payload, ContextOffset + 32, context.scan);
		}

		private static MapSpaceScanContext ReadContext(byte[] payload) => new()
		{
			space = SyncBytes.Read<Guid>(payload, ContextOffset),
			frame = SyncBytes.Read<Guid>(payload, ContextOffset + 16),
			scan = SyncBytes.Read<Guid>(payload, ContextOffset + 32)
		};
	}
}
