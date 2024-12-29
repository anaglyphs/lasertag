using Anaglyph.XRTemplate;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.SharedSpaces
{
	public class EnvironmentMapSync : MonoBehaviour
	{
		private NetworkManager manager;
		private EnvironmentMapper mapper;
		private NativeArray<int> perFrameData;

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
			EnvironmentMapper.OnApply += OnApply;

			int size = mapper.TextureSize;
			perFrameData = new(size * size, Allocator.Persistent);
		}

		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		{
			if (NetcodeHelpers.ThisClientConnected(data))
			{
				EnvironmentMapper.Instance.ClearMap();
				manager.CustomMessagingManager.OnUnnamedMessage += OnUnnamedMessage;
			}
		}

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

		private struct SerializePerFrameDataJob : IJob
		{
			public NativeArray<PixelUpdate> updates;
			[NativeDisableUnsafePtrRestriction]
			public FastBufferWriter writer;

			public void Execute()
			{
				writer.TryBeginWrite(updates.Length * PixelUpdate.byteSize);

				foreach (var update in updates)
					update.Serialize(writer);
			}
		}

		private struct DeserializePerFrameDataJob : IJob
		{
			[NativeDisableUnsafePtrRestriction]
			public FastBufferReader reader;
			public NativeArray<int> perFrameData;

			public void Execute()
			{
				reader.TryBeginRead(reader.Length);

				int updateCount = reader.Length / PixelUpdate.byteSize;
				for (int i = 0; i < updateCount; i++)
				{
					var update = new PixelUpdate();
					update.Deserialize(reader);
					perFrameData[update.index] = update.value;
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
						updates = new(collectJob.updates.AsArray(), Allocator.TempJob),
						writer = writer,
					};
					collectJob.updates.Dispose();

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

			if (manager.IsClient)
			{
				StartCoroutine(Deserialize());
				IEnumerator Deserialize()
				{
					var deserializeJob = new DeserializePerFrameDataJob()
					{
						reader = reader,
						perFrameData = perFrameData,
					};

					var handle = deserializeJob.Schedule();
					while (handle.IsCompleted) yield return null;
					handle.Complete();
				}
			}

			if (manager.IsServer) 
				ForwardToAllOtherClients(reader, clientId);
		}

		private void OnApply() 
		{
			EnvironmentMapper.Instance.ApplyData(perFrameData);
			for (int i = 0; i < perFrameData.Length; i++)
				perFrameData[i] = EnvironmentMapper.PER_FRAME_UNWRITTEN;
		}

		private unsafe void ForwardToAllOtherClients(FastBufferReader reader, ulong sender)
		{
			int numBytes = reader.Length;
			
			FastBufferWriter writer = new FastBufferWriter(numBytes, Allocator.Temp);
			using (writer)
			{
				byte[] byteArray = new byte[numBytes];
				fixed (byte* bytePtr = byteArray)
				{
					reader.Seek(0);
					reader.TryBeginRead(numBytes);
					reader.ReadBytes(bytePtr, numBytes);
					writer.TryBeginWrite(numBytes);
					writer.WriteBytes(bytePtr, numBytes);
				}

				SendToAllOtherClients(writer, sender);
			} 
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
			EnvironmentMapper.OnApply -= OnApply;
		}
	}
}
