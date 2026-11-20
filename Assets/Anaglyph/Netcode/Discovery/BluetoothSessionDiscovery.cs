using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Anaglyph.Bluetooth;
using UnityEngine;

namespace Anaglyph.Netcode
{
	/// <summary>
	/// Sessions announced and found over Bluetooth between nearby devices, whatever network each
	/// is on. A host advertises one service UUID holding its LAN address, relay session name or direct session token;
	/// a listener scans for UUIDs with this game's prefix and hands them to <see cref="SessionDiscovery"/>.
	/// UUID bytes: 4 of prefix, 1 naming the kind of session, 11 of address or name.
	/// </summary>
	public sealed class BluetoothSessionDiscovery : MonoBehaviour
	{
		private const string UuidPrefix = "4c544147";
		private const byte LanKind = 1;
		private const byte RelayKind = 2;
		private const byte DirectKind = 3;
		private const int UuidByteCount = 16;
		private const int KindIndex = 4;
		private const int PayloadIndex = 5;
		private const int PayloadByteCount = UuidByteCount - PayloadIndex;
		private const float RefreshSeconds = 0.5f;

		private BluetoothBeacon beacon;
		private float nextRefresh;
		private bool paused;
		private string unadvertisableSession;

		private void Awake()
		{
			if (BluetoothBeacon.IsSupported) beacon = new BluetoothBeacon();
			else enabled = false;
		}

		private void OnApplicationPause(bool isPaused) => paused = isPaused;

		private void OnDisable()
		{
			beacon?.StopScanning();
			beacon?.StopAdvertising();
			SessionDiscovery.SetReceiving(this, false);
		}

		private void OnDestroy() => beacon?.Dispose();

		private void Update()
		{
			if (Time.unscaledTime < nextRefresh) return;
			nextRefresh = Time.unscaledTime + RefreshSeconds;

			SessionDiscoveryActivity activity = paused ? SessionDiscoveryActivity.Disabled : SessionDiscovery.Activity;

			bool listening = activity == SessionDiscoveryActivity.Listening;
			if (!listening) beacon.StopScanning();
			SessionDiscovery.SetReceiving(this, listening && beacon.TryStartScanning(UuidPrefix));

			string serviceUuid = activity == SessionDiscoveryActivity.Advertising
				? ServiceUuidFor(SessionDiscovery.CurrentAdvertisement())
				: null;
			if (serviceUuid == null) beacon.StopAdvertising();
			else beacon.TryStartAdvertising(serviceUuid);

			while (beacon.TryTakeSighting(out string heardUuid))
				if (TryReadAdvertisement(heardUuid, out string advertisement, out string hostAddress))
					SessionDiscovery.OnAdvertisementHeard(advertisement, hostAddress);
		}

		/// <summary>Null when the advertisement doesn't fit in a UUID: a relay name over 11 bytes, an address that isn't IPv4, or a direct token that isn't 11 bytes of hex.</summary>
		private string ServiceUuidFor(string advertisement)
		{
			byte[] uuid = new byte[UuidByteCount];
			for (int i = 0; i < KindIndex; i++)
				uuid[i] = byte.Parse(UuidPrefix.Substring(i * 2, 2), NumberStyles.HexNumber);

			byte[] payload = null;
			if (advertisement.StartsWith(SessionDiscovery.LanPrefix))
			{
				uuid[KindIndex] = LanKind;
				if (IPAddress.TryParse(advertisement.Substring(SessionDiscovery.LanPrefix.Length), out IPAddress address) &&
				    address.AddressFamily == AddressFamily.InterNetwork)
					payload = address.GetAddressBytes();
			}
			else if (advertisement.StartsWith(SessionDiscovery.RelayPrefix))
			{
				uuid[KindIndex] = RelayKind;
				payload = Encoding.UTF8.GetBytes(advertisement.Substring(SessionDiscovery.RelayPrefix.Length));
			}
			else if (advertisement.StartsWith(SessionDiscovery.DirectPrefix))
			{
				uuid[KindIndex] = DirectKind;
				payload = BytesFromHex(advertisement.Substring(SessionDiscovery.DirectPrefix.Length));
			}

			if (payload == null || payload.Length == 0 || payload.Length > PayloadByteCount)
			{
				if (advertisement != "" && advertisement != unadvertisableSession)
					Debug.LogWarning($"[{nameof(BluetoothSessionDiscovery)}] can't advertise {advertisement}: it doesn't fit in a service UUID");
				unadvertisableSession = advertisement;
				return null;
			}

			Array.Copy(payload, 0, uuid, PayloadIndex, payload.Length);

			string hex = BitConverter.ToString(uuid).Replace("-", "").ToLowerInvariant();
			return $"{hex.Substring(0, 8)}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}-{hex.Substring(16, 4)}-{hex.Substring(20)}";
		}

		private static bool TryReadAdvertisement(string serviceUuid, out string advertisement, out string hostAddress)
		{
			advertisement = hostAddress = null;

			string hex = serviceUuid.Replace("-", "");
			if (hex.Length != UuidByteCount * 2 || !hex.StartsWith(UuidPrefix, StringComparison.OrdinalIgnoreCase)) return false;

			byte[] uuid = new byte[UuidByteCount];
			for (int i = 0; i < UuidByteCount; i++)
				if (!byte.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uuid[i]))
					return false;

			switch (uuid[KindIndex])
			{
				case LanKind:
					hostAddress = $"{uuid[PayloadIndex]}.{uuid[PayloadIndex + 1]}.{uuid[PayloadIndex + 2]}.{uuid[PayloadIndex + 3]}";
					advertisement = SessionDiscovery.LanPrefix + hostAddress;
					return true;

				case RelayKind:
					int nameEnd = Array.IndexOf(uuid, (byte)0, PayloadIndex);
					int nameByteCount = (nameEnd < 0 ? UuidByteCount : nameEnd) - PayloadIndex;
					if (nameByteCount == 0) return false;
					advertisement = SessionDiscovery.RelayPrefix + Encoding.UTF8.GetString(uuid, PayloadIndex, nameByteCount);
					return true;

				case DirectKind:
					advertisement = SessionDiscovery.DirectPrefix +
					                BitConverter.ToString(uuid, PayloadIndex, PayloadByteCount).Replace("-", "").ToLowerInvariant();
					return true;

				default:
					return false;
			}
		}

		private static byte[] BytesFromHex(string hex)
		{
			if (hex.Length != PayloadByteCount * 2) return null;

			byte[] bytes = new byte[PayloadByteCount];
			for (int i = 0; i < bytes.Length; i++)
				if (!byte.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
					return null;
			return bytes;
		}
	}
}
