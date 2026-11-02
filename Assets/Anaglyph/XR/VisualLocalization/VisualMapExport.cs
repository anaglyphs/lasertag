using System.IO;
using UnityEngine;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>A map and the frames behind it as readable files, for diagnosis.</summary>
	public static class VisualMapExport
	{
		/// Saves the map and the frames behind it into <paramref name="folder"/>:
		/// settings.json (<paramref name="settings"/>), map.json (keyframes: frame, node,
		/// camera, intrinsics, per-feature landmark ids; landmarks: node,
		/// reference camera, bearing, inverse depth and its sigma, sightings),
		/// and the frame log (frames.json, frames/*.png, frames/*.features).
		/// </summary>
		public static async Awaitable ExportAsync(VisualMap map, VisualMapQuality q, VisualFrameLog frames, string settings, string folder)
		{
			Directory.CreateDirectory(folder);

			Json j = new();
			j.BeginObject();
			j.Key("quality");
			j.BeginObject();
			j.Field("keyframes", q.keyframes);
			j.Field("landmarks", q.landmarks);
			j.Field("usable", q.usable);
			j.Field("ranged", q.ranged);
			j.EndObject();

			j.Key("keyframes");
			j.BeginArray();
			foreach (VisualKeyframe k in map.Keyframes)
			{
				j.BeginObject();
				j.Field("frame", k.frame);
				j.Field("slot", k.slot);
				j.Field("node", k.node);
				j.Field("cameraInNode", k.cameraInNode);
				j.Field("camera", k.camera);
				j.Field("intrinsics", k.intrinsics);
				j.Field("timestampNs", k.timestampNs);
				j.Field("locked", k.locked);
				j.Field("featureCount", k.features.Count);
				j.Key("landmarks");
				j.BeginArray();
				foreach (int id in k.landmarks) j.Value(id);
				j.EndArray();
				j.EndObject();
			}

			j.EndArray();

			j.Key("landmarks");
			j.BeginArray();
			foreach (Landmark l in map.Landmarks)
			{
				j.BeginObject();
				j.Field("id", l.id);
				j.Field("node", l.node);
				j.Field("reference", l.reference);
				j.Field("bearing", l.bearing);
				j.Field("inverseDepth", l.inverseDepth);
				j.Field("inverseDepthSigma", l.inverseDepthSigma);
				j.Field("references", l.references);
				j.Key("observations");
				j.BeginArray();
				foreach (Observation o in l.observations)
				{
					j.BeginObject();
					j.Field("frame", o.keyframe?.frame ?? 0);
					j.Field("feature", o.feature);
					j.Field("node", o.node);
					j.Field("cameraInNode", o.cameraInNode);
					j.Field("ray", o.ray);
					j.Field("angularSigma", o.angularSigma);
					j.EndObject();
				}

				j.EndArray();
				j.EndObject();
			}

			j.EndArray();
			j.EndObject();
			string json = j.ToString();

			await Awaitable.BackgroundThreadAsync();
			try
			{
				File.WriteAllText(Path.Combine(folder, "settings.json"), settings);
				File.WriteAllText(Path.Combine(folder, "map.json"), json);
			}
			finally
			{
				await Awaitable.MainThreadAsync();
			}

			await frames.ExportAsync(folder);
		}
	}
}
