using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace Anaglyph.Netcode
{
	/// <summary>
	/// Sessions announced and found by UDP multicast on the local network, the same on Quest, Android
	/// phones, iPhones and the editor. A host repeats its announcement to a multicast group; a
	/// listener joins the group and hands what it hears to <see cref="SessionDiscovery"/>.
	/// iOS delivers multicast only to apps holding the com.apple.developer.networking.multicast entitlement.
	/// </summary>
	public sealed class MulticastSessionDiscovery : MonoBehaviour
	{
		// organization-local scope, never routed off the site
		private static readonly IPAddress Group = IPAddress.Parse("239.255.76.84");
		private const int Port = 47784;
		private const string Magic = "LASERTAG1 ";
		private const float AnnounceSeconds = 1;
		private const float FailedRetrySeconds = 10;
		private bool announceFailed;
		private float nextListenAttempt;

		private UdpClient listener, announcer;
		private float nextAnnounce;
		private bool paused;
		private AndroidMulticastLock multicastLock;

		private void OnApplicationPause(bool isPaused) => paused = isPaused;

		private void OnDisable()
		{
			StopListening();
			StopAnnouncing();
		}

		private void OnDestroy() => multicastLock?.Dispose();

		private void Update()
		{
			SessionDiscoveryActivity activity = paused ? SessionDiscoveryActivity.Disabled : SessionDiscovery.Activity;

			if (activity == SessionDiscoveryActivity.Listening) StartListening();
			else StopListening();

			if (activity == SessionDiscoveryActivity.Advertising) Announce();
			else StopAnnouncing();

			HearAnnouncements();
		}

		private void StartListening()
		{
			if (listener != null || Time.unscaledTime < nextListenAttempt) return;
			try
			{
				multicastLock ??= AndroidMulticastLock.Create();
				multicastLock?.Acquire();

				listener = new UdpClient { ExclusiveAddressUse = false };
				listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
				listener.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
				listener.JoinMulticastGroup(Group);

				SessionDiscovery.Receiving = true;
				Debug.Log($"[{nameof(MulticastSessionDiscovery)}] listening on {Group}:{Port}");
			}
			catch (Exception e) when (e is SocketException or ObjectDisposedException)
			{
				Debug.LogWarning($"[{nameof(MulticastSessionDiscovery)}] couldn't listen: {e.Message}");
				StopListening();
				nextListenAttempt = Time.unscaledTime + FailedRetrySeconds;
			}
		}

		// takes only what has already arrived, so the frame never waits on the network
		private void HearAnnouncements()
		{
			try
			{
				while (listener != null && listener.Client.Poll(0, SelectMode.SelectRead))
				{
					IPEndPoint from = new(IPAddress.Any, 0);
					string text = Encoding.UTF8.GetString(listener.Receive(ref from));
					if (text.StartsWith(Magic))
						SessionDiscovery.OnAdvertisementHeard(text.Substring(Magic.Length), from.Address.ToString());
				}
			}
			catch (Exception e) when (e is SocketException or ObjectDisposedException)
			{
				Debug.LogWarning($"[{nameof(MulticastSessionDiscovery)}] couldn't receive: {e.Message}");
				StopListening();
				nextListenAttempt = Time.unscaledTime + FailedRetrySeconds;
			}
		}

		private void StopListening()
		{
			SessionDiscovery.Receiving = false;
			multicastLock?.Release();
			if (listener == null) return;
			listener.Close();
			listener = null;
		}

		private void Announce()
		{
			if (Time.unscaledTime < nextAnnounce) return;
			nextAnnounce = Time.unscaledTime + AnnounceSeconds;

			string advertisement = SessionDiscovery.CurrentAdvertisement();
			if (string.IsNullOrEmpty(advertisement)) return;

			try
			{
				announcer ??= new UdpClient { Ttl = 1 };
				byte[] packet = Encoding.UTF8.GetBytes(Magic + advertisement);
				announcer.Send(packet, packet.Length, new IPEndPoint(Group, Port));
			}
			catch (Exception e) when (e is SocketException or ObjectDisposedException)
			{
				// iOS refuses multicast to apps without Apple's multicast entitlement: say so once, retry slowly
				if (!announceFailed) Debug.LogWarning($"[{nameof(MulticastSessionDiscovery)}] couldn't announce: {e.Message}");
				announceFailed = true;
				nextAnnounce = Time.unscaledTime + FailedRetrySeconds;
				StopAnnouncing();
			}
		}

		private void StopAnnouncing()
		{
			announcer?.Close();
			announcer = null;
		}

		/// <summary>Android drops multicast packets to save power unless an app holds this lock.</summary>
		private sealed class AndroidMulticastLock : IDisposable
		{
			private readonly AndroidJavaObject wifiLock;
			private bool held;

			private AndroidMulticastLock(AndroidJavaObject wifiLock) => this.wifiLock = wifiLock;

			public static AndroidMulticastLock Create()
			{
#if UNITY_ANDROID && !UNITY_EDITOR
				using AndroidJavaClass player = new("com.unity3d.player.UnityPlayer");
				using AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");
				using AndroidJavaObject wifi = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
				AndroidJavaObject wifiLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "Lasertag session discovery");
				wifiLock.Call("setReferenceCounted", false);
				return new AndroidMulticastLock(wifiLock);
#else
				return null;
#endif
			}

			public void Acquire()
			{
				if (held) return;
				wifiLock.Call("acquire");
				held = true;
			}

			public void Release()
			{
				if (!held) return;
				wifiLock.Call("release");
				held = false;
			}

			public void Dispose()
			{
				Release();
				wifiLock.Dispose();
			}
		}
	}
}
