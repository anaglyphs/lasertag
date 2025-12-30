using Anaglyph.Netcode;
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
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			ColocationManager.Colocated += OnColocationChanged;

			UpdateVisibility();
		}

		private void OnDestroy()
		{
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			ColocationManager.Colocated -= OnColocationChanged;
		}

		private void OnColocationChanged(bool b)
		{
			UpdateVisibility();
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			UpdateVisibility();
		}

		private async void UpdateVisibility()
		{
			var state = NetcodeManagement.State;
			connecting.SetActive(state == NetcodeState.Connecting);
			colocating.SetActive(state == NetcodeState.Connected && !ColocationManager.IsColocated);
			var isReady = state == NetcodeState.Connected && ColocationManager.IsColocated;
			ready.SetActive(isReady);
			if (isReady)
			{
				await Awaitable.WaitForSecondsAsync(1);
				ready.SetActive(false);
			}
		}
	}
}