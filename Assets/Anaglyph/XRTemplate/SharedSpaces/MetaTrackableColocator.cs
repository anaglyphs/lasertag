using Anaglyph.SharedSpaces;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class MetaTrackableColocator : SingletonBehavior<MetaTrackableColocator>, IColocator
	{
		[SerializeField] private GameObject worldLockAnchorPrefab = null;
		private WorldLock currentWorldLock = null;

		private OVRAnchor.Tracker tracker;

		private bool _isColocated;
		public event Action<bool> IsColocatedChange;
		private void SetIsColocated(bool b) => IsColocated = b;
		public bool IsColocated
		{
			get => _isColocated;
			set
			{
				bool changed = value != _isColocated;
				_isColocated = value;
				if (changed)
					IsColocatedChange?.Invoke(_isColocated);
			}
		}

		protected override void SingletonAwake()
		{
			tracker = new OVRAnchor.Tracker();
		}

		protected override void OnSingletonDestroy()
		{
			tracker.Dispose();
		}

		public void Colocate()
		{
			MainXROrigin.TrackingSpace.position = new Vector3(0, 1000, 0);
			FindKeyboard();
		}

		public void StopColocation()
		{
			IsColocated = false;

			if (currentWorldLock != null)
				Destroy(currentWorldLock.gameObject);
		}

		public async void FindKeyboard()
		{
			StopColocation();

			if (!OVRAnchor.TrackerConfiguration.KeyboardTrackingSupported)
				throw new Exception("Keyboard tracking isn't supported on this device!");

			var result = await tracker.ConfigureAsync(new OVRAnchor.TrackerConfiguration
			{
				KeyboardTrackingEnabled = true,
			});

			if(!result.Success)
				throw new Exception("Couldn't start keyboard tracking!");

			List<OVRAnchor> anchors = new();
			OVRAnchor keyboardAnchor = default;

			while (keyboardAnchor == default)
			{
				var fetchResult = await tracker.FetchTrackablesAsync(anchors);

				await Awaitable.FixedUpdateAsync();

				if (!fetchResult.Success)
				{
					continue;
				}

				foreach (var anchor in anchors)
				{
					if (anchor.GetTrackableType() != OVRAnchor.TrackableType.Keyboard)
						continue;

					keyboardAnchor = anchor;
					break;
				}
			}
			
			if (!keyboardAnchor.TryGetComponent(out OVRLocatable locatable))
				throw new Exception("Couldn't find " + nameof(OVRLocatable));

			await locatable.SetEnabledAsync(true);

			if(!locatable.TryGetSceneAnchorPose(out var ovrPose))
				throw new Exception("Couldn't get anchor pose");

			Transform trackingSpace = MainXROrigin.TrackingSpace;
			Pose keyboardPose = new Pose();
			keyboardPose.position = trackingSpace.TransformPoint(ovrPose.Position.Value);
			keyboardPose.rotation = trackingSpace.transform.rotation * (ovrPose.Rotation.Value * Quaternion.Euler(90, 0, 0));

			var g = Instantiate(worldLockAnchorPrefab, keyboardPose.position, keyboardPose.rotation);
			g.TryGetComponent(out currentWorldLock);

			//while (currentWorldLock != null && !currentWorldLock.Anchor.Localized)
			//{
			//	await Awaitable.FixedUpdateAsync();
			//}

			IsColocated = true;
		}
	}
}
