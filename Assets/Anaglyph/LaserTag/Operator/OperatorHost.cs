using System;
using System.Collections.Generic;
using System.Threading;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Player;
using Anaglyph.Netcode;
using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR.SharedSpaces;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Anaglyph.LaserTag.Operator
{
	/// <summary>
	/// What the operator machine does whether or not a panel is showing it: host a LAN
	/// session on an AprilTag map, and report the session and the headsets on it.
	/// The operator machine does not play.
	/// </summary>
	public static class OperatorHost
	{
		/// <summary>A connected machine. The avatar is null until that headset spawns one.</summary>
		public readonly struct ConnectedClient
		{
			public readonly ulong clientId;
			public readonly bool isThisServer;
			public readonly PlayerAvatar avatar;

			public ConnectedClient(ulong clientId, bool isThisServer, PlayerAvatar avatar)
			{
				this.clientId = clientId;
				this.isThisServer = isThisServer;
				this.avatar = avatar;
			}
		}

		public static bool IsHosting => NetworkManager.Singleton != null &&
		                                NetworkManager.Singleton.IsListening;

		/// <summary>The IP an operator reads out to the room, session up or not.</summary>
		public static string LocalAddress => NetcodeManagement.GetLocalIPv4();

		public static string SessionAddress
		{
			get
			{
				if (NetcodeManagement.State != NetcodeState.Connected)
					return "";

				NetworkTransport currentTransport =
					NetworkManager.Singleton?.NetworkConfig?.NetworkTransport;

				if (currentTransport == null)
					return "";

				// DistributedAuthorityTransport is internal, so identify it the same
				// way as MultiplayerMenu rather than casting to an inaccessible type.
				if (string.Equals(currentTransport.GetType().Name,
					    "DistributedAuthorityTransport", StringComparison.Ordinal))
					return $"Relay: {NetcodeManagement.CurrentSessionName}";

				if (currentTransport is UnityTransport unityTransport)
					return $"LAN: {unityTransport.ConnectionData.Address}";

				return currentTransport.GetType().Name;
			}
		}

		/// <summary>
		/// Waits for the networking prefabs to spawn, then hosts on the last AprilTag map.
		/// Returns the reason hosting failed, or an empty string.
		/// </summary>
		public static async Awaitable<string> StartSessionAsync(CancellationToken cancellationToken)
		{
			while (NetworkManager.Singleton == null || ColocationManager.Instance == null ||
			       LaserTagMapCoordinator.Instance == null)
				await Awaitable.NextFrameAsync(cancellationToken);

			LoadLastAprilTagMap();
			TryStartHosting(out string error);

			EnvMesher.Instance.SetChunksVisible(true);

			return error;
		}

		public static bool TryStartHosting(out string error)
		{
			error = "";

			if (NetcodeManagement.State != NetcodeState.Disconnected)
			{
				error = "The network session is already starting or connected.";
				return false;
			}

			if (NetworkManager.Singleton == null)
			{
				error = "The NetworkManager has not been created yet.";
				return false;
			}

			ColocationManager colocation = ColocationManager.Instance;
			if (colocation == null)
			{
				error = "The ColocationManager has not been created yet.";
				return false;
			}

			// The tag size is not a hosting choice: it belongs to the map whose tags are being
			// used, and reaches the provider when that map is loaded.
			if (colocation.TagProvider == null)
			{
				error = "AprilTag colocation is selected, but no AprilTag provider is configured.";
				return false;
			}

			PlayerAvatarSpawner.Instance?.SetIsParticipating(false);

			try
			{
				NetcodeManagement.Host(NetcodeManagement.Protocol.LAN);
				return true;
			}
			catch (Exception exception)
			{
				error = $"Could not start the host: {exception.Message}";
				Debug.LogException(exception);
				return false;
			}
		}

		private static void LoadLastAprilTagMap()
		{
			if (LaserTagMapCoordinator.Instance.CurrentMap != null)
				return;

			foreach (GameMap map in MapStore.Default.GetByLastUsed())
				if (map.HasTags)
				{
					LaserTagMapCoordinator.Instance.LoadMap(map.id);
					return;
				}
		}

		/// <summary>Empty while not hosting.</summary>
		public static IReadOnlyList<ConnectedClient> GetConnectedClients()
		{
			NetworkManager manager = NetworkManager.Singleton;
			if (manager == null || !manager.IsListening)
				return Array.Empty<ConnectedClient>();

			List<ConnectedClient> clients = new();

			foreach (ulong clientId in manager.ConnectedClientsIds)
			{
				PlayerAvatar.All.TryGetValue(clientId, out PlayerAvatar avatar);
				clients.Add(new ConnectedClient(
					clientId, clientId == manager.LocalClientId, avatar));
			}

			return clients;
		}
	}
}
