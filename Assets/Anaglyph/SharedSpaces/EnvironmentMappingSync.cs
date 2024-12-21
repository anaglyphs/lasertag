using Anaglyph.XRTemplate;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.SharedSpaces
{
	public class EnvironmentMappingSync : NetworkBehaviour
	{
		private NetworkManager manager;
		private byte[] perFrameEnvMapData;

		private void Start()
		{
			EnvironmentMapper.OnPerFrameEnvMap += OnPerFrameEnvMap;
			manager = NetworkManager.Singleton;
			
			var map = EnvironmentMapper.Instance.Map;
			perFrameEnvMapData = new byte[map.width * map.height * sizeof(int)];

			manager.OnConnectionEvent += OnConnectionEvent;
		}

		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		{
			if(data.EventType == ConnectionEvent.ClientConnected)
			{
				EnvironmentMapper.Instance.ClearMap();
				manager.CustomMessagingManager.OnUnnamedMessage += OnUnnamedMessage;
			}
		}

		private void OnUnnamedMessage(ulong clientId, FastBufferReader reader)
		{
			perFrameEnvMapData = new byte[reader.Length]; // Initialize the data array with the received data length
			reader.ReadBytesSafe(ref perFrameEnvMapData, reader.Length);
		}

		private void OnPerFrameEnvMap(ComputeBuffer perFrameEnvMap)
		{
			if (!manager.IsHost)
				return;

			perFrameEnvMap.GetData(perFrameEnvMapData);

			var writer = new FastBufferWriter(perFrameEnvMapData.Length * sizeof(int), Allocator.Temp);

			using (writer)
			{
				writer.WriteBytesSafe(perFrameEnvMapData);

				manager.CustomMessagingManager.SendUnnamedMessage(NetworkManager.ConnectedClientsIds, writer, NetworkDelivery.ReliableFragmentedSequenced);

				EnvironmentMapper.Instance.ApplyData(perFrameEnvMapData);
			}

		}

		

		public override void OnDestroy()
		{
			base.OnDestroy();
			EnvironmentMapper.OnPerFrameEnvMap -= OnPerFrameEnvMap;
		}
	}
}
