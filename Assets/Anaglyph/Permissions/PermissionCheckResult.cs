using System;

namespace Anaglyph.Permissions
{
	public interface IGameCapabilityCheckResult
	{
		GameCapability capability { get; }
		CapabilitySupport support { get; }
		PermissionAuthorization authorization { get; }
		bool isReady { get; }
	}

	public enum GameCapability
	{
		Scene,
		PassthroughCamera,
		SharedSpatialAnchors,
		BodyTracking
	}

	public enum CapabilitySupport
	{
		Unknown,
		Unsupported,
		Supported
	}

	public enum PermissionAuthorization
	{
		Unknown,
		NotRequired,
		Denied,
		Granted
	}

	/// <summary>
	/// Whether an Android permission ID is recognized by the current operating system.
	/// </summary>
	public enum PermissionAvailability
	{
		Unknown,
		NotRequired,
		Unavailable,
		Available
	}

	public enum PermissionRequestOutcome
	{
		NotRequired,
		AlreadyGranted,
		Granted,
		Denied,
		Dismissed,
		Unavailable,
		AvailabilityUnknown
	}

	public enum CapabilityConfiguration
	{
		Unknown,
		Disabled,
		Enabled
	}

	/// <summary>
	/// Meta calls the VPS / "Share Point Cloud Data" setting Enhanced Spatial Services.
	/// Its state cannot be read directly, so it remains Unknown until a shared-anchor
	/// operation succeeds or returns the cloud-storage-disabled OpenXR error.
	/// </summary>
	public enum VpsStatus
	{
		Unknown,
		Disabled,
		Enabled
	}

	public readonly struct AndroidPermissionCheckResult
	{
		public string permission { get; }
		public PermissionAvailability availability { get; }
		public PermissionAuthorization authorization { get; }

		public AndroidPermissionCheckResult(
			string permission,
			PermissionAvailability availability,
			PermissionAuthorization authorization)
		{
			this.permission = permission;
			this.availability = availability;
			this.authorization = authorization;
		}

		public override string ToString() =>
			$"{permission ?? "(none)"}: availability={availability}, authorization={authorization}";
	}

	public readonly struct PermissionRequestResult
	{
		public string permission { get; }
		public PermissionRequestOutcome outcome { get; }

		public PermissionAuthorization authorization =>
			outcome switch
			{
				PermissionRequestOutcome.NotRequired => PermissionAuthorization.NotRequired,
				PermissionRequestOutcome.AlreadyGranted or
					PermissionRequestOutcome.Granted => PermissionAuthorization.Granted,
				PermissionRequestOutcome.Denied or
					PermissionRequestOutcome.Dismissed => PermissionAuthorization.Denied,
				_ => PermissionAuthorization.Unknown
			};

		public bool wasGranted =>
			authorization is PermissionAuthorization.Granted or PermissionAuthorization.NotRequired;

		public PermissionRequestResult(string permission, PermissionRequestOutcome outcome)
		{
			this.permission = permission;
			this.outcome = outcome;
		}

		public override string ToString() =>
			$"{permission ?? "(none)"}: request={outcome}, authorization={authorization}";
	}

	public readonly struct PermissionCheckResult : IGameCapabilityCheckResult
	{
		public GameCapability capability { get; }
		public CapabilitySupport support { get; }
		public PermissionAuthorization authorization { get; }

		public bool isReady =>
			support == CapabilitySupport.Supported &&
			authorization is PermissionAuthorization.Granted or PermissionAuthorization.NotRequired;

		public PermissionCheckResult(
			GameCapability capability,
			CapabilitySupport support,
			PermissionAuthorization authorization)
		{
			this.capability = capability;
			this.support = support;
			this.authorization = authorization;
		}

		public override string ToString() =>
			$"{capability}: support={support}, authorization={authorization}";
	}

	public readonly struct PassthroughCameraCheckResult : IGameCapabilityCheckResult
	{
		public GameCapability capability => GameCapability.PassthroughCamera;
		public CapabilitySupport support { get; }
		public CapabilityConfiguration configuration { get; }
		public AndroidPermissionCheckResult requiredPermission { get; }
		public PermissionAvailability androidCameraAvailability { get; }
		public PermissionAvailability headsetCameraAvailability { get; }
		public PermissionAuthorization androidCameraAuthorization { get; }
		public PermissionAuthorization headsetCameraAuthorization { get; }

		/// <summary>
		/// Authorization of the highest-priority permission available on this system.
		/// HEADSET_CAMERA is preferred; the broader Android CAMERA permission is only
		/// selected when HEADSET_CAMERA is unavailable.
		/// </summary>
		public PermissionAuthorization authorization => requiredPermission.authorization;

		public bool isReady =>
			support == CapabilitySupport.Supported &&
			configuration == CapabilityConfiguration.Enabled &&
			authorization is PermissionAuthorization.Granted or PermissionAuthorization.NotRequired;

		public PassthroughCameraCheckResult(
			CapabilitySupport support,
			CapabilityConfiguration configuration,
			AndroidPermissionCheckResult requiredPermission,
			AndroidPermissionCheckResult androidCamera,
			AndroidPermissionCheckResult headsetCamera)
		{
			this.support = support;
			this.configuration = configuration;
			this.requiredPermission = requiredPermission;
			androidCameraAvailability = androidCamera.availability;
			headsetCameraAvailability = headsetCamera.availability;
			androidCameraAuthorization = androidCamera.authorization;
			headsetCameraAuthorization = headsetCamera.authorization;
		}

		public override string ToString() =>
			$"{GameCapability.PassthroughCamera}: support={support}, configuration={configuration}, " +
			$"required={requiredPermission}, " +
			$"androidCamera=({androidCameraAvailability}, {androidCameraAuthorization}), " +
			$"headsetCamera=({headsetCameraAvailability}, {headsetCameraAuthorization})";
	}

	public readonly struct SharedSpatialAnchorsCheckResult : IGameCapabilityCheckResult
	{
		public GameCapability capability => GameCapability.SharedSpatialAnchors;
		public CapabilitySupport support { get; }
		public VpsStatus vps { get; }

		/// <summary>
		/// Permission-like projection of Enhanced Spatial Services for a common gate UI.
		/// Disabled maps to Denied, even though it must be changed in system settings
		/// rather than requested through Android's runtime permission API.
		/// </summary>
		public PermissionAuthorization authorization =>
			vps switch
			{
				VpsStatus.Enabled => PermissionAuthorization.Granted,
				VpsStatus.Disabled => PermissionAuthorization.Denied,
				_ => PermissionAuthorization.Unknown
			};

		public bool isReady =>
			support == CapabilitySupport.Supported &&
			authorization == PermissionAuthorization.Granted;

		public SharedSpatialAnchorsCheckResult(CapabilitySupport support, VpsStatus vps)
		{
			this.support = support;
			this.vps = vps;
		}

		public override string ToString() =>
			$"{GameCapability.SharedSpatialAnchors}: support={support}, vps={vps}";
	}
}
