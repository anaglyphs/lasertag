using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace Anaglyph.Updates
{
	/// <summary>
	/// Updates a desktop player from zipped builds attached to GitHub releases tagged
	/// "{tagPrefix}{build number}". The zip holds the player folder's contents. Files are
	/// swapped by a script that waits for this process to quit, then relaunches the player
	/// whether or not the swap succeeded.
	/// </summary>
	public class GitHubReleaseUpdater
	{
		public readonly struct Release
		{
			public readonly int buildNumber;
			public readonly string pageUrl;
			public readonly string zipUrl;

			public Release(int buildNumber, string pageUrl, string zipUrl)
			{
				this.buildNumber = buildNumber;
				this.pageUrl = pageUrl;
				this.zipUrl = zipUrl;
			}
		}

		private readonly string repository;
		private readonly string tagPrefix;
		private readonly string zipNamePrefix;

		public GitHubReleaseUpdater(string repository, string tagPrefix, string zipNamePrefix)
		{
			this.repository = repository;
			this.tagPrefix = tagPrefix;
			this.zipNamePrefix = zipNamePrefix;
		}

		public static bool PlatformCanInstall =>
			!Application.isEditor && Application.platform is RuntimePlatform.WindowsPlayer or RuntimePlatform.LinuxPlayer;

		public static string InstallFolder => Path.GetDirectoryName(Application.dataPath);

		public static bool InstallFolderIsWritable()
		{
			string probe = Path.Combine(InstallFolder, ".update-write-probe");
			try
			{
				File.WriteAllText(probe, "");
				File.Delete(probe);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>The highest-numbered release with a zip for this platform, if newer than currentBuildNumber.</summary>
		public async Awaitable<Release?> FindNewerReleaseAsync(int currentBuildNumber, CancellationToken cancellationToken)
		{
			using UnityWebRequest request = UnityWebRequest.Get($"https://api.github.com/repos/{repository}/releases?per_page=50");
			request.SetRequestHeader("Accept", "application/vnd.github+json");
			request.SetRequestHeader("User-Agent", "Anaglyph-Updater");
			await SendAsync(request, cancellationToken);

			ReleaseList list = JsonUtility.FromJson<ReleaseList>($"{{\"items\":{request.downloadHandler.text}}}");
			Release? newest = null;
			foreach (ReleaseJson release in list.items)
			{
				if (release.draft || !release.tag_name.StartsWith(tagPrefix, StringComparison.Ordinal) ||
				    !int.TryParse(release.tag_name.Substring(tagPrefix.Length), out int buildNumber) ||
				    buildNumber <= (newest?.buildNumber ?? currentBuildNumber))
					continue;

				foreach (AssetJson asset in release.assets)
					if (asset.name.StartsWith(zipNamePrefix, StringComparison.Ordinal) &&
					    asset.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
						newest = new Release(buildNumber, release.html_url, asset.browser_download_url);
			}

			return newest;
		}

		/// <summary>Downloads and unpacks the release, starts the swap script, and quits.</summary>
		public async Awaitable InstallAndRestartAsync(Release release, Action<float> downloadProgress,
			CancellationToken cancellationToken)
		{
			if (!PlatformCanInstall)
				throw new PlatformNotSupportedException("Only Windows and Linux players can install updates.");

			string updateFolder = Path.Combine(Application.temporaryCachePath, "update");
			string zipPath = Path.Combine(updateFolder, $"{release.buildNumber}.zip");
			string stagingFolder = Path.Combine(updateFolder, $"{release.buildNumber}");
			if (Directory.Exists(updateFolder))
				Directory.Delete(updateFolder, true);
			Directory.CreateDirectory(stagingFolder);

			using (UnityWebRequest request = new(release.zipUrl, UnityWebRequest.kHttpVerbGET))
			{
				request.downloadHandler = new DownloadHandlerFile(zipPath) { removeFileOnAbort = true };
				request.SetRequestHeader("User-Agent", "Anaglyph-Updater");
				Awaitable sending = SendAsync(request, cancellationToken);
				while (!request.isDone)
				{
					downloadProgress?.Invoke(request.downloadProgress);
					await Awaitable.NextFrameAsync(cancellationToken);
				}
				await sending;
			}

			await Awaitable.BackgroundThreadAsync();
			ExtractInto(zipPath, stagingFolder);
			await Awaitable.MainThreadAsync();
			cancellationToken.ThrowIfCancellationRequested();

			string executable = Process.GetCurrentProcess().MainModule.FileName;
			string stagedExecutable = Path.Combine(stagingFolder, Path.GetFileName(executable));
			if (!File.Exists(stagedExecutable))
				throw new InvalidDataException($"The update has no {Path.GetFileName(executable)}.");

			StartSwapScript(updateFolder, stagingFolder, InstallFolder, executable);
			Application.Quit();
		}

		private static async Awaitable SendAsync(UnityWebRequest request, CancellationToken cancellationToken)
		{
			using CancellationTokenRegistration abortOnCancel = cancellationToken.Register(request.Abort);
			await request.SendWebRequest();
			cancellationToken.ThrowIfCancellationRequested();
			if (request.result != UnityWebRequest.Result.Success)
				throw new IOException($"{request.url}: {request.error}");
		}

		private static void ExtractInto(string zipPath, string folder)
		{
			string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
			using ZipArchive archive = ZipFile.OpenRead(zipPath);
			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				string destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
				if (!destination.StartsWith(root, StringComparison.Ordinal))
					throw new InvalidDataException($"The update has an entry outside its folder: {entry.FullName}");

				if (entry.FullName.EndsWith("/"))
				{
					Directory.CreateDirectory(destination);
					continue;
				}

				Directory.CreateDirectory(Path.GetDirectoryName(destination));
				using Stream source = entry.Open();
				using FileStream target = File.Create(destination);
				source.CopyTo(target);
			}
		}

		private static void StartSwapScript(string updateFolder, string stagingFolder, string installFolder, string executable)
		{
			int processId = Process.GetCurrentProcess().Id;
			string log = Path.Combine(Path.GetTempPath(), "anaglyph-update.log");
			ProcessStartInfo start;

			if (Application.platform == RuntimePlatform.WindowsPlayer)
			{
				string script = Path.Combine(Path.GetTempPath(), "anaglyph-update.ps1");
				File.WriteAllText(script, $@"
Start-Transcript -LiteralPath {PowerShellQuote(log)}
Wait-Process -Id {processId} -ErrorAction SilentlyContinue
robocopy {PowerShellQuote(stagingFolder)} {PowerShellQuote(installFolder)} /E /R:10 /W:1 /NP
if ($LASTEXITCODE -lt 8) {{ Remove-Item -LiteralPath {PowerShellQuote(updateFolder)} -Recurse -Force }}
Start-Process -FilePath {PowerShellQuote(executable)} -WorkingDirectory {PowerShellQuote(installFolder)}
", new UTF8Encoding(true));
				start = new ProcessStartInfo("powershell.exe",
					$"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"");
			}
			else
			{
				string script = Path.Combine(Path.GetTempPath(), "anaglyph-update.sh");
				File.WriteAllText(script, $@"exec >{ShellQuote(log)} 2>&1
while kill -0 {processId} 2>/dev/null; do sleep 0.2; done
cp -Rf {ShellQuote(stagingFolder + "/.")} {ShellQuote(installFolder + "/")} && rm -rf {ShellQuote(updateFolder)}
chmod +x {ShellQuote(executable)}
cd {ShellQuote(installFolder)} && nohup {ShellQuote(executable)} >/dev/null 2>&1 &
");
				start = new ProcessStartInfo("/bin/sh", $"\"{script}\"");
			}

			start.UseShellExecute = false;
			start.CreateNoWindow = true;
			Process.Start(start);
			Debug.Log($"[{nameof(GitHubReleaseUpdater)}] Installing from {stagingFolder}; log at {log}");
		}

		private static string PowerShellQuote(string text) => $"'{text.Replace("'", "''")}'";

		private static string ShellQuote(string text) => $"'{text.Replace("'", "'\\''")}'";

		[Serializable] private class ReleaseList { public ReleaseJson[] items; }

		[Serializable]
		private class ReleaseJson
		{
			public string tag_name;
			public string html_url;
			public bool draft;
			public AssetJson[] assets;
		}

		[Serializable]
		private class AssetJson
		{
			public string name;
			public string browser_download_url;
		}
	}
}
