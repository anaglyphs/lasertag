using System;
using System.Threading;
using Anaglyph.Netcode;
using Anaglyph.Updates;
using UnityEngine;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Operator
{
	/// <summary>
	/// Checks GitHub for a newer operator build once per launch and offers it in a pop-up.
	/// Clicking the dimmed backdrop dismisses it. Installing quits the operator, which ends
	/// the session for every connected headset.
	/// </summary>
	public class OperatorUpdatePrompt : IDisposable
	{
		private const string releaseRepository = "anaglyphs/lasertag";
		private const string releaseTagPrefix = "release/";

		private readonly VisualElement backdrop;
		private readonly Label status;
		private readonly Button updateButton;
		private readonly CancellationTokenSource cancellation = new();
		private readonly GitHubReleaseUpdater updater;
		private GitHubReleaseUpdater.Release? available;
		private bool installing;
		private Func<string> describeState = () => "";
		private string buttonCopyKey = "update.button";

		public OperatorUpdatePrompt(VisualElement container)
		{
			backdrop = Require<VisualElement>(container, "update-popup-backdrop");
			status = Require<Label>(backdrop, "update-status");
			updateButton = Require<Button>(backdrop, "update-button");
			updateButton.clicked += Install;
			backdrop.RegisterCallback<PointerDownEvent>(DismissOnBackdropPress);
			backdrop.style.display = DisplayStyle.None;
			MenuCopy.Changed += RefreshCopy;

			string platformName = Application.platform == RuntimePlatform.LinuxPlayer ? "Linux" : "Windows";
			updater = new GitHubReleaseUpdater(releaseRepository, releaseTagPrefix, $"Lasertag-Operator-{platformName}-");
			CheckAsync().RunUntilCancelled();
		}

		public void Dispose()
		{
			cancellation.Cancel();
			cancellation.Dispose();
			updateButton.clicked -= Install;
			backdrop.UnregisterCallback<PointerDownEvent>(DismissOnBackdropPress);
			MenuCopy.Changed -= RefreshCopy;
		}

		private async Awaitable CheckAsync()
		{
			if (!int.TryParse(NetcodeManagement.BuildNumber, out int currentBuildNumber))
				return;

			try
			{
				available = await updater.FindNewerReleaseAsync(currentBuildNumber, cancellation.Token);
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				Debug.LogWarning($"[{nameof(OperatorUpdatePrompt)}] Couldn't check for updates: {exception.Message}");
				return;
			}

			if (available is not { } release)
				return;

			backdrop.style.display = DisplayStyle.Flex;
			if (!GitHubReleaseUpdater.PlatformCanInstall || !GitHubReleaseUpdater.InstallFolderIsWritable())
			{
				buttonCopyKey = "update.open-page";
				ShowState(() => MenuCopy.Format("OperatorMenu", "update.manual", release.buildNumber));
				return;
			}

			ShowState(() => MenuCopy.Format("OperatorMenu", "update.available", release.buildNumber));
		}

		private void Install()
		{
			if (available is not { } release)
				return;

			if (!GitHubReleaseUpdater.PlatformCanInstall || !GitHubReleaseUpdater.InstallFolderIsWritable())
			{
				Application.OpenURL(release.pageUrl);
				return;
			}

			InstallAsync(release).RunUntilCancelled();
		}

		private async Awaitable InstallAsync(GitHubReleaseUpdater.Release release)
		{
			installing = true;
			updateButton.SetEnabled(false);
			try
			{
				await updater.InstallAndRestartAsync(release,
					progress => ShowState(() => MenuCopy.Format("OperatorMenu", "update.downloading",
						release.buildNumber, Mathf.RoundToInt(progress * 100))),
					cancellation.Token);
				ShowState(() => MenuCopy.Format("OperatorMenu", "update.installing", release.buildNumber));
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				Debug.LogException(exception);
				ShowState(() => MenuCopy.Format("OperatorMenu", "update.failed", exception.Message));
				updateButton.SetEnabled(true);
				installing = false;
			}
		}

		private void DismissOnBackdropPress(PointerDownEvent pointerDown)
		{
			if (pointerDown.target == backdrop && !installing)
				backdrop.style.display = DisplayStyle.None;
		}

		private void ShowState(Func<string> describe)
		{
			describeState = describe;
			RefreshCopy();
		}

		private void RefreshCopy()
		{
			status.text = describeState();
			updateButton.text = MenuCopy.Get("OperatorMenu", buttonCopyKey);
		}
	}
}
