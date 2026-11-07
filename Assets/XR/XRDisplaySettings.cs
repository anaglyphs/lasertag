using UnityEngine;
using UnityEngine.XR.OpenXR.Features.Extensions.PerformanceSettings;
#if UNITY_ANDROID && !UNITY_EDITOR
using Unity.Collections;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR.Features.Meta;
#endif

/// Applies headset display and performance settings once XR is running. Does nothing outside Android player builds.
public class XRDisplaySettings : MonoBehaviour
{
	[Tooltip("Snaps to the nearest refresh rate the headset supports")]
	[SerializeField] private float targetRefreshRate = 90f;

	[Tooltip("0 = off, 1 = maximum. Requires Unity's Foveated Rendering OpenXR feature")]
	[Range(0f, 1f)]
	[SerializeField] private float foveationLevel = 0f;

	[Tooltip("Requires the Performance Settings OpenXR feature")]
	[SerializeField] private PerformanceLevelHint cpuPerformanceLevel = PerformanceLevelHint.SustainedHigh;

	[Tooltip("Requires the Performance Settings OpenXR feature")]
	[SerializeField] private PerformanceLevelHint gpuPerformanceLevel = PerformanceLevelHint.SustainedHigh;

#if UNITY_ANDROID && !UNITY_EDITOR
	private bool applied;

	private void Update()
	{
		if (applied)
			return;

		var display = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRDisplaySubsystem>();
		if (display == null || !display.running)
			return;

		RequestNearestSupportedRefreshRate(display);
		display.foveatedRenderingLevel = foveationLevel;
		RequestPerformanceLevel(PerformanceDomain.Cpu, cpuPerformanceLevel);
		RequestPerformanceLevel(PerformanceDomain.Gpu, gpuPerformanceLevel);
		applied = true;
	}

	private void OnValidate() => applied = false;

	private void OnApplicationPause(bool paused)
	{
		if (!paused)
			applied = false;
	}

	private void RequestNearestSupportedRefreshRate(XRDisplaySubsystem display)
	{
		if (!display.TryGetSupportedDisplayRefreshRates(Allocator.Temp, out var supportedRates))
			return;

		float nearestRate = supportedRates[0];
		foreach (float rate in supportedRates)
			if (Mathf.Abs(rate - targetRefreshRate) < Mathf.Abs(nearestRate - targetRefreshRate))
				nearestRate = rate;
		supportedRates.Dispose();

		if (!display.TryRequestDisplayRefreshRate(nearestRate))
			Debug.LogWarning($"[{nameof(XRDisplaySettings)}] Request for {nearestRate} Hz failed");
	}

	private static void RequestPerformanceLevel(PerformanceDomain domain, PerformanceLevelHint level)
	{
		if (!XrPerformanceSettingsFeature.SetPerformanceLevelHint(domain, level))
			Debug.LogWarning($"[{nameof(XRDisplaySettings)}] Request for {domain} {level} failed");
	}
#endif
}
