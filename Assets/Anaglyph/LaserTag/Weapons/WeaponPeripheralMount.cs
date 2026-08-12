using Anaglyph.XR.Input;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	/// <summary>
	/// While a peripheral is mounted the player is holding a physical blaster, so the virtual
	/// one is hidden and shots leave the peripheral's barrel instead of the model's.
	/// Only the owner has this - everyone else hides the weapon off the synced visibility.
	/// </summary>
	[RequireComponent(typeof(HandSubject))]
	public class WeaponPeripheralMount : MonoBehaviour
	{
		// a direct child, so its local pose is measured from the tracked controller
		[SerializeField] private Transform muzzle;
		[SerializeField] private WeaponVisual visual;

		private HandSubject hand;
		private Pose modelMuzzlePose;

		private void Awake()
		{
			TryGetComponent(out hand);

			muzzle.GetLocalPositionAndRotation(out Vector3 position, out Quaternion rotation);
			modelMuzzlePose = new Pose(position, rotation);
		}

		private void OnEnable()
		{
			MountedPeripheral.Changed += OnMountedPeripheralChanged;
			hand.Changed += OnHandChanged;
			Apply();
		}

		private void OnDisable()
		{
			MountedPeripheral.Changed -= OnMountedPeripheralChanged;
			hand.Changed -= OnHandChanged;
		}

		// the hand is assigned after this is instantiated, so the first Apply runs without one
		private void OnMountedPeripheralChanged(HandPeripheral peripheral) => Apply();
		private void OnHandChanged(HandInput current) => Apply();

		private void Apply()
		{
			bool mounted = hand.Current != null &&
				MountedPeripheral.IsMountedOn(hand.Current.Handedness);

			Pose pose = mounted
				? MountedPeripheral.Current.BarrelFromController
				: modelMuzzlePose;

			muzzle.SetLocalPositionAndRotation(pose.position, pose.rotation);
			visual.gameObject.SetActive(!mounted);
		}
	}
}
