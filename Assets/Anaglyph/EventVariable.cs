using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph
{
	[Serializable]
	public struct EventVariable<T>
	{
		[SerializeField] private T value;
		public event Action<T> OnChange;
		public event Action<T> BeforeChange;

		public EventVariable(T value) {
			this.value = value;
			OnChange = delegate { };
			BeforeChange = delegate { };
		}

		public T Value
		{
			get => value;

			set
			{
				if (EqualityComparer<T>.Default.Equals(this.value, value))
					return;

				BeforeChange?.Invoke(this.value);
				this.value = value;
				OnChange?.Invoke(this.value);
			}
		}

		public void Set(T x)
		{
			Value = x;
		}

		public void AddListenerAndCheck(Action<T> f)
		{
			f.Invoke(value);

			if (OnChange == null)
				OnChange = delegate { };

			OnChange += f;
		}
	}
}
