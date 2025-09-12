using Anaglyph.Netcode;
using Anaglyph.XRTemplate.SharedSpaces;
using System.Threading.Tasks;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class ConnectionHUD : MonoBehaviour
	{
		[SerializeField] private GameObject connecting;
		[SerializeField] private GameObject colocating;
		[SerializeField] private GameObject ready;

		private void Awake()
		{
			NetcodeManagement.StateChange += OnNetcodeStateChanged;
			Colocation.IsColocatedChange += OnColocationChanged;

			UpdateVisibility();
		}

		private void OnDestroy()
		{
			NetcodeManagement.StateChange -= OnNetcodeStateChanged;
			Colocation.IsColocatedChange -= OnColocationChanged;
		}

		private void OnColocationChanged(bool isColocated) => UpdateVisibility();
		private void OnNetcodeStateChanged(NetcodeManagement.NetworkState state) => UpdateVisibility();

		private async void UpdateVisibility()
		{
			var state = NetcodeManagement.State;
			connecting.SetActive(state == NetcodeManagement.NetworkState.Connecting);
			colocating.SetActive(state == NetcodeManagement.NetworkState.Connected && !Colocation.IsColocated);
			bool isReady = state == NetcodeManagement.NetworkState.Connected && Colocation.IsColocated;
			ready.SetActive(isReady);
			if(isReady)
			{
				await Awaitable.WaitForSecondsAsync(1);
				ready.SetActive(false);
			}
		}
	}
}
