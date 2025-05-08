using Anaglyph.Netcode;
using Anaglyph.XRTemplate.SharedSpaces;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag.Gallery
{
	public class RangeManager : MonoBehaviour
	{
		[SerializeField] private bool isHost;
		[SerializeField] private bool startGame;
		[SerializeField] private string hostIP = "192.168.0.1";

		[SerializeField] private NetworkObject[] possiblePrefabs;

		[SerializeField] AnimationCurve spacing;
		[SerializeField] private float gameLength;
		private float gameEndTime;

		[SerializeField] private float spawnZ = 20;

		private async void Start()
		{
			await Awaitable.WaitForSecondsAsync(0.1f);

			if (isHost)
			{
				NetworkHelper.Host(NetworkHelper.Protocol.LAN);
			}

			Colocation.ActiveColocator.Colocate();
		}

		private void LateUpdate()
		{
			if(!isHost && !NetworkManager.Singleton.IsListening)
			{
				NetworkHelper.ConnectLAN(hostIP);
			}

			if(startGame)
			{
				startGame = false;

				gameEndTime = Time.time + gameLength;

				StartCoroutine(SpawnLoop());
			}
		}

		private IEnumerator SpawnLoop()
		{
			while (Time.time < gameEndTime)
			{
				float wait = spacing.Evaluate(Random.Range(0f, 1f));
				yield return new WaitForSeconds(wait);

				int index = Random.Range(0, possiblePrefabs.Length);
				GameObject prefab = possiblePrefabs[index].gameObject;
				Vector3 spawnPos = new Vector3(0, 0, spawnZ);
				NetworkObject.InstantiateAndSpawn(prefab, NetworkManager.Singleton, NetworkManager.ServerClientId, false, false, false, spawnPos, Quaternion.identity);
			}
		}
	}
}
