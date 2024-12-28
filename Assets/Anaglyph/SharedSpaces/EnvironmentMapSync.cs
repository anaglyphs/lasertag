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
		private int[] dataBuffer;

		[Serializable]
		private struct PixelUpdate
		{
			public int index;
			public short value;

			public const int byteSize = sizeof(int) + sizeof(short);

			public void Serialize(FastBufferWriter writer)
			{
				BytePacker.WriteValueBitPacked(writer, index);
				BytePacker.WriteValueBitPacked(writer, value);
			}

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

			EnvironmentMapper.OnPerFrameEnvMap += OnPerFrameEnvMap;
			EnvironmentMapper.OnApply += OnApply;

			dataBuffer = new int[EnvironmentMapper.Instance.TextureSize * EnvironmentMapper.Instance.TextureSize];
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
						value = (short)perFrameData[i]
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
						SendToHost(writer);
				}
			}
		}

		private void OnUnnamedMessage(ulong clientId, FastBufferReader reader)
		{
			if (clientId == manager.LocalClientId)
				return;

			if (manager.IsClient)
			{
				reader.TryBeginRead(reader.Length);

				int updateCount = reader.Length / PixelUpdate.byteSize;
				for (int i = 0; i < updateCount; i++)
				{
					var update = new PixelUpdate();
					update.Deserialize(reader);
					dataBuffer[update.index] = (int)update.value;
				}
			}

			if (manager.IsServer) 
				ForwardToAllOtherClients(reader, clientId);
		}

		private void OnApply() 
		{
			EnvironmentMapper.Instance.ApplyData(dataBuffer);
			for (int i = 0; i < dataBuffer.Length; i++)
				dataBuffer[i] = EnvironmentMapper.PER_FRAME_UNWRITTEN;
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

			//if (!IsServer)
			//	throw new Exception("You can't call this on clients!");

			//int numPeers = NetworkManager.ConnectedClientsIds.Count - 1;

			//if (numPeers == 0)
			//	return;

			//ulong[] peerIDs = new ulong[numPeers];
			//int i = 0;
			//foreach (ulong id in NetworkManager.ConnectedClientsIds)
			//{
			//	if (id != NetworkManager.ServerClientId && id != sender)
			//	{
			//		peerIDs[i] = id;
			//		i++;
			//	}
			//}

			//manager.CustomMessagingManager.SendUnnamedMessage(peerIDs, writer,
			//			NetworkDelivery.ReliableFragmentedSequenced);
		}

		private void SendToHost(FastBufferWriter writer) => 
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
