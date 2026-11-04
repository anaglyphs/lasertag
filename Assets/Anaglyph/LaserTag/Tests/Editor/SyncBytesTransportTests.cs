using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR.DepthKit.EnvScanning.Encoding;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Netcode;

namespace Anaglyph.LaserTag.Tests
{
	/// <summary>
	/// The byte path env frames and snapshot chunks take: pieces reassembled in a kept
	/// buffer, bytes read into pooled arrays longer than the payload, and codecs reading
	/// those slices without running past their end.
	/// </summary>
	public class SyncBytesTransportTests
	{
		private const BindingFlags AnyMember = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
		private const int PieceBytes = 1024;
		private const int ChunkVoxels = 32 * 32 * 32;

		private readonly List<byte[]> delivered = new();
		private byte[] payload;
		private SyncEventBytes pieceEvent;
		private uint pieceEventId;

		[SetUp]
		public void SetUp()
		{
			delivered.Clear();
			payload = new byte[PieceBytes * 3 + 100];
			new System.Random(3).NextBytes(payload);

			pieceEvent = new SyncEventBytes("test.pieces." + Guid.NewGuid(), EventRoute.DirectUnreliable);
			pieceEvent.Register();
			pieceEventId = (uint)typeof(SyncEndpoint).GetProperty("Id", AnyMember).GetValue(pieceEvent);
			pieceEvent.ReceivedBorrowed += (_, received) => delivered.Add(received.ToArray());
		}

		[TearDown]
		public void TearDown() => pieceEvent.Unregister();

		private void Receive(ushort sequence, byte index, byte count, ArraySegment<byte> piece) =>
			typeof(SyncBus).GetMethod("ReceivePiece", AnyMember)
				.Invoke(null, new object[] { (ulong)7, pieceEventId, sequence, index, count, piece });

		private ArraySegment<byte> Piece(int index) =>
			new(payload, index * PieceBytes, Math.Min(PieceBytes, payload.Length - index * PieceBytes));

		[Test]
		public void PiecesOutOfOrderAndRepeatedDeliverThePayloadOnce()
		{
			Receive(10, 3, 4, Piece(3));
			Receive(10, 0, 4, Piece(0));
			Receive(10, 0, 4, Piece(0));
			Receive(10, 2, 4, Piece(2));
			Assert.That(delivered, Is.Empty);

			Receive(10, 1, 4, Piece(1));
			Receive(10, 1, 4, Piece(1));

			Assert.That(delivered.Count, Is.EqualTo(1));
			Assert.That(delivered[0], Is.EqualTo(payload));
		}

		[Test]
		public void NewerPayloadAbandonsAnIncompleteOneAndItsStragglers()
		{
			Receive(11, 0, 4, Piece(0));
			Receive(11, 1, 4, Piece(1));
			Receive(12, 0, 4, Piece(0));
			Receive(12, 1, 4, Piece(1));
			Receive(12, 2, 4, Piece(2));
			Receive(11, 2, 4, Piece(2));
			Receive(11, 3, 4, Piece(3));
			Assert.That(delivered, Is.Empty);

			Receive(12, 3, 4, Piece(3));
			Assert.That(delivered.Count, Is.EqualTo(1));
			Assert.That(delivered[0], Is.EqualTo(payload));
		}

		[Test]
		public void PayloadShorterThanTheKeptBufferKeepsItsOwnLength()
		{
			Receive(10, 0, 4, Piece(0));
			Receive(10, 1, 4, Piece(1));
			Receive(10, 2, 4, Piece(2));
			Receive(10, 3, 4, Piece(3));
			delivered.Clear();

			Receive(11, 1, 2, new ArraySegment<byte>(payload, PieceBytes, 5));
			Receive(11, 0, 2, Piece(0));

			Assert.That(delivered.Count, Is.EqualTo(1));
			Assert.That(delivered[0], Is.EqualTo(payload.Take(PieceBytes + 5).ToArray()));
		}

		[Test]
		public void OnePiecePayloadArrivesWhole()
		{
			Receive(10, 0, 1, new ArraySegment<byte>(payload, 50, 200));

			Assert.That(delivered.Count, Is.EqualTo(1));
			Assert.That(delivered[0], Is.EqualTo(payload.Skip(50).Take(200).ToArray()));
		}

		[Test]
		public void OversizedPieceIsRefused()
		{
			Receive(10, 0, 1, new ArraySegment<byte>(payload, 0, PieceBytes + 1));
			Assert.That(delivered, Is.Empty);
		}

