using Anaglyph.Menu;
using UnityEngine;

namespace Anaglyph.Lasertag.UI
{
	public class SpawnObjectButton : MonoBehaviour
	{
		public GameObject objectToSpawn;
		private HandedButton handedButton;

		private void Awake()
		{
			TryGetComponent(out handedButton);
			handedButton.onClickIsRight.AddListener(OnClick);
		}

		private void OnClick(bool isRight)
		{
			var spawner = isRight ? Spawner.Right : Spawner.Left;
			spawner.gameObject.SetActive(true);
			spawner.SetObjectToSpawn(objectToSpawn);
		}
	}
}