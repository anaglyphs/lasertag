using System.Threading;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.XR.ARFoundation;

namespace Anaglyph.DepthKit
{
	[DefaultExecutionOrder(-9999)]
	public class MetaDepthPermissionManager : MonoBehaviour
	{
		private AROcclusionManager occlusionManager;
		// private ARShaderOcclusion shaderOcclusion;

		private const string permStr = "com.oculus.permission.USE_SCENE";

		private CancellationTokenSource ctkn;

		private void Awake()
		{
			TryGetComponent(out occlusionManager);
			// TryGetComponent(out shaderOcclusion);

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

		private async void SetOcclusionEnabled(bool b)
		{
			ctkn?.Cancel();
			ctkn = new CancellationTokenSource();

			occlusionManager.enabled = b;

			// stupid bullshit I need to do for some reason
			if (b)
			{
				await Awaitable.NextFrameAsync();
				await Awaitable.NextFrameAsync();
				if (ctkn.Token.IsCancellationRequested) return;
			}

			// shaderOcclusion.enabled = b;
		}
	}
}
