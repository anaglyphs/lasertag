using System.Net.Sockets;
using System.Net;
using UnityEngine;
using UnityEngine.UI;
using Anaglyph;

namespace Anaglyph.Menu
{
	public class IpText : MonoBehaviour
	{
		public string text = "{0}";
		[SerializeField] private Text label;

		private void OnValidate()
		{
			this.SetComponent(ref label);
		}

		private void OnEnable() => UpdateTextWithIp();

		private void OnApplicationFocus(bool focus) => UpdateTextWithIp();

		private void UpdateTextWithIp()
		{
			string ip = GetLocalIPAddress();
			string formattedText = string.Format(text, ip);
			label.text = formattedText;
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