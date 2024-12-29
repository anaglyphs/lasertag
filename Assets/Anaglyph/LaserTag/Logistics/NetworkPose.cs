using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag.Logistics
{
	public struct NetworkPose : INetworkSerializable, System.IEquatable<NetworkPose>
	{
		public Vector3 position;
		public Quaternion rotation;

		public NetworkPose(Vector3 position, Quaternion rotation)
		{
			this.position = position;
			this.rotation = rotation;
		}

		public NetworkPose(Transform transform)
		{
			this.position = transform.position;
			this.rotation = transform.rotation;
		}

		public bool Equals(NetworkPose other)
		{
			return position == other.position && rotation == other.rotation;
		}

		public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
		{
			if (serializer.IsReader)
			{
				var reader = serializer.GetFastBufferReader();
				reader.ReadValueSafe(out position);
				reader.ReadValueSafe(out rotation);
			}
			else
			{
				var writer = serializer.GetFastBufferWriter();
				writer.WriteValueSafe(position);
				writer.WriteValueSafe(rotation);
			}
		}
	}
}
