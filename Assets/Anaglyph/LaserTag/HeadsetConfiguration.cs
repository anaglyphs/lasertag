using System;
using System.Security.Cryptography;
using System.Text;
using Anaglyph.Netcode;
using Anaglyph.Netcode.SyncVariables;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Localization;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Stores and synchronizes operator policy and the headset's local opt-out.
	/// SessionConnectionController consumes this policy to manage automatic connections.
	/// </summary>
	[DefaultExecutionOrder(-100)]
	public sealed class HeadsetConfiguration : MonoBehaviour
	{
		public enum DeviceRole { Headset, Operator }

		[SerializeField] private DeviceRole role;
		private bool IsOperator => role == DeviceRole.Operator;

		/// <summary>An operator machine runs the session but never plays or aligns itself.</summary>
		public static bool IsOperatorDevice => Instance != null && Instance.IsOperator;

		/// <summary>
		/// Whether an operator machine runs the session this device is in. An operator publishes
		/// its policy the moment the bus activates, so a client reads this off the same value.
		/// </summary>
		public static bool SessionIsOperatorManaged => Instance != null &&
			(Instance.IsOperator ||
			 (SyncBus.Active && Instance.policySync.Value.operatorManaged != 0));

		public readonly struct OperatorSettings
		{
			public readonly bool requireMenuPassword;
			public readonly bool hasMenuPassword;
			public readonly bool pinHeadsetsToHost;
			public readonly string hostAddress;

			public OperatorSettings(bool requireMenuPassword, bool hasMenuPassword,
				bool pinHeadsetsToHost, string hostAddress)
			{
				this.requireMenuPassword = requireMenuPassword;
				this.hasMenuPassword = hasMenuPassword;
				this.pinHeadsetsToHost = pinHeadsetsToHost;
				this.hostAddress = hostAddress;
			}
		}

		private struct SyncedPolicy
		{
			public Guid revision;
			public byte operatorManaged;
			public byte requireMenuPassword;
			public byte pinHeadsetsToHost;
			public FixedString128Bytes menuPasswordHash;
			public FixedString64Bytes hostAddress;
		}

		private const string OperatorPasswordRequiredPref =
			"HeadsetConfiguration.Operator.PasswordRequired";
		private const string OperatorPasswordHashPref =
			"HeadsetConfiguration.Operator.PasswordHash";
		private const string OperatorPinToHostPref =
			"HeadsetConfiguration.Operator.PinToHost";
		private const string HeadsetPasswordRequiredPref =
			"HeadsetConfiguration.Headset.PasswordRequired";
		private const string HeadsetPasswordHashPref =
			"HeadsetConfiguration.Headset.PasswordHash";
		private const string HeadsetPinnedHostPref =
			"HeadsetConfiguration.Headset.PinnedHost";
		private const string HeadsetDeclinedHostPref =
			"HeadsetConfiguration.Headset.DeclinedHost";
		private const string ProvisioningHostPref = "HeadsetConfiguration.Headset.ProvisioningHost";
		private const string DeclinedProvisioningHostPref = "HeadsetConfiguration.Headset.DeclinedProvisioningHost";
		private const string OperatorRevisionPref = "HeadsetConfiguration.Operator.Revision";
		private const string HeadsetRevisionPref = "HeadsetConfiguration.Headset.Revision";
		private const string DeclinedRevisionPref = "HeadsetConfiguration.Headset.DeclinedRevision";

		private readonly SyncVariable<SyncedPolicy> policySync =
			new("lasertag.headset-configuration");

		private bool requireMenuPassword;
		private string menuPasswordHash = "";
		private bool pinHeadsetsToHost;
		private string pinnedHostAddress = "";
		private string declinedHostAddress = "";
		private string provisioningHostAddress = "";
		private string declinedProvisioningHostAddress = "";
		private Guid provisioningRevision;
		private Guid declinedRevision;

		public static HeadsetConfiguration Instance { get; private set; }

		public static bool MenuPasswordRequired =>
			Instance != null && !Instance.IsOperator && Instance.requireMenuPassword &&
			!string.IsNullOrEmpty(Instance.menuPasswordHash);

		public static bool PinnedHostEnabled =>
			Instance != null && !Instance.IsOperator && Instance.pinHeadsetsToHost &&
			!string.IsNullOrEmpty(Instance.pinnedHostAddress);

		public static bool IsProvisioned => MenuPasswordRequired || PinnedHostEnabled;

		public static string PinnedHostAddress => PinnedHostEnabled
			? Instance.pinnedHostAddress
			: "";

		public static event Action Changed = delegate { };
		public static event Action MenuAccessChanged = delegate { };

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Init()
		{
			Instance = null;
			Changed = delegate { };
			MenuAccessChanged = delegate { };
		}

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Debug.LogError($"[{nameof(HeadsetConfiguration)}] More than one instance was created.");
				enabled = false;
				return;
			}

			Instance = this;
			LoadLocalSettings();

			policySync.Validate = (_, _) => false;
			policySync.Changed += OnSyncedPolicyChanged;
			policySync.Synced += OnPolicySynced;
			policySync.Register();
			SyncBus.Activated += OnSyncBusActivated;
		}

		private void OnDestroy()
		{
			if (Instance != this)
				return;

			SyncBus.Activated -= OnSyncBusActivated;
			policySync.Synced -= OnPolicySynced;
			policySync.Changed -= OnSyncedPolicyChanged;
			policySync.Unregister();
			policySync.Validate = null;
			Instance = null;
		}

		public OperatorSettings GetOperatorSettings()
		{
			if (!IsOperator)
				return default;

			return new OperatorSettings(
				requireMenuPassword,
				!string.IsNullOrEmpty(menuPasswordHash),
				pinHeadsetsToHost,
				NetcodeManagement.GetLocalIPv4() ?? "");
		}

		/// <summary>
		/// Saves the desktop operator's choices and immediately publishes them when a
		/// session is active. An empty password preserves an existing password hash.
		/// </summary>
		public bool TrySetOperatorSettings(bool requirePassword, string newPassword,
			bool pinToHost, out LocalizedString error)
		{
			error = null;
			if (!IsOperator)
			{
				error = MenuCopy.String("Operator", "configuration.operator-only");
				return false;
			}

			string hash = menuPasswordHash;
			if (!string.IsNullOrEmpty(newPassword))
				hash = HashPassword(newPassword);

			if (requirePassword && string.IsNullOrEmpty(hash))
			{
				error = MenuCopy.String("Operator", "configuration.password-required");
				return false;
			}

			string hostAddress = NetcodeManagement.GetLocalIPv4() ?? "";
			if (pinToHost && string.IsNullOrWhiteSpace(hostAddress))
			{
				error = MenuCopy.String("Operator", "configuration.address-required");
				return false;
			}

			requireMenuPassword = requirePassword;
			menuPasswordHash = requirePassword ? hash : "";
			pinHeadsetsToHost = pinToHost;
			// Every explicit Apply is a new provisioning request, even if its settings
			// are identical. Passive snapshots and host restarts keep this revision.
			provisioningRevision = Guid.NewGuid();

			PlayerPrefs.SetInt(OperatorPasswordRequiredPref, requirePassword ? 1 : 0);
			PlayerPrefs.SetString(OperatorPasswordHashPref, menuPasswordHash);
			PlayerPrefs.SetInt(OperatorPinToHostPref, pinToHost ? 1 : 0);
			PlayerPrefs.SetString(OperatorRevisionPref, provisioningRevision.ToString("N"));
			PlayerPrefs.Save();

			PublishOperatorPolicy();
			Changed.Invoke();
			return true;
		}

		public bool CheckMenuPassword(string password)
		{
			if (!MenuPasswordRequired)
				return true;

			return FixedTimeEquals(menuPasswordHash, HashPassword(password ?? ""));
		}

		/// <summary>
		/// Removes this headset's password and automatic connection settings. Ignore
		/// that policy revision until the operator explicitly applies settings again.
		/// </summary>
		public void Unprovision()
		{
			if (IsOperator || !IsProvisioned)
				return;

			declinedProvisioningHostAddress = provisioningHostAddress;
			declinedRevision = provisioningRevision;
			if (string.IsNullOrEmpty(declinedProvisioningHostAddress))
				declinedProvisioningHostAddress = pinnedHostAddress;
			bool passwordChanged = MenuPasswordRequired;
			requireMenuPassword = false;
			menuPasswordHash = "";
			pinHeadsetsToHost = false;
			pinnedHostAddress = "";
			provisioningHostAddress = "";
			declinedHostAddress = "";

			SaveHeadsetSettings();

			Changed.Invoke();
			if (passwordChanged) MenuAccessChanged.Invoke();
		}

		private void LoadLocalSettings()
		{
			if (IsOperator)
			{
				requireMenuPassword = PlayerPrefs.GetInt(OperatorPasswordRequiredPref, 0) != 0;
				menuPasswordHash = PlayerPrefs.GetString(OperatorPasswordHashPref, "");
				pinHeadsetsToHost = PlayerPrefs.GetInt(OperatorPinToHostPref, 0) != 0;
				Guid.TryParse(PlayerPrefs.GetString(OperatorRevisionPref, ""), out provisioningRevision);
				return;
			}

			requireMenuPassword = PlayerPrefs.GetInt(HeadsetPasswordRequiredPref, 0) != 0;
			menuPasswordHash = PlayerPrefs.GetString(HeadsetPasswordHashPref, "");
			pinnedHostAddress = PlayerPrefs.GetString(HeadsetPinnedHostPref, "");
			declinedHostAddress = PlayerPrefs.GetString(HeadsetDeclinedHostPref, "");
			provisioningHostAddress = PlayerPrefs.GetString(ProvisioningHostPref, pinnedHostAddress);
			declinedProvisioningHostAddress = PlayerPrefs.GetString(DeclinedProvisioningHostPref, "");
			Guid.TryParse(PlayerPrefs.GetString(HeadsetRevisionPref, ""), out provisioningRevision);
			Guid.TryParse(PlayerPrefs.GetString(DeclinedRevisionPref, ""), out declinedRevision);
			pinHeadsetsToHost = !string.IsNullOrEmpty(pinnedHostAddress);
		}

		private void OnSyncBusActivated()
		{
			if (IsOperator && SyncBus.IsAuthority)
				PublishOperatorPolicy();
		}

		private void OnPolicySynced()
		{
			if (!IsOperator)
				ApplySyncedPolicy(policySync.Value);
		}

		private void OnSyncedPolicyChanged(SyncedPolicy _, SyncedPolicy policy)
		{
			if (!IsOperator)
				ApplySyncedPolicy(policy);
		}

		private void PublishOperatorPolicy()
		{
			if (!IsOperator || !SyncBus.IsAuthority)
				return;

			string hostAddress = NetcodeManagement.GetLocalIPv4() ?? "";
			SyncedPolicy policy = new()
			{
				revision = provisioningRevision,
				operatorManaged = 1,
				requireMenuPassword = requireMenuPassword ? (byte)1 : (byte)0,
				pinHeadsetsToHost = pinHeadsetsToHost && !string.IsNullOrEmpty(hostAddress)
					? (byte)1
					: (byte)0
			};
			policy.menuPasswordHash.CopyFromTruncated(menuPasswordHash);
			policy.hostAddress.CopyFromTruncated(hostAddress);
			policySync.Value = policy;
		}

		private void ApplySyncedPolicy(SyncedPolicy policy)
		{
			if (policy.operatorManaged == 0)
				return;

			bool previousPasswordRequired = requireMenuPassword;
			string previousPasswordHash = menuPasswordHash;
			string previousPinnedAddress = pinnedHostAddress;
			string previousDeclinedAddress = declinedHostAddress;
			string previousProvisioningAddress = provisioningHostAddress;
			string previousDeclinedProvisioningAddress = declinedProvisioningHostAddress;
			Guid previousRevision = provisioningRevision;
			string incomingAddress = policy.hostAddress.ToString();
			bool operatorPinsToHost = policy.pinHeadsetsToHost != 0 &&
			                           !string.IsNullOrEmpty(incomingAddress);
			bool operatorRequiresPassword = policy.requireMenuPassword != 0 &&
			                                !string.IsNullOrEmpty(policy.menuPasswordHash.ToString());
			bool operatorProvisions = operatorPinsToHost || operatorRequiresPassword;
			if (operatorProvisions && !string.IsNullOrEmpty(declinedProvisioningHostAddress) &&
			    incomingAddress == declinedProvisioningHostAddress && policy.revision == declinedRevision)
				return;

			provisioningRevision = policy.revision;
			declinedRevision = Guid.Empty;
			declinedProvisioningHostAddress = "";
			provisioningHostAddress = operatorProvisions ? incomingAddress : "";

			requireMenuPassword = policy.requireMenuPassword != 0;
			menuPasswordHash = policy.menuPasswordHash.ToString();

			if (!operatorPinsToHost)
			{
				pinHeadsetsToHost = false;
				pinnedHostAddress = "";
				declinedHostAddress = "";
			}
			else if (!string.Equals(incomingAddress, declinedHostAddress,
				         StringComparison.Ordinal))
			{
				pinHeadsetsToHost = true;
				pinnedHostAddress = incomingAddress;
				declinedHostAddress = "";
			}
			else
			{
				pinHeadsetsToHost = false;
				pinnedHostAddress = "";
			}

			bool passwordChanged = previousPasswordRequired != requireMenuPassword ||
			                       previousPasswordHash != menuPasswordHash;
			if (!passwordChanged && previousPinnedAddress == pinnedHostAddress &&
			    previousDeclinedAddress == declinedHostAddress &&
			    previousProvisioningAddress == provisioningHostAddress &&
			    previousDeclinedProvisioningAddress == declinedProvisioningHostAddress &&
			    previousRevision == provisioningRevision)
				return;

			SaveHeadsetSettings();

			Changed.Invoke();
			if (passwordChanged)
				MenuAccessChanged.Invoke();
		}

		private void SaveHeadsetSettings()
		{
			PlayerPrefs.SetInt(HeadsetPasswordRequiredPref, requireMenuPassword ? 1 : 0);
			PlayerPrefs.SetString(HeadsetPasswordHashPref, menuPasswordHash);
			PlayerPrefs.SetString(HeadsetPinnedHostPref, pinnedHostAddress);
			PlayerPrefs.SetString(HeadsetDeclinedHostPref, declinedHostAddress);
			PlayerPrefs.SetString(ProvisioningHostPref, provisioningHostAddress);
			PlayerPrefs.SetString(DeclinedProvisioningHostPref, declinedProvisioningHostAddress);
			PlayerPrefs.SetString(HeadsetRevisionPref, provisioningRevision.ToString("N"));
			PlayerPrefs.SetString(DeclinedRevisionPref, declinedRevision.ToString("N"));
			PlayerPrefs.Save();
		}

		private static string HashPassword(string password)
		{
			using SHA256 sha = SHA256.Create();
			byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
			StringBuilder hex = new(digest.Length * 2);
			foreach (byte value in digest)
				hex.Append(value.ToString("x2"));
			return hex.ToString();
		}

		private static bool FixedTimeEquals(string left, string right)
		{
			if (left == null || right == null || left.Length != right.Length)
				return false;

			int different = 0;
			for (int i = 0; i < left.Length; i++)
				different |= left[i] ^ right[i];
			return different == 0;
		}
	}
}
