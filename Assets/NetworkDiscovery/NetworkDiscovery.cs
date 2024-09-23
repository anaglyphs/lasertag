using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using System.Globalization;

namespace NetworkDiscoveryUnity
{
	
	public class NetworkDiscovery : MonoBehaviour
	{
		
		public class DiscoveryInfo
		{
			public readonly IPEndPoint EndPoint;
			public readonly IReadOnlyDictionary<string, string> KeyValuePairs;
			private float m_timeWhenReceived = 0f;
			public float TimeSinceReceived => Time.realtimeSinceStartup - m_timeWhenReceived;

			public DiscoveryInfo (IPEndPoint endPoint, Dictionary<string, string> keyValuePairs)
			{
				this.EndPoint = endPoint;
				this.KeyValuePairs = keyValuePairs;
				m_timeWhenReceived = Time.realtimeSinceStartup;
			}

            public ushort GetGameServerPort() => ushort.Parse(this.KeyValuePairs[kPortKey], CultureInfo.InvariantCulture);

            public bool TryGetGameServerPort(out ushort port)
            {
				port = 0;
                return this.KeyValuePairs.TryGetValue(kPortKey, out string portString)
					&& ushort.TryParse(portString, NumberStyles.None, CultureInfo.InvariantCulture, out port);
            }
        }

		public UnityEngine.Events.UnityEvent<DiscoveryInfo> onReceivedServerResponse =
			new UnityEngine.Events.UnityEvent<DiscoveryInfo>();

		// server sends this data as a response to broadcast
		readonly Dictionary<string, string> m_responseData =
			new Dictionary<string, string> (System.StringComparer.InvariantCulture);

		public static NetworkDiscovery Instance { get ; private set ; }

		public const string kSignatureKey = "Signature", kPortKey = "Port", kMapNameKey = "Map";
		
		public const int kDefaultServerPort = 18418;

		[SerializeField] int m_serverPort = kDefaultServerPort;
		UdpClient m_serverUdpCl = null;
		UdpClient m_clientUdpCl = null;

		public static bool SupportedOnThisPlatform { get { return Application.platform != RuntimePlatform.WebGLPlayer; } }

		public int gameServerPortNumber = 7777;

		private static string s_cachedSignature = null;
		


		void Awake ()
		{
			if (null == Instance)
				Instance = this;

			RegisterResponseData(kSignatureKey, GetCachedSignature());
			RegisterResponseData(kPortKey, this.gameServerPortNumber.ToString(CultureInfo.InvariantCulture));
			RegisterResponseData(kMapNameKey, SceneManager.GetActiveScene().name);
		}

        void OnEnable()
        {
			SceneManager.activeSceneChanged += OnActiveSceneChanged;
		}

        void OnDisable ()
		{
			SceneManager.activeSceneChanged -= OnActiveSceneChanged;
			ShutdownUdpClients ();
		}

		void OnActiveSceneChanged(Scene oldScene, Scene newScene)
		{
			RegisterResponseData(kMapNameKey, SceneManager.GetActiveScene().name);
		}

        void Update()
        {
			UpdateServer();
			UpdateClient();
        }


        public void EnsureServerIsInitialized()
		{

			if (m_serverUdpCl != null)
				return;

			m_serverUdpCl = new UdpClient (m_serverPort);
			RunSafe( () => { m_serverUdpCl.EnableBroadcast = true; } );
			RunSafe( () => { m_serverUdpCl.MulticastLoopback = false; } );

		}

		void EnsureClientIsInitialized()
		{

			if (m_clientUdpCl != null)
				return;

			m_clientUdpCl = new UdpClient (0);
			RunSafe( () => { m_clientUdpCl.EnableBroadcast = true; } );
			// turn off receiving from our IP
			RunSafe( () => { m_clientUdpCl.MulticastLoopback = false; } );

		}

		void ShutdownUdpClients()
		{
			CloseServerUdpClient();
			CloseClientUdpClient();
		}

		public void CloseServerUdpClient()
		{
			if (m_serverUdpCl != null) {
				m_serverUdpCl.Close ();
				m_serverUdpCl = null;
			}
		}

		void CloseClientUdpClient()
		{
			if (m_clientUdpCl != null) {
				m_clientUdpCl.Close ();
				m_clientUdpCl = null;
			}
		}


		static DiscoveryInfo ReadDataFromUdpClient(UdpClient udpClient)
		{
			
			// only proceed if there is available data in network buffer, or otherwise Receive() will block
			// average time for UdpClient.Available : 10 us
			if (udpClient.Available <= 0)
				return null;
			
			Profiler.BeginSample("UdpClient.Receive");
			IPEndPoint remoteEP = new IPEndPoint (IPAddress.Any, 0);
			byte[] receivedBytes = udpClient.Receive (ref remoteEP);
			Profiler.EndSample ();

			if (remoteEP != null && receivedBytes != null && receivedBytes.Length > 0) {

				Profiler.BeginSample ("Convert data");
				var dict = ConvertByteArrayToDictionary (receivedBytes);
				Profiler.EndSample ();

				return new DiscoveryInfo(remoteEP, dict);
			}

			return null;
		}

		void UpdateServer()
		{
			if (null == m_serverUdpCl)
				return;

			
			// average time for this (including data receiving and processing): less than 100 us
			Profiler.BeginSample ("Receive broadcast");
			//	var timer = System.Diagnostics.Stopwatch.StartNew ();

			var info = ReadDataFromUdpClient(m_serverUdpCl);
			if (info != null)
				OnReceivedBroadcast(info);

		//	Debug.Log("receive broadcast time: " + timer.GetElapsedMicroSeconds () + " us");
			Profiler.EndSample ();

		}

		void OnReceivedBroadcast(DiscoveryInfo info)
		{
			if(info.KeyValuePairs.ContainsKey(kSignatureKey) && info.KeyValuePairs[kSignatureKey] == GetCachedSignature())
			{
				// signature matches
				// send response

				Profiler.BeginSample("Send response");
				byte[] bytes = ConvertDictionaryToByteArray( m_responseData );
				m_serverUdpCl.Send( bytes, bytes.Length, info.EndPoint );
				Profiler.EndSample();
			}
		}

		void UpdateClient()
		{
			if (null == m_clientUdpCl)
				return;

			var info = ReadDataFromUdpClient(m_clientUdpCl);
			if (info != null)
				OnReceivedServerResponse(info);

		}


		public static byte[] GetDiscoveryRequestData()
		{
			Profiler.BeginSample("ConvertDictionaryToByteArray");
			var dict = new Dictionary<string, string>(System.StringComparer.InvariantCulture) {{kSignatureKey, GetCachedSignature()}};
			byte[] buffer = ConvertDictionaryToByteArray (dict);
			Profiler.EndSample();

			return buffer;
		}

		public void SendBroadcast()
		{
			if (!SupportedOnThisPlatform)
				return;
			
			byte[] buffer = GetDiscoveryRequestData();

			// We can't just send packet to 255.255.255.255 - the OS will only broadcast it to the network interface
			// which the socket is bound to.
			// We need to broadcast packet on every network interface.

			IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, m_serverPort);

			foreach(var address in GetBroadcastAdresses())
			{
				endPoint.Address = address;
				SendDiscoveryRequest(endPoint, buffer);
			}
			
		}

		public void SendDiscoveryRequest(IPEndPoint endPoint)
		{
			SendDiscoveryRequest(endPoint, GetDiscoveryRequestData());
		}

