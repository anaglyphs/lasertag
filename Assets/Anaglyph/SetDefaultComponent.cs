using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph
{
    public static partial class MonoBehaviourExtensions
    {
		/// <summary>
		/// Meant to be called in OnValidate()
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="component"></param>
		/// <param name="comp"></param>
		/// <exception cref="MissingComponentException"></exception>

		public static void SetComponent<T>(this Component behaviour, ref T comp) where T : Component
        {
            if (comp == null)
            {
                comp = behaviour.GetComponentInParent<T>(true);
            }
        }

		public static void SetComponetFromParent<T>(this Component behaviour, ref T comp) where T : Component
		{
			if (comp == null)
			{
				comp = behaviour.GetComponentInParent<T>(true);
			}
		}

		public static void SetComponentFromChild<T>(this Component behaviour, ref T comp) where T : Component
		{
			if (comp == null)
			{
				comp = behaviour.GetComponentInChildren<T>(true);
			}
		}

		public static void AddPersistentListenerOnce<T>(this UnityEvent<T> unityEvent, UnityAction<T> unityAction)
		{
#if UNITY_EDITOR
			if (unityEvent == null)
				return;

			for (int index = 0; index < unityEvent.GetPersistentEventCount(); index++)
			{
				Object curEventObj = unityEvent.GetPersistentTarget(index);
				if ((Object)unityAction.Target == curEventObj)
				{
					return;
				}
			}

			UnityEventTools.AddPersistentListener(unityEvent, unityAction);
#endif
		}
	}
}
