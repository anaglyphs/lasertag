using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Anaglyph.XR.Haptics
{
	/// <summary>A Meta Haptics Studio clip (.haptic JSON) played on a phone's vibration motor: Core Haptics on iOS, an amplitude waveform on Android. Plays nothing elsewhere.</summary>
	public sealed class PhoneHapticClip
	{
		private const float AndroidSampleSeconds = 0.01f;
		private const int CoreHapticsMaxCurvePoints = 16;

		private readonly StudioEnvelopes envelopes;

#if UNITY_IOS && !UNITY_EDITOR
		private readonly int continuousPattern;
		private readonly int emphasisPattern;

		[DllImport("__Internal")] private static extern int AnaglyphHapticsRegister(string ahapJson);
		[DllImport("__Internal")] private static extern void AnaglyphHapticsPlay(int pattern);
#elif UNITY_ANDROID && !UNITY_EDITOR
		private static AndroidJavaObject vibrator;
		private readonly AndroidJavaObject waveform;
#endif

		public float DurationSeconds { get; }

		public PhoneHapticClip(string hapticsStudioJson)
		{
			envelopes = JsonUtility.FromJson<StudioClip>(hapticsStudioJson)?.signals?.continuous?.envelopes;
			if (envelopes?.amplitude == null || envelopes.amplitude.Length == 0) return;
			envelopes.frequency ??= Array.Empty<StudioFrequencyPoint>();

			DurationSeconds = envelopes.amplitude[^1].time;
			if (envelopes.frequency.Length > 0)
				DurationSeconds = Mathf.Max(DurationSeconds, envelopes.frequency[^1].time);

#if UNITY_IOS && !UNITY_EDITOR
			// Core Haptics intensity curves scale every event, so emphasis plays as its own pattern to stay independent of the envelope
			continuousPattern = AnaglyphHapticsRegister(BuildContinuousAhap());
			emphasisPattern = HasEmphasis() ? AnaglyphHapticsRegister(BuildEmphasisAhap()) : -1;
#elif UNITY_ANDROID && !UNITY_EDITOR
			waveform = BuildAndroidWaveform();
#endif
		}

		public void Play()
		{
			if (DurationSeconds <= 0) return;
#if UNITY_IOS && !UNITY_EDITOR
			AnaglyphHapticsPlay(continuousPattern);
			if (emphasisPattern >= 0) AnaglyphHapticsPlay(emphasisPattern);
#elif UNITY_ANDROID && !UNITY_EDITOR
			if (waveform == null) return;
			vibrator ??= GetAndroidVibrator();
			vibrator?.Call("vibrate", waveform);
#endif
		}

		private bool HasEmphasis()
		{
			foreach (StudioAmplitudePoint point in envelopes.amplitude)
				if (point.emphasis != null && point.emphasis.amplitude > 0) return true;
			return false;
		}

		private float AmplitudeAt(float time)
		{
			StudioAmplitudePoint[] points = envelopes.amplitude;
			if (time <= points[0].time) return points[0].amplitude;
			for (int i = 1; i < points.Length; i++)
			{
				if (time > points[i].time) continue;
				float span = points[i].time - points[i - 1].time;
				float t = span > 0 ? (time - points[i - 1].time) / span : 1;
				return Mathf.Lerp(points[i - 1].amplitude, points[i].amplitude, t);
			}

			return points[^1].amplitude;
		}

#if UNITY_ANDROID && !UNITY_EDITOR
		private static AndroidJavaObject GetAndroidVibrator()
		{
			using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
			using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
			AndroidJavaObject found = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
			return found != null && found.Call<bool>("hasVibrator") ? found : null;
		}

		// Android motors have no frequency control: the amplitude envelope is sampled, with emphasis peaks raised to full strength
		private AndroidJavaObject BuildAndroidWaveform()
		{
			int sampleCount = Mathf.Max(1, Mathf.CeilToInt(DurationSeconds / AndroidSampleSeconds));
			int[] samples = new int[sampleCount];
			for (int i = 0; i < sampleCount; i++)
				samples[i] = Mathf.RoundToInt(AmplitudeAt((i + 0.5f) * AndroidSampleSeconds) * 255);

			foreach (StudioAmplitudePoint point in envelopes.amplitude)
			{
				if (point.emphasis == null || point.emphasis.amplitude <= 0) continue;
				int i = Mathf.Min(sampleCount - 1, (int)(point.time / AndroidSampleSeconds));
				samples[i] = Mathf.Max(samples[i], Mathf.RoundToInt(point.emphasis.amplitude * 255));
			}

			List<long> timings = new();
			List<int> amplitudes = new();
			long sampleMilliseconds = Mathf.RoundToInt(AndroidSampleSeconds * 1000);
			foreach (int amplitude in samples)
			{
				int clamped = Mathf.Clamp(amplitude, 0, 255);
				if (amplitudes.Count > 0 && amplitudes[^1] == clamped)
					timings[^1] += sampleMilliseconds;
				else
				{
					timings.Add(sampleMilliseconds);
					amplitudes.Add(clamped);
				}
			}

			using AndroidJavaClass effectClass = new("android.os.VibrationEffect");
			return effectClass.CallStatic<AndroidJavaObject>("createWaveform", timings.ToArray(), amplitudes.ToArray(), -1);
		}
#endif

		private string BuildContinuousAhap()
		{
			StringBuilder ahap = new("{\"Version\":1.0,\"Pattern\":[");
			// base intensity 1 is scaled by the intensity curve; base sharpness 0 is offset by the sharpness curve
			ahap.Append("{\"Event\":{\"Time\":0,\"EventType\":\"HapticContinuous\",\"EventDuration\":").Append(Format(DurationSeconds))
				.Append(",\"EventParameters\":[{\"ParameterID\":\"HapticIntensity\",\"ParameterValue\":1},{\"ParameterID\":\"HapticSharpness\",\"ParameterValue\":0}]}}");

			List<(float time, float value)> intensity = new();
			foreach (StudioAmplitudePoint point in envelopes.amplitude) intensity.Add((point.time, point.amplitude));
			AppendCurves(ahap, "HapticIntensityControl", intensity);

			List<(float time, float value)> sharpness = new();
			foreach (StudioFrequencyPoint point in envelopes.frequency) sharpness.Add((point.time, point.frequency));
			AppendCurves(ahap, "HapticSharpnessControl", sharpness);

			return ahap.Append("]}").ToString();
		}

		private string BuildEmphasisAhap()
		{
			StringBuilder ahap = new("{\"Version\":1.0,\"Pattern\":[");
			bool first = true;
			foreach (StudioAmplitudePoint point in envelopes.amplitude)
			{
				if (point.emphasis == null || point.emphasis.amplitude <= 0) continue;
				if (!first) ahap.Append(',');
				first = false;
				ahap.Append("{\"Event\":{\"Time\":").Append(Format(point.time))
					.Append(",\"EventType\":\"HapticTransient\",\"EventParameters\":[{\"ParameterID\":\"HapticIntensity\",\"ParameterValue\":").Append(Format(point.emphasis.amplitude))
					.Append("},{\"ParameterID\":\"HapticSharpness\",\"ParameterValue\":").Append(Format(point.emphasis.frequency)).Append("}]}}");
			}

			return ahap.Append("]}").ToString();
		}

		// a Core Haptics curve holds at most 16 points, so longer envelopes become consecutive curves sharing their boundary point
		private static void AppendCurves(StringBuilder ahap, string parameterId, List<(float time, float value)> points)
		{
			for (int start = 0; start < points.Count; start += CoreHapticsMaxCurvePoints - 1)
			{
				int end = Mathf.Min(points.Count, start + CoreHapticsMaxCurvePoints);
				float curveTime = points[start].time;
				ahap.Append(",{\"ParameterCurve\":{\"ParameterID\":\"").Append(parameterId).Append("\",\"Time\":").Append(Format(curveTime))
					.Append(",\"ParameterCurveControlPoints\":[");
				for (int i = start; i < end; i++)
				{
					if (i > start) ahap.Append(',');
					ahap.Append("{\"Time\":").Append(Format(points[i].time - curveTime))
						.Append(",\"ParameterValue\":").Append(Format(points[i].value)).Append('}');
				}

				ahap.Append("]}}");
				if (end == points.Count) break;
			}
		}

		private static string Format(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

		[Serializable] private class StudioClip { public StudioSignals signals; }
		[Serializable] private class StudioSignals { public StudioContinuous continuous; }
		[Serializable] private class StudioContinuous { public StudioEnvelopes envelopes; }

		[Serializable]
		private class StudioEnvelopes
		{
			public StudioAmplitudePoint[] amplitude;
			public StudioFrequencyPoint[] frequency;
		}

		[Serializable]
		private class StudioAmplitudePoint
		{
			public float time;
			public float amplitude;
			public StudioEmphasis emphasis;
		}

		[Serializable]
		private class StudioEmphasis
		{
			public float amplitude;
			public float frequency;
		}

		[Serializable]
		private class StudioFrequencyPoint
		{
			public float time;
			public float frequency;
		}
	}
}
