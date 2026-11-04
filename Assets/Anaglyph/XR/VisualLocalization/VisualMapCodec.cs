using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>
	/// A map (or a few of its views) packed for the network, about a third of
	/// <see cref="VisualMapFile"/>'s size. Descriptors are three quarters of a map, and they go
	/// as int8 coefficients over a fixed PCA basis shipped with the app
	/// (Resources/XFeat/descriptor-pca): <see cref="SightingDimensions"/> for features that
	/// sight a landmark, the ones a pose stands on, and <see cref="RetrievalDimensions"/> for
	/// the rest, which only rank views for retrieval. On two recorded maps, held-out views
	/// localized against 24 coefficients as accurately as against the full descriptors
	/// (2.5–3 cm median) and passed the solve's gates as often; LighterGlue lost a fifth of its
	/// matches at 16, which retrieval doesn't mind. Landmarks are numbered within the message,
	/// keep the sender's ids as deltas, and name their reference view when it is in the
	/// message; bearings are octahedral. Then the whole is deflated.
	/// </summary>
	public static class VisualMapCodec
	{
		public const int SightingDimensions = 24;
		public const int RetrievalDimensions = 16;

		private const uint Magic = 0x434E4D56; // VMNC
		private const byte Version = 2;
		private const float PixelScale = 50f;
		private const int D = FeatureSet.DescriptorSize;
		// the largest view the extractor makes, so a count fits a ushort
		private const int MaxFeatures = XFeatExtractor.MaxKeypoints;

		private static Basis basis;

		private sealed class Basis
		{
			public float[] mean;
			/// <summary>Components, row-major: each a unit D-vector, strongest first.</summary>
			public float[] components;
			/// <summary>Each coefficient's largest magnitude: int8 ±127 spans it.</summary>
			public float[] scale;
			public int count;
		}

		private static Basis LoadBasis()
		{
			if (basis != null) return basis;
			TextAsset asset = Resources.Load<TextAsset>("XFeat/descriptor-pca");
			if (asset == null) throw new InvalidOperationException("Resources/XFeat/descriptor-pca is missing");
			using BinaryReader r = new(new MemoryStream(asset.bytes));
			if (r.ReadUInt32() != 0x41435056 || r.ReadInt32() != D) throw new InvalidOperationException("descriptor-pca is not a basis for these descriptors");
			int count = r.ReadInt32();
			if (count < SightingDimensions) throw new InvalidOperationException($"descriptor-pca has {count} components, {SightingDimensions} needed");
			Basis b = new() { count = count, mean = new float[D], components = new float[count * D], scale = new float[count] };
			for (int i = 0; i < D; i++) b.mean[i] = r.ReadSingle();
			for (int i = 0; i < count * D; i++) b.components[i] = r.ReadSingle();
			for (int i = 0; i < count; i++) b.scale[i] = r.ReadSingle();
			basis = b;
			return b;
		}

		/// <summary>Loads the basis on the main thread, so encoding and decoding can run on a worker.</summary>
		public static void Prepare() => LoadBasis();

		// ---- encoding ------------------------------------------------------------

		public static byte[] Encode(VisualMapData data)
		{
			Basis b = LoadBasis();
			using MemoryStream raw = new();
			using (BinaryWriter w = new(raw, System.Text.Encoding.UTF8, true))
			{
				// the landmarks by id, numbered within the message
				List<VisualMapData.Landmark> landmarks = new(data.landmarks);
				landmarks.Sort((x, y) => x.id.CompareTo(y.id));
				Dictionary<int, int> number = new(landmarks.Count);
				for (int i = 0; i < landmarks.Count; i++) number[landmarks[i].id] = i;

				w.Write((byte)SightingDimensions);
				w.Write((byte)RetrievalDimensions);
				WriteVarint(w, data.keyframes.Count);
				WriteVarint(w, landmarks.Count);

				// the nodes named, once each: thousands of landmarks hang off a few
				Dictionary<ulong, int> nodes = new();
				foreach (VisualMapData.Keyframe k in data.keyframes) nodes.TryAdd(k.node, nodes.Count);
				foreach (VisualMapData.Landmark l in landmarks) nodes.TryAdd(l.node, nodes.Count);
				WriteVarint(w, nodes.Count);
				foreach ((ulong node, int _) in nodes) w.Write(node);

				Dictionary<Vector3, int> viewAt = new();
				float[] centered = new float[D];
				for (int v = 0; v < data.keyframes.Count; v++)
				{
					VisualMapData.Keyframe k = data.keyframes[v];
					viewAt.TryAdd(k.camera.position, v);
					int count = math.min(k.count, MaxFeatures);
					WritePose(w, k.camera);
					w.Write(k.intrinsics.fx);
					w.Write(k.intrinsics.fy);
					w.Write(k.intrinsics.cx);
					w.Write(k.intrinsics.cy);
					w.Write(k.timestampNs);
					w.Write(k.original);
					w.Write(k.locked);
					WriteVarint(w, nodes[k.node]);
					w.Write((ushort)count);
					for (int i = 0; i < count; i++)
					{
						float2 keypoint = k.keypoints[k.Row(i)];
						w.Write((ushort)math.clamp(math.round(keypoint.x * PixelScale), 0, ushort.MaxValue));
						w.Write((ushort)math.clamp(math.round(keypoint.y * PixelScale), 0, ushort.MaxValue));
						// 0 for none, else the landmark's number + 1
						WriteVarint(w, k.landmarks[i] >= 0 && number.TryGetValue(k.landmarks[i], out int n) ? n + 1 : 0);
					}

					for (int i = 0; i < count; i++)
					{
						int dims = k.landmarks[i] >= 0 && number.ContainsKey(k.landmarks[i]) ? SightingDimensions : RetrievalDimensions;
						int offset = k.Row(i) * D;
						for (int j = 0; j < D; j++) centered[j] = (float)k.descriptors[offset + j] - b.mean[j];
						for (int c = 0; c < dims; c++)
						{
							float y = 0;
							int row = c * D;
							for (int j = 0; j < D; j++) y += centered[j] * b.components[row + j];
							w.Write((sbyte)math.clamp(math.round(y / b.scale[c] * 127f), -127, 127));
						}
					}
				}

				int previous = -1;
				foreach (VisualMapData.Landmark l in landmarks)
				{
					// ids ascend: the step from the last
					WriteVarint(w, l.id - previous);
					previous = l.id;
					WriteVarint(w, nodes[l.node]);
					// its reference camera, when that is a view in the message: 0 for one that isn't
					if (viewAt.TryGetValue(l.origin, out int view)) WriteVarint(w, view + 1);
					else
					{
						WriteVarint(w, 0);
						w.Write(l.origin.x);
						w.Write(l.origin.y);
						w.Write(l.origin.z);
					}

					float2 octahedral = Octahedral(math.normalizesafe(l.direction));
					w.Write((short)math.round(octahedral.x * short.MaxValue));
					w.Write((short)math.round(octahedral.y * short.MaxValue));
					w.Write(((half)l.inverseDepth).value);
					w.Write(((half)math.min(l.inverseDepthSigma, 65000f)).value);
				}
			}

			using MemoryStream packed = new();
			using (BinaryWriter w = new(packed, System.Text.Encoding.UTF8, true))
			{
				w.Write(Magic);
				w.Write(Version);
				w.Write((int)raw.Length);
			}

			using (DeflateStream deflate = new(packed, System.IO.Compression.CompressionLevel.Optimal, true))
				raw.WriteTo(deflate);
			return packed.ToArray();
		}

		// ---- decoding ------------------------------------------------------------

		/// <summary>The map, or null when the bytes aren't one this build reads.</summary>
		public static VisualMapData Decode(byte[] bytes)
		{
			try
			{
				return Read(bytes);
			}
			catch (Exception e) when (e is EndOfStreamException or InvalidDataException or ArgumentException or IndexOutOfRangeException)
			{
				return null;
			}
		}

		private static VisualMapData Read(byte[] bytes)
		{
			Basis b = LoadBasis();
			using MemoryStream packed = new(bytes);
			using BinaryReader header = new(packed);
			if (header.ReadUInt32() != Magic || header.ReadByte() != Version) return null;
			int length = header.ReadInt32();
			if (length < 0 || length > 256 << 20) return null;
			byte[] raw = new byte[length];
			using (DeflateStream inflate = new(packed, CompressionMode.Decompress))
			{
				int read = 0;
				while (read < length)
				{
					int n = inflate.Read(raw, read, length - read);
					if (n <= 0) return null;
					read += n;
				}
			}

			using BinaryReader r = new(new MemoryStream(raw));
			int sightingDims = r.ReadByte(), retrievalDims = r.ReadByte();
			if (sightingDims > b.count || retrievalDims > b.count) return null;
			int keyframes = ReadVarint(r), landmarks = ReadVarint(r);
			ulong[] nodes = new ulong[ReadVarint(r)];
			for (int n = 0; n < nodes.Length; n++) nodes[n] = r.ReadUInt64();

			VisualMapData data = new();
			// per feature, the landmark's number in the message, resolved to its id below
			List<int[]> numbers = new(keyframes);
			float[] descriptor = new float[D];
			for (int k = 0; k < keyframes; k++)
			{
				VisualMapData.Keyframe entry = new()
				{
					camera = ReadPose(r),
					intrinsics = new PinholeCamera(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
					timestampNs = r.ReadInt64(),
					original = r.ReadBoolean(),
					locked = r.ReadBoolean(),
					node = nodes[ReadVarint(r)],
				};
				int count = r.ReadUInt16();
				if (count > MaxFeatures) return null;
				entry.count = count;
				entry.keypoints = new float2[count];
				entry.scores = new float[count];
				entry.landmarks = new int[count];
				entry.descriptors = new half[count * D];
				int[] sighted = new int[count];
				for (int i = 0; i < count; i++)
				{
					entry.keypoints[i] = new float2(r.ReadUInt16(), r.ReadUInt16()) / PixelScale;
					// stored strongest first: the order is what retrieval and capping read
					entry.scores[i] = 1;
					sighted[i] = ReadVarint(r) - 1;
					if (sighted[i] >= landmarks) return null;
				}

				for (int i = 0; i < count; i++)
				{
					int dims = sighted[i] >= 0 ? sightingDims : retrievalDims;
					Array.Copy(b.mean, descriptor, D);
					for (int c = 0; c < dims; c++)
					{
						float y = r.ReadSByte() / 127f * b.scale[c];
						int row = c * D;
						for (int j = 0; j < D; j++) descriptor[j] += y * b.components[row + j];
					}

					// unit length again: the matchers were trained on unit descriptors
					float sum = 0;
					for (int j = 0; j < D; j++) sum += descriptor[j] * descriptor[j];
					float norm = sum > 0 ? 1f / math.sqrt(sum) : 0;
					for (int j = 0; j < D; j++) entry.descriptors[i * D + j] = (half)(descriptor[j] * norm);
				}

				numbers.Add(sighted);
				data.keyframes.Add(entry);
			}

			int[] ids = new int[landmarks];
			int id = -1;
			for (int l = 0; l < landmarks; l++)
			{
				id += ReadVarint(r);
				ids[l] = id;
				ulong landmarkNode = nodes[ReadVarint(r)];
				int view = ReadVarint(r);
				float3 origin;
				if (view > 0)
				{
					if (view > keyframes) return null;
					origin = data.keyframes[view - 1].camera.position;
				}
				else origin = new float3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

				float2 octahedral = new float2(r.ReadInt16(), r.ReadInt16()) / short.MaxValue;
				data.landmarks.Add(new VisualMapData.Landmark
				{
					id = id,
					node = landmarkNode,
					origin = origin,
					direction = FromOctahedral(octahedral),
					inverseDepth = new half { value = r.ReadUInt16() },
					inverseDepthSigma = new half { value = r.ReadUInt16() },
				});
			}

			for (int k = 0; k < keyframes; k++)
			{
				int[] sighted = numbers[k];
				int[] resolved = data.keyframes[k].landmarks;
				for (int i = 0; i < sighted.Length; i++) resolved[i] = sighted[i] >= 0 ? ids[sighted[i]] : -1;
			}

			return data;
		}

		// ---- packing -----------------------------------------------------------------

		private static void WriteVarint(BinaryWriter w, int value)
		{
			uint v = (uint)value;
			while (v >= 0x80)
			{
				w.Write((byte)(v | 0x80));
				v >>= 7;
			}

			w.Write((byte)v);
		}

		private static int ReadVarint(BinaryReader r)
		{
			uint v = 0;
			for (int shift = 0; shift < 35; shift += 7)
			{
				byte b = r.ReadByte();
				v |= (uint)(b & 0x7f) << shift;
				if (b < 0x80) return (int)v;
			}

			throw new InvalidDataException("varint too long");
		}

		// a unit vector as two coordinates in [-1, 1], near-uniform in angle
		private static float2 Octahedral(float3 n)
		{
			n /= math.abs(n.x) + math.abs(n.y) + math.abs(n.z);
			float2 p = n.xy;
			if (n.z < 0) p = (1 - math.abs(p.yx)) * math.select(-1f, 1f, p >= 0);
			return p;
		}

		private static float3 FromOctahedral(float2 p)
		{
			float3 n = new(p.x, p.y, 1 - math.abs(p.x) - math.abs(p.y));
			if (n.z < 0) n.xy = (1 - math.abs(n.yx)) * math.select(-1f, 1f, n.xy >= 0);
			return math.normalizesafe(n);
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
