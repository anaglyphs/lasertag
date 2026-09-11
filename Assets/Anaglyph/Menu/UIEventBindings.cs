using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Anaglyph.Menu
{
	/// <summary>Owns callbacks for one binding to a visual tree, including anonymous handlers.</summary>
	public sealed class UIEventBindings : IDisposable
	{
		private readonly List<Action> unregister = new();

		public void Click(Button button, Action handler)
		{
			button.clicked += handler;
			unregister.Add(() => button.clicked -= handler);
		}

		public void Value<T>(BaseField<T> field, EventCallback<ChangeEvent<T>> handler)
		{
			field.RegisterValueChangedCallback(handler);
			unregister.Add(() => field.UnregisterValueChangedCallback(handler));
		}

		public void Dispose()
		{
			for (int i = unregister.Count - 1; i >= 0; i--)
				unregister[i]();
			unregister.Clear();
		}
	}
}
