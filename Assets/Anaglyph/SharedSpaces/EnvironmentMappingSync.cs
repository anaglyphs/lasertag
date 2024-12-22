using Anaglyph.XRTemplate;
using SharedSpaces;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

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
			public int value;

			public const int byteSize = sizeof(int) + sizeof(int);

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
			} else if(NetcodeHelpers.ThisClientDisconnected(data))
			{
				manager.CustomMessagingManager.OnUnnamedMessage -= OnUnnamedMessage;
			}
		}

		bool requesting = false;

		private void OnPerFrameEnvMap(ComputeBuffer perFrameEnvMap)
		{
			if (requesting)
				return;

			if (!manager.IsHost)
				return;

			StartCoroutine(Readback());
			IEnumerator Readback()
			{
				requesting = true;
				AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(perFrameEnvMap);

				
				while (!request.done)
				{
					yield return null;
				}

				if (request.hasError)
				{
					Debug.Log("GPU readback error detected.");
				}
				else
				{
					request.GetData<int>().CopyTo(perFrameEnvMapData);

					updates.Clear();

					for (int i = 0; i < perFrameEnvMapData.Length; i++)
					{
						if (perFrameEnvMapData[i] == EnvironmentMapper.UNWRITTEN_INT)
							continue;

						updates.Add(new PixelUpdate
						{
							index = i,
							value = perFrameEnvMapData[i]
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
				}
				requesting = false;
			}
		}

		private void OnUnnamedMessage(ulong clientId, FastBufferReader reader)
		{
			int updateCount = reader.Length / PixelUpdate.byteSize;

            for (int i = 0; i < perFrameEnvMapData.Length; i++)
            {
				perFrameEnvMapData[i] = EnvironmentMapper.UNWRITTEN_INT;
            }

            reader.TryBeginRead(reader.Length);

			for(int i = 0; i < updateCount; i++)
			{
				var update = new PixelUpdate();
				update.Deserialize(reader);
				perFrameEnvMapData[update.index] = update.value;
			}

			EnvironmentMapper.Instance.ApplyData(perFrameEnvMapData);
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
			EnvironmentMapper.OnPerFrameEnvMap -= OnPerFrameEnvMap;
		}
	}
}
