using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>
	/// Wire form of one or more <see cref="VisualQuery"/> frames: keypoints as 1/50 px fixed
	/// point and descriptors as signed bytes, 68 bytes a feature. The transport carries
	/// 64 KB a message: one frame of 768 features fits with room to spare, two frames of
	/// 448 each just fit.
	/// </summary>
	public static class VisualQueryCodec
	{
		private const byte Version = 2;
		private const float PixelScale = 50f;

		public const int DefaultMaxFeatures = 768;

		public static byte[] Encode(VisualQuery query, int queryId, int maxFeatures = DefaultMaxFeatures) =>
			Encode(new[] { query }, queryId, maxFeatures);

		/// <summary>Several frames of one stretch of tracking, each with its camera in the same world, the first the one the answer describes.</summary>
		public static byte[] Encode(IReadOnlyList<VisualQuery> frames, int queryId, int maxFeatures = DefaultMaxFeatures)
		{
			int count = Mathf.Min(frames.Count, byte.MaxValue);
			using MemoryStream stream = new(16 + count * (64 + maxFeatures * (4 + FeatureSet.DescriptorSize)));
			using BinaryWriter w = new(stream);

			w.Write(Version);
			w.Write(queryId);
			w.Write((byte)count);
			for (int i = 0; i < count; i++) WriteFrame(w, frames[i], maxFeatures);
			return stream.ToArray();
		}

		private static void WriteFrame(BinaryWriter w, VisualQuery query, int maxFeatures)
		{
			FeatureSet f = query.features;
			int n = Mathf.Min(f.Count, maxFeatures);
			w.Write(query.timestampNs);
			w.Write(query.nearMapFrame);
			WritePose(w, query.camera);
			WritePose(w, query.trackingSpace);
			w.Write(query.intrinsics.fx);
			w.Write(query.intrinsics.fy);
			w.Write(query.intrinsics.cx);
			w.Write(query.intrinsics.cy);
			w.Write((ushort)n);

			for (int i = 0; i < n; i++)
			{
				w.Write((ushort)math.clamp(math.round(f.keypoints[i].x * PixelScale), 0, ushort.MaxValue));
				w.Write((ushort)math.clamp(math.round(f.keypoints[i].y * PixelScale), 0, ushort.MaxValue));
			}

			for (int i = 0; i < n * FeatureSet.DescriptorSize; i++)
				w.Write((sbyte)math.clamp(math.round((float)f.descriptors[i] * 127f), -127, 127));
		}

		/// <summary>The query id alone, from the header: to answer a query without decoding its features.</summary>
		public static bool TryReadId(byte[] payload, out int queryId)
		{
			queryId = 0;
			if (payload == null || payload.Length < 5 || payload[0] != Version) return false;
			queryId = BitConverter.ToInt32(payload, 1);
			return true;
		}

		/// <summary>The first frame alone.</summary>
		public static bool TryDecode(byte[] payload, out VisualQuery query, out int queryId)
		{
			query = null;
			if (!TryDecode(payload, out List<VisualQuery> frames, out queryId) || frames.Count == 0) return false;
			query = frames[0];
			return true;
		}

		public static bool TryDecode(byte[] payload, out List<VisualQuery> frames, out int queryId)
		{
			frames = null;
			queryId = 0;
			try
			{
				using MemoryStream stream = new(payload);
				using BinaryReader r = new(stream);
				if (r.ReadByte() != Version) return false;

				queryId = r.ReadInt32();
				int count = r.ReadByte();
				frames = new List<VisualQuery>(count);
				for (int i = 0; i < count; i++) frames.Add(ReadFrame(r));
				return true;
			}
			catch (EndOfStreamException)
			{
				frames = null;
				return false;
			}
		}

		private static VisualQuery ReadFrame(BinaryReader r)
		{
			VisualQuery query = new()
			{
				timestampNs = r.ReadInt64(),
				nearMapFrame = r.ReadBoolean(),
				camera = ReadPose(r),
				trackingSpace = ReadPose(r),
				intrinsics = new PinholeCamera(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
			};

			int n = r.ReadUInt16();
			FeatureSet f = new(n);
			for (int i = 0; i < n; i++)
			{
				f.keypoints[i] = new float2(r.ReadUInt16(), r.ReadUInt16()) / PixelScale;
				f.scores[i] = 1;
			}

			// renormalized: the matcher was trained on unit descriptors
			float[] v = new float[FeatureSet.DescriptorSize];
			for (int i = 0; i < n; i++)
			{
				int o = i * FeatureSet.DescriptorSize;
				float sum = 0;
				for (int c = 0; c < FeatureSet.DescriptorSize; c++)
				{
					v[c] = r.ReadSByte() / 127f;
					sum += v[c] * v[c];
				}

				float inv = sum > 0 ? 1f / math.sqrt(sum) : 0;
				for (int c = 0; c < FeatureSet.DescriptorSize; c++) f.descriptors[o + c] = (half)(v[c] * inv);
			}

			query.features = f;
			return query;
		}

		private static void WritePose(BinaryWriter w, Pose pose)
		{
			w.Write(pose.position.x);
			w.Write(pose.position.y);
			w.Write(pose.position.z);
			w.Write(pose.rotation.x);
			w.Write(pose.rotation.y);
			w.Write(pose.rotation.z);
			w.Write(pose.rotation.w);
		}

		private static Pose ReadPose(BinaryReader r) =>
			new(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
				new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
	}
}
