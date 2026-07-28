using UnityEngine;

namespace Anaglyph.Lasertag
{
	/// <summary>Initializes the shared Lasertag settings asset after scene startup.</summary>
	public class LasertagSettingsInitializer : MonoBehaviour
	{
		[SerializeField] private LasertagSettings settings;

		private void Start()
		{
			settings.Apply();
		}

		private void OnDestroy()
		{
			settings.RemoveChangeListeners();
		}
	}
}
