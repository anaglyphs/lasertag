using Anaglyph.XRTemplate;
using SharedSpaces;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.SharedSpaces
{
	public class EnvironmentMappingSync : NetworkBehaviour
	{
		private NetworkManager manager;

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
			EnvironmentMapper.OnPerFrameEnvMap += OnPerFrameEnvMap;
			manager = NetworkManager.Singleton;
			manager.OnConnectionEvent += OnConnectionEvent;
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

		private void OnPerFrameEnvMap(NativeArray<int> data)
		{
			if (!manager.IsHost)
				return;

			StartCoroutine(ScheduleJobs());
			IEnumerator ScheduleJobs()
			{
				CollectPerFrameUpdates collectJob = new()
				{
					perFrameData = new(data, Allocator.TempJob),
					updates = new(Allocator.TempJob),
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

					manager.CustomMessagingManager.SendUnnamedMessage(
						NetworkManager.ConnectedClientsIds, 
						writer, 
						NetworkDelivery.ReliableFragmentedSequenced);
				}
			}
		}

		private void OnUnnamedMessage(ulong clientId, FastBufferReader reader)
		{
			if (IsHost)
				return;

			var map = EnvironmentMapper.Instance.Map;
			int[] data = new int[map.width * map.height];

			reader.TryBeginRead(reader.Length);

			int updateCount = reader.Length / PixelUpdate.byteSize;
			for (int i = 0; i < updateCount; i++)
			{
				var update = new PixelUpdate();
				update.Deserialize(reader);
				data[update.index] = (int)update.value;
			}

			EnvironmentMapper.Instance.ApplyData(data);
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
			EnvironmentMapper.OnPerFrameEnvMap -= OnPerFrameEnvMap;
		}
	}
}
