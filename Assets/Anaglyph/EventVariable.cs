using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph
{
	[Serializable]
	public class EventVariable<T>
	{
		[SerializeField] private T variableValue;
		[SerializeField] private UnityEvent<T> onChange = new();
		[SerializeField] private UnityEvent<T> beforeChange = new();

		public T Value
		{
			get => variableValue;

			set
			{
				if (EqualityComparer<T>.Default.Equals(variableValue, value))
					return;

				beforeChange.Invoke(variableValue);
				variableValue = value;
				onChange.Invoke(variableValue);
			}
		}

		public void Set(T x)
		{
			Value = x;
		}

		public void AddListener(UnityAction<T> f)
		{
			onChange.AddListener(f);
		}

		public void AddListenerAndCheck(UnityAction<T> f)
		{
			f.Invoke(variableValue);
			AddListener(f);
		}

		public void RemoveListener(UnityAction<T> f)
		{
			onChange.RemoveListener(f);
		}
	}
}
