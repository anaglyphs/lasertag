//using Anaglyph.Netcode;
//using Anaglyph.XRTemplate;
//using Anaglyph.XRTemplate.DepthKit;
//using System;
//using System.Collections;
//using System.Runtime.CompilerServices;
//using Unity.Burst;
//using Unity.Collections;
//using Unity.Collections.LowLevel.Unsafe;
//using Unity.Jobs;
//using Unity.Netcode;
//using UnityEngine;

//namespace Anaglyph.Lasertag.EnvironmentSync
//{
//	public class EnvironmentSynchronizer : MonoBehaviour
//	{
//		public static EnvironmentSynchronizer Instance { get; private set; }
//		private void Awake()
//		{
//			Instance = this;
//		}

//		[SerializeField] private float updateFrequency;


//		private NetworkManager manager => NetworkManager.Singleton;
//		private EnvironmentMapper mapper => EnvironmentMapper.Instance;
//		private DepthKitDriver depth => DepthKitDriver.Instance;

//		private void Start()
//		{
//			manager.OnConnectionEvent += OnConnectionEvent;
//		}

//		[Serializable]
//		private struct DepthUpdate
//		{
//			public Matrix4x4 viewInv;
//			public byte[] depthTexRaw;


//			[MethodImpl(MethodImplOptions.AggressiveInlining)]
//			public void Serialize(FastBufferWriter writer)
//			{
//				float[] viewProjArray = new float[4 * 4];

//				for (int x = 0; x < 4; x++)
//					for (int y = 0; y < 4; y++)
//						viewProjArray[x * 4 + y] = viewInv[x, y];

//				writer.WriteValueSafe(viewProjArray);
//				writer.WriteBytes(depthTexRaw);
//			}

//			[MethodImpl(MethodImplOptions.AggressiveInlining)]
//			public unsafe void Deserialize(FastBufferReader reader)
//			{
//				float[] viewProjArray = new float[4 * 4];

//				reader.ReadValueSafe(out viewProjArray);
//				fixed (byte* bytePtr = depthTexRaw)
//					reader.ReadBytesSafe(bytePtr, reader.Length - reader.Position);
//			}
//		}

//		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
//		{
//			if (NetcodeHelpers.ThisClientConnected(data))
//			{
//				EnvironmentMapper.Instance.Clear();
//				manager.CustomMessagingManager.OnUnnamedMessage += OnUnnamedMessage;

//				SyncLoop();
//			}
//		}

//		private async void SyncLoop()
//		{
//			while (manager.IsConnectedClient && manager.IsListening)
//			{
//				await Awaitable.WaitForSecondsAsync(updateFrequency);

//				if (!DepthKitDriver.DepthAvailable)
//					continue;

//				DepthUpdate update = new();

//				update.viewInv = Shader.GetGlobalMatrixArray(DepthKitDriver.agDepthViewInv_ID)[0];
//				update.depthTexRaw = depth.DepthTexCPU.EncodeToJPG();

//				FastBufferWriter writer = new(bytes.Length, Allocator.TempJob);
//				using (writer)
//				{
//					SerializeDepthImage serializeJob = new()
//					{
//						rawImage = bytes,
//						writer = writer,
//					};

//					var serializeHandle = serializeJob.Schedule();
//					while (!serializeHandle.IsCompleted) await Awaitable.NextFrameAsync();
//					serializeHandle.Complete();
//					serializeJob.rawImage.Dispose();

//					if (manager.IsServer)
//						SendToAllOtherClients(writer, NetworkManager.ServerClientId);
//					else
//						SendToServer(writer);
//				}
//			}
//		}

//		[BurstCompile]
//		private struct SerializeDepthImage : IJob
//		{
//			public NativeArray<byte> rawImage;
//			[NativeDisableUnsafePtrRestriction]
//			public FastBufferWriter writer;

//			public void Execute()
//			{
//				writer.WriteBytesSafe(rawImage);
//			}
//		}

//		[BurstCompile]
//		private struct DeserializeDepthImage : IJob
//		{
//			[NativeDisableUnsafePtrRestriction]
//			public FastBufferReader reader;
//			public NativeArray<byte> rawImage;

//			public unsafe void Execute()
//			{
//				reader.Seek(0);

//				int numBytes = reader.Length;
//				byte[] byteArray = new byte[reader.Length];
//				fixed (byte* bytePtr = byteArray)
//				{
//					reader.ReadBytesSafe(bytePtr, reader.Length);
//				}
//				rawImage.CopyFrom(byteArray);
//			}
//		}

//		private struct CopyReaderToWriterJob : IJob
//		{
//			[NativeDisableUnsafePtrRestriction]
//			public FastBufferReader reader;
//			[NativeDisableUnsafePtrRestriction]
//			public FastBufferWriter writer;

//			public unsafe void Execute()
//			{
//				int numBytes = reader.Length;
//				byte[] byteArray = new byte[numBytes];
//				fixed (byte* bytePtr = byteArray)
//				{
//					reader.ReadBytesSafe(bytePtr, numBytes);
//					writer.WriteBytesSafe(bytePtr, numBytes);
//				}
//			}
//		}
//		private void OnUnnamedMessage(ulong clientId, FastBufferReader reader)
//		{
//			if (clientId == manager.LocalClientId)
//				throw new Exception("Client should not send a message to itself!");

//			if (manager.IsServer)
//			{
//				reader.Seek(0);
//				ForwardToAllOtherClients(reader, clientId);
//			}

//			if (manager.IsClient)
//			{
//				StartCoroutine(Deserialize());
//				IEnumerator Deserialize()
//				{
//					reader.Seek(0);

//					var job = new DeserializeDepthImage()
//					{
//						reader = reader,
//						rawImage = new(reader.Length, Allocator.TempJob),
//					};

//					var handle = job.Schedule();
//					while (!handle.IsCompleted) yield return null;
//					handle.Complete();

//					// EnvironmentMapper.Instance.ApplyData(job.rawImage);
//					job.rawImage.Dispose();
//				}
//			}
//		}

//		private unsafe void ForwardToAllOtherClients(FastBufferReader reader, ulong sender)
//		{
//			int numBytes = reader.Length;

//			StartCoroutine(CopyReader());
//			IEnumerator CopyReader()
//			{
//				FastBufferWriter writer = new FastBufferWriter(numBytes, Allocator.TempJob);
//				using (writer)
//				{

//					var job = new CopyReaderToWriterJob()
//					{
//						reader = reader,
//						writer = writer,
//					};

//					var handle = job.Schedule();
//					while (!handle.IsCompleted) yield return null;
//					handle.Complete();

//					SendToAllOtherClients(writer, sender);
//				}
//			}
//		}

//		private void SendToAllOtherClients(FastBufferWriter writer, ulong sender)
//		{
//			foreach (ulong id in manager.ConnectedClientsIds)
//			{
//				if (id != sender && id != NetworkManager.ServerClientId)
//					manager.CustomMessagingManager?.SendUnnamedMessage(
//						id, writer, NetworkDelivery.ReliableFragmentedSequenced);
//			}
//		}

//		private void SendToServer(FastBufferWriter writer) =>
//			manager.CustomMessagingManager?.SendUnnamedMessage(
//							NetworkManager.ServerClientId, writer,
//							NetworkDelivery.ReliableFragmentedSequenced);
//	}
//}