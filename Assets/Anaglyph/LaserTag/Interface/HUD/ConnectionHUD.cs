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
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			Colocation.IsColocatedChange += OnColocationChanged;

			UpdateVisibility();
		}

		private void OnDestroy()
		{
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			Colocation.IsColocatedChange -= OnColocationChanged;
		}

		private void OnColocationChanged(bool isColocated) => UpdateVisibility();
		private void OnNetcodeStateChanged(NetcodeState state) => UpdateVisibility();

		private async void UpdateVisibility()
		{
			var state = NetcodeManagement.State;
			connecting.SetActive(state == NetcodeState.Connecting);
			colocating.SetActive(state == NetcodeState.Connected && !Colocation.IsColocated);
			bool isReady = state == NetcodeState.Connected && Colocation.IsColocated;
			ready.SetActive(isReady);
			if(isReady)
			{
				await Awaitable.WaitForSecondsAsync(1);
				ready.SetActive(false);
			}
		}
	}
}
