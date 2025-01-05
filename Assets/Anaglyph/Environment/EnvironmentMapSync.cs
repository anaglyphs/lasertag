using Anaglyph.XRTemplate;
using System;
using System.Collections;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Netcode;
using UnityEngine;
using Unity.Burst;

namespace Anaglyph.SharedSpaces
{
	public class EnvironmentMapSync : MonoBehaviour
	{
		private NetworkManager manager;
		private EnvironmentMapper mapper;

		[Serializable]
		private struct PixelUpdate
		{
			public int index;
			public short value;

			public const int byteSize = sizeof(int) + sizeof(short);

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public void Serialize(FastBufferWriter writer)
			{
				BytePacker.WriteValueBitPacked(writer, index);
				BytePacker.WriteValueBitPacked(writer, value);
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public void Deserialize(FastBufferReader reader)
			{
				ByteUnpacker.ReadValueBitPacked(reader, out index);
				ByteUnpacker.ReadValueBitPacked(reader, out value);
			}
		}

		private void Start()
		{
			manager = NetworkManager.Singleton;
			manager.OnConnectionEvent += OnConnectionEvent;

			mapper = EnvironmentMapper.Instance;
			EnvironmentMapper.OnPerFrameEnvMap += OnPerFrameEnvMap;
		}

		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		{
			if (NetcodeHelpers.ThisClientConnected(data))
			{
				EnvironmentMapper.Instance.ClearMap();
				manager.CustomMessagingManager.OnUnnamedMessage += OnUnnamedMessage;
			}
		}

		[BurstCompile]
		private struct CollectPerFrameUpdates : IJob
		{
			public NativeArray<int> perFrameData;
			public NativeList<PixelUpdate> updates;

			public void Execute()
			{
				for (int i = 0; i < perFrameData.Length; i++)
				{
					if (perFrameData[i] == EnvironmentMapper.PER_FRAME_UNWRITTEN)
						continue;

					updates.Add(new PixelUpdate
					{
						index = i,
						value = (short)(perFrameData[i])
					});
				}
			}
		}

		[BurstCompile]
		private struct SerializePerFrameDataJob : IJob
		{
			public NativeList<PixelUpdate> updates;
			[NativeDisableUnsafePtrRestriction]
			public FastBufferWriter writer;

			public void Execute()
			{
				writer.TryBeginWrite(updates.Length * PixelUpdate.byteSize);

				foreach (var update in updates)
					update.Serialize(writer);
			}
		}

		[BurstCompile]
		private struct DeserializePerFrameDataJob : IJob
		{
			[NativeDisableUnsafePtrRestriction]
			public FastBufferReader reader;
			public NativeArray<int> perFrameData;

			public void Execute()
			{
				reader.Seek(0);
				reader.TryBeginRead(reader.Length);
				while(reader.Position < reader.Length)
				{
					var update = new PixelUpdate();
					update.Deserialize(reader);
					perFrameData[update.index] = update.value;
				}
			}
		}
		
		private struct CopyReaderToWriterJob : IJob
		{
			[NativeDisableUnsafePtrRestriction]
			public FastBufferReader reader;
			[NativeDisableUnsafePtrRestriction]
			public FastBufferWriter writer;

			public unsafe void Execute()
			{
				int numBytes = reader.Length;
				byte[] byteArray = new byte[numBytes];
				fixed (byte* bytePtr = byteArray)
				{
					reader.ReadBytesSafe(bytePtr, numBytes);
					writer.WriteBytesSafe(bytePtr, numBytes);
				}
			}
		}

		// send data to server
		private void OnPerFrameEnvMap(NativeArray<int> data)
		{
			if (!manager.IsConnectedClient)
				return;

			StartCoroutine(ScheduleJobs());
			IEnumerator ScheduleJobs()
			{
				CollectPerFrameUpdates collectJob = new()
				{
					perFrameData = new(data, Allocator.TempJob),
					updates = new(800, Allocator.TempJob),
				};

				var collectHandle = collectJob.Schedule();
				while (!collectHandle.IsCompleted) yield return null;
				collectHandle.Complete();
				collectJob.perFrameData.Dispose();

				int bufferBytes = collectJob.updates.Length * PixelUpdate.byteSize;
				FastBufferWriter writer = new(bufferBytes, Allocator.TempJob);
				using (writer)
				{
					SerializePerFrameDataJob serializeJob = new()
					{
						updates = collectJob.updates,
						writer = writer,
					};

					var serializeHandle = serializeJob.Schedule();
					while (!serializeHandle.IsCompleted) yield return null;
					serializeHandle.Complete();
					serializeJob.updates.Dispose();

					if (manager.IsServer)
						SendToAllOtherClients(writer, NetworkManager.ServerClientId);
					else
						SendToServer(writer);
				}
			}
		}

		private void OnUnnamedMessage(ulong clientId, FastBufferReader reader)
		{
			if (clientId == manager.LocalClientId)
				throw new Exception("Client should not send a message to itself!");

			if (manager.IsServer)
			{
				reader.Seek(0);
				ForwardToAllOtherClients(reader, clientId);
			}

			if (manager.IsClient)
			{
				StartCoroutine(Deserialize());
				IEnumerator Deserialize()
				{
					reader.Seek(0);
					var job = new DeserializePerFrameDataJob()
					{
						reader = reader,
						perFrameData = new(mapper.TextureSize * mapper.TextureSize, Allocator.TempJob),
					};

					var handle = job.Schedule();
					while (!handle.IsCompleted) yield return null;
					handle.Complete();

					EnvironmentMapper.Instance.ApplyData(job.perFrameData);
					job.perFrameData.Dispose();
				}
			}
		}

		private unsafe void ForwardToAllOtherClients(FastBufferReader reader, ulong sender)
		{
			int numBytes = reader.Length;

			StartCoroutine(CopyReader());
			IEnumerator CopyReader()
			{
				FastBufferWriter writer = new FastBufferWriter(numBytes, Allocator.TempJob);
				using (writer) {

					var job = new CopyReaderToWriterJob()
					{
						reader = reader,
						writer = writer,
					};

					var handle = job.Schedule();
					while (!handle.IsCompleted) yield return null;
					handle.Complete();

					SendToAllOtherClients(writer, sender);
				}
			}
			
			//FastBufferWriter writer = new FastBufferWriter(numBytes, Allocator.Temp);
			//using (writer)
			//{
			//	byte[] byteArray = new byte[numBytes];
			//	fixed (byte* bytePtr = byteArray)
			//	{
			//		reader.Seek(0);
			//		reader.ReadBytesSafe(bytePtr, numBytes);
			//		writer.WriteBytesSafe(bytePtr, numBytes);
			//	}

			//	SendToAllOtherClients(writer, sender);
			//} 
		}

		private void SendToAllOtherClients(FastBufferWriter writer, ulong sender)
		{
			foreach (ulong id in manager.ConnectedClientsIds)
			{
				if (id != sender && id != NetworkManager.ServerClientId)
					manager.CustomMessagingManager.SendUnnamedMessage(
						id, writer, NetworkDelivery.ReliableFragmentedSequenced);
			}
		}

		private void SendToServer(FastBufferWriter writer) => 
			manager.CustomMessagingManager.SendUnnamedMessage(
							NetworkManager.ServerClientId, writer,
							NetworkDelivery.ReliableFragmentedSequenced);

		private void OnDestroy()
		{
			EnvironmentMapper.OnPerFrameEnvMap -= OnPerFrameEnvMap;
		}
	}
}
