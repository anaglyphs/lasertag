using System;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph
{
	public static class GuidSerialization
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
		private static void Init()
		{
			UserNetworkVariableSerialization<Guid>.WriteValue = Write;
			UserNetworkVariableSerialization<Guid>.ReadValue = Read;
			UserNetworkVariableSerialization<Guid>.DuplicateValue = Duplicate;
		}

		private static void Read(FastBufferReader reader, out Guid guid)
		{
			var bytes = new byte[16];
			reader.ReadBytesSafe(ref bytes, 16);
			guid = new Guid(bytes);
		}

		private static void Write(FastBufferWriter writer, in Guid guid)
		{
			var bytes = guid.ToByteArray();
			writer.WriteBytesSafe(bytes, 16);
		}

		private static void Duplicate(in Guid value, ref Guid duplicatedValue)
		{
			duplicatedValue = value;
		}
	}
}