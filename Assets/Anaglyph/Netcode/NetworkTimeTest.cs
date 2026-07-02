using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Netcode
{
	public class NetworkTimeTest : MonoBehaviour
	{
		public void Update()
		{
			// Move up and down by 5 meters and change direction every 3 seconds.
			float positionY = Mathf.PingPong(NetworkManager.Singleton.ServerTime.TimeAsFloat / 3f, 1f) * 5f;
			transform.position = new Vector3(0, positionY, 0);
		}
	}
}