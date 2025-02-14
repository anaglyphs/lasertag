using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Netcode
{
	public class NetworkTimeTest : MonoBehaviour
	{
		void Update()
		{
			float networkTime = NetworkManager.Singleton.LocalTime.TimeAsFloat;

			transform.position = Vector3.up * Mathf.PingPong(networkTime, 1f);
		}
	}
}
