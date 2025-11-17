using Unity.Netcode;
using UnityEngine;

namespace Anaglyph
{
	public static class PoseSerialization
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		public static void InitializePoseNetworkVariable()
		{
			UserNetworkVariableSerialization<Pose>.WriteValue = WriteValueSafe;
			UserNetworkVariableSerialization<Pose>.ReadValue = ReadValueSafe;
		}

		private static void ReadValueSafe(this FastBufferReader reader, out Pose pose)
		{
			reader.ReadValueSafe(out Vector3 position);
			reader.ReadValueSafe(out Quaternion rotation);
			pose = new Pose(position, rotation);
		}

		private static void WriteValueSafe(this FastBufferWriter writer, in Pose pose)
		{
			writer.WriteValueSafe(pose.position);
			writer.WriteValueSafe(pose.rotation);
		}

		public static void SerializeValue<TReaderWriter>(this BufferSerializer<TReaderWriter> serializer, ref Pose pose)
			where TReaderWriter : IReaderWriter
		{
			if (serializer.IsReader) pose = new Pose();
			serializer.SerializeValue(ref pose);
		}
	}
}