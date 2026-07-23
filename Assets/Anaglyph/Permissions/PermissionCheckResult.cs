namespace Anaglyph.Permissions
{
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

	public readonly struct PermissionCheckResult
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

	public readonly struct PassthroughCameraCheckResult
	{
		public CapabilitySupport support { get; }
		public CapabilityConfiguration configuration { get; }
		public PermissionAuthorization androidCameraAuthorization { get; }
		public PermissionAuthorization headsetCameraAuthorization { get; }

		/// <summary>
		/// Horizon OS accepts either the broad Android camera permission or Meta's
		/// passthrough-only headset camera permission.
		/// </summary>
		public PermissionAuthorization authorization
		{
			get
			{
				if (androidCameraAuthorization == PermissionAuthorization.Granted ||
				    headsetCameraAuthorization == PermissionAuthorization.Granted)
					return PermissionAuthorization.Granted;

				if (androidCameraAuthorization == PermissionAuthorization.NotRequired &&
				    headsetCameraAuthorization == PermissionAuthorization.NotRequired)
					return PermissionAuthorization.NotRequired;

				if (androidCameraAuthorization == PermissionAuthorization.Unknown ||
				    headsetCameraAuthorization == PermissionAuthorization.Unknown)
					return PermissionAuthorization.Unknown;

				return PermissionAuthorization.Denied;
			}
		}

		public bool isReady =>
			support == CapabilitySupport.Supported &&
			configuration == CapabilityConfiguration.Enabled &&
			authorization is PermissionAuthorization.Granted or PermissionAuthorization.NotRequired;

		public PassthroughCameraCheckResult(
			CapabilitySupport support,
			CapabilityConfiguration configuration,
			PermissionAuthorization androidCameraAuthorization,
			PermissionAuthorization headsetCameraAuthorization)
		{
			this.support = support;
			this.configuration = configuration;
			this.androidCameraAuthorization = androidCameraAuthorization;
			this.headsetCameraAuthorization = headsetCameraAuthorization;
		}

		public override string ToString() =>
			$"{GameCapability.PassthroughCamera}: support={support}, configuration={configuration}, " +
			$"androidCamera={androidCameraAuthorization}, headsetCamera={headsetCameraAuthorization}";
	}

	public readonly struct SharedSpatialAnchorsCheckResult
	{
		public CapabilitySupport support { get; }
		public VpsStatus vps { get; }

		public bool isReady =>
			support == CapabilitySupport.Supported &&
			vps == VpsStatus.Enabled;

		public SharedSpatialAnchorsCheckResult(CapabilitySupport support, VpsStatus vps)
		{
			this.support = support;
			this.vps = vps;
		}

		public override string ToString() =>
			$"{GameCapability.SharedSpatialAnchors}: support={support}, vps={vps}";
	}
}
