using UnityEngine;
using UnityEngine.Android;
using UnityEngine.XR.ARFoundation;

namespace Anaglyph.DepthKit
{
	[DefaultExecutionOrder(-9999)]
	public class MetaDepthPermissionManager : MonoBehaviour
	{
		private AROcclusionManager occlusionManager;

		private const string permStr = "com.oculus.permission.USE_SCENE";

		private void Awake()
		{
			TryGetComponent(out occlusionManager);

			SetOcclusionEnabled(false);
		}

		private void OnEnable()
		{
			RefreshPermission();
		}

		private void OnApplicationFocus(bool hasFocus)
		{
			if (hasFocus)
				RefreshPermission();
		}

		private void OnApplicationPause(bool isPaused)
		{
			if (!isPaused)
				RefreshPermission();
		}

		private void RefreshPermission()
		{
			// Permission requests are owned by the menu gate.
			SetOcclusionEnabled(Permission.HasUserAuthorizedPermission(permStr));
		}

		private void SetOcclusionEnabled(bool b)
		{
			occlusionManager.enabled = b;
		}
	}
}
