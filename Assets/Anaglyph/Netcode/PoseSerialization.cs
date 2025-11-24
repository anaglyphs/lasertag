using System;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph
{
	public static class NetworkSerializationHelpers
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
		private static void Init()
		{
			UserNetworkVariableSerialization<Pose>.WriteValue = Write;
			UserNetworkVariableSerialization<Pose>.ReadValue = Read;
		}

		private static void Read(FastBufferReader reader, out Pose pose)
		{
			reader.ReadValueSafe(out Vector3 position);
			reader.ReadValueSafe(out Quaternion rotation);
			pose = new Pose(position, rotation);
		}

		private static void Write(FastBufferWriter writer, in Pose pose)
		{
			writer.WriteValueSafe(pose.position);
			writer.WriteValueSafe(pose.rotation);
		}
	}
}