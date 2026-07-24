using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;

namespace Anaglyph.Permissions
{
	/// <summary>
	/// Android permission availability, authorization, candidate selection, and
	/// caller-driven request helpers. Ordered candidate lists use the first
	/// permission recognized by the operating system.
	/// </summary>
	public static class AndroidPermissionChecks
	{
		private const string NameNotFoundException = "NameNotFoundException";

		private static readonly Dictionary<string, PermissionAvailability>
			permissionAvailabilityCache = new();

		/// <summary>
		/// Checks whether an Android permission exists on this OS and whether it is granted.
		/// </summary>
		public static AndroidPermissionCheckResult CheckPermission(string permission)
		{
			ValidatePermission(permission);

#if UNITY_EDITOR
			if (EditorPermissionSimulation.enabled)
				return EditorPermissionSimulation.CheckPermission(permission);
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
			PermissionAvailability availability = CheckPermissionAvailability(permission);
			PermissionAuthorization authorization =
				availability == PermissionAvailability.Available
					? Permission.HasUserAuthorizedPermission(permission)
						? PermissionAuthorization.Granted
						: PermissionAuthorization.Denied
					: PermissionAuthorization.Unknown;

			return new AndroidPermissionCheckResult(permission, availability, authorization);
#else
			return new AndroidPermissionCheckResult(
				permission,
				PermissionAvailability.NotRequired,
				PermissionAuthorization.NotRequired);
#endif
		}

		/// <summary>
		/// Selects the first available permission. An Unknown candidate stops selection
		/// so a lower-priority, broader permission is never used due to a failed query.
		/// </summary>
		public static AndroidPermissionCheckResult CheckPreferredPermission(
			params string[] orderedCandidates)
		{
			ValidateCandidates(orderedCandidates);

#if UNITY_EDITOR
			if (EditorPermissionSimulation.enabled)
				return SelectPreferredPermission(orderedCandidates);
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
			return SelectPreferredPermission(orderedCandidates);
#else
			return new AndroidPermissionCheckResult(
				null,
				PermissionAvailability.NotRequired,
				PermissionAuthorization.NotRequired);
#endif
		}

		private static AndroidPermissionCheckResult SelectPreferredPermission(
			string[] orderedCandidates)
		{
			foreach (string candidate in orderedCandidates)
			{
				AndroidPermissionCheckResult check = CheckPermission(candidate);

				if (check.availability == PermissionAvailability.Available)
					return check;

				if (check.availability == PermissionAvailability.Unknown)
					return check;
			}

			return new AndroidPermissionCheckResult(
				null,
				PermissionAvailability.Unavailable,
				PermissionAuthorization.Unknown);
		}

		/// <summary>
		/// Requests a single permission. The completion callback can run synchronously
		/// when no platform request is necessary or possible.
		/// </summary>
		public static void RequestPermission(
			string permission,
			Action<PermissionRequestResult> completed)
		{
			RequestCheckedPermission(CheckPermission(permission), completed);
		}

		/// <summary>
		/// Requests only the highest-priority available permission. Lower-priority
		/// candidates are fallbacks for systems where earlier candidates do not exist.
		/// </summary>
		public static void RequestPreferredPermission(
			string[] orderedCandidates,
			Action<PermissionRequestResult> completed)
		{
			RequestCheckedPermission(CheckPreferredPermission(orderedCandidates), completed);
		}

		public static void ClearAvailabilityCache()
		{
			permissionAvailabilityCache.Clear();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			ClearAvailabilityCache();
		}

		private static void RequestCheckedPermission(
			AndroidPermissionCheckResult check,
			Action<PermissionRequestResult> completed)
		{
			if (completed == null)
				throw new ArgumentNullException(nameof(completed));

#if UNITY_EDITOR
			if (EditorPermissionSimulation.enabled)
			{
				EditorPermissionSimulation.RequestPermission(check, completed);
				return;
			}
#endif

			switch (check.availability)
			{
				case PermissionAvailability.NotRequired:
					completed(new PermissionRequestResult(
						check.permission,
						PermissionRequestOutcome.NotRequired));
					return;

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

#if UNITY_ANDROID && !UNITY_EDITOR
			bool didComplete = false;

			void Complete(string permission, PermissionRequestOutcome outcome)
			{
				if (didComplete)
					return;

				didComplete = true;
				completed(new PermissionRequestResult(permission, outcome));
			}

			PermissionCallbacks callbacks = new();
			callbacks.PermissionGranted +=
				permission => Complete(permission, PermissionRequestOutcome.Granted);
			callbacks.PermissionDenied +=
				permission => Complete(permission, PermissionRequestOutcome.Denied);
			callbacks.PermissionRequestDismissed +=
				permission => Complete(permission, PermissionRequestOutcome.Dismissed);

			Permission.RequestUserPermission(check.permission, callbacks);
#else
			completed(new PermissionRequestResult(
				check.permission,
				PermissionRequestOutcome.NotRequired));
#endif
		}

		private static PermissionAvailability CheckPermissionAvailability(string permission)
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			if (permissionAvailabilityCache.TryGetValue(
				    permission,
				    out PermissionAvailability cachedAvailability))
				return cachedAvailability;

			PermissionAvailability availability = QueryPermissionAvailability(permission);

			// Unknown can be transient (for example, a JNI problem), so do not cache it.
			if (availability != PermissionAvailability.Unknown)
				permissionAvailabilityCache.Add(permission, availability);

			return availability;
#else
			return PermissionAvailability.NotRequired;
#endif
		}

#if UNITY_ANDROID && !UNITY_EDITOR
		private static PermissionAvailability QueryPermissionAvailability(string permission)
		{
			try
			{
				using AndroidJavaClass unityPlayer =
					new("com.unity3d.player.UnityPlayer");
				using AndroidJavaObject activity =
					unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
				using AndroidJavaObject packageManager =
					activity.Call<AndroidJavaObject>("getPackageManager");
				using AndroidJavaObject permissionInfo =
					packageManager.Call<AndroidJavaObject>("getPermissionInfo", permission, 0);

				return permissionInfo == null
					? PermissionAvailability.Unavailable
					: PermissionAvailability.Available;
			}
			catch (AndroidJavaException exception)
			{
				return IsNameNotFound(exception)
					? PermissionAvailability.Unavailable
					: PermissionAvailability.Unknown;
			}
			catch
			{
				return PermissionAvailability.Unknown;
			}
		}

		private static bool IsNameNotFound(AndroidJavaException exception)
		{
			return exception.ToString().IndexOf(NameNotFoundException, StringComparison.Ordinal) >= 0;
		}
#endif

		private static void ValidateCandidates(string[] orderedCandidates)
		{
			if (orderedCandidates == null)
				throw new ArgumentNullException(nameof(orderedCandidates));

			if (orderedCandidates.Length == 0)
				throw new ArgumentException(
					"At least one permission candidate is required.",
					nameof(orderedCandidates));

			foreach (string permission in orderedCandidates)
				ValidatePermission(permission);
		}

		private static void ValidatePermission(string permission)
		{
			if (string.IsNullOrWhiteSpace(permission))
				throw new ArgumentException(
					"Permission IDs cannot be null or whitespace.",
					nameof(permission));
		}
	}
}
