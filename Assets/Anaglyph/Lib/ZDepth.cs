using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Anaglyph
{
    public static class ZDepth
    {
		private const string dll = "zdepthunity";
		private const string logPrefix = "[ZDepth] ";
		private static void Log(string str) => Debug.Log(logPrefix + str);

		[DllImport(dll)]
		private static extern bool Initialize();

		[RuntimeInitializeOnLoadMethod]
		public static void Init()
		{
			if (Initialize())
				Log("Initialized successfully");
			else
				Debug.LogError(logPrefix + "failed to initialize!");
		}

		[DllImport(dll)]
		private static extern void Compress(int width, int height, byte[] depth, out IntPtr data_out, out int length_out);

		[DllImport(dll)]
		private static extern void Decompress(int width, int height, byte[] compressed, int compressedSize, out IntPtr data_out, out int length_out);

		public static byte[] Compress(int width, int height, byte[] depth)
		{
			Compress(width, height, depth, out IntPtr data, out int size);
			byte[] bytes = new byte[size];
			Marshal.Copy(data, bytes, 0, size * sizeof(byte));
			return bytes;
		}

		public static byte[] Decompress(int width, int height, byte[] compressed)
		{
			Decompress(width, height, compressed, compressed.Length * sizeof(byte), out IntPtr data, out int size);
			byte[] bytes = new byte[size];
			Marshal.Copy(data, bytes, 0, size * sizeof(ushort));
			return bytes;
		}
	}
}
