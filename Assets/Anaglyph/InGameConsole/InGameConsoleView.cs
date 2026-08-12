using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.InGameConsole
{
	/// <summary>
	/// Draws <see cref="InGameConsole"/> into a UI Toolkit hierarchy containing a
	/// ScrollView named "console-scroll", a Button named "console-clear-button" and a
	/// Toggle named "console-stack-traces-toggle".
	/// <para>
	/// The view only listens to the log while it is visible - see <see cref="SetVisible"/>.
	/// Messages logged while it is hidden are drawn the next time it is shown.
	/// </para>
	/// </summary>
	public sealed class InGameConsoleView : IDisposable
	{
		public const string EntryClass = "console-entry";
		public const string WarningClass = "console-entry-warning";
		public const string ErrorClass = "console-entry-error";

		private readonly ScrollView scrollView;
		private readonly Button clearButton;
		private readonly Toggle stackTracesToggle;
		private readonly List<Label> labels = new(InGameConsole.MaxEntries);

		private bool isVisible;
		private bool scrollToBottomQueued;
		private int drawnVersion = -1;

		public InGameConsoleView(VisualElement root)
		{
			if (root == null)
				throw new ArgumentNullException(nameof(root));

			scrollView = Require<ScrollView>(root, "console-scroll");
			clearButton = Require<Button>(root, "console-clear-button");
			stackTracesToggle = Require<Toggle>(root, "console-stack-traces-toggle");

			clearButton.clicked += InGameConsole.Clear;

			stackTracesToggle.SetValueWithoutNotify(InGameConsole.StackTracesEnabled);
			stackTracesToggle.RegisterValueChangedCallback(OnStackTracesToggled);
		}

		/// <summary>
		/// Subscribes to the log and redraws whatever was missed while hidden.
		/// Nothing is drawn or allocated while invisible.
		/// </summary>
		public void SetVisible(bool visible)
		{
			if (isVisible == visible)
				return;

			isVisible = visible;

			if (visible)
			{
				InGameConsole.EntryAdded += OnEntryAdded;
				InGameConsole.LastEntryChanged += OnLastEntryChanged;
				InGameConsole.Cleared += OnCleared;

				if (drawnVersion != InGameConsole.Version)
					Rebuild();

				QueueScrollToBottom();
			}
			else
			{
				InGameConsole.EntryAdded -= OnEntryAdded;
				InGameConsole.LastEntryChanged -= OnLastEntryChanged;
				InGameConsole.Cleared -= OnCleared;
			}
		}

		public void Dispose()
		{
			SetVisible(false);

			clearButton.clicked -= InGameConsole.Clear;
			stackTracesToggle.UnregisterValueChangedCallback(OnStackTracesToggled);
		}

		private void OnEntryAdded(InGameConsole.Entry entry, bool droppedOldest)
		{
			bool wasAtBottom = IsScrolledToBottom();

			Label label;

			if (droppedOldest && labels.Count > 0)
			{
				// the oldest label becomes the newest instead of allocating another one
				label = labels[0];
				labels.RemoveAt(0);
				label.BringToFront();
			}
			else
			{
				label = AddLabel();
			}

			labels.Add(label);
			ApplyEntry(label, entry);
			drawnVersion = InGameConsole.Version;

			if (wasAtBottom)
				QueueScrollToBottom();
		}

		private void OnLastEntryChanged(InGameConsole.Entry entry)
		{
			if (labels.Count == 0)
			{
				Rebuild();
				return;
			}

			ApplyEntry(labels[^1], entry);
			drawnVersion = InGameConsole.Version;
		}

		private void OnCleared()
		{
			foreach (Label label in labels)
				label.RemoveFromHierarchy();

			labels.Clear();
			drawnVersion = InGameConsole.Version;
		}

		private void OnStackTracesToggled(ChangeEvent<bool> change)
		{
			InGameConsole.StackTracesEnabled = change.newValue;
			Rebuild();
			QueueScrollToBottom();
		}

		private void Rebuild()
		{
			IReadOnlyList<InGameConsole.Entry> entries = InGameConsole.Entries;

			while (labels.Count > entries.Count)
			{
				int last = labels.Count - 1;
				labels[last].RemoveFromHierarchy();
				labels.RemoveAt(last);
			}

			while (labels.Count < entries.Count)
				labels.Add(AddLabel());

			for (int i = 0; i < entries.Count; i++)
				ApplyEntry(labels[i], entries[i]);

			drawnVersion = InGameConsole.Version;
		}

		private Label AddLabel()
		{
			Label label = new()
			{
				// log messages are not markup, and tags in them shouldn't be parsed
				enableRichText = false,
				pickingMode = PickingMode.Ignore,
			};

			label.AddToClassList(EntryClass);
			scrollView.Add(label);
			return label;
		}

		private static void ApplyEntry(Label label, in InGameConsole.Entry entry)
		{
			label.text = FormatEntry(entry);
			label.EnableInClassList(WarningClass, entry.type == LogType.Warning);
			label.EnableInClassList(ErrorClass, entry.IsError);
		}

		private static string FormatEntry(in InGameConsole.Entry entry)
		{
			string text = entry.repeats > 1 ? $"({entry.repeats}) {entry.message}" : entry.message;

			bool showTrace = InGameConsole.StackTracesEnabled && entry.IsError &&
				!string.IsNullOrWhiteSpace(entry.stackTrace);

			return showTrace ? $"{text}\n{entry.stackTrace.TrimEnd()}" : text;
		}

		private bool IsScrolledToBottom()
		{
			Scroller scroller = scrollView.verticalScroller;
			return scroller.value >= scroller.highValue - 1f;
		}

		private void QueueScrollToBottom()
		{
			if (scrollToBottomQueued)
				return;

			// the scroller only knows how far it can go once the new text has been laid out
			scrollToBottomQueued = true;
			scrollView.schedule.Execute(ScrollToBottom);
		}

		private void ScrollToBottom()
		{
			scrollToBottomQueued = false;
			Scroller scroller = scrollView.verticalScroller;
			scroller.value = scroller.highValue;
		}

		private static T Require<T>(VisualElement root, string name) where T : VisualElement
		{
			T element = root.Q<T>(name);
			if (element == null)
				throw new InvalidOperationException(
					$"Required UI Toolkit element '{name}' ({typeof(T).Name}) was not found.");

			return element;
		}
	}
}
