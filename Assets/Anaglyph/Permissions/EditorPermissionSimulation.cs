#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace Anaglyph.Permissions
{
	/// <summary>
	/// Editor-only backing state for permission and capability simulation.
	/// The editor menu owns persistence; this class is excluded from players.
	/// </summary>
	public static class EditorPermissionSimulation
	{
		private sealed class PermissionState
		{
			public bool available = true;
			public bool granted;
		}

		private static readonly Dictionary<string, PermissionState> permissionStates = new();

		public static bool enabled { get; set; }
		public static bool grantRequests { get; set; } = true;
		public static VpsStatus vpsStatus { get; set; } = VpsStatus.Disabled;

		public static event Action<string, bool> permissionGrantedChanged;

		public static AndroidPermissionCheckResult CheckPermission(string permission)
		{
			PermissionState state = GetOrCreateState(permission);

			return new AndroidPermissionCheckResult(
				permission,
				state.available
					? PermissionAvailability.Available
					: PermissionAvailability.Unavailable,
				state.available
					? state.granted
						? PermissionAuthorization.Granted
						: PermissionAuthorization.Denied
					: PermissionAuthorization.Unknown);
		}

		public static bool IsPermissionAvailable(string permission)
		{
			return GetOrCreateState(permission).available;
		}

		public static void SetPermissionAvailable(string permission, bool available)
		{
			GetOrCreateState(permission).available = available;
		}

		public static bool IsPermissionGranted(string permission)
		{
			return GetOrCreateState(permission).granted;
		}

		public static void SetPermissionGranted(string permission, bool granted)
		{
			PermissionState state = GetOrCreateState(permission);
			if (state.granted == granted)
				return;

			state.granted = granted;
			permissionGrantedChanged?.Invoke(permission, granted);
		}

		internal static void RequestPermission(
			AndroidPermissionCheckResult check,
			Action<PermissionRequestResult> completed)
		{
			switch (check.availability)
			{
				case PermissionAvailability.Unavailable:
					completed(new PermissionRequestResult(
						check.permission,
						PermissionRequestOutcome.Unavailable));
					return;

				case PermissionAvailability.Unknown:
					completed(new PermissionRequestResult(
						check.permission,
						PermissionRequestOutcome.AvailabilityUnknown));
					return;
			}

			if (check.authorization == PermissionAuthorization.Granted)
			{
				completed(new PermissionRequestResult(
					check.permission,
					PermissionRequestOutcome.AlreadyGranted));
				return;
			}

			if (grantRequests)
			{
				SetPermissionGranted(check.permission, true);
				completed(new PermissionRequestResult(
					check.permission,
					PermissionRequestOutcome.Granted));
			}
			else
			{
				completed(new PermissionRequestResult(
					check.permission,
					PermissionRequestOutcome.Denied));
			}
		}

		private static PermissionState GetOrCreateState(string permission)
		{
			if (!permissionStates.TryGetValue(permission, out PermissionState state))
			{
				state = new PermissionState();
				permissionStates.Add(permission, state);
			}

			return state;
		}
	}
}
#endif
