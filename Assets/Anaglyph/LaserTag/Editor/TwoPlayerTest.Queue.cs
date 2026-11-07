#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anaglyph.XR;
using UnityEditor;
using UnityEngine;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>
	/// Runs one after another, each in its own environment and with its own seed, Player 2 kept
	/// open between them. Each result is appended to <see cref="QueueLogPath"/> as its run ends, and
	/// the environment goes back to what it was when the queue ends. A queue belongs to the editor
	/// session that started it: one left by a closed editor is dropped, not resumed.
	/// </summary>
	public static partial class TwoPlayerTest
	{
		private const string QueueSessionKey = "Anaglyph.TwoPlayerTest.Queue";
		// Player 2 waits this long after a queued run for the next, then closes: the host may be gone
		private const double QueueGapSeconds = 600;

		[Serializable]
		private class QueuedRuns
		{
			public string id;
			public string restoreEnvironment;
			public List<RunSettings> runs = new();
			public int next;
		}

		private static double nextQueueCheck;

		private static string QueuePath => Path.Combine(LogRoot, "queue.json");

		/// <summary>One line per queued run as it ends: environment | seed | folder | summary.</summary>
		public static string QueueLogPath => Path.Combine(LogRoot, "queue.txt");

		/// <summary>Each seed through every environment, all with <paramref name="each"/>'s other settings.</summary>
		public static string Queue(IEnumerable<string> environments, IEnumerable<int> seeds, RunSettings each) =>
			Queue(seeds.SelectMany(seed => environments.Select(environment =>
			{
				RunSettings settings = each.Copy();
				settings.environment = environment;
				settings.seed = seed;
				return settings;
			})));

		/// <summary>Runs these in order from edit mode in the main editor. Returns why not, or null when queued.</summary>
		public static string Queue(IEnumerable<RunSettings> runs)
		{
			if (!SimPlayer.IsMainEditor) return "queue from the main editor";
			if (IsRunning || IsQueueActive) return "a run or queue is already under way";

			QueuedRuns queue = new() { id = Guid.NewGuid().ToString(), restoreEnvironment = SimEnvironmentSetting.Current };
			foreach (RunSettings settings in runs)
			{
				RunSettings resolved = settings.Copy();
				if (string.IsNullOrEmpty(resolved.environment)) resolved.environment = queue.restoreEnvironment;
				else if (!SimEnvironmentSetting.TryFind(resolved.environment, out resolved.environment, out string problem)) return problem;
				queue.runs.Add(resolved);
			}

			if (queue.runs.Count == 0) return "nothing to queue";
			Directory.CreateDirectory(LogRoot);
			WriteQueue(queue);
			SessionState.SetString(QueueSessionKey, queue.id);
			LogQueue($"queue of {queue.runs.Count} started {DateTime.Now:yyyy-MM-dd HH:mm}");
			return null;
		}

		/// <summary>Where the queue is, for a glance.</summary>
		public static string QueueStatus
		{
			get
			{
				QueuedRuns queue = ReadOwnQueue();
				if (queue == null) return "no queue";
				return queue.next == 0 ? $"{queue.runs.Count} queued, none started" : $"run {queue.next} of {queue.runs.Count}: {Describe(queue.runs[queue.next - 1])}";
			}
		}

		[MenuItem(Menu + "Stop After This Run")]
		private static void StopAfterThisRunMenu()
		{
			QueuedRuns queue = ReadOwnQueue();
			if (queue == null) return;
			queue.runs.RemoveRange(queue.next, queue.runs.Count - queue.next);
			WriteQueue(queue);
		}

		[MenuItem(Menu + "Stop After This Run", true)]
		private static bool CanStopAfterThisRun() => IsQueueActive;

		private static bool IsQueueActive => ReadOwnQueue() != null;

		// Player 2's view: the host means to start another run soon
		private static bool QueueExpectsAnotherRun
		{
			get
			{
				if (!File.Exists(QueuePath) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(QueuePath)).TotalSeconds >= QueueGapSeconds) return false;
				QueuedRuns queue = ReadQueueFile();
				// a file caught as the host writes it still counts
				return queue == null || queue.next < queue.runs.Count;
			}
		}

		private static void TickQueue()
		{
			if (EditorApplication.timeSinceStartup < nextQueueCheck) return;
			nextQueueCheck = EditorApplication.timeSinceStartup + 1;
			if (IsRunning || WaitingForPlayer || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;

			QueuedRuns queue = ReadQueueFile();
			if (queue == null) return;
			if (queue.id != SessionState.GetString(QueueSessionKey, ""))
			{
				File.Delete(QueuePath);
				Report("dropped a queue left by an earlier editor session");
				return;
			}

			if (queue.next >= queue.runs.Count)
			{
				EndQueue("done");
				return;
			}

			RunSettings next = queue.runs[queue.next++];
			WriteQueue(queue);
			string problem = Start(next);
			if (problem != null) LogQueue($"{Describe(next)} | not run: {problem}");
		}

		private static void NoteQueuedRun(Run ended, string summary)
		{
			QueuedRuns queue = ReadOwnQueue();
			if (queue == null) return;
			LogQueue($"{Describe(ended)} | {Path.GetFileName(ended.folder)} | {summary}");
			// Player 2 counts the wait for the next run from here
			WriteQueue(queue);
		}

		private static void EndQueue(string how)
		{
			QueuedRuns queue = ReadOwnQueue();
			if (queue == null) return;
			if (!string.IsNullOrEmpty(queue.restoreEnvironment)) Report(SimEnvironmentSetting.Set(queue.restoreEnvironment));
			File.Delete(QueuePath);
			SessionState.EraseString(QueueSessionKey);
			LogQueue($"queue {how}: {queue.next} of {queue.runs.Count} runs started");
		}

		private static string Describe(RunSettings settings) => $"{Path.GetFileNameWithoutExtension(settings.environment)} | seed {settings.seed}";

		private static QueuedRuns ReadOwnQueue()
		{
			QueuedRuns queue = ReadQueueFile();
			return queue != null && queue.id == SessionState.GetString(QueueSessionKey, "") ? queue : null;
		}

		private static QueuedRuns ReadQueueFile()
		{
			try
			{
				return File.Exists(QueuePath) ? JsonUtility.FromJson<QueuedRuns>(File.ReadAllText(QueuePath)) : null;
			}
			catch (Exception e) when (e is IOException or ArgumentException)
			{
				return null;
			}
		}

		private static void WriteQueue(QueuedRuns queue) => File.WriteAllText(QueuePath, JsonUtility.ToJson(queue, true));

		private static void LogQueue(string line)
		{
			File.AppendAllLines(QueueLogPath, new[] { $"{DateTime.Now:HH:mm:ss} {line}" });
			Debug.Log($"[{nameof(TwoPlayerTest)}] {line}");
		}
	}
}
#endif
