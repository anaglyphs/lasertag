using Anaglyph.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
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

		private GameObject instantiatedObject;
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

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			switch (state)
			{
				case NetcodeState.Connected:
					TryInstantiateSelected();
					break;

				case NetcodeState.Disconnected:
					if (instantiatedObject)
						Destroy(instantiatedObject);
					break;
			}
		}

		private async void TryInstantiateSelected()
		{
			if (NetcodeManagement.State != NetcodeState.Connected) return;

			if (instantiatedObject)
				Destroy(instantiatedObject);

			//loadOp = selectedObject.InstantiateAsync(transform, false);
			//instantiatedObject = await loadOp.Task;
			instantiatedObject = Instantiate(selectedPrefab, transform);

			if (instantiatedObject)
			{
				instantiatedObject.transform.localPosition = Vector3.zero;
				instantiatedObject.transform.localRotation = Quaternion.identity;
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