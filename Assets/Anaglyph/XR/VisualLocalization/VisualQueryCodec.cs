using System;
using System.IO;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>
	/// Wire form of a <see cref="VisualQuery"/>: keypoints as 1/50 px fixed point and
	/// descriptors as signed bytes, 68 bytes a feature, so 768 features fit one
	/// 60 KB message with room to spare.
	/// </summary>
	public static class VisualQueryCodec
	{
		private const byte Version = 1;
		private const float PixelScale = 50f;

		public const int DefaultMaxFeatures = 768;

		public static byte[] Encode(VisualQuery query, int queryId, int maxFeatures = DefaultMaxFeatures)
		{
			FeatureSet f = query.features;
			int n = Mathf.Min(f.Count, maxFeatures);
			using MemoryStream stream = new(64 + n * (4 + FeatureSet.DescriptorSize));
			using BinaryWriter w = new(stream);

			w.Write(Version);
			w.Write(queryId);
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

			return stream.ToArray();
		}

		/// <summary>The query id alone, from the header: to answer a query without decoding its features.</summary>
		public static bool TryReadId(byte[] payload, out int queryId)
		{
			queryId = 0;
			if (payload == null || payload.Length < 5 || payload[0] != Version) return false;
			queryId = BitConverter.ToInt32(payload, 1);
			return true;
		}

		public static bool TryDecode(byte[] payload, out VisualQuery query, out int queryId)
		{
			query = null;
			queryId = 0;
			try
			{
				using MemoryStream stream = new(payload);
				using BinaryReader r = new(stream);
				if (r.ReadByte() != Version) return false;

				queryId = r.ReadInt32();
				query = new VisualQuery
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
				return true;
			}
			catch (EndOfStreamException)
			{
				return false;
			}
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
