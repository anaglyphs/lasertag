using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Scripting.APIUpdating;

namespace VariableObjects
{
	[MovedFrom("VariableObjects.GenericScriptableVariable")]
	public abstract class GenericVariableObject<T> : ScriptableObject
    {
		[SerializeField] private T variableValue;

		public event Action<T> onChange = delegate { };
		public event Action<T> beforeChange = delegate { };

		public void Set(T t)
		{
			Value = t;
		}

		public void AddChangeListenerAndCheck(Action<T> f)
		{
			f.Invoke(Value);
			onChange += f;
		}

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
	}

	[MovedFrom("VariableObjects.GenericScriptableVariableEvents")]
	public abstract class GenericVariableObjectEvents<T> : SuperAwakeBehavior
	{
		[SerializeField] private GenericVariableObject<T> scriptableVariable;

		public UnityEvent<T> onChange;
		public UnityEvent<T> beforeChange;

		protected override void SuperAwake()
		{
			scriptableVariable.onChange += onChange.Invoke;
			scriptableVariable.beforeChange += beforeChange.Invoke;
			onChange.Invoke(scriptableVariable.Value);
		}

		public void AddChangeListenerAndCheck(UnityAction<T> f)
		{
			f.Invoke(scriptableVariable.Value);
			onChange.AddListener(f);
		}

		private void OnDestroy()
		{
			scriptableVariable.onChange -= onChange.Invoke;
			scriptableVariable.beforeChange -= beforeChange.Invoke;
		}
	}
}
