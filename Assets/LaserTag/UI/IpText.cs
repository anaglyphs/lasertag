using System.Net.Sockets;
using System.Net;
using UnityEngine;
using UnityEngine.UI;

namespace LaserTag.UI
{
	public class IpText : MonoBehaviour
	{
		public string text = "{0}";
		private Text textMesh;

		private void Awake()
		{
			textMesh = GetComponent<Text>();
		}

		private void OnEnable() => UpdateTextWithIp();

		private void OnApplicationFocus(bool focus) => UpdateTextWithIp();

		private void UpdateTextWithIp()
		{
			string ip = GetLocalIPAddress();
			string formattedText = string.Format(text, ip);
			textMesh.text = formattedText;
		}

		public static string GetLocalIPAddress()
		{
			var host = Dns.GetHostEntry(Dns.GetHostName());
			foreach (var ip in host.AddressList)
			{
				if (ip.AddressFamily == AddressFamily.InterNetwork)
				{
					return ip.ToString();
				}
			}
			throw new System.Exception("No network adapters with an IPv4 address in the system!");
		}
	}
}