using UnityEngine;

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

		public static void SetDefaultComponent<T>(this Component behaviour, ref T comp) where T : Component
        {
            if (comp == null)
            {
                comp = behaviour.GetComponentInParent<T>(true);
            }
        }

		public static void SetDefaultComponentFromParent<T>(this Component behaviour, ref T comp) where T : Component
		{
			if (comp == null)
			{
				comp = behaviour.GetComponentInParent<T>(true);
			}
		}

		public static void SetDefaultComponetFromChild<T>(this Component behaviour, ref T comp) where T : Component
		{
			if (comp == null)
			{
				comp = behaviour.GetComponentInChildren<T>(true);
			}
		}
	}
}
