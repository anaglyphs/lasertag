using Anaglyph.Netcode;
using Oculus.Haptics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Anaglyph.Lasertag
{
	public class WeaponSwitcher : MonoBehaviour
	{
		public static WeaponSwitcher Left { get; private set; }
		public static WeaponSwitcher Right { get; private set; }

		[SerializeField] private InteractorHandedness hand;

		[FormerlySerializedAs("selectedObject")] [SerializeField]
		private GameObject selectedPrefab;

		private GameObject instObj;
		// private AsyncOperationHandle<GameObject> loadOp;

		private void Awake()
		{
			switch (hand)
			{
				case InteractorHandedness.Left:
					Left = this;
					break;
				case InteractorHandedness.Right:
					Right = this;
					break;
			}

			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
		}

		private void OnDestroy()
		{
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			switch (state)
			{
				case NetcodeState.Connected:
					TryInstantiateSelected();
					break;

				case NetcodeState.Disconnected:
					if (instObj)
						Destroy(instObj);
					break;
			}
		}

		private void TryInstantiateSelected()
		{
			if (NetcodeManagement.State != NetcodeState.Connected) return;

			if (instObj)
				Destroy(instObj);

			//loadOp = selectedObject.InstantiateAsync(transform, false);
			//instantiatedObject = await loadOp.Task;
			instObj = Instantiate(selectedPrefab, transform);

			if (instObj)
			{
				instObj.transform.localPosition = Vector3.zero;
				instObj.transform.localRotation = Quaternion.identity;
			}

			if (XRSettings.enabled)
			{
				// placeholder bullshit sry
				HapticSource hapt = instObj.GetComponentInChildren<HapticSource>();
				if (hapt)
					hapt.controller = this == Left ? Controller.Left : Controller.Right;
			}
		}

		// public void Select(AssetReferenceGameObject prefab)
		// {
		// 	if (selectedObject == prefab)
		// 		return;
		//
		// 	if (loadOp.IsValid() && !loadOp.IsDone)
		// 		Addressables.Release(loadOp);
		//
		// 	selectedObject = prefab;
		// 	TryInstantiateSelected();
		// }

		public void Select(GameObject prefab)
		{
			if (selectedPrefab == prefab)
				return;

			selectedPrefab = prefab;
			TryInstantiateSelected();
		}
	}
}