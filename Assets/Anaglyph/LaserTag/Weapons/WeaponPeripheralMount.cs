using Anaglyph.XR.Input;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	/// <summary>
	/// While the player holds a physical blaster - a mounted peripheral, or the phone itself - the
	/// virtual one is hidden and shots leave the physical barrel instead of the model's.
	/// Only the owner has this - everyone else hides the weapon off the synced visibility.
	/// </summary>
	[RequireComponent(typeof(HandSubject))]
	public class WeaponPeripheralMount : MonoBehaviour
	{
		[SerializeField] private Transform muzzle;
		[SerializeField] private WeaponVisual visual;

		// a phone's hand is its camera: shots leave just below it, along the view
		private static readonly Pose PhoneBarrelFromCamera = new(new Vector3(0, -0.05f, 0), Quaternion.identity);

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
			// also raised when the hands start or stop following a phone's camera
			hand.IsTrackingChanged += OnTrackingChanged;
			Apply();
		}

		private void OnDisable()
		{
			MountedPeripheral.Changed -= OnMountedPeripheralChanged;
			hand.Changed -= OnHandChanged;
			hand.IsTrackingChanged -= OnTrackingChanged;
		}

		// the hand is assigned after this is instantiated, so the first Apply runs without one
		private void OnMountedPeripheralChanged(HandPeripheral peripheral) => Apply();
		private void OnHandChanged(HandInput current) => Apply();
		private void OnTrackingChanged(bool tracking) => Apply();

		private void Apply()
		{
			Pose? barrelFromHand = HandInput.FollowedCamera != null ? PhoneBarrelFromCamera
				: hand.Current != null && MountedPeripheral.IsMountedOn(hand.Current.Handedness) ? MountedPeripheral.Current.BarrelFromController
				: null;

			// the handle sits on the hand's pose, so the barrel is measured from it
			if (barrelFromHand is Pose barrel)
			{
				Transform handPose = visual.Handle;
				muzzle.SetPositionAndRotation(handPose.TransformPoint(barrel.position), handPose.rotation * barrel.rotation);
			}
			else
				muzzle.SetLocalPositionAndRotation(modelMuzzlePose.position, modelMuzzlePose.rotation);

			visual.gameObject.SetActive(barrelFromHand == null);
		}
	}
}