		// the overload taking one value by reference, not an array or a native collection
		private static MethodInfo SerializeOne(Type buffer, string name, Type valueType) =>
			buffer.GetMethods().First(method =>
			{
				ParameterInfo[] parameters = method.GetParameters();
				return method.Name == name && parameters.Length == 1 && parameters[0].ParameterType.IsByRef &&
				       parameters[0].ParameterType.GetElementType().IsGenericParameter;
			}).MakeGenericMethod(valueType);

		[Test]
		public void BusBytesSendASliceAndReceiveExactlyThoseBytes()
		{
			Type busBytes = typeof(SyncBus).GetNestedType("BusBytes", BindingFlags.NonPublic);
			object sent = Activator.CreateInstance(busBytes, new ArraySegment<byte>(payload, 7, 300));

			FastBufferWriter writer = new(4096, Allocator.Temp);
			SerializeOne(typeof(FastBufferWriter), nameof(FastBufferWriter.WriteNetworkSerializable), busBytes).Invoke(writer, new[] { sent });
			Assert.That(writer.Length, Is.EqualTo(sizeof(int) + 300));

			FastBufferReader reader = new(writer, Allocator.Temp);
			object[] readArguments = { Activator.CreateInstance(busBytes) };
			SerializeOne(typeof(FastBufferReader), nameof(FastBufferReader.ReadNetworkSerializable), busBytes).Invoke(reader, readArguments);
			object received = readArguments[0];

			ArraySegment<byte> bytes = (ArraySegment<byte>)busBytes.GetField("Bytes").GetValue(received);
			Assert.That(bytes.ToArray(), Is.EqualTo(payload.Skip(7).Take(300).ToArray()));
			busBytes.GetMethod("ReturnToPool").Invoke(received, null);

			reader.Dispose();
			writer.Dispose();
		}

		[TestCase(-1)]
		[TestCase(301)]
		[TestCase(int.MaxValue)]
		public void BusBytesRefuseASizeTheMessageDoesNotHold(int claimedSize)
		{
			Type busBytes = typeof(SyncBus).GetNestedType("BusBytes", BindingFlags.NonPublic);

			FastBufferWriter writer = new(4096, Allocator.Temp);
			writer.WriteValueSafe(claimedSize);
			writer.WriteBytesSafe(payload, 300);

			FastBufferReader reader = new(writer, Allocator.Temp);
			object[] readArguments = { Activator.CreateInstance(busBytes) };
			Assert.Throws<OverflowException>(() =>
				SerializeOne(typeof(FastBufferReader), nameof(FastBufferReader.ReadNetworkSerializable), busBytes).Invoke(reader, readArguments));

			reader.Dispose();
			writer.Dispose();
		}

		[TestCase((byte)12, DepthFrameCompression.PredictedDeflate)]
		[TestCase((byte)8, DepthFrameCompression.PredictedDeflate)]
		[TestCase((byte)12, DepthFrameCompression.Raw)]
		[TestCase((byte)8, DepthFrameCompression.Raw)]
		public void FrameSurvivesFromASliceOfALongerArray(byte codeBits, DepthFrameCompression compression)
		{
			DepthFrameHeader header = new()
			{
				codeBits = codeBits, width = 80, height = 80, weight = 3,
				position = new float3(1, 2, 3), rotation = quaternion.identity,
				tangents = new float4(-1, 1, -1, 1), zRange = new float2(0.5f, 6f)
			};

			using NativeArray<byte> codes = new(header.PackedBytes, Allocator.Temp);
			using NativeArray<byte> decoded = new(header.PackedBytes, Allocator.Temp);
			NativeArray<byte> writableCodes = codes;
			Unity.Mathematics.Random random = new(7);
			for (int i = 0; i < codes.Length; i++) writableCodes[i] = (byte)(i / 7 % 200 + random.NextInt(0, 3));

			const int prefix = 40;
			MemoryStream stream = new();

			// twice, so the second pass runs on the codec's kept buffers
			for (int pass = 0; pass < 2; pass++)
			{
				stream.SetLength(prefix);
				stream.Position = prefix;
				DepthFrameCodec.Encode(header, codes, compression, stream);

				int length = (int)stream.Length;
				byte[] longer = Enumerable.Repeat((byte)0xAB, length + 500).ToArray();
				Buffer.BlockCopy(stream.GetBuffer(), 0, longer, 0, length);
				ArraySegment<byte> wire = new(longer, 0, length);

				Assert.That(DepthFrameCodec.TryReadHeader(wire, prefix, out DepthFrameHeader read));
				Assert.That(DepthFrameCodec.Decode(wire, prefix, read, decoded));
				Assert.That(read.weight, Is.EqualTo(header.weight));
				Assert.That(decoded.ToArray(), Is.EqualTo(codes.ToArray()));
			}
		}

