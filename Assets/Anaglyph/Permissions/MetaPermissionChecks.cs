using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.XR;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace Anaglyph.Permissions
{
	/// <summary>
	/// Read-only permission and capability checks for Meta Quest.
	/// These methods do not request permissions, display UI, or start XR subsystems.
	/// Call them from the Unity main thread after XR initialization.
	/// </summary>
	public static class MetaPermissionChecks
	{
		public const string ScenePermission = "com.oculus.permission.USE_SCENE";
		public const string AndroidCameraPermission = "android.permission.CAMERA";
		public const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";

		public const string SceneExtension = "XR_FB_scene";
		public const string EnvironmentDepthExtension = "XR_META_environment_depth";
		public const string PassthroughExtension = "XR_FB_passthrough";
		public const string SharedSpatialAnchorsExtension = "XR_META_spatial_entity_group_sharing";

		// XR_ERROR_SPACE_CLOUD_STORAGE_DISABLED_FB
		public const int VpsDisabledNativeStatusCode = -1000169004;

		private const string AndroidCameraSystemFeature = "android.hardware.camera";

		private static readonly List<XRAnchorSubsystem> anchorSubsystems = new();
		private static VpsStatus observedVpsStatus;

		public static PermissionCheckResult CheckScene()
		{
			CapabilitySupport support = CheckAnyOpenXRExtension(
				SceneExtension,
				EnvironmentDepthExtension);

			return new PermissionCheckResult(
				GameCapability.Scene,
				support,
				CheckAndroidPermission(ScenePermission));
		}

		public static PassthroughCameraCheckResult CheckPassthroughCamera()
		{
			CapabilitySupport openXRSupport = CheckAnyOpenXRExtension(PassthroughExtension);
			CapabilitySupport cameraHardwareSupport = CheckAndroidSystemFeature(AndroidCameraSystemFeature);

			CapabilitySupport support = CombineRequiredSupport(openXRSupport, cameraHardwareSupport);

			return new PassthroughCameraCheckResult(
				support,
				CheckPassthroughCameraConfiguration(),
				CheckAndroidPermission(AndroidCameraPermission),
				CheckAndroidPermission(HeadsetCameraPermission));
		}

		public static SharedSpatialAnchorsCheckResult CheckSharedSpatialAnchors()
		{
			return new SharedSpatialAnchorsCheckResult(
				CheckSharedSpatialAnchorsSupport(),
				observedVpsStatus);
		}

		public static VpsStatus CheckVps() => observedVpsStatus;

		/// <summary>
		/// Updates the observable VPS state from a Meta shared-anchor load or share result.
		/// Pass only results from shared-anchor operations. Local anchor operations do not
		/// test Enhanced Spatial Services.
		/// </summary>
		public static VpsStatus ObserveSharedAnchorOperation(XRResultStatus result)
		{
			if (result.nativeStatusCode == VpsDisabledNativeStatusCode)
				observedVpsStatus = VpsStatus.Disabled;
			else if (result.IsSuccess())
				observedVpsStatus = VpsStatus.Enabled;

			return observedVpsStatus;
		}

		public static void ResetVpsObservation()
		{
			observedVpsStatus = VpsStatus.Unknown;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			anchorSubsystems.Clear();
			ResetVpsObservation();
		}

		private static CapabilitySupport CheckSharedSpatialAnchorsSupport()
		{
			if (!IsOpenXRInitialized())
				return CapabilitySupport.Unknown;

			anchorSubsystems.Clear();
			SubsystemManager.GetSubsystems(anchorSubsystems);

			foreach (XRAnchorSubsystem subsystem in anchorSubsystems)
			{
				if (subsystem is not MetaOpenXRAnchorSubsystem metaSubsystem)
					continue;

				return metaSubsystem.isSharedAnchorsSupported switch
				{
					Supported.Supported => CapabilitySupport.Supported,
					Supported.Unsupported => CapabilitySupport.Unsupported,
					_ => CapabilitySupport.Unknown
				};
			}

			// XR initialized but the Meta anchor subsystem was not created. This
			// usually means its OpenXR feature is disabled or unsupported.
			return CapabilitySupport.Unsupported;
		}

		private static CapabilitySupport CheckAnyOpenXRExtension(params string[] extensionNames)
		{
			if (!IsOpenXRInitialized())
				return CapabilitySupport.Unknown;

			foreach (string extensionName in extensionNames)
				if (OpenXRRuntime.IsExtensionEnabled(extensionName))
					return CapabilitySupport.Supported;

			return CapabilitySupport.Unsupported;
		}

		private static CapabilityConfiguration CheckPassthroughCameraConfiguration()
		{
			ARCameraFeature cameraFeature =
				OpenXRSettings.Instance?.GetFeature<ARCameraFeature>();

			if (cameraFeature == null || !cameraFeature.enabled)
				return CapabilityConfiguration.Disabled;

			return cameraFeature.cameraImageSupportEnabled
				? CapabilityConfiguration.Enabled
				: CapabilityConfiguration.Disabled;
		}

		private static bool IsOpenXRInitialized()
		{
			XRManagerSettings manager = XRGeneralSettings.Instance?.Manager;

			return manager != null &&
			       manager.isInitializationComplete &&
			       manager.activeLoader is OpenXRLoader;
		}

		private static CapabilitySupport CombineRequiredSupport(
			CapabilitySupport first,
			CapabilitySupport second)
		{
			if (first == CapabilitySupport.Unsupported || second == CapabilitySupport.Unsupported)
				return CapabilitySupport.Unsupported;

			if (first == CapabilitySupport.Supported && second == CapabilitySupport.Supported)
				return CapabilitySupport.Supported;

			return CapabilitySupport.Unknown;
		}

		private static PermissionAuthorization CheckAndroidPermission(string permission)
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			return Permission.HasUserAuthorizedPermission(permission)
				? PermissionAuthorization.Granted
				: PermissionAuthorization.Denied;
#else
			return PermissionAuthorization.NotRequired;
#endif
		}

		private static CapabilitySupport CheckAndroidSystemFeature(string feature)
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			try
			{
				using AndroidJavaClass unityPlayer =
					new("com.unity3d.player.UnityPlayer");
				using AndroidJavaObject activity =
					unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
				using AndroidJavaObject packageManager =
					activity.Call<AndroidJavaObject>("getPackageManager");

				return packageManager.Call<bool>("hasSystemFeature", feature)
					? CapabilitySupport.Supported
					: CapabilitySupport.Unsupported;
			}
			catch
			{
				return CapabilitySupport.Unknown;
			}
#else
			return CapabilitySupport.Unknown;
#endif
		}

		/*
		// Future body-tracking check. Add "Oculus.VR" to this assembly's
		// references when enabling this method.
		public static PermissionCheckResult CheckBodyTracking()
		{
			CapabilitySupport support = CheckAnyOpenXRExtension("XR_FB_body_tracking");

			if (support == CapabilitySupport.Supported && !OVRPlugin.bodyTrackingSupported)
				support = CapabilitySupport.Unsupported;

			return new PermissionCheckResult(
				GameCapability.BodyTracking,
				support,
				CheckAndroidPermission("com.oculus.permission.BODY_TRACKING"));
		}
		*/
	}
}
