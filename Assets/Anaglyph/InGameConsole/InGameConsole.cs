using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph
{
	/// <summary>
	/// Captures Unity log messages at runtime so they can be shown in-game.
	/// Capturing is always on and cheap - nothing is formatted or drawn here.
	/// Drawing is up to a listener such as <see cref="InGameConsoleView"/>.
	/// </summary>
	public static class InGameConsole
	{
		public readonly struct Entry
		{
			public readonly string message;
			public readonly string stackTrace;
			public readonly LogType type;

			/// <summary>How many times this message arrived in a row. 1 the first time.</summary>
			public readonly int repeats;

			public Entry(string message, string stackTrace, LogType type, int repeats)
			{
				this.message = message;
				this.stackTrace = stackTrace;
				this.type = type;
				this.repeats = repeats;
			}

			public bool IsError => type is LogType.Error or LogType.Exception or LogType.Assert;

			public Entry Repeated() => new(message, stackTrace, type, repeats + 1);
		}

		public const int MaxEntries = 256;

		private static readonly List<Entry> entries = new(MaxEntries);

		/// <summary>Oldest first. Only valid on the main thread.</summary>
		public static IReadOnlyList<Entry> Entries => entries;

		/// <summary>
		/// Bumped every time the log changes. A hidden view can compare this against
		/// what it last drew to tell whether it needs to redraw at all.
		/// </summary>
		public static int Version { get; private set; }

		/// <summary>Whether stack traces are appended to error entries.</summary>
		public static bool StackTracesEnabled { get; set; } = true;

		/// <summary>
		/// Invoked with the new entry and whether the oldest entry was dropped to make room for it.
		/// </summary>
		public static event Action<Entry, bool> EntryAdded = delegate { };

		/// <summary>Invoked with the newest entry when it repeats instead of a new entry being added.</summary>
		public static event Action<Entry> LastEntryChanged = delegate { };

		public static event Action Cleared = delegate { };

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Initialize()
		{
			// statics survive play sessions when domain reloading is off
			entries.Clear();
			Version = 0;

			Application.logMessageReceived -= OnLogMessageReceived;
			Application.logMessageReceived += OnLogMessageReceived;
		}

		private static void OnLogMessageReceived(string message, string stackTrace, LogType type)
		{
			Version++;

			if (entries.Count > 0)
			{
				Entry last = entries[^1];

				if (last.type == type && last.message == message)
				{
					Entry repeated = last.Repeated();
					entries[^1] = repeated;
					LastEntryChanged.Invoke(repeated);
					return;
				}
			}

			bool droppedOldest = entries.Count == MaxEntries;
			if (droppedOldest)
				entries.RemoveAt(0);

			Entry entry = new(message, stackTrace, type, 1);
			entries.Add(entry);
			EntryAdded.Invoke(entry, droppedOldest);
		}

		public static void Clear()
		{
			entries.Clear();
			Version++;
			Cleared.Invoke();
		}
	}
}
