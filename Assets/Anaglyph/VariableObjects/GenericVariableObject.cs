using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;

namespace Anaglyph.VariableObjects
{
	[MovedFrom("VariableObjects.GenericScriptableVariable")]
	public abstract class GenericVariableObject<T> : ScriptableObject
	{
		[FormerlySerializedAs("variableValue")]
		[SerializeField] protected T defaultVal;
		protected T val;

		public event Action<T> Changed = delegate { };
		// public event Action<T> beforeChange = delegate { };

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

		public void SetDefaultVal(T t)
		{
			defaultVal = t;
			val = t;
		}

		public void AddChangeListenerAndCheck(Action<T> f)
		{
			f.Invoke(Value);
			Changed += f;
		}

		public T Value
		{
			get => val;

			set
			{
				if (EqualityComparer<T>.Default.Equals(val, value))
					return;

				// beforeChange.Invoke(val);
				val = value;
				Changed.Invoke(val);
			}
		}
	}


	[MovedFrom("VariableObjects.GenericScriptableVariableEvents")]
	public abstract class GenericVariableObjectEvents<T> : MonoBehaviour
	{
		[FormerlySerializedAs("scriptableVariable")] [SerializeField] private GenericVariableObject<T> variableObject;

		[FormerlySerializedAs("onChange")] [SerializeField] private UnityEvent<T> OnChange;
		[FormerlySerializedAs("beforeChange")] [SerializeField] private UnityEvent<T> OnBeforeChange;

		private void Awake()
		{
			variableObject.Changed += OnChange.Invoke;
			// variableObject.beforeChange += beforeChange.Invoke;
			OnChange.Invoke(variableObject.Value);
		}

		public void AddChangeListenerAndCheck(UnityAction<T> f)
		{
			f.Invoke(variableObject.Value);
			OnChange.AddListener(f);
		}

		private void OnDestroy()
		{
			variableObject.Changed -= OnChange.Invoke;
			// variableObject.beforeChange -= beforeChange.Invoke;
		}
	}
}
