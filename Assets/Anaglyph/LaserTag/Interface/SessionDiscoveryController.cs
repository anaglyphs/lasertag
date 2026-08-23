using Anaglyph.Netcode;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	[DefaultExecutionOrder(-50)]
	public sealed class SessionDiscoveryController : MonoBehaviour
	{
		private bool requiredPermissionsGranted;
		private bool menuAllowsListening = true;
		private MetaSessionDiscovery sessionDiscovery;

		private void OnEnable()
		{
			NetcodeManagement.StateChanged += OnNetworkStateChanged;
			ApplyDiscoveryActivity();
		}

		private void Start()
		{
			ApplyDiscoveryActivity();
		}

		private void OnDisable()
		{
			NetcodeManagement.StateChanged -= OnNetworkStateChanged;
			SetDiscoveryActivity(MetaSessionDiscovery.Activity.Disabled);
		}

		public void SetRequiredPermissionsGranted(bool granted)
		{
			if (requiredPermissionsGranted == granted)
				return;

			requiredPermissionsGranted = granted;
			ApplyDiscoveryActivity();
		}

		public void SetMenuAllowsListening(bool allowed)
		{
			if (menuAllowsListening == allowed)
				return;

			menuAllowsListening = allowed;
			ApplyDiscoveryActivity();
		}

		private void OnNetworkStateChanged(NetcodeState state)
		{
			ApplyDiscoveryActivity();
		}

		private void ApplyDiscoveryActivity()
		{
			MetaSessionDiscovery.Activity activity =
				MetaSessionDiscovery.Activity.Disabled;

			if (isActiveAndEnabled && requiredPermissionsGranted)
			{
				activity = NetcodeManagement.State switch
				{
					NetcodeState.Disconnected when menuAllowsListening =>
						MetaSessionDiscovery.Activity.Listening,
					NetcodeState.Connected => MetaSessionDiscovery.Activity.Advertising,
					_ => MetaSessionDiscovery.Activity.Disabled
				};
			}

			SetDiscoveryActivity(activity);
		}

		private void SetDiscoveryActivity(MetaSessionDiscovery.Activity activity)
		{
			MetaSessionDiscovery current = MetaSessionDiscovery.Instance;
			if (current != sessionDiscovery)
			{
				sessionDiscovery?.SetActivity(MetaSessionDiscovery.Activity.Disabled);
				sessionDiscovery = current;
			}

			sessionDiscovery?.SetActivity(activity);
		}
	}
}
