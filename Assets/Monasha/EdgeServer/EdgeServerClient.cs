using System;
using UnityEngine;
using System.Net.Sockets;

namespace Monasha.EdgeServer
{
	public class EdgeServerClient : MonoBehaviour
	{
		[SerializeField] private string serverIP = "127.0.0.1";
		[SerializeField] private int serverPort = 9876;

		private TcpClient client;
		private NetworkStream stream;

		private void Start()
		{
			try
			{
				client = new TcpClient(serverIP, serverPort);
				stream = client.GetStream();
				Debug.Log($"[EdgeServerClient] Connected to edge server at {serverIP}:{serverPort}");
			}
			catch (Exception e)
			{
				Debug.LogError($"[EdgeServerClient] Failed to connect: {e.Message}");
			}
		}

		private void Update()
		{
			if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
			{
				SendTestMessage();
			}

			if (stream == null) return;

			// Check if ack bytes arrived
			if (!stream.DataAvailable) return;

			var buffer = new byte[256];
			var bytesRead = stream.Read(buffer, 0, buffer.Length);
			Debug.Log($"[EdgeServerClient] Received ack: {bytesRead} bytes, type=0x{buffer[0]:X2}");
		}

		public void SendTestMessage()
		{
			if (stream == null)
			{
				Debug.LogError($"[EdgeServerClient] Stream is null");
				return;
			}

			var message = new byte[1024];
			message[0] = 0x01; // depth frame message type
			stream.Write(message, 0, message.Length);
			Debug.Log("[EdgeServerClient] Sent 1024 TEST bytes");
		}

		private void OnDestroy()
		{
			stream?.Close();
			client?.Close();
		}
	}
}