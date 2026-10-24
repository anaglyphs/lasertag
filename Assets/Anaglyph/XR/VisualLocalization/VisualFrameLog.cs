using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>One frame the localizer looked at, kept for offline diagnosis and tuning.</summary>
	public sealed class VisualFrameRecord
	{
		public int sequence;
		/// <summary>"map" (a keyframe candidate), "query" (this headset's), or "remote-query" (another's, features only).</summary>
		public string purpose;
		public long timestampNs;
		public float realtime;
		public PinholeCamera intrinsics;
		public Pose camera;
		public Pose cameraInTrackingSpace;
		public Pose trackingSpace;
		public ulong node;
		public Pose cameraInNode;
		public float angularSpeed;
		public bool rendered;
		/// <summary>Grayscale PNG of the extractor's input, top row first; null until encoded, and for remote queries.</summary>
		public byte[] png;
		public FeatureSet features;
		public readonly List<string> notes = new();
		internal bool pinned;
		// the raw image until the PNG exists, so an export never waits on the encoder
		internal float[] pixels;
	}

	/// <summary>
	/// Keeps what the localizer saw so a diagnosis capture can save it: the image (PNG,
	/// encoded off the main thread), the camera's poses and intrinsics, its features, and
	/// notes on what became of it. Keyframes' frames are kept while they are in the map;
	/// other frames in a ring of <see cref="capacity"/>. Everything needed to replay
	/// mapping and localization offline, with different settings.
	/// </summary>
	public sealed class VisualFrameLog
	{
		public bool enabled = true;
		public int capacity = 96;

		private readonly List<VisualFrameRecord> records = new();
		private readonly Dictionary<int, VisualFrameRecord> bySequence = new();
		private int nextSequence = 1;

		public int Count => records.Count;

		/// <summary>Records a captured frame; null when logging is off.</summary>
		public VisualFrameRecord Begin(string purpose, CapturedImage image, ulong node = 0, Pose cameraInNode = default)
		{
			if (!enabled) return null;
			VisualFrameRecord r = Add(purpose);
			r.timestampNs = image.timestampNs;
			r.intrinsics = image.intrinsics;
			r.camera = image.cameraInWorld;
			r.cameraInTrackingSpace = image.cameraInTrackingSpace;
			r.trackingSpace = image.trackingSpace;
			r.node = node;
			r.cameraInNode = cameraInNode;
			r.angularSpeed = image.angularSpeed;
			r.rendered = image.rendered;

			// copied now, the image buffer is reused; converted with the encoder, off the main
			// thread. A pooled buffer: a fresh 1.2 MB array per frame is garbage the GC must chase
			r.pixels = ArrayPool<float>.Shared.Rent(image.pixels.Length);
			NativeArray<float>.Copy(image.pixels, r.pixels, image.pixels.Length);
			Encode(r);
			return r;
		}

		/// <summary>Records a query another headset sent: its features and camera, no image.</summary>
		public VisualFrameRecord BeginRemote(VisualQuery query, ulong sender)
		{
			if (!enabled) return null;
			VisualFrameRecord r = Add("remote-query");
			r.timestampNs = query.timestampNs;
			r.intrinsics = query.intrinsics;
			r.camera = query.camera;
			r.trackingSpace = query.trackingSpace;
			r.features = query.features;
			r.notes.Add($"from client {sender}");
			return r;
		}

		private VisualFrameRecord Add(string purpose)
		{
			VisualFrameRecord r = new() { sequence = nextSequence++, purpose = purpose, realtime = Time.realtimeSinceStartup };
			records.Add(r);
			bySequence[r.sequence] = r;
			Trim();
			return r;
		}

		public bool TryGet(int sequence, out VisualFrameRecord record) => bySequence.TryGetValue(sequence, out record);

		public void Note(int sequence, string note)
		{
			if (bySequence.TryGetValue(sequence, out VisualFrameRecord r)) r.notes.Add(note);
		}

		/// <summary>A keyframe's frame stays until the keyframe leaves the map.</summary>
		public void Pin(int sequence, bool pinned)
		{
			if (!bySequence.TryGetValue(sequence, out VisualFrameRecord r)) return;
			r.pinned = pinned;
			if (!pinned) Trim();
		}

		public void UnpinAll()
		{
			foreach (VisualFrameRecord r in records) r.pinned = false;
			Trim();
		}

		public void Clear()
		{
			records.Clear();
			bySequence.Clear();
		}

		private void Trim()
		{
			int unpinned = 0;
			foreach (VisualFrameRecord r in records) if (!r.pinned) unpinned++;
			for (int i = 0; i < records.Count && unpinned > capacity; i++)
			{
				if (records[i].pinned) continue;
				bySequence.Remove(records[i].sequence);
				records.RemoveAt(i--);
				unpinned--;
			}
		}

		private static async void Encode(VisualFrameRecord r)
		{
			float[] pixels = r.pixels;
			try
			{
				await Awaitable.BackgroundThreadAsync();
				byte[] png = EncodePng(pixels);
				await Awaitable.MainThreadAsync();
				r.png ??= png;
				r.pixels = null;
				ArrayPool<float>.Shared.Return(pixels);
			}
			catch (Exception e)
			{
				await Awaitable.MainThreadAsync();
				r.notes.Add($"PNG encoding failed: {e.Message}");
			}
		}

		// flipped: Unity's encoder writes row 0 at the bottom
		private static byte[] EncodePng(float[] pixels)
		{
			const int w = XFeatExtractor.Width, h = XFeatExtractor.Height;
			byte[] gray = new byte[w * h];
			for (int y = 0; y < h; y++)
			{
				int src = y * w, dst = (h - 1 - y) * w;
				for (int x = 0; x < w; x++) gray[dst + x] = (byte)math.clamp(pixels[src + x] * 255f + 0.5f, 0, 255);
			}

			return ImageConversion.EncodeArrayToPNG(gray, GraphicsFormat.R8_UNorm, w, h);
		}

		// ---- export ------------------------------------------------------------

		/// <summary>
		/// Writes every kept frame into <paramref name="folder"/>: frames/NNNNNN.png and
		/// .features (binary: count, keypoints as float2, scores, descriptors as half) and
		/// frames.json with each frame's metadata. A PNG still being encoded is encoded
		/// again here rather than waited for.
		/// </summary>
		public async Awaitable ExportAsync(string folder)
		{
			string frames = Path.Combine(folder, "frames");
			Directory.CreateDirectory(frames);
			VisualFrameRecord[] snapshot = records.ToArray();

			Json j = new();
			j.BeginArray();
			foreach (VisualFrameRecord r in snapshot)
			{
				j.BeginObject();
				j.Field("sequence", r.sequence);
				j.Field("purpose", r.purpose);
				j.Field("timestampNs", r.timestampNs);
				j.Field("realtime", r.realtime);
				j.Field("pinned", r.pinned);
				j.Field("intrinsics", r.intrinsics);
				j.Field("camera", r.camera);
				j.Field("cameraInTrackingSpace", r.cameraInTrackingSpace);
				j.Field("trackingSpace", r.trackingSpace);
				j.Field("node", r.node);
				j.Field("cameraInNode", r.cameraInNode);
				j.Field("angularSpeed", r.angularSpeed);
				j.Field("rendered", r.rendered);
				j.Field("image", r.png != null || r.pixels != null ? $"frames/{r.sequence:D6}.png" : null);
				j.Field("features", r.features != null ? $"frames/{r.sequence:D6}.features" : null);
				j.Field("featureCount", r.features?.Count ?? 0);
				j.Key("notes");
				j.BeginArray();
				foreach (string note in r.notes) j.Value(note);
				j.EndArray();
				j.EndObject();
			}

			j.EndArray();
			string json = j.ToString();
			// frames still encoding: their pooled buffers go back when the encode ends, so copy them
			(VisualFrameRecord record, byte[] png, float[] pixels)[] images = Array.ConvertAll(snapshot, r => (r, r.png, r.png == null ? (float[])r.pixels?.Clone() : null));

			await Awaitable.BackgroundThreadAsync();
			try
			{
				File.WriteAllText(Path.Combine(folder, "frames.json"), json);
				foreach ((VisualFrameRecord r, byte[] png, float[] pixels) in images)
				{
					byte[] image = png ?? (pixels != null ? EncodePng(pixels) : null);
					if (image != null) File.WriteAllBytes(Path.Combine(frames, $"{r.sequence:D6}.png"), image);
					if (r.features != null) WriteFeatures(Path.Combine(frames, $"{r.sequence:D6}.features"), r.features);
				}
			}
			finally
			{
				await Awaitable.MainThreadAsync();
			}
		}

		public static void WriteFeatures(string path, FeatureSet f)
		{
			using BinaryWriter w = new(File.Create(path));
			w.Write(f.Count);
			for (int i = 0; i < f.Count; i++)
			{
				w.Write(f.keypoints[i].x);
				w.Write(f.keypoints[i].y);
			}

			for (int i = 0; i < f.Count; i++) w.Write(f.scores[i]);
			for (int i = 0; i < f.Count * FeatureSet.DescriptorSize; i++) w.Write(f.descriptors[i].value);
		}
	}

	/// <summary>A small JSON writer: invariant culture, poses as {p:[x,y,z], q:[x,y,z,w]}, node keys as hex strings.</summary>
	public sealed class Json
	{
		private readonly StringBuilder sb = new();
		private bool needComma;

		public override string ToString() => sb.ToString();

		private void Separate()
		{
			if (needComma) sb.Append(',');
			needComma = false;
		}

		public void BeginObject() { Separate(); sb.Append('{'); }
		public void EndObject() { sb.Append('}'); needComma = true; }
		public void BeginArray() { Separate(); sb.Append('['); }
		public void EndArray() { sb.Append(']'); needComma = true; }

		public void Key(string key)
		{
			Separate();
			WriteString(key);
			sb.Append(':');
		}

		public void Value(string v) { Separate(); if (v == null) sb.Append("null"); else WriteString(v); needComma = true; }
		public void Value(float v) { Separate(); Number(v); needComma = true; }
		public void Value(double v) { Separate(); sb.Append(double.IsFinite(v) ? v.ToString("R", CultureInfo.InvariantCulture) : "null"); needComma = true; }
		public void Value(long v) { Separate(); sb.Append(v.ToString(CultureInfo.InvariantCulture)); needComma = true; }
		public void Value(int v) { Separate(); sb.Append(v.ToString(CultureInfo.InvariantCulture)); needComma = true; }
		public void Value(bool v) { Separate(); sb.Append(v ? "true" : "false"); needComma = true; }
		public void Node(ulong key) => Value(key == 0 ? null : key.ToString("X16"));

		public void Value(float3 v) { BeginArray(); Value(v.x); Value(v.y); Value(v.z); EndArray(); }
		public void Value(float2 v) { BeginArray(); Value(v.x); Value(v.y); EndArray(); }

		public void Value(Pose p)
		{
			BeginObject();
			Key("p"); Value((float3)p.position);
			Key("q"); BeginArray(); Value(p.rotation.x); Value(p.rotation.y); Value(p.rotation.z); Value(p.rotation.w); EndArray();
			EndObject();
		}

		public void Value(PinholeCamera c)
		{
			BeginObject();
			Field("fx", c.fx); Field("fy", c.fy); Field("cx", c.cx); Field("cy", c.cy);
			Field("width", XFeatExtractor.Width); Field("height", XFeatExtractor.Height);
			EndObject();
		}

		public void Field(string key, string v) { Key(key); Value(v); }
		public void Field(string key, float v) { Key(key); Value(v); }
		public void Field(string key, double v) { Key(key); Value(v); }
		public void Field(string key, long v) { Key(key); Value(v); }
		public void Field(string key, int v) { Key(key); Value(v); }
		public void Field(string key, bool v) { Key(key); Value(v); }
		public void Field(string key, ulong node) { Key(key); Node(node); }
		public void Field(string key, float3 v) { Key(key); Value(v); }
		public void Field(string key, Pose v) { Key(key); Value(v); }
		public void Field(string key, PinholeCamera v) { Key(key); Value(v); }

		private void Number(float v) => sb.Append(float.IsFinite(v) ? v.ToString("R", CultureInfo.InvariantCulture) : "null");

		private void WriteString(string s)
		{
			sb.Append('"');
			foreach (char c in s)
			{
				switch (c)
				{
					case '"': sb.Append("\\\""); break;
					case '\\': sb.Append("\\\\"); break;
					case '\n': sb.Append("\\n"); break;
					case '\r': sb.Append("\\r"); break;
					case '\t': sb.Append("\\t"); break;
					default:
						if (c < 0x20) sb.Append($"\\u{(int)c:X4}");
						else sb.Append(c);
						break;
				}
			}

			sb.Append('"');
		}
	}
}