		[Test]
		public void CorruptFrameIsRefused()
		{
			DepthFrameHeader header = new()
			{
				codeBits = 12, width = 80, height = 80, weight = 1, compression = (byte)DepthFrameCompression.PredictedDeflate,
				version = DepthFrameHeader.CurrentVersion,
				rotation = quaternion.identity, tangents = new float4(-1, 1, -1, 1), zRange = new float2(0.5f, 6f)
			};
			using NativeArray<byte> decoded = new(header.PackedBytes, Allocator.Temp);

			byte[] junk = new byte[DepthFrameCodec.HeaderSize + 200];
			new System.Random(1).NextBytes(junk);
			Assert.That(DepthFrameCodec.Decode(junk, 0, header, decoded), Is.False);
		}

		private static ArraySegment<byte> DeflatedChunk(NativeArray<EnvScanner.Voxel> voxels, out int runLengthBytes)
		{
			const int prefix = 44;
			NativeList<byte> encoding = new(Allocator.TempJob);
			VolumeRLE.EncodeChunkNow(voxels, encoding);
			runLengthBytes = encoding.Length;

			MemoryStream stream = new();
			stream.SetLength(prefix);
			stream.Position = prefix;
			DepthFrameCodec.Deflate(encoding.AsArray(), stream);

			int length = (int)stream.Length;
			byte[] longer = Enumerable.Repeat((byte)0xAB, length + 300).ToArray();
			Buffer.BlockCopy(stream.GetBuffer(), 0, longer, 0, length);
			encoding.Dispose();
			return new ArraySegment<byte>(longer, 0, length);
		}

		private static NativeArray<EnvScanner.Voxel> NoisyVoxels()
		{
			NativeArray<EnvScanner.Voxel> voxels = new(ChunkVoxels, Allocator.TempJob);
			Unity.Mathematics.Random random = new(9);
			for (int i = 0; i < ChunkVoxels; i++)
				voxels[i] = i % 5 == 0
					? new EnvScanner.Voxel(sbyte.MaxValue, 0)
					: new EnvScanner.Voxel((sbyte)random.NextInt(-127, 127), (byte)random.NextInt(1, 16));
			return voxels;
		}

		[Test]
		public void ChunkSurvivesRunLengthAndDeflate()
		{
			using NativeArray<EnvScanner.Voxel> voxels = NoisyVoxels();
			ArraySegment<byte> wire = DeflatedChunk(voxels, out int runLengthBytes);

			using NativeArray<byte> inflated = new(ChunkVoxels * 3, Allocator.TempJob);
			Assert.That(DepthFrameCodec.TryInflate(wire, 44, inflated, out int length));
			Assert.That(length, Is.EqualTo(runLengthBytes));

			using NativeArray<EnvScanner.Voxel> decoded = new(ChunkVoxels, Allocator.TempJob);
			Assert.That(VolumeRLE.DecodeChunkNow(inflated.GetSubArray(0, length), decoded));

			for (int i = 0; i < ChunkVoxels; i++)
			{
				Assert.That(decoded[i].count, Is.EqualTo(voxels[i].count));
				if (voxels[i].count > 0) Assert.That(decoded[i].value, Is.EqualTo(voxels[i].value));
			}
		}

		[Test]
		public void InflateFillsAnExactOutputAndRefusesOverflowAndJunk()
		{
			using NativeArray<EnvScanner.Voxel> voxels = NoisyVoxels();
			ArraySegment<byte> wire = DeflatedChunk(voxels, out int runLengthBytes);

			using NativeArray<byte> exact = new(runLengthBytes, Allocator.Temp);
			Assert.That(DepthFrameCodec.TryInflate(wire, 44, exact, out int length));
			Assert.That(length, Is.EqualTo(runLengthBytes));

			using NativeArray<byte> tooSmall = new(runLengthBytes - 1, Allocator.Temp);
			Assert.That(DepthFrameCodec.TryInflate(wire, 44, tooSmall, out _), Is.False);

			byte[] junk = new byte[200];
			new System.Random(1).NextBytes(junk);
			bool accepted = DepthFrameCodec.TryInflate(junk, 0, exact, out int junkLength);
			Assert.That(!accepted || junkLength == 0);
		}
	}
}
