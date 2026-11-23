using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>
	/// Every iPhone build, however started: ARKit as iOS's XR runtime (without it the app starts
	/// as the desktop operator), the usage text iOS shows when it asks for the local network or Bluetooth,
	/// the hotspot entitlement joining a Wi-Fi Direct session needs, and, once Apple has granted it to the team,
	/// the multicast entitlement session discovery needs.
	/// </summary>
	public class IosSetup : IPreprocessBuildWithReport, IPostprocessBuildWithReport
	{
		private const string EntitlementGrantedPref = "Anaglyph.iOS.MulticastEntitlementGranted";
		private const string EntitlementMenu = "Lasertag/Mobile/iOS Team Has Multicast Entitlement";

		private const string ARKitLoader = "UnityEngine.XR.ARKit.ARKitLoader";
		private const string GizmosDefine = "XR_GIZMOS";
		private const string MulticastEntitlement = "com.apple.developer.networking.multicast";
		private const string HotspotEntitlement = "com.apple.developer.networking.HotspotConfiguration";
		private const string EntitlementsPath = "Unity-iPhone/Lasertag.entitlements";
		private const string CameraUsage = "Lasertag sees the room through the camera to line up players and map walls.";
		private const string LocalNetworkUsage = "Lasertag finds and joins games with players on the same Wi-Fi.";
		private const string BluetoothUsage = "Lasertag finds and joins games with players near you.";
		private const string PhotoLibraryAddUsage = "Lasertag saves the videos you record in camera mode to your photo library.";
		private const string OutputPath = "Builds/iOS";

		public int callbackOrder => -100;

		/// <summary>
		/// Apple grants com.apple.developer.networking.multicast on request, and never to a free
		/// team: asking for it ungranted fails signing. Without it the app runs but hears no hosts.
		/// </summary>
		private static bool MulticastEntitlementGranted
		{
			get => EditorPrefs.GetBool(EntitlementGrantedPref, false);
			set => EditorPrefs.SetBool(EntitlementGrantedPref, value);
		}

		[MenuItem(EntitlementMenu)]
		private static void ToggleEntitlement() => MulticastEntitlementGranted = !MulticastEntitlementGranted;

		[MenuItem(EntitlementMenu, true)]
		private static bool ShowEntitlement()
		{
			Menu.SetChecked(EntitlementMenu, MulticastEntitlementGranted);
			return true;
		}

		public void OnPreprocessBuild(BuildReport report)
		{
			if (report.summary.platform != BuildTarget.iOS) return;
			SetUp();
#if !UNITY_XR_ARKIT_LOADER_ENABLED
			// in batch mode ARKit includes its native library only when editor code compiled with this define
			if (Application.isBatchMode)
				throw new BuildFailedException("UNITY_XR_ARKIT_LOADER_ENABLED isn't compiled into the editor, so the ARKit library " +
					"would be left out and Xcode would fail to link. Activate the iOS build profile, relaunch Unity, then build.");
#endif
		}

		[MenuItem("Lasertag/Mobile/Set Up iOS")]
		public static void SetUp()
		{
			if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget))
			{
				Debug.LogError($"[{nameof(IosSetup)}] XR Plug-in Management has no settings yet");
				return;
			}

			if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.iOS)) perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.iOS);
			if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.iOS)) perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.iOS);
			XRManagerSettings manager = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.iOS).Manager;
			XRPackageMetadataStore.AssignLoader(manager, ARKitLoader, BuildTargetGroup.iOS);
			XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.iOS).InitManagerOnStart = true;

			// debug drawing (XRGizmos) compiles away without it, as Android's defines have it
			string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.iOS);
			if (!defines.Split(';').Contains(GizmosDefine))
				PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.iOS, string.IsNullOrEmpty(defines) ? GizmosDefine : defines + ";" + GizmosDefine);

			PlayerSettings.iOS.cameraUsageDescription = CameraUsage;
			if (string.IsNullOrEmpty(PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS)))
				PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.trev3d.lasertagphone");
			SetIcons();
			EditorUtility.SetDirty(perTarget);
			AssetDatabase.SaveAssets();
			Debug.Log($"[{nameof(IosSetup)}] iOS uses ARKit");
		}

		private const string ProfilePath = "Assets/Settings/Build Profiles/iOS.asset";
		/// <summary>Opaque and 1024 square, as the App Store's own icon must be; every iOS size is made from it.</summary>
		private const string IconPath = "Assets/Settings/Build Profiles/iOS/AppIcon.png";

		private static void SetIcons()
		{
			Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
			if (icon == null) return;
			foreach (PlatformIconKind kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.iOS))
			{
				PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.iOS, kind);
				foreach (PlatformIcon slot in icons) slot.SetTexture(icon);
				PlayerSettings.SetPlatformIcons(NamedBuildTarget.iOS, kind, icons);
			}
		}

		[MenuItem("Lasertag/Mobile/Build iOS Xcode Project")]
		public static void Build() => BuildXcodeProject();

		/// <summary>
		/// The Xcode project for a TestFlight upload: a release build under <paramref name="bundleId"/>,
		/// signed automatically by <paramref name="teamId"/>, with a build number newer than any before it.
		/// A development build keeps the debug console, development-only tools and script debugging.
		/// </summary>
		public static BuildReport BuildForTestFlight(string teamId, string bundleId, bool development = false)
		{
			PlayerSettings.iOS.appleDeveloperTeamID = teamId;
			PlayerSettings.iOS.appleEnableAutomaticSigning = true;
			PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, bundleId);
			PlayerSettings.iOS.buildNumber = System.DateTime.UtcNow.ToString("yyyyMMddHHmm");
			AssetDatabase.SaveAssets();
			return BuildXcodeProject(development ? BuildOptions.Development : BuildOptions.None);
		}

		/// <summary>For <c>-executeMethod</c> in batch mode: exits 0 when the Xcode project was written.</summary>
		public static void BuildFromCommandLine()
		{
			BuildReport report = BuildXcodeProject();
			EditorApplication.Exit(report != null && report.summary.result == BuildResult.Succeeded ? 0 : 1);
		}

		private static BuildReport BuildXcodeProject(BuildOptions options = BuildOptions.Development)
		{
			BuildProfile profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(ProfilePath);
			BuildReport report = profile != null
				? BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions { buildProfile = profile, locationPathName = OutputPath, options = options })
				: BuildPipeline.BuildPlayer(new BuildPlayerOptions
				{
					scenes = System.Array.ConvertAll(EditorBuildSettings.scenes, scene => scene.path),
					target = BuildTarget.iOS,
					locationPathName = OutputPath,
					options = options,
				});
			Debug.Log($"[{nameof(IosSetup)}] {report.summary.result}, {report.summary.totalErrors} errors, at {Path.GetFullPath(OutputPath)}");
			return report;
		}

		public void OnPostprocessBuild(BuildReport report)
		{
			if (report.summary.platform != BuildTarget.iOS) return;
#if UNITY_IOS
			string output = report.summary.outputPath;
			string plistPath = Path.Combine(output, "Info.plist");
			PlistDocument plist = new();
			plist.ReadFromFile(plistPath);
			plist.root.SetString("NSLocalNetworkUsageDescription", LocalNetworkUsage);
			plist.root.SetString("NSBluetoothAlwaysUsageDescription", BluetoothUsage);
			plist.root.SetString("NSPhotoLibraryAddUsageDescription", PhotoLibraryAddUsage);
			// only the system's standard encryption (HTTPS): exempt from export paperwork, so TestFlight asks nothing
			plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
			plist.WriteToFile(plistPath);

			string frameworkProjectPath = PBXProject.GetPBXProjectPath(output);
			PBXProject frameworkProject = new();
			frameworkProject.ReadFromFile(frameworkProjectPath);
			frameworkProject.AddFrameworkToProject(frameworkProject.GetUnityFrameworkTargetGuid(), "Photos.framework", false);
			frameworkProject.AddFrameworkToProject(frameworkProject.GetUnityFrameworkTargetGuid(), "CoreHaptics.framework", false);
			frameworkProject.AddFrameworkToProject(frameworkProject.GetUnityFrameworkTargetGuid(), "CoreBluetooth.framework", false);
			frameworkProject.AddFrameworkToProject(frameworkProject.GetUnityFrameworkTargetGuid(), "NetworkExtension.framework", false);
			frameworkProject.WriteToFile(frameworkProjectPath);

			string entitlementsFile = Path.Combine(output, EntitlementsPath);
			PlistDocument entitlements = new();
			if (File.Exists(entitlementsFile)) entitlements.ReadFromFile(entitlementsFile);
			entitlements.root.SetBoolean(HotspotEntitlement, true);
			if (MulticastEntitlementGranted) entitlements.root.SetBoolean(MulticastEntitlement, true);
			entitlements.WriteToFile(entitlementsFile);

			string projectPath = PBXProject.GetPBXProjectPath(output);
			PBXProject project = new();
			project.ReadFromFile(projectPath);
			string target = project.GetUnityMainTargetGuid();
			project.AddFile(EntitlementsPath, Path.GetFileName(EntitlementsPath));
			project.SetBuildProperty(target, "CODE_SIGN_ENTITLEMENTS", EntitlementsPath);
			project.WriteToFile(projectPath);
#endif
		}
	}
}
