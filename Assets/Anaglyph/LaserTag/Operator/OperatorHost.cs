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
using UnityEngine.Localization;

namespace Anaglyph.LaserTag.Operator
{
	/// <summary>
	/// What the operator machine does whether or not a panel is showing it: host a LAN
	/// session, delegate shared-anchor creation to a headset, and report connected headsets.
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
					return MenuCopy.Format("Operator", "address.relay", NetcodeManagement.CurrentSessionName);

				if (currentTransport is UnityTransport unityTransport)
					return MenuCopy.Format("Operator", "address.lan", unityTransport.ConnectionData.Address);

				return currentTransport.GetType().Name;
			}
		}

		/// <summary>
		/// Waits for the networking prefabs to spawn, then hosts on the last supported map.
		/// Returns the reason hosting failed, or an empty string.
		/// </summary>
		public static async Awaitable<LocalizedString> StartSessionAsync(CancellationToken cancellationToken)
		{
			while (NetworkManager.Singleton == null || ColocationManager.Instance == null ||
			       LaserTagMapCoordinator.Instance == null)
				await Awaitable.NextFrameAsync(cancellationToken);

			LoadLastSupportedMap();
			TryStartHosting(out LocalizedString error);

			EnvMesher.Instance.SetChunksVisible(true);

			return error;
		}

		public static bool TryStartHosting(out LocalizedString error)
		{
			error = null;

			if (NetcodeManagement.State != NetcodeState.Disconnected)
			{
				error = MenuCopy.String("Operator", "error.already-hosting");
				return false;
			}

			if (NetworkManager.Singleton == null)
			{
				error = MenuCopy.String("Operator", "error.network-unavailable");
				return false;
			}

			ColocationManager colocation = ColocationManager.Instance;
			if (colocation == null)
			{
				error = MenuCopy.String("Operator", "error.colocation-unavailable");
				return false;
			}

			// The tag size is not a hosting choice: it belongs to the map whose tags are being
			// used, and reaches the provider when that map is loaded.
			if (colocation.PreferredSessionMethod == ColocationManager.ColocationMethod.AprilTag &&
				colocation.TagProvider == null)
			{
				error = MenuCopy.String("Operator", "error.tags-unavailable");
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
				error = MenuCopy.String("Operator", "error.host-start", exception.Message);
				Debug.LogException(exception);
				return false;
			}
		}

		/// <summary>The same catalog is used for startup restoration and the operator's map picker.</summary>
		public static bool CanHostMap(GameMap map) => map != null &&
			ColocationManager.IsValidMethod(map.preferredColocationMethod);

		private static void LoadLastSupportedMap()
		{
			if (LaserTagMapCoordinator.Instance.CurrentMap != null)
				return;

			foreach (GameMap map in MapStore.Default.GetByLastUsed())
				if (CanHostMap(map))
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
