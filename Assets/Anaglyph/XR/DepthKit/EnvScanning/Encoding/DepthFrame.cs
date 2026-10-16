using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.DepthKit.EnvScanning.Encoding
{
	/// <summary>
	/// Everything a receiver needs to integrate one depth frame besides its pixels:
	/// the camera pose and the projection as view-angle tangents (the XrFovf
	/// convention), so frames from a headset with different optics still land where
	/// they were seen. Pixels are linear depth codes of <see cref="codeBits"/> bits,
	/// 0 = invalid, row-major; codes wider than 8 bits travel in 16-bit containers,
	/// whose top bit (0x8000) flags a mask pixel: the depth is a player's silhouette,
	/// and receivers carve free space in front of it without writing a surface.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct DepthFrameHeader
	{
		public const byte CurrentVersion = 1;

		public byte version;
		public byte compression; // DepthFrameCompression

		/// <summary>
		/// Bits per depth code: 8 or 12. Codes span zRange linearly, so the step is
		/// (zMax-zMin)/(2^bits-2): 21.6mm at 8 bits over 0.5–6m (visible banding), 1.3mm at 12.
		/// </summary>
		public byte codeBits;
		public byte reserved0;
		public ushort width;
		public ushort height;
		/// <summary>
		/// How many sensor frames this one stands for. The sender integrates every frame
		/// it captures but only broadcasts some; the ones it held back are folded into the
		/// next sent frame's weight so peers' voxels move and settle as far as the sender's did.
		/// </summary>
		public ushort weight;
		public ushort reserved1;

		/// <summary>World-space camera pose. The view convention is GL: camera looks down -Z.</summary>
		public float3 position;
		public quaternion rotation;

		/// <summary>Tangents of the left, right, bottom and top view angles (left and bottom negative).</summary>
		public float4 tangents;

		/// <summary>Linear depth spanned by code 1..max, uniformly.</summary>
		public float2 zRange;

		public int PixelCount => width * height;

		/// <summary>Bytes per code in the packed buffer: 1 for 8-bit codes, 2 otherwise.</summary>
		public int CodeBytes => codeBits <= 8 ? 1 : 2;

		/// <summary>Length of the packed code buffer for these dimensions.</summary>
		public int PackedBytes => PixelCount * CodeBytes;

		public bool IsSane =>
			version == CurrentVersion &&
			(codeBits == 8 || codeBits == 12) &&
			weight >= 1 &&
			width >= 4 && height >= 1 && width % 4 == 0 &&
			width <= 1024 && height <= 1024 &&
			zRange.x > 0 && zRange.y > zRange.x &&
			tangents.y > tangents.x && tangents.w > tangents.z &&
			math.all(math.isfinite(position)) && math.all(math.isfinite(rotation.value)) &&
			math.all(math.isfinite(tangents));

		// mirrors DepthKitDriver: a GL-style view matrix is a Unity TRS with Z flipped
		private static readonly Vector3 GLScale = new(1, 1, -1);

		public Matrix4x4 ViewInverse => Matrix4x4.TRS(position, rotation, GLScale);
		public Matrix4x4 View => ViewInverse.inverse;

		public static void PoseFromViewInverse(Matrix4x4 viewInverse, out float3 position, out quaternion rotation)
		{
			position = (Vector3)viewInverse.GetColumn(3);
			// columns are R.x, R.y, -R.z of the camera rotation
			Vector3 forward = -(Vector3)viewInverse.GetColumn(2);
			Vector3 up = viewInverse.GetColumn(1);
			rotation = Quaternion.LookRotation(forward, up);
		}

		/// <summary>Tangents of an off-center GL projection matrix's frustum edges.</summary>
		public static float4 TangentsFromProjection(Matrix4x4 p)
		{
			float x = p.m00, a = p.m02, y = p.m11, b = p.m12;
			return new float4((a - 1f) / x, (a + 1f) / x, (b - 1f) / y, (b + 1f) / y);
		}
	}

	public enum DepthFrameCompression : byte
	{
		Raw = 0,

		// each code predicted from its left neighbor (first column: from above), the
		// residuals deflated. depth is smooth, so residuals cluster near zero
		PredictedDeflate = 1
	}

	/// <summary>
	/// Wire format of a depth frame: <see cref="DepthFrameHeader"/> followed by the
	/// (possibly compressed) codes. Small enough to run synchronously.
	/// </summary>
	public static class DepthFrameCodec
	{
		public static readonly int HeaderSize = UnsafeUtility.SizeOf<DepthFrameHeader>();

		public static byte[] Encode(DepthFrameHeader header, NativeArray<byte> codes, DepthFrameCompression compression)
		{
			if (codes.Length != header.PackedBytes)
				throw new ArgumentException("code buffer does not match the header's dimensions and code width");

			header.version = DepthFrameHeader.CurrentVersion;
			header.compression = (byte)compression;

			byte[] body;
			switch (compression)
			{
				case DepthFrameCompression.Raw:
					body = codes.ToArray();
					break;

				case DepthFrameCompression.PredictedDeflate:
					byte[] residuals = new byte[codes.Length];
					if (header.CodeBytes == 1) Predict8(codes, header.width, residuals);
					else Predict16(codes, header.width, residuals);
					body = Deflate(residuals, 0, residuals.Length);
					break;

				default:
					throw new ArgumentOutOfRangeException(nameof(compression));
			}

			byte[] wire = new byte[HeaderSize + body.Length];
			WriteHeader(wire, 0, header);
			Buffer.BlockCopy(body, 0, wire, HeaderSize, body.Length);
			return wire;
		}

		/// <summary>
		/// Reads the header alone, so a receiver can size <paramref name="codes"/> for
		/// <see cref="Decode"/> and reject frames before touching the payload
		/// </summary>
		public static bool TryReadHeader(byte[] wire, int offset, out DepthFrameHeader header)
		{
			header = default;
			if (wire == null || wire.Length < offset + HeaderSize) return false;
			header = ReadHeader(wire, offset);
			return header.IsSane;
		}

		public static bool Decode(byte[] wire, int offset, in DepthFrameHeader header, NativeArray<byte> codes)
		{
			if (codes.Length != header.PackedBytes) return false;

			int bodyOffset = offset + HeaderSize;
			int bodyLength = wire.Length - bodyOffset;
			if (bodyLength < 0) return false;

			switch ((DepthFrameCompression)header.compression)
			{
				case DepthFrameCompression.Raw:
					if (bodyLength != codes.Length) return false;
					NativeArray<byte>.Copy(wire, bodyOffset, codes, 0, codes.Length);
					return true;

				case DepthFrameCompression.PredictedDeflate:
					byte[] residuals = new byte[codes.Length];
					try
					{
						using MemoryStream stream = new(wire, bodyOffset, bodyLength, false);
						using DeflateStream deflate = new(stream, CompressionMode.Decompress);
						int read = 0;
						while (read < residuals.Length)
						{
							int n = deflate.Read(residuals, read, residuals.Length - read);
							if (n <= 0) break;
							read += n;
						}

						if (read != residuals.Length) return false;
					}
					catch (InvalidDataException)
					{
						return false;
					}

					if (header.CodeBytes == 1) Unpredict8(residuals, header.width, codes);
					else Unpredict16(residuals, header.width, codes);
					return true;

				default:
					return false;
			}
		}

		/// <summary>Deflates a byte range; shared by frames and chunk snapshots.</summary>
		public static byte[] Deflate(byte[] src, int offset, int count)
		{
			using MemoryStream stream = new();
			using (DeflateStream deflate = new(stream, System.IO.Compression.CompressionLevel.Fastest, true))
				deflate.Write(src, offset, count);
			return stream.ToArray();
		}

		/// <summary>
		/// Inflates a byte range, refusing output beyond <paramref name="maxLength"/> so a
		/// hostile or corrupt payload cannot balloon. Null on failure.
		/// </summary>
		public static byte[] Inflate(byte[] src, int offset, int count, int maxLength)
		{
			try
			{
				using MemoryStream input = new(src, offset, count, false);
				using DeflateStream inflate = new(input, CompressionMode.Decompress);
				using MemoryStream output = new();
				byte[] buffer = new byte[16384];
				int read;
				while ((read = inflate.Read(buffer, 0, buffer.Length)) > 0)
				{
					if (output.Length + read > maxLength) return null;
					output.Write(buffer, 0, read);
				}

				return output.ToArray();
			}
			catch (InvalidDataException)
			{
				return null;
			}
		}

		private static void Predict8(NativeArray<byte> codes, int width, byte[] residuals)
		{
			for (int i = 0; i < codes.Length; i++)
			{
				int x = i % width;
				byte prediction = x > 0 ? codes[i - 1] : i >= width ? codes[i - width] : (byte)0;
				residuals[i] = unchecked((byte)(codes[i] - prediction));
			}
		}

		private static void Unpredict8(byte[] residuals, int width, NativeArray<byte> codes)
		{
			for (int i = 0; i < residuals.Length; i++)
			{
				int x = i % width;
				byte prediction = x > 0 ? codes[i - 1] : i >= width ? codes[i - width] : (byte)0;
				codes[i] = unchecked((byte)(residuals[i] + prediction));
			}
		}

		// 16-bit containers: residuals are split into a low-byte plane and a high-byte
		// plane so Deflate sees the nearly constant high bytes as one long run
		private static void Predict16(NativeArray<byte> codes, int width, byte[] residuals)
		{
			NativeArray<ushort> values = codes.Reinterpret<ushort>(1);
			int count = values.Length;
			for (int i = 0; i < count; i++)
			{
				int x = i % width;
				ushort prediction = x > 0 ? values[i - 1] : i >= width ? values[i - width] : (ushort)0;
				ushort residual = unchecked((ushort)(values[i] - prediction));
				residuals[i] = (byte)(residual & 0xFF);
				residuals[count + i] = (byte)(residual >> 8);
			}
		}

		private static void Unpredict16(byte[] residuals, int width, NativeArray<byte> codes)
		{
			NativeArray<ushort> values = codes.Reinterpret<ushort>(1);
			int count = values.Length;
			for (int i = 0; i < count; i++)
			{
				int x = i % width;
				ushort prediction = x > 0 ? values[i - 1] : i >= width ? values[i - width] : (ushort)0;
				ushort residual = (ushort)(residuals[i] | (residuals[count + i] << 8));
				values[i] = unchecked((ushort)(residual + prediction));
			}
		}

		private static void WriteHeader(byte[] dst, int offset, in DepthFrameHeader header)
		{
			DepthFrameHeader copy = header;
			MemoryMarshal.Write(dst.AsSpan(offset, HeaderSize), ref copy);
		}

		private static DepthFrameHeader ReadHeader(byte[] src, int offset)
		{
			return MemoryMarshal.Read<DepthFrameHeader>(src.AsSpan(offset, HeaderSize));
		}
	}
}