		void SendDiscoveryRequest(IPEndPoint endPoint, byte[] buffer)
		{
			if (!SupportedOnThisPlatform)
				return;

			EnsureClientIsInitialized();

			if (null == m_clientUdpCl)
				return;
			
			
			Profiler.BeginSample("UdpClient.Send");
			try {
				m_clientUdpCl.Send (buffer, buffer.Length, endPoint);
			} catch(SocketException ex) {
				if(ex.ErrorCode == 10051) {
					// Network is unreachable
					// ignore this error

				} else {
					throw;
				}
			}
			Profiler.EndSample();

		}


		public static IPAddress[] GetBroadcastAdresses()
		{
			// try multiple methods - because some of them may fail on some devices, especially if IL2CPP comes into play

			IPAddress[] ips = null;

			RunSafe(() => ips = GetBroadcastAdressesFromNetworkInterfaces(), false);
			
			if (null == ips || ips.Length < 1)
			{
				// try another method
				RunSafe(() => ips = GetBroadcastAdressesFromHostEntry(), false);
			}
			
			if (null == ips || ips.Length < 1)
			{
				// all methods failed, or there is no network interface on this device
				// just use full-broadcast address
				ips = new IPAddress[]{IPAddress.Broadcast};
			}
			
			return ips;
		}

		static IPAddress[] GetBroadcastAdressesFromNetworkInterfaces()
		{
			List<IPAddress> ips = new List<IPAddress>();

			var nifs = NetworkInterface.GetAllNetworkInterfaces()
				.Where(nif => nif.OperationalStatus == OperationalStatus.Up)
				.Where(nif => nif.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || nif.NetworkInterfaceType == NetworkInterfaceType.Ethernet);

			foreach (var nif in nifs)
			{
				foreach (UnicastIPAddressInformation ipInfo in nif.GetIPProperties().UnicastAddresses)
				{
					var ip = ipInfo.Address;
					if (ip.AddressFamily == AddressFamily.InterNetwork)
					{
						if(ToBroadcastAddress(ref ip, ipInfo.IPv4Mask))
							ips.Add(ip);
					}
				}
			}

			return ips.ToArray();
		}

		static IPAddress[] GetBroadcastAdressesFromHostEntry()
		{
			var ips = new List<IPAddress> ();

			IPHostEntry hostEntry = Dns.GetHostEntry(Dns.GetHostName());

			foreach(var address in hostEntry.AddressList)
			{
				if (address.AddressFamily == AddressFamily.InterNetwork)
				{
					// this is IPv4 address
					// convert it to broadcast address
					// use default subnet

					var subnetMask = GetDefaultSubnetMask(address);
					if (subnetMask != null)
					{
						var broadcastAddress = address;
						if (ToBroadcastAddress(ref broadcastAddress, subnetMask))
							ips.Add( broadcastAddress );
					}
				}
			}

			if (ips.Count > 0)
			{
				// if we found at least 1 ip, then also add full-broadcast address
				// this will compensate in case we used a wrong subnet mask
				ips.Add(IPAddress.Broadcast);
			}

			return ips.ToArray();
		}

		static bool ToBroadcastAddress(ref IPAddress ip, IPAddress subnetMask)
		{
			if (ip.AddressFamily != AddressFamily.InterNetwork || subnetMask.AddressFamily != AddressFamily.InterNetwork)
				return false;
			
			byte[] bytes = ip.GetAddressBytes();
			byte[] subnetMaskBytes = subnetMask.GetAddressBytes();

			for(int i=0; i < 4; i++)
			{
				// on places where subnet mask has 1s, address bits are copied,
				// and on places where subnet mask has 0s, address bits are 1
				bytes[i] = (byte) ((~subnetMaskBytes[i]) | bytes[i]);
			}

			ip = new IPAddress(bytes);

			return true;
		}

