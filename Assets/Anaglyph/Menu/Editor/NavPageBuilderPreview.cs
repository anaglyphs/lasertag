using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Menu.Editor
{
	[InitializeOnLoad]
	internal static class NavPageBuilderPreview
	{
		private static readonly Dictionary<EditorWindow, NavPageBuilderSelectionProxy> previews = new();
		private static readonly Type builderType = Type.GetType("Unity.UI.Builder.Builder, UnityEditor.UIBuilderModule");
		private static FieldInfo windowReady;

		static NavPageBuilderPreview()
		{
			EditorApplication.delayCall += Initialize;
			AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
		}

		private static void Initialize()
		{
			try
			{
				windowReady = builderType.GetField("onActiveBuilderWindowReady");
				windowReady.SetValue(null, (Action<EditorWindow>)windowReady.GetValue(null) + Attach);
				foreach (EditorWindow window in Resources.FindObjectsOfTypeAll(builderType))
					Attach(window);
			}
			catch (Exception error)
			{
				Debug.LogWarning($"NavPage UI Builder preview is unavailable: {error.GetBaseException().Message}");
			}
		}

		private static void Attach(EditorWindow window)
		{
			Detach(window);
			try
			{
				Type notifierType = builderType.Assembly.GetType("Unity.UI.Builder.IBuilderSelectionNotifier", true);
				object proxy = typeof(DispatchProxy).GetMethod(nameof(DispatchProxy.Create))
					.MakeGenericMethod(notifierType, typeof(NavPageBuilderSelectionProxy)).Invoke(null, null);
				var preview = (NavPageBuilderSelectionProxy)proxy;
				previews.Add(window, preview);
				preview.Attach(window, builderType.GetProperty("selection").GetValue(window), () => Detach(window));
			}
			catch (Exception error)
			{
				Detach(window);
				Debug.LogWarning($"NavPage UI Builder preview is unavailable: {error.GetBaseException().Message}");
			}
		}

		private static void Detach(EditorWindow window)
		{
			if (!previews.Remove(window, out var preview))
				return;
			preview.Dispose();
		}

		private static void Shutdown()
		{
			if (windowReady != null)
				windowReady.SetValue(null, (Action<EditorWindow>)windowReady.GetValue(null) - Attach);
			foreach (var preview in previews.Values)
				preview.Dispose();
			previews.Clear();
		}
	}

	public class NavPageBuilderSelectionProxy : DispatchProxy, IDisposable
	{
		private readonly Dictionary<NavPage, (StyleEnum<DisplayStyle> original, DisplayStyle applied)> overrides = new();
		private object selection;
		private PropertyInfo selectedElements;
		private MethodInfo removeNotifier;
		private VisualElement windowRoot;
		private ToolbarToggle previewToggle;
		private Action detached;
		private bool refreshQueued;

		internal void Attach(EditorWindow window, object builderSelection, Action onDetached)
		{
			selection = builderSelection;
			selectedElements = selection.GetType().GetProperty("selection");
			removeNotifier = selection.GetType().GetMethod("RemoveNotifier");
			windowRoot = window.rootVisualElement;
			detached = onDetached;
			previewToggle = windowRoot.Q<ToolbarToggle>("preview-button");
			if (selectedElements == null || removeNotifier == null || previewToggle == null)
				throw new MissingMemberException("UI Builder selection or Preview control changed.");
			selection.GetType().GetMethod("AddNotifier").Invoke(selection, new object[] { this });
			windowRoot.RegisterCallback<DetachFromPanelEvent>(OnDetached);
			previewToggle.RegisterValueChangedCallback(OnPreviewChanged);
			QueueRefresh();
		}

		protected override object Invoke(MethodInfo method, object[] args)
		{
			switch (method.Name)
			{
				case "BeforeSelectionChanged":
					Restore();
					break;
				case "SelectionChanged":
				case "HierarchyChanged":
				case "StylingChanged":
				case "PreSaveDocument":
					Restore();
					QueueRefresh();
					break;
			}
			return null;
		}

		private void OnPreviewChanged(ChangeEvent<bool> evt)
		{
			Restore();
			QueueRefresh();
		}

		private void OnDetached(DetachFromPanelEvent evt)
		{
			if (evt.target == windowRoot)
				detached();
		}

		private void QueueRefresh()
		{
			if (refreshQueued || selection == null)
				return;
			refreshQueued = true;
			EditorApplication.delayCall += Refresh;
		}

		private void Refresh()
		{
			refreshQueued = false;
			Restore();
			if (selection == null || windowRoot.panel == null || previewToggle.value)
				return;

			foreach (var element in (IEnumerable<VisualElement>)selectedElements.GetValue(selection))
			{
				NavPage page = element as NavPage ?? element.GetFirstAncestorOfType<NavPage>();
				if (page == null)
					continue;

				for (VisualElement ancestor = page; ancestor != null; ancestor = ancestor.parent)
				{
					if (ancestor is not NavPage selectedPage || selectedPage.parent is not NavView view)
						continue;
					foreach (VisualElement child in view.Children())
					{
						if (child is not NavPage sibling)
							continue;
						DisplayStyle display = sibling == selectedPage ? DisplayStyle.Flex : DisplayStyle.None;
						overrides.Add(sibling, (sibling.style.display, display));
						sibling.style.display = display;
					}
				}
				break;
			}
		}

		private void Restore()
		{
			foreach (var entry in overrides)
				if (entry.Key.style.display == new StyleEnum<DisplayStyle>(entry.Value.applied))
					entry.Key.style.display = entry.Value.original;
			overrides.Clear();
		}

		public void Dispose()
		{
			EditorApplication.delayCall -= Refresh;
			Restore();
			windowRoot?.UnregisterCallback<DetachFromPanelEvent>(OnDetached);
			previewToggle?.UnregisterValueChangedCallback(OnPreviewChanged);
			if (selection != null && removeNotifier != null)
				removeNotifier.Invoke(selection, new object[] { this });
			selection = null;
		}
	}
}
