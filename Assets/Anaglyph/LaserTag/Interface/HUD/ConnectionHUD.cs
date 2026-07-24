using Anaglyph.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Lasertag
{
	public class ConnectionHUD : MonoBehaviour
	{
		private Label connectingLabel;
		private Label aligningLabel;
		private Label readyLabel;

		private void OnEnable()
		{
			connectingLabel = HUDElement.Require<Label>(this, "connecting-label");
			aligningLabel = HUDElement.Require<Label>(this, "aligning-label");
			readyLabel = HUDElement.Require<Label>(this, "ready-label");

			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			ColocationManager.Colocated += OnColocationChanged;

			UpdateVisibility();
		}

		private void OnDisable()
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
			HUDElement.SetDisplayed(connectingLabel, state == NetcodeState.Connecting);
			HUDElement.SetDisplayed(aligningLabel,
				state == NetcodeState.Connected && !ColocationManager.IsColocated);
			var isReady = state == NetcodeState.Connected && ColocationManager.IsColocated;
			HUDElement.SetDisplayed(readyLabel, isReady);
			if (isReady)
			{
				await Awaitable.WaitForSecondsAsync(1);
				if (this == null) return;
				HUDElement.SetDisplayed(readyLabel, false);
			}
		}
	}
}
