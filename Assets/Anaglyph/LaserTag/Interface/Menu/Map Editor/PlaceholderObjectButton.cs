using Anaglyph.Netcode;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class PlaceholderObjectButton : MonoBehaviour
	{
		[SerializeField] private NetworkObject networkPrefab;

		private void Awake()
		{
			Button button = GetComponent<Button>();
			button.onClick.AddListener(delegate
			{
				if (NetcodeManagement.State != NetcodeState.Connected)
					return;

				Camera cam = Camera.main;

				Ray ray = new(cam.transform.position, Vector3.down);

				if (!Physics.Raycast(ray, out RaycastHit hit))
					return;

				NetworkObject.InstantiateAndSpawn(networkPrefab.gameObject, NetworkManager.Singleton,
					NetworkManager.Singleton.LocalClientId, false, false, false, hit.point, quaternion.identity);
			});
		}
	}
}