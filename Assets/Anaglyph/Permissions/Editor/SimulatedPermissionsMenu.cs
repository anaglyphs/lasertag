using UnityEditor;

namespace Anaglyph.Permissions.Editor
{
	[InitializeOnLoad]
	internal static class SimulatedPermissionsMenu
	{
		private const string MenuRoot = "Lasertag/Simulated Permissions/";
		private const string SessionKeyRoot = "Anaglyph.Lasertag.SimulatedPermissions.";

		private const string EnableMenu = MenuRoot + "Enable Simulation";
		private const string SceneGrantedMenu = MenuRoot + "Scene/Granted";
		private const string HeadsetAvailableMenu =
			MenuRoot + "Passthrough Camera/Headset Permission Available";
		private const string HeadsetGrantedMenu =
			MenuRoot + "Passthrough Camera/Headset Permission Granted";
		private const string AndroidAvailableMenu =
			MenuRoot + "Passthrough Camera/Android Fallback Available";
		private const string AndroidGrantedMenu =
			MenuRoot + "Passthrough Camera/Android Fallback Granted";
		private const string VpsKnownMenu = MenuRoot + "VPS/Status Known";
		private const string VpsEnabledMenu = MenuRoot + "VPS/Enabled";
		private const string GrantRequestsMenu = MenuRoot + "Requests/Grant Requests";

		private const string EnableKey = SessionKeyRoot + "Enabled";
		private const string SceneGrantedKey = SessionKeyRoot + "SceneGranted";
		private const string HeadsetAvailableKey = SessionKeyRoot + "HeadsetAvailable";
		private const string HeadsetGrantedKey = SessionKeyRoot + "HeadsetGranted";
		private const string AndroidAvailableKey = SessionKeyRoot + "AndroidAvailable";
		private const string AndroidGrantedKey = SessionKeyRoot + "AndroidGranted";
		private const string VpsKnownKey = SessionKeyRoot + "VpsKnown";
		private const string VpsEnabledKey = SessionKeyRoot + "VpsEnabled";
		private const string GrantRequestsKey = SessionKeyRoot + "GrantRequests";

		static SimulatedPermissionsMenu()
		{
			EditorPermissionSimulation.permissionGrantedChanged +=
				OnPermissionGrantedChanged;
			ApplySessionState();
		}

		[MenuItem(EnableMenu, false, 0)]
		private static void ToggleSimulation()
		{
			SetBool(
				EnableKey,
				!GetBool(EnableKey, false));
			ApplySessionState();
		}

		[MenuItem(EnableMenu, true)]
		private static bool ValidateSimulation()
		{
			return SetChecked(EnableMenu, EditorPermissionSimulation.enabled);
		}

		[MenuItem(SceneGrantedMenu, false, 20)]
		private static void ToggleSceneGranted()
		{
			EditorPermissionSimulation.SetPermissionGranted(
				MetaPermissionChecks.ScenePermission,
				!EditorPermissionSimulation.IsPermissionGranted(
					MetaPermissionChecks.ScenePermission));
		}

		[MenuItem(SceneGrantedMenu, true)]
		private static bool ValidateSceneGranted()
		{
			return SetChecked(
				SceneGrantedMenu,
				EditorPermissionSimulation.IsPermissionGranted(
					MetaPermissionChecks.ScenePermission));
		}

		[MenuItem(HeadsetAvailableMenu, false, 40)]
		private static void ToggleHeadsetAvailable()
		{
			bool available = !EditorPermissionSimulation.IsPermissionAvailable(
				MetaPermissionChecks.HeadsetCameraPermission);
			EditorPermissionSimulation.SetPermissionAvailable(
				MetaPermissionChecks.HeadsetCameraPermission,
				available);
			SetBool(HeadsetAvailableKey, available);
		}

		[MenuItem(HeadsetAvailableMenu, true)]
		private static bool ValidateHeadsetAvailable()
		{
			return SetChecked(
				HeadsetAvailableMenu,
				EditorPermissionSimulation.IsPermissionAvailable(
					MetaPermissionChecks.HeadsetCameraPermission));
		}

		[MenuItem(HeadsetGrantedMenu, false, 41)]
		private static void ToggleHeadsetGranted()
		{
			EditorPermissionSimulation.SetPermissionGranted(
				MetaPermissionChecks.HeadsetCameraPermission,
				!EditorPermissionSimulation.IsPermissionGranted(
					MetaPermissionChecks.HeadsetCameraPermission));
		}

		[MenuItem(HeadsetGrantedMenu, true)]
		private static bool ValidateHeadsetGranted()
		{
			return SetChecked(
				HeadsetGrantedMenu,
				EditorPermissionSimulation.IsPermissionGranted(
					MetaPermissionChecks.HeadsetCameraPermission));
		}

		[MenuItem(AndroidAvailableMenu, false, 42)]
		private static void ToggleAndroidAvailable()
		{
			bool available = !EditorPermissionSimulation.IsPermissionAvailable(
				MetaPermissionChecks.AndroidCameraPermission);
			EditorPermissionSimulation.SetPermissionAvailable(
				MetaPermissionChecks.AndroidCameraPermission,
				available);
			SetBool(AndroidAvailableKey, available);
		}

