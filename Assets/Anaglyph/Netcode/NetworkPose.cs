using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Netcode
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

		public NetworkPose(Pose pose)
		{
			this.position = pose.position;
			this.rotation = pose.rotation;
		}

		public static implicit operator Pose(NetworkPose pose) => new Pose(pose.position, pose.rotation);
		public static explicit operator NetworkPose(Pose pose) => new NetworkPose(pose.position, pose.rotation);

		public Pose ToPose()
		{
			return new Pose(position, rotation);
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
