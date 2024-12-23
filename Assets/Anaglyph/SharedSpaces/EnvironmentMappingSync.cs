using Anaglyph.XRTemplate;
using SharedSpaces;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.SharedSpaces
{
	public class EnvironmentMappingSync : NetworkBehaviour
	{
		private NetworkManager manager;
		private int[] perFrameEnvMapData;
		private List<PixelUpdate> updates = new(500);

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

			var map = EnvironmentMapper.Instance.Map;
			perFrameEnvMapData = new int[map.width * map.height];

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

		private void OnPerFrameEnvMap(int[] perFrameEnvMap)
		{
			try
			{
				if (!manager.IsHost)
					return;

				updates.Clear();

				for (int i = 0; i < perFrameEnvMap.Length; i++)
				{
					if (perFrameEnvMap[i] == EnvironmentMapper.UNWRITTEN_INT)
						continue;

					updates.Add(new PixelUpdate
					{
						index = i,
						value = (short)perFrameEnvMap[i]
					});
				}

				FastBufferWriter writer = new FastBufferWriter(PixelUpdate.byteSize * updates.Count, Allocator.Temp);

				using (writer)
				{
					if (!writer.TryBeginWrite(PixelUpdate.byteSize * updates.Count))
					{
						throw new OverflowException("Not enough space in the buffer");
					}

					foreach (var update in updates)
					{
						update.Serialize(writer);
					}

					manager.CustomMessagingManager.SendUnnamedMessage(NetworkManager.ConnectedClientsIds, writer, NetworkDelivery.ReliableFragmentedSequenced);
				}

				writer.Dispose();
			} catch (Exception ex)
			{
				Debug.LogException(ex);
			}
		}

		private void OnUnnamedMessage(ulong clientId, FastBufferReader reader)
		{
			try
			{
				if (IsHost)
					return;

				int updateCount = reader.Length / PixelUpdate.byteSize;

				for (int i = 0; i < perFrameEnvMapData.Length; i++)
				{
					perFrameEnvMapData[i] = EnvironmentMapper.UNWRITTEN_INT;
				}

				reader.TryBeginRead(reader.Length);

				for (int i = 0; i < updateCount; i++)
				{
					var update = new PixelUpdate();
					update.Deserialize(reader);
					perFrameEnvMapData[update.index] = update.value;
				}

				EnvironmentMapper.Instance.ApplyData(perFrameEnvMapData);
			} catch(Exception ex)
			{
				Debug.LogException(ex);
			}
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
			EnvironmentMapper.OnPerFrameEnvMap -= OnPerFrameEnvMap;
		}
	}
}
