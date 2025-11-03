using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;

namespace VariableObjects
{
	[MovedFrom("VariableObjects.GenericScriptableVariable")]
	public abstract class GenericVariableObject<T> : ScriptableObject
	{
		[FormerlySerializedAs("variableValue")]
		[SerializeField] protected T defaultVal;
		protected T val;

		public event Action<T> onChange = delegate { };
		public event Action<T> beforeChange = delegate { };

		protected void OnValidate()
		{
			val = defaultVal;
		}

		protected virtual void OnEnable()
		{
			val = defaultVal;
		}

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
			get => val;

			set
			{
				if (EqualityComparer<T>.Default.Equals(val, value))
					return;

				beforeChange.Invoke(val);
				val = value;
				onChange.Invoke(val);
			}
		}
	}


	[MovedFrom("VariableObjects.GenericScriptableVariableEvents")]
	public abstract class GenericVariableObjectEvents<T> : MonoBehaviour
	{
		[SerializeField] private GenericVariableObject<T> scriptableVariable;

		public UnityEvent<T> onChange;
		public UnityEvent<T> beforeChange;

		private void Awake()
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