		static IPAddress GetDefaultSubnetMask(IPAddress ip)
		{
			if (ip.AddressFamily != AddressFamily.InterNetwork)
				return null;

			IPAddress subnetMask;

			byte[] bytes = ip.GetAddressBytes();
			byte firstByte = bytes[0];

			if (firstByte >= 0 && firstByte <= 127)
				subnetMask = new IPAddress(new byte[]{255, 0, 0, 0});
			else if (firstByte >= 128 && firstByte <= 191)
				subnetMask = new IPAddress(new byte[]{255, 255, 0, 0});
			else if (firstByte >= 192 && firstByte <= 223)
				subnetMask = new IPAddress(new byte[]{255, 255, 255, 0});
			else // undefined subnet
				subnetMask = null;

			return subnetMask;
		}


		void OnReceivedServerResponse(DiscoveryInfo info) {

			// check if data is valid
			if(!IsDataFromServerValid(info))
				return;
			
			// invoke event
			this.onReceivedServerResponse?.Invoke(info);

		}

		public static bool IsDataFromServerValid(DiscoveryInfo data)
		{
			// data must contain signature which matches, and port number
			return data.KeyValuePairs.ContainsKey(kSignatureKey) && data.KeyValuePairs[kSignatureKey] == GetCachedSignature()
				&& data.KeyValuePairs.ContainsKey(kPortKey);
		}


		public void RegisterResponseData( string key, string value )
		{
			m_responseData[key] = value;
		}

		public void UnRegisterResponseData( string key )
		{
			m_responseData.Remove (key);
		}

		static string GetCachedSignature()
		{
			if (s_cachedSignature != null)
				return s_cachedSignature;

			s_cachedSignature = CalculateGameSignature();

			return s_cachedSignature;
		}

		/// Signature identifies this game among others.
		public static string CalculateGameSignature()
        {
			string[] strings = new string[]{ Application.companyName, Application.productName,
				Application.unityVersion };

			string signature = "";

			foreach (string str in strings)
			{
				// only use it's hash code
				signature += str.GetHashCode() + ".";
			}

			return signature;
		}


		public static string ConvertDictionaryToString( Dictionary<string, string> dict )
		{
			return string.Join( "\n", dict.Select( pair => pair.Key + ": " + pair.Value ) );
		}

		public static Dictionary<string, string> ConvertStringToDictionary( string str )
		{
			var dict = new Dictionary<string, string>(System.StringComparer.InvariantCulture);
			string[] lines = str.Split("\n".ToCharArray(), System.StringSplitOptions.RemoveEmptyEntries);
			foreach(string line in lines)
			{
				int index = line.IndexOf(": ");
				if(index < 0)
					continue;
				dict[line.Substring(0, index)] = line.Substring(index + 2, line.Length - index - 2);
			}
			return dict;
		}

		public static byte[] ConvertDictionaryToByteArray( Dictionary<string, string> dict )
		{
			return ConvertStringToPacketData( ConvertDictionaryToString( dict ) );
		}

		public static Dictionary<string, string> ConvertByteArrayToDictionary( byte[] data )
		{
			return ConvertStringToDictionary( ConvertPacketDataToString( data ) );
		}

		public static byte[] ConvertStringToPacketData(string str)
		{
			byte[] data = new byte[str.Length * 2];
			for (int i = 0; i < str.Length; i++)
			{
				ushort c = str[i];
				data[i * 2] = (byte) ((c & 0xff00) >> 8);
				data[i * 2 + 1] = (byte) (c & 0x00ff);
			}
			return data;
		}

		public static string ConvertPacketDataToString(byte[] data)
		{
			char[] arr = new char[data.Length / 2];
			for (int i = 0; i < arr.Length; i++)
			{
				ushort b1 = data[i * 2];
				ushort b2 = data[i * 2 + 1];
				arr[i] = (char)((b1 << 8) | b2);
			}
			return new string(arr);
		}


		static bool RunSafe(System.Action action, bool logException = true)
		{
			try
			{
				action();
				return true;
			}
			catch(System.Exception ex)
			{
				if (logException)
					Debug.LogException(ex);
				return false;
			}
		}

	}

}