		[MenuItem(AndroidAvailableMenu, true)]
		private static bool ValidateAndroidAvailable()
		{
			return SetChecked(
				AndroidAvailableMenu,
				EditorPermissionSimulation.IsPermissionAvailable(
					MetaPermissionChecks.AndroidCameraPermission));
		}

		[MenuItem(AndroidGrantedMenu, false, 43)]
		private static void ToggleAndroidGranted()
		{
			EditorPermissionSimulation.SetPermissionGranted(
				MetaPermissionChecks.AndroidCameraPermission,
				!EditorPermissionSimulation.IsPermissionGranted(
					MetaPermissionChecks.AndroidCameraPermission));
		}

		[MenuItem(AndroidGrantedMenu, true)]
		private static bool ValidateAndroidGranted()
		{
			return SetChecked(
				AndroidGrantedMenu,
				EditorPermissionSimulation.IsPermissionGranted(
					MetaPermissionChecks.AndroidCameraPermission));
		}

		[MenuItem(VpsKnownMenu, false, 60)]
		private static void ToggleVpsKnown()
		{
			SetBool(VpsKnownKey, !GetBool(VpsKnownKey, true));
			ApplyVpsState();
		}

		[MenuItem(VpsKnownMenu, true)]
		private static bool ValidateVpsKnown()
		{
			return SetChecked(VpsKnownMenu, GetBool(VpsKnownKey, true));
		}

		[MenuItem(VpsEnabledMenu, false, 61)]
		private static void ToggleVpsEnabled()
		{
			SetBool(VpsEnabledKey, !GetBool(VpsEnabledKey, false));
			ApplyVpsState();
		}

		[MenuItem(VpsEnabledMenu, true)]
		private static bool ValidateVpsEnabled()
		{
			return SetChecked(VpsEnabledMenu, GetBool(VpsEnabledKey, false));
		}

		[MenuItem(GrantRequestsMenu, false, 80)]
		private static void ToggleGrantRequests()
		{
			bool grantRequests = !GetBool(GrantRequestsKey, true);
			SetBool(GrantRequestsKey, grantRequests);
			EditorPermissionSimulation.grantRequests = grantRequests;
		}

		[MenuItem(GrantRequestsMenu, true)]
		private static bool ValidateGrantRequests()
		{
			return SetChecked(
				GrantRequestsMenu,
				EditorPermissionSimulation.grantRequests);
		}

		private static void ApplySessionState()
		{
			EditorPermissionSimulation.enabled = GetBool(EnableKey, false);
			EditorPermissionSimulation.grantRequests =
				GetBool(GrantRequestsKey, true);

			EditorPermissionSimulation.SetPermissionAvailable(
				MetaPermissionChecks.ScenePermission,
				true);
			EditorPermissionSimulation.SetPermissionGranted(
				MetaPermissionChecks.ScenePermission,
				GetBool(SceneGrantedKey, false));

			EditorPermissionSimulation.SetPermissionAvailable(
				MetaPermissionChecks.HeadsetCameraPermission,
				GetBool(HeadsetAvailableKey, true));
			EditorPermissionSimulation.SetPermissionGranted(
				MetaPermissionChecks.HeadsetCameraPermission,
				GetBool(HeadsetGrantedKey, false));

			EditorPermissionSimulation.SetPermissionAvailable(
				MetaPermissionChecks.AndroidCameraPermission,
				GetBool(AndroidAvailableKey, true));
			EditorPermissionSimulation.SetPermissionGranted(
				MetaPermissionChecks.AndroidCameraPermission,
				GetBool(AndroidGrantedKey, false));

			ApplyVpsState();
		}

		private static void ApplyVpsState()
		{
			EditorPermissionSimulation.vpsStatus =
				!GetBool(VpsKnownKey, true)
					? VpsStatus.Unknown
					: GetBool(VpsEnabledKey, false)
						? VpsStatus.Enabled
						: VpsStatus.Disabled;
		}

		private static void OnPermissionGrantedChanged(string permission, bool granted)
		{
			switch (permission)
			{
				case MetaPermissionChecks.ScenePermission:
					SetBool(SceneGrantedKey, granted);
					break;

				case MetaPermissionChecks.HeadsetCameraPermission:
					SetBool(HeadsetGrantedKey, granted);
					break;

				case MetaPermissionChecks.AndroidCameraPermission:
					SetBool(AndroidGrantedKey, granted);
					break;
			}
		}

		private static bool GetBool(string key, bool defaultValue)
		{
			return SessionState.GetBool(key, defaultValue);
		}

		private static void SetBool(string key, bool value)
		{
			SessionState.SetBool(key, value);
		}

		private static bool SetChecked(string menuPath, bool isChecked)
		{
			Menu.SetChecked(menuPath, isChecked);
			return true;
		}
	}
}
