using UnityEngine;
using Unity.WebRTC;

namespace Anaglyph.Lasertag.EnvironmentSync
{
	public class EnvironmentSynchronizer : MonoBehaviour
	{
		public static EnvironmentSynchronizer Instance { get; private set; }
		private void Awake()
		{
			Instance = this;
		}

	}
}