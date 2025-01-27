using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.SharedSpaces
{
	/// <summary>
	/// Transforms VR playspace so that the anchor matches its networked position
	/// </summary>
	[DefaultExecutionOrder(500)]
	public class WorldLock : MonoBehaviour
	{
		private static WorldLock _activeLock;
		public static event Action<WorldLock> ActiveLockChange;
		public static WorldLock ActiveLock
		{
			get => _activeLock;
			set
			{
				bool changed = value != _activeLock;
				_activeLock = value;
				if (changed) 
					ActiveLockChange?.Invoke(_activeLock);
			}
		}

		private IDesiredPose desiredPose;
		[SerializeField] private OVRSpatialAnchor anchor;
		public OVRSpatialAnchor Anchor => anchor;
		[SerializeField] private float colocateAtDistance = 3;

		[RuntimeInitializeOnLoadMethod]
		private static void OnInit()
		{
			OVRManager.display.RecenteredPose += HandleRecenter;

			Application.quitting += delegate
			{
				OVRManager.display.RecenteredPose -= HandleRecenter;
			};
		}

		private static async void HandleRecenter()
		{
			await Awaitable.EndOfFrameAsync();
			ActiveLock?.LockOnto();
		}

		private void OnValidate()
		{
			TryGetComponent(out anchor);
		}

		private void Awake()
		{
			TryGetComponent(out desiredPose);
		}

		private void OnDestroy()
		{
			if(ActiveLock == this)
				ActiveLock = null;
		}

        private void LateUpdate()
        {
			if (ActiveLock == this)
				return;

			Vector3 camPosition = MainXROrigin.Instance.Camera.transform.position;
			float distanceFromOrigin = Vector3.Distance(anchor.transform.position, camPosition);

			if (distanceFromOrigin < colocateAtDistance || ActiveLock == null)
				MakeActiveAnchor();
		}

		public void MakeActiveAnchor()
		{
			if (!anchor.Localized)
				return;

			ActiveLock = this;

			LockOnto();
		}

		public void LockOnto()
		{
			Pose fromPose = new Pose(transform.position, transform.rotation);

			Pose toPose = Pose.identity;

			if(desiredPose != null)
				toPose = desiredPose.DesiredPose;

			Colocation.TransformTrackingSpace(fromPose, toPose);
		}
	}
}