using Anaglyph.Permissions;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Owns occlusion startup. The AROcclusionManager must be disabled in the prefab;
	/// enabling it starts the native depth provider immediately, before Start runs.
	/// Permission requests remain owned by the permission UI.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(AROcclusionManager))]
	[DefaultExecutionOrder(-10001)]
	public sealed class DepthPermissionController : MonoBehaviour
	{
		private const float PermissionCheckInterval = 0.5f;

		private AROcclusionManager occlusionManager;
		private bool paused;
		private bool focused = true;
		private bool permissionReady;
		private float nextPermissionCheck;

		private void Awake()
		{
			occlusionManager = GetComponent<AROcclusionManager>();
		}

		private void OnEnable()
		{
			// Wait until Update so rig/XR initialization can finish first.
			nextPermissionCheck = 0f;
		}

		private void Update()
		{
			if (paused || !focused || Time.unscaledTime < nextPermissionCheck)
				return;

			nextPermissionCheck = Time.unscaledTime + PermissionCheckInterval;
			bool ready = IsDepthReady();
			if (ready == permissionReady)
				return;

			permissionReady = ready;
			occlusionManager.enabled = ready;
		}

		private static bool IsDepthReady()
		{
			var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
			if (loader == null || loader.GetLoadedSubsystem<XROcclusionSubsystem>() == null)
				return false;

			PermissionAuthorization authorization = AndroidPermissionChecks.CheckPermission(
				MetaPermissionChecks.ScenePermission).authorization;

			// Non-Android providers (including AR Foundation simulation) do not
			// require the Quest scene permission or Meta environment-depth extension.
			if (authorization == PermissionAuthorization.NotRequired)
				return true;

			return authorization == PermissionAuthorization.Granted &&
			       MetaPermissionChecks.CheckEnvironmentDepth() == CapabilitySupport.Supported;
		}

		private void OnApplicationPause(bool pause)
		{
			paused = pause;
			StopOcclusion();
		}

		private void OnApplicationFocus(bool focus)
		{
			focused = focus;
			StopOcclusion();
		}

		private void OnDisable()
		{
			// Clear enabled while the rig is inactive, so reactivation cannot
			// restart the provider before the next permission check.
			StopOcclusion();
		}

		private void StopOcclusion()
		{
			permissionReady = false;
			nextPermissionCheck = 0f;
			if (occlusionManager != null && occlusionManager.enabled)
				occlusionManager.enabled = false;
		}
	}
}
