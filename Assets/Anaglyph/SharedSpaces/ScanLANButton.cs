using NetworkDiscoveryUnity;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SharedSpaces
{
    public class ScanLANButton : MonoBehaviour
    {
		public float secondsToWaitBetweenScans = 1f;

		private Button button;

		private void Awake()
		{
			button = GetComponent<Button>();

			button.onClick.AddListener(delegate
			{
				NetworkDiscovery.Instance.SendBroadcast();
				StartCoroutine(DisableButtonTemporarily());
			});
		}

		private void OnEnable()
		{
			button.interactable = true;
		}

		private IEnumerator DisableButtonTemporarily()
		{
			button.interactable = false;

			yield return new WaitForSeconds(secondsToWaitBetweenScans);

			button.interactable = true;
		}
	}
}
