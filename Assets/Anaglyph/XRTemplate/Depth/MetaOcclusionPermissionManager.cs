using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.XR.ARFoundation;

namespace Anaglyph.DepthKit
{
	[DefaultExecutionOrder(-9999)]
	public class MetaOcclusionPermissionManager : MonoBehaviour
	{
		private AROcclusionManager occlusionManager;
		private ARShaderOcclusion shaderOcclusion;

		private const string permStr = "com.oculus.permission.USE_SCENE";

		private CancellationTokenSource ctkn;

		private void Awake()
		{
			TryGetComponent(out occlusionManager);
			TryGetComponent(out shaderOcclusion);

			SetOcclusionEnabled(false);
		}

		private void OnApplicationPause(bool isPaused)
		{
			if (isPaused)
				return;

			bool hasPerm = Permission.HasUserAuthorizedPermission(permStr);
			SetOcclusionEnabled(hasPerm);

			if (!hasPerm)
				RequestPermission();
		}

		private void RequestPermission()
		{
			PermissionCallbacks callbacks = new();
			callbacks.PermissionGranted += _ => SetOcclusionEnabled(true);
			Permission.RequestUserPermission(permStr, callbacks);
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

			shaderOcclusion.enabled = b;
		}
	}
}