using System.Linq;
using System.Net;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Anaglyph.Lasertag.UI
{
	public class NetworkMenuFunctions : MonoBehaviour
	{
		private const ushort Port = 25001;
		private const string Listen = "0.0.0.0";

		public void Host()
		{
			Debug.Log($"Hosting on {GetLocalIPv4()}:{Port}...");

//#if !UNITY_EDITOR
//			if (!PlatformData.Initialized)
//			{
//				Debug.Log("Host failed, platform not initialized");
//				return;
//			}
//#endif

			NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData(GetLocalIPv4(), Port, Listen);
			NetworkManager.Singleton.StartHost();
		}

		public string ip;

		public void SetIp(string ip)
		{
			this.ip = ip;
		}
		
		public void Join()
		{
			Debug.Log($"Joining {ip}:{Port}...");

//#if !UNITY_EDITOR
//			if (!PlatformData.Initialized)
//			{
//				Debug.Log("Join failed, platform not initialized");
//				return;
//			}
//#endif

			//Ping ping = new Ping(ip);

			//while (!ping.isDone)
			//{

			//}

			//Debug.Log($"Ping Done: {ping.time}ms");

			var sceneNetObjects = FindObjectsOfType<NetworkObject>();
			foreach (NetworkObject obj in sceneNetObjects)
			{
				obj.SetSceneObjectStatus(true);
			}

			NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData(ip, Port, Listen);
			NetworkManager.Singleton.StartClient();
		}

		public void Disconnect()
		{
			NetworkManager.Singleton.Shutdown();
		}

		// private void ShowConnectionPage(bool t)
		// {
		// 	startPage.gameObject.SetActive(!t);
		// 	connectionPage.gameObject.SetActive(t);
		// 	
		// 	if (!t)
		// 	{
		// 		statusLabel.text = "Connecting...";
		// 		statusLabel.color = Color.white;
		// 	}
		// }

		private string GetLocalIPv4()
		{
			return Dns.GetHostEntry(Dns.GetHostName())
				.AddressList.First(
					f => f.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
				.ToString();
		}
	}
}