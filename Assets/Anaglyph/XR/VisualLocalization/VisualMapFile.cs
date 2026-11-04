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
	/// established landmarks are frozen. Poses are relative to the graph's knots, which come
	/// with them, so a map loaded goes on moving as the one that was saved did.
	/// </summary>
	public sealed class VisualMapData
	{
		public sealed class Keyframe
		{
			public Pose camera;
			public PinholeCamera intrinsics;
			public long timestampNs;
			public bool original;
			/// <summary>Sent between headsets, not saved: see <see cref="VisualKeyframe.locked"/>.</summary>
			public bool locked;
			/// <summary>The knot it hangs off, under its name on every headset.</summary>
			public ulong node;
			/// <summary>The headset whose tracking made it.</summary>
			public uint owner;
			public int count;
			/// <summary>In a snapshot, the row of the view's own arrays each feature is: those are shared, never written. Null when they hold these features alone.</summary>
			public int[] rows;
			public float2[] keypoints;
			public float[] scores;
			public half[] descriptors;
			/// <summary>Per feature, the landmark id it sights, or -1.</summary>
			public int[] landmarks;
			/// <summary>The words it shows (<see cref="VisualVocabulary.Bitset"/>), or null.</summary>
			public ulong[] probe;

			/// <summary>The row of <see cref="keypoints"/>, <see cref="scores"/> and <see cref="descriptors"/> feature i is.</summary>
			public int Row(int i) => rows != null ? rows[i] : i;
		}

		public struct Landmark
		{
			public int id;
			/// <summary>The reference camera's center and the unit direction to the landmark from it, storage frame.</summary>
			public float3 origin, direction;
			public float inverseDepth, inverseDepthSigma;
			/// <summary>As <see cref="Keyframe.node"/>.</summary>
			public ulong node;
			/// <summary>As <see cref="Keyframe.owner"/>: <see cref="id"/> means something only within its owner's.</summary>
			public uint owner;
		}

		/// <summary>The frame the poses are in.</summary>
		public string frameId = "";
		public long savedTicks;
		/// <summary>The vocabulary the probe bitsets were made with; 0 for none.</summary>
		public ulong vocabulary;
		/// <summary>The headset whose views these are, when sent between headsets.</summary>
		public uint owner;
		public readonly List<Keyframe> keyframes = new();
		public readonly List<Landmark> landmarks = new();
		/// <summary>The knots and edges the poses hang off, as the graph that holds them encodes them; saved only.</summary>
		public byte[] graph;
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
		private const int Version = 2;
		private const float PixelScale = 50f;
		private const int D = FeatureSet.DescriptorSize;

		// ---- snapshot ----------------------------------------------------------

		private const int LandmarkNotLookedUp = -2, SightsNothing = -1;

		/// <summary>
		/// The map as it stands, in the storage frame <paramref name="toStorage"/> maps world
		/// poses into (<paramref name="toStorageRotation"/> its rotation alone, for directions).
		/// Main thread: it reads the live map. Features are shared with the map, not copied, so
		/// any thread may read the result. With <paramref name="only"/>, those of its
		/// keyframes alone, with the landmarks they sight. With <paramref name="raw"/>, for another
		/// headset, poses go as they are relative to their node instead (its tracking's, whatever
		/// the graph does next), under the name <paramref name="wireNode"/> gives the node there,
		/// and landmarks by their owner's ids.
		/// </summary>
		public static VisualMapData Snapshot(VisualMap map, Func<Pose, Pose> toStorage, Quaternion toStorageRotation,
			IEnumerable<VisualKeyframe> only = null, Func<ulong, Pose, Pose> raw = null, Func<ulong, ulong> wireNode = null)
		{
			VisualMapData data = new();
			if (only == null) data.landmarks.Capacity = map.LandmarkCount;
			int[] snapshotIds = new int[map.LandmarkIdLimit];
			Array.Fill(snapshotIds, LandmarkNotLookedUp);
			int[] rows = new int[XFeatExtractor.MaxKeypoints], sights = new int[XFeatExtractor.MaxKeypoints];
			foreach (VisualKeyframe k in only ?? map.Keyframes)
			{
				// the features that sight a usable landmark, and the strongest retrieval ranks by
				int count = 0;
				for (int j = 0; j < k.features.Count; j++)
				{
					int id = k.landmarks[j], sighted = SightsNothing;
					if (id >= 0)
					{
						if (snapshotIds[id] == LandmarkNotLookedUp)
							snapshotIds[id] = AddLandmark(data, map, id, toStorage, toStorageRotation, raw, wireNode);
						sighted = snapshotIds[id];
					}

					if (sighted == SightsNothing && j >= VisualMap.RetrievalFeatures) continue;
					rows[count] = j;
					sights[count++] = sighted;
				}

				data.keyframes.Add(new VisualMapData.Keyframe
				{
					camera = raw != null ? raw(k.node, k.cameraInNode) : toStorage(k.camera),
					intrinsics = k.intrinsics,
					timestampNs = k.timestampNs,
					original = k.original,
					locked = k.locked,
					node = wireNode?.Invoke(k.node) ?? 0,
					owner = k.owner,
					count = count,
					rows = rows.AsSpan(0, count).ToArray(),
					keypoints = k.features.keypoints,
					scores = k.features.scores,
					descriptors = k.features.descriptors,
					landmarks = sights.AsSpan(0, count).ToArray(),
				});
			}

			return data;
		}

		// the id features name a usable landmark by in the snapshot, adding it; otherwise SightsNothing
		private static int AddLandmark(VisualMapData data, VisualMap map, int id, Func<Pose, Pose> toStorage, Quaternion toStorageRotation,
			Func<ulong, Pose, Pose> raw, Func<ulong, ulong> wireNode)
		{
			if (!map.TryGetLandmark(id, out Landmark l) || !l.IsUsable) return SightsNothing;
			if (raw != null)
			{
				if (!l.hasWire)
				{
					Pose reference = raw(l.node, l.reference);
					l.wire = new VisualMapData.Landmark
					{
						id = l.ownerId,
						origin = reference.position,
						direction = reference.rotation * (Vector3)l.bearing,
						inverseDepth = l.inverseDepth,
						inverseDepthSigma = l.inverseDepthSigma,
						node = wireNode?.Invoke(l.node) ?? 0,
						owner = l.owner,
					};
					l.hasWire = l.established && map.freezeEstablished;
				}

				data.landmarks.Add(l.wire);
				return l.ownerId;
			}

			if (l.TryPlace(map.Anchoring, out float3 origin, out float3 direction))
				data.landmarks.Add(new VisualMapData.Landmark
				{
					id = id,
					origin = toStorage(new Pose(origin, Quaternion.identity)).position,
					direction = toStorageRotation * (Vector3)direction,
					inverseDepth = l.inverseDepth,
					inverseDepthSigma = l.inverseDepthSigma,
				});
			return id;
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

			byte[] packedDescriptor = new byte[D];
			foreach (VisualMapData.Keyframe k in data.keyframes)
			{
				WritePose(w, k.camera);
				w.Write(k.intrinsics.fx);
				w.Write(k.intrinsics.fy);
				w.Write(k.intrinsics.cx);
				w.Write(k.intrinsics.cy);
				w.Write(k.timestampNs);
				w.Write(k.original);
				w.Write(k.node);
				w.Write(k.owner);
				w.Write(k.count);
				for (int i = 0; i < k.count; i++)
				{
					int row = k.Row(i);
					w.Write((ushort)math.clamp(math.round(k.keypoints[row].x * PixelScale), 0, ushort.MaxValue));
					w.Write((ushort)math.clamp(math.round(k.keypoints[row].y * PixelScale), 0, ushort.MaxValue));
					w.Write(((half)k.scores[row]).value);
					w.Write(k.landmarks[i]);
				}

				for (int i = 0; i < k.count; i++)
				{
					int row = k.Row(i) * D;
					for (int c = 0; c < D; c++)
						packedDescriptor[c] = (byte)(sbyte)math.clamp(math.round((float)k.descriptors[row + c] * 127f), -127, 127);
					w.Write(packedDescriptor);
				}
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
				w.Write(l.node);
				w.Write(l.owner);
			}

			w.Write(data.graph?.Length ?? 0);
			if (data.graph != null) w.Write(data.graph);
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
					node = r.ReadUInt64(),
					owner = r.ReadUInt32(),
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
					node = r.ReadUInt64(),
					owner = r.ReadUInt32(),
				});

			int graphLength = r.ReadInt32();
			if (graphLength < 0) return null;
			data.graph = r.ReadBytes(graphLength);
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
