#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.VisualLocalization;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>One view as <see cref="GraphReplay"/> needs it: all its features, and its landmarks as raw tracking placed them.</summary>
	public sealed class RecordedView
	{
		public uint owner;
		public ulong knotKey;
		public double capturedAt, evictedAt;
		public bool shared;
		public Pose raw;
		public PinholeCamera intrinsics;
		public long timestampNs;
		public float2[] keypoints;
		public half[] descriptors;
		/// <summary>Per feature, the landmark it sights, or -1.</summary>
		public int[] landmarks;
		public float3[] points, directions;
		public float[] inverseDepths, inverseDepthSigmas;
		/// <summary>Per feature, how far along its ray the simulated room really is; NaN where unknown.</summary>
		public float[] trueDepths;

		public int Count => keypoints.Length;
	}

	/// <summary>
	/// Keeps every view this headset makes during a run, evicted ones too, and writes them all when
	/// it ends: a map keeps only so many, and a replay wants what the walk saw. Landmarks are read
	/// again every so often, since one goes when the last view sighting it does.
	/// </summary>
	public sealed class ViewRecorder : IDisposable
	{
		private const uint Magic = 0x57454956, Version = 2;

		private readonly VisualColocationConstraintProvider provider;
		private readonly VisualLocalizer localizer;
		private readonly Func<double> now;
		private readonly List<(VisualKeyframe keyframe, RecordedView view)> views = new();
		private readonly Dictionary<VisualKeyframe, RecordedView> byKeyframe = new();
		private readonly HashSet<VisualKeyframe> evicted = new();

		public ViewRecorder(VisualColocationConstraintProvider provider, VisualLocalizer localizer, Func<double> now)
		{
			this.provider = provider;
			this.localizer = localizer;
			this.now = now;
			localizer.KeyframeAdded += OnKeyframeAdded;
			localizer.Map.Evicted += OnEvicted;
			localizer.Map.Cleared += OnCleared;
		}

		public void Dispose()
		{
			if (!localizer) return;
			localizer.KeyframeAdded -= OnKeyframeAdded;
			localizer.Map.Evicted -= OnEvicted;
			localizer.Map.Cleared -= OnCleared;
		}

		private void OnKeyframeAdded(VisualKeyframe keyframe)
		{
			ulong wire = provider.Graph.WireNode(keyframe.node);
			if (wire == 0) return;
			FeatureSet features = keyframe.features;
			RecordedView view = new()
			{
				owner = keyframe.owner,
				knotKey = KnotKeys.Key(wire),
				capturedAt = now(),
				evictedAt = double.PositiveInfinity,
				raw = provider.Track.Raw(keyframe.node, keyframe.cameraInNode),
				intrinsics = keyframe.intrinsics,
				timestampNs = keyframe.timestampNs,
				keypoints = features.keypoints.AsSpan(0, features.Count).ToArray(),
				descriptors = features.descriptors,
				landmarks = new int[features.Count],
				points = new float3[features.Count],
				directions = new float3[features.Count],
				inverseDepths = new float[features.Count],
				inverseDepthSigmas = new float[features.Count],
			};
			Array.Fill(view.landmarks, -1);
			views.Add((keyframe, view));
			byKeyframe[keyframe] = view;
		}

		private void OnEvicted(VisualKeyframe keyframe)
		{
			if (!byKeyframe.TryGetValue(keyframe, out RecordedView view)) return;
			if (!evicted.Add(keyframe)) return;
			view.evictedAt = now();
		}

		private void OnCleared()
		{
			foreach ((VisualKeyframe keyframe, RecordedView _) in views) OnEvicted(keyframe);
		}

		/// <summary>Reads the landmarks of every view still in the map as they are now.</summary>
		public void Refresh()
		{
			VisualMap map = localizer.Map;
			KeyframeGraph graph = provider.Graph;
			TrackAnchoring track = provider.Track;
			foreach ((VisualKeyframe keyframe, RecordedView view) in views)
			{
				if (evicted.Contains(keyframe)) continue;
				view.shared = keyframe.shared;
				for (int j = 0; j < view.Count; j++)
				{
					view.landmarks[j] = -1;
					if (!map.TryGetLandmark(keyframe.landmarks[j], out Landmark landmark) || !landmark.IsUsable) continue;
					view.landmarks[j] = landmark.ownerId;
					view.points[j] = graph.RawPoint(landmark);
					view.directions[j] = Pose4.RotateY(math.mul(landmark.reference.rotation, landmark.bearing), -track.CorrectionOf(landmark.node).yaw);
					view.inverseDepths[j] = landmark.inverseDepth;
					view.inverseDepthSigmas[j] = landmark.inverseDepthSigma;
				}
			}
		}

		/// <summary>Whole or not at all: a replay can tell a recording is complete by its being there.</summary>
		public void Write(string path)
		{
			Refresh();
			foreach ((VisualKeyframe keyframe, RecordedView view) in views)
			{
				view.trueDepths = new float[view.Count];
				Array.Fill(view.trueDepths, float.NaN);
				float[] known = SimLandmarkDepthCheck.TrueDepths(keyframe.frame);
				if (known != null) Array.Copy(known, view.trueDepths, math.min(known.Length, view.Count));
			}

			string temporary = path + ".tmp";
			using (BinaryWriter w = new(File.Create(temporary)))
			{
				w.Write(Magic);
				w.Write(Version);
				w.Write(views.Count);
				foreach ((VisualKeyframe _, RecordedView v) in views)
				{
					w.Write(v.owner);
					w.Write(v.knotKey);
					w.Write(v.capturedAt);
					w.Write(v.evictedAt);
					w.Write(v.shared);
					w.Write(v.raw.position.x);
					w.Write(v.raw.position.y);
					w.Write(v.raw.position.z);
					w.Write(v.raw.rotation.x);
					w.Write(v.raw.rotation.y);
					w.Write(v.raw.rotation.z);
					w.Write(v.raw.rotation.w);
					w.Write(v.intrinsics.fx);
					w.Write(v.intrinsics.fy);
					w.Write(v.intrinsics.cx);
					w.Write(v.intrinsics.cy);
					w.Write(v.timestampNs);
					w.Write(v.Count);
					Put<float2>(w, v.keypoints.AsSpan(0, v.Count));
					Put<half>(w, v.descriptors.AsSpan(0, v.Count * FeatureSet.DescriptorSize));
					Put<int>(w, v.landmarks);
					Put<float3>(w, v.points);
					Put<float3>(w, v.directions);
					Put<float>(w, v.inverseDepths);
					Put<float>(w, v.inverseDepthSigmas);
					Put<float>(w, v.trueDepths);
				}
			}

			File.Delete(path);
			File.Move(temporary, path);
		}

		/// <summary>Null when the file isn't a whole recording.</summary>
		public static List<RecordedView> Read(string path)
		{
			if (!File.Exists(path)) return null;
			using BinaryReader r = new(File.OpenRead(path));
			if (r.ReadUInt32() != Magic) return null;
			uint version = r.ReadUInt32();
			int count = r.ReadInt32();
			List<RecordedView> views = new(count);
			for (int i = 0; i < count; i++)
			{
				RecordedView v = new()
				{
					owner = r.ReadUInt32(),
					knotKey = r.ReadUInt64(),
					capturedAt = r.ReadDouble(),
					evictedAt = r.ReadDouble(),
					shared = r.ReadBoolean(),
					raw = new Pose(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle())),
					intrinsics = new PinholeCamera(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
					timestampNs = r.ReadInt64(),
				};
				int features = r.ReadInt32();
				v.keypoints = Take<float2>(r, features);
				v.descriptors = Take<half>(r, features * FeatureSet.DescriptorSize);
				v.landmarks = Take<int>(r, features);
				v.points = Take<float3>(r, features);
				v.directions = Take<float3>(r, features);
				v.inverseDepths = Take<float>(r, features);
				v.inverseDepthSigmas = Take<float>(r, features);
				if (version >= 2) v.trueDepths = Take<float>(r, features);
				views.Add(v);
			}

			return views;
		}

		private static void Put<T>(BinaryWriter w, ReadOnlySpan<T> values) where T : unmanaged => w.Write(MemoryMarshal.AsBytes(values));

		private static T[] Take<T>(BinaryReader r, int count) where T : unmanaged
		{
			T[] values = new T[count];
			Span<byte> bytes = MemoryMarshal.AsBytes(values.AsSpan());
			while (bytes.Length > 0)
			{
				int read = r.BaseStream.Read(bytes);
				if (read <= 0) throw new EndOfStreamException();
				bytes = bytes.Slice(read);
			}

			return values;
		}
	}
}
#endif
