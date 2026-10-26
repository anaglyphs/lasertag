using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>
	/// A map on its way to or from disk: only what localization reads. Per keyframe the
	/// features that sight a usable landmark plus the strongest ones retrieval ranks by,
	/// with int8 descriptors; per landmark its reference ray and inverse depth, since
	/// established landmarks are frozen. Poses are in the caller's storage frame.
	/// </summary>
	public sealed class VisualMapData
	{
		public sealed class Keyframe
		{
			public Pose camera;
			public PinholeCamera intrinsics;
			public long timestampNs;
			public bool original;
			public int count;
			public float2[] keypoints;
			public float[] scores;
			public half[] descriptors;
			/// <summary>Per feature, the landmark id it sights, or -1.</summary>
			public int[] landmarks;
			/// <summary>The words it shows (<see cref="VisualVocabulary.Bitset"/>), or null.</summary>
			public ulong[] probe;
		}

		public struct Landmark
		{
			public int id;
			/// <summary>The reference camera's center and the unit direction to the landmark from it, storage frame.</summary>
			public float3 origin, direction;
			public float inverseDepth, inverseDepthSigma;
		}

		/// <summary>The frame the poses are in.</summary>
		public string frameId = "";
		public long savedTicks;
		/// <summary>The vocabulary the probe bitsets were made with; 0 for none.</summary>
		public ulong vocabulary;
		public readonly List<Keyframe> keyframes = new();
		public readonly List<Landmark> landmarks = new();
	}

	/// <summary>What a map file says about itself before its views are read: enough to rank it as a place.</summary>
	public sealed class VisualMapHeader
	{
		public string frameId;
		public long savedTicks;
		public ulong vocabulary;
		public int keyframes, landmarks;
		/// <summary>Per keyframe, the words it shows.</summary>
		public ulong[][] probes;
	}

	public static class VisualMapFile
	{
		public const string Extension = ".vmap";
		private const uint Magic = 0x50414D56; // VMAP
		private const int Version = 1;
		private const float PixelScale = 50f;
		private const int D = FeatureSet.DescriptorSize;

		// ---- snapshot ----------------------------------------------------------

		/// <summary>
		/// The map as it stands, in the storage frame <paramref name="toStorage"/> maps world
		/// poses into (<paramref name="toStorageRotation"/> its rotation alone, for directions).
		/// Main thread: it reads the live map.
		/// </summary>
		public static VisualMapData Snapshot(VisualMap map, Func<Pose, Pose> toStorage, Quaternion toStorageRotation, bool singleViewMesh)
		{
			VisualMapData data = new();
			HashSet<int> saved = new();
			foreach (VisualKeyframe k in map.Keyframes)
			{
				// the features that sight a usable landmark, and the strongest retrieval ranks by
				List<int> selected = new();
				for (int j = 0; j < k.features.Count; j++)
				{
					bool sights = map.TryGetLandmark(k.landmarks[j], out Landmark l) && l.IsUsable(singleViewMesh);
					if (sights || j < VisualMap.RetrievalFeatures) selected.Add(j);
				}

				VisualMapData.Keyframe entry = new()
				{
					camera = toStorage(k.camera),
					intrinsics = k.intrinsics,
					timestampNs = k.timestampNs,
					original = k.original,
					count = selected.Count,
					keypoints = new float2[selected.Count],
					scores = new float[selected.Count],
					descriptors = new half[selected.Count * D],
					landmarks = new int[selected.Count],
				};
				for (int i = 0; i < selected.Count; i++)
				{
					int j = selected[i];
					entry.keypoints[i] = k.features.keypoints[j];
					entry.scores[i] = k.features.scores[j];
					Array.Copy(k.features.descriptors, j * D, entry.descriptors, i * D, D);
					bool sights = map.TryGetLandmark(k.landmarks[j], out Landmark l) && l.IsUsable(singleViewMesh);
					entry.landmarks[i] = sights ? k.landmarks[j] : -1;
					if (sights) saved.Add(k.landmarks[j]);
				}

				data.keyframes.Add(entry);
			}

			foreach (int id in saved)
			{
				if (!map.TryGetLandmark(id, out Landmark l) || !l.TryPlace(map.Anchoring, out float3 origin, out float3 direction)) continue;
				data.landmarks.Add(new VisualMapData.Landmark
				{
					id = id,
					origin = toStorage(new Pose(origin, Quaternion.identity)).position,
					direction = toStorageRotation * (Vector3)direction,
					inverseDepth = l.inverseDepth,
					inverseDepthSigma = l.inverseDepthSigma,
				});
			}

			return data;
		}

		// ---- writing -----------------------------------------------------------

		public static void Write(VisualMapData data, Stream stream)
		{
			using BinaryWriter w = new(stream, System.Text.Encoding.UTF8, true);
			w.Write(Magic);
			w.Write(Version);
			w.Write(data.frameId ?? "");
			w.Write(data.savedTicks);
			w.Write(data.vocabulary);
			w.Write(data.keyframes.Count);
			w.Write(data.landmarks.Count);

			// the probes first, so a reader ranking places stops after them
			w.Write(VisualVocabulary.BitsetLength);
			foreach (VisualMapData.Keyframe k in data.keyframes)
				for (int i = 0; i < VisualVocabulary.BitsetLength; i++)
					w.Write(k.probe != null ? k.probe[i] : 0ul);

			foreach (VisualMapData.Keyframe k in data.keyframes)
			{
				WritePose(w, k.camera);
				w.Write(k.intrinsics.fx);
				w.Write(k.intrinsics.fy);
				w.Write(k.intrinsics.cx);
				w.Write(k.intrinsics.cy);
				w.Write(k.timestampNs);
				w.Write(k.original);
				w.Write(k.count);
				for (int i = 0; i < k.count; i++)
				{
					w.Write((ushort)math.clamp(math.round(k.keypoints[i].x * PixelScale), 0, ushort.MaxValue));
					w.Write((ushort)math.clamp(math.round(k.keypoints[i].y * PixelScale), 0, ushort.MaxValue));
					w.Write(((half)k.scores[i]).value);
					w.Write(k.landmarks[i]);
				}

				for (int i = 0; i < k.count * D; i++)
					w.Write((sbyte)math.clamp(math.round((float)k.descriptors[i] * 127f), -127, 127));
			}

			foreach (VisualMapData.Landmark l in data.landmarks)
			{
				w.Write(l.id);
				w.Write(l.origin.x);
				w.Write(l.origin.y);
				w.Write(l.origin.z);
				w.Write(l.direction.x);
				w.Write(l.direction.y);
				w.Write(l.direction.z);
				w.Write(l.inverseDepth);
				w.Write(l.inverseDepthSigma);
			}
		}

		// ---- reading -----------------------------------------------------------

		/// <summary>The header and the probes alone: a few hundred KB at most, read to rank the map as a place.</summary>
		public static VisualMapHeader ReadHeader(Stream stream)
		{
			using BinaryReader r = new(stream, System.Text.Encoding.UTF8, true);
			return ReadHeader(r);
		}

		private static VisualMapHeader ReadHeader(BinaryReader r)
		{
			if (r.ReadUInt32() != Magic || r.ReadInt32() != Version) return null;
			VisualMapHeader h = new()
			{
				frameId = r.ReadString(),
				savedTicks = r.ReadInt64(),
				vocabulary = r.ReadUInt64(),
				keyframes = r.ReadInt32(),
				landmarks = r.ReadInt32(),
			};
			if (h.keyframes < 0 || h.landmarks < 0 || r.ReadInt32() != VisualVocabulary.BitsetLength) return null;
			h.probes = new ulong[h.keyframes][];
			for (int k = 0; k < h.keyframes; k++)
			{
				ulong[] bits = new ulong[VisualVocabulary.BitsetLength];
				for (int i = 0; i < bits.Length; i++) bits[i] = r.ReadUInt64();
				h.probes[k] = bits;
			}

			return h;
		}

		/// <summary>The whole map; null when the file isn't one.</summary>
		public static VisualMapData Read(Stream stream)
		{
			using BinaryReader r = new(stream, System.Text.Encoding.UTF8, true);
			VisualMapHeader h = ReadHeader(r);
			if (h == null) return null;
			VisualMapData data = new() { frameId = h.frameId, savedTicks = h.savedTicks, vocabulary = h.vocabulary };
			for (int k = 0; k < h.keyframes; k++)
			{
				VisualMapData.Keyframe entry = new()
				{
					camera = ReadPose(r),
					intrinsics = new PinholeCamera(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
					timestampNs = r.ReadInt64(),
					original = r.ReadBoolean(),
					probe = h.probes[k],
				};
				int count = r.ReadInt32();
				if (count < 0 || count > XFeatExtractor.MaxKeypoints) return null;
				entry.count = count;
				entry.keypoints = new float2[count];
				entry.scores = new float[count];
				entry.landmarks = new int[count];
				entry.descriptors = new half[count * D];
				for (int i = 0; i < count; i++)
				{
					entry.keypoints[i] = new float2(r.ReadUInt16(), r.ReadUInt16()) / PixelScale;
					entry.scores[i] = new half { value = r.ReadUInt16() };
					entry.landmarks[i] = r.ReadInt32();
				}

				// renormalized: the matchers were trained on unit descriptors
				float[] v = new float[D];
				for (int i = 0; i < count; i++)
				{
					float sum = 0;
					for (int c = 0; c < D; c++)
					{
						v[c] = r.ReadSByte() / 127f;
						sum += v[c] * v[c];
					}

					float inv = sum > 0 ? 1f / math.sqrt(sum) : 0;
					for (int c = 0; c < D; c++) entry.descriptors[i * D + c] = (half)(v[c] * inv);
				}

				data.keyframes.Add(entry);
			}

			for (int i = 0; i < h.landmarks; i++)
				data.landmarks.Add(new VisualMapData.Landmark
				{
					id = r.ReadInt32(),
					origin = new float3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
					direction = new float3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
					inverseDepth = r.ReadSingle(),
					inverseDepthSigma = r.ReadSingle(),
				});

			return data;
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
