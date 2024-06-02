using Anaglyph.XRTemplate;
using UnityEngine;
using Unity.Netcode;
using Anaglyph.Menu;

namespace Anaglyph.LaserTag
{
	public class ToolPalette : MonoBehaviour
	{
		public static ToolPalette Left { get; private set; }
		public static ToolPalette Right { get; private set; }

		public Spawner spawner;
		public SingleChildActivator toolSelector;

		private void Awake()
		{
			bool isRight = GetComponentInParent<HandSide>().isRight;

			if (isRight)
				Right = this;
			else
				Left = this;
		}

		private void Start()
		{
			gameObject.SetActive(NetworkManager.Singleton.IsConnectedClient);
		}

		public void OpenSpawnerWithObject(GameObject objectToSpawn)
		{
			toolSelector.SetActiveChild(spawner.transform.GetSiblingIndex());
			spawner.SetObjectToSpawn(objectToSpawn);
		}
	}
}