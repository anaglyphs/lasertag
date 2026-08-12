using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Anaglyph.XR.AprilTags
{
	/// <summary>Which of a stereo pair of cameras a lens belongs to.</summary>
	public enum LensPosition
	{
		Unknown,
		Left,
		Right,
	}

	public static class AndroidCamExtrinsicsHelper
	{
		// android.hardware.camera2.CameraMetadata constants
		private const int LensFacingFront = 0;
		private const int LensFacingBack = 1;
		private const int CapabilityMonochrome = 12;

		// android.graphics.ImageFormat.YUV_420_888
		private const int ImageFormatYuv420888 = 0x23;

		// Vendor tags Meta puts on the Quest passthrough cameras. Absent on
		// every other device, so they only ever refine the choice.
		private const string MetaSourceKeyName = "com.meta.extra_metadata.camera_source";
		private const string MetaPositionKeyName = "com.meta.extra_metadata.position";
		private const int MetaSourcePassthrough = 0;
		private const int MetaPositionLeft = 0;
		private const int MetaPositionRight = 1;

		private class Candidate
		{
			public string id;
			public Pose extrinsics;
			public int lensFacing = -1;
			public bool monochrome;
			public bool isPassthrough;
			public LensPosition position;
			public bool resolutionMatches;

			// Higher is better. Facing is filtered before scoring, so this only
			// ranks cameras that already point the right way.
			public int ScoreFor(LensPosition wanted)
			{
				int score = (isPassthrough ? 8 : 0) + (resolutionMatches ? 4 : 0);

				if (wanted == LensPosition.Unknown)
					// Nothing told us which lens produced the image, so lean on
					// AR Foundation's mono image coming from the left eye.
					return score + (position == LensPosition.Left ? 2 : 0);

				if (position == wanted)
					score += 16;
				else if (position != LensPosition.Unknown)
					score -= 16; // known to be the other eye

				return score;
			}

			public override string ToString() =>
				$"id {id}, facing {lensFacing}, mono {monochrome}, passthrough {isPassthrough}, " +
				$"lens {position}, resMatch {resolutionMatches}, pos {extrinsics.position}";
		}

		/// <summary>
		/// Finds the physical camera feeding an AR Foundation camera image and
		/// returns its lens pose relative to the head, without hard-coding IDs.
		/// </summary>
		/// <param name="facing">Facing direction reported by ARCameraManager.</param>
		/// <param name="imageResolution">Resolution of the acquired XRCpuImage,
		/// used to disambiguate cameras that otherwise look alike.</param>
		/// <param name="lensPosition">Which lens the image came from, when the
		/// platform told us. <see cref="LensPosition.Unknown"/> falls back to
		/// inferring it.</param>
		public static bool TryFindCameraExtrinsics(CameraFacingDirection facing,
			Vector2Int imageResolution, LensPosition lensPosition, out Pose pose, out string cameraId)
		{
			pose = Pose.identity;
			cameraId = null;

			int wantedFacing = facing switch
			{
				CameraFacingDirection.User => LensFacingFront,
				_ => LensFacingBack, // world-facing, i.e. the front of a headset
			};

			List<Candidate> candidates = EnumerateCameras(imageResolution);

			if (candidates.Count == 0)
			{
				Debug.LogError("No cameras with lens extrinsics found on this device");
				return false;
			}

			Candidate best = null;

			// Colour cameras pointing the right way, then anything pointing the
			// right way, then anything at all -- so an unusual device still
			// gets a usable pose instead of nothing.
			foreach (Func<Candidate, bool> filter in new Func<Candidate, bool>[]
			{
				c => c.lensFacing == wantedFacing && !c.monochrome,
				c => c.lensFacing == wantedFacing,
				c => true,
			})
			{
				foreach (Candidate c in candidates)
				{
					if (!filter(c))
						continue;

					if (best == null || c.ScoreFor(lensPosition) > best.ScoreFor(lensPosition) ||
					    // Break ties towards the leftmost lens: AR Foundation's
					    // mono world camera on Quest is the left eye camera.
					    (c.ScoreFor(lensPosition) == best.ScoreFor(lensPosition) &&
					     c.extrinsics.position.x < best.extrinsics.position.x))
						best = c;
				}

				if (best != null)
					break;
			}

			if (best == null)
				return false;

			StringBuilder log = new();
			log.AppendLine($"Using camera {best.id} for tag tracking ({best})");
			foreach (Candidate c in candidates)
				log.AppendLine($"  candidate: {c}");
			Debug.Log(log.ToString());

			pose = best.extrinsics;
			cameraId = best.id;
			return true;
		}

		private static List<Candidate> EnumerateCameras(Vector2Int imageResolution)
		{
			List<Candidate> candidates = new();

			using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
			using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

			using AndroidJavaObject cameraManager =
				activity.Call<AndroidJavaObject>("getSystemService", "camera");

			string[] ids = cameraManager.Call<string[]>("getCameraIdList");

			foreach (string id in ids)
			{
				AndroidJavaObject characteristics = null;

				try
				{
					characteristics = cameraManager.Call<AndroidJavaObject>("getCameraCharacteristics", id);

					if (!TryReadExtrinsics(characteristics, out Pose extrinsics))
						continue;

					Candidate candidate = new()
					{
						id = id,
						extrinsics = extrinsics,
						lensFacing = GetIntCharacteristic(characteristics, "LENS_FACING") ?? -1,
						monochrome = HasCapability(characteristics, CapabilityMonochrome),
						resolutionMatches = OutputsResolution(characteristics, imageResolution),
					};

					int? source = GetVendorInt(characteristics, MetaSourceKeyName);
					candidate.isPassthrough = source == MetaSourcePassthrough;

					candidate.position = GetVendorInt(characteristics, MetaPositionKeyName) switch
					{
						MetaPositionLeft => LensPosition.Left,
						MetaPositionRight => LensPosition.Right,
						_ => LensPosition.Unknown,
					};

					candidates.Add(candidate);
				}
				catch (Exception e)
				{
					Debug.LogWarning($"Could not read characteristics of camera {id}: {e.Message}");
				}
				finally
				{
					characteristics?.Dispose();
				}
			}

			return candidates;
		}

		private static bool TryReadExtrinsics(AndroidJavaObject characteristics, out Pose pose)
		{
			pose = Pose.identity;

			using AndroidJavaClass cc = new("android.hardware.camera2.CameraCharacteristics");

			using AndroidJavaObject keyRotation = cc.GetStatic<AndroidJavaObject>("LENS_POSE_ROTATION");
			using AndroidJavaObject keyTranslation = cc.GetStatic<AndroidJavaObject>("LENS_POSE_TRANSLATION");

			float[] pos = characteristics.Call<float[]>("get", keyTranslation);
			float[] rot = characteristics.Call<float[]>("get", keyRotation);

			if (pos == null || rot == null || pos.Length < 3 || rot.Length < 4)
				return false;

			Vector3 position = new(pos[0], pos[1], -pos[2]);
			Quaternion rotation = Quaternion.Inverse(new Quaternion(-rot[0], -rot[1], rot[2], rot[3])) *
			                      Quaternion.Euler(180, 0, 0);

			pose = new Pose(position, rotation);
			return true;
		}

		private static int? GetIntCharacteristic(AndroidJavaObject characteristics, string keyName)
		{
			using AndroidJavaClass cc = new("android.hardware.camera2.CameraCharacteristics");
			using AndroidJavaObject key = cc.GetStatic<AndroidJavaObject>(keyName);

			using AndroidJavaObject value = characteristics.Call<AndroidJavaObject>("get", key);
			return UnboxInt(value);
		}

		private static bool HasCapability(AndroidJavaObject characteristics, int capability)
		{
			try
			{
				using AndroidJavaClass cc = new("android.hardware.camera2.CameraCharacteristics");
				using AndroidJavaObject key = cc.GetStatic<AndroidJavaObject>("REQUEST_AVAILABLE_CAPABILITIES");

				int[] capabilities = characteristics.Call<int[]>("get", key);

				if (capabilities == null)
					return false;

				return Array.IndexOf(capabilities, capability) >= 0;
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static bool OutputsResolution(AndroidJavaObject characteristics, Vector2Int resolution)
		{
			if (resolution.x <= 0 || resolution.y <= 0)
				return false;

			try
			{
				using AndroidJavaClass cc = new("android.hardware.camera2.CameraCharacteristics");
				using AndroidJavaObject key = cc.GetStatic<AndroidJavaObject>("SCALER_STREAM_CONFIGURATION_MAP");

				using AndroidJavaObject map = characteristics.Call<AndroidJavaObject>("get", key);

				if (map == null)
					return false;

				AndroidJavaObject[] sizes = map.Call<AndroidJavaObject[]>("getOutputSizes", ImageFormatYuv420888);

				if (sizes == null)
					return false;

				foreach (AndroidJavaObject size in sizes)
				{
					using (size)
					{
						int width = size.Call<int>("getWidth");
						int height = size.Call<int>("getHeight");

						if ((width == resolution.x && height == resolution.y) ||
						    (width == resolution.y && height == resolution.x))
							return true;
					}
				}
			}
			catch (Exception)
			{
				// treated as "can't tell", the caller falls back to other signals
			}

			return false;
		}

		// Vendor tags can't be reached through the static key fields, so look
		// the key up by name among the ones this camera actually publishes.
		private static int? GetVendorInt(AndroidJavaObject characteristics, string keyName)
		{
			try
			{
				using AndroidJavaObject keys = characteristics.Call<AndroidJavaObject>("getKeys");

				if (keys == null)
					return null;

				int count = keys.Call<int>("size");

				for (int i = 0; i < count; i++)
				{
					using AndroidJavaObject key = keys.Call<AndroidJavaObject>("get", i);

					if (key == null || key.Call<string>("getName") != keyName)
						continue;

					using AndroidJavaObject value = characteristics.Call<AndroidJavaObject>("get", key);
					return UnboxInt(value);
				}
			}
			catch (Exception)
			{
				// device doesn't expose vendor tags
			}

			return null;
		}

		// CameraCharacteristics.get returns boxed numbers whose exact type
		// varies by tag, so try the plausible unboxers in turn.
		private static int? UnboxInt(AndroidJavaObject value)
		{
			if (value == null)
				return null;

			try { return value.Call<int>("intValue"); }
			catch (Exception) { }

			try { return value.Call<sbyte>("byteValue"); }
			catch (Exception) { }

			return null;
		}
	}
}
