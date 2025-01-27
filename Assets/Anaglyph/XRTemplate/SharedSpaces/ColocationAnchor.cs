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
	[RequireComponent(typeof(NetworkedSpatialAnchor))]
	public class ColocationAnchor : NetworkBehaviour
	{
		private static ColocationAnchor _activeAnchor;
		public static event Action<ColocationAnchor> ActiveAnchorChange;
		public static ColocationAnchor ActiveAnchor
		{
			get => _activeAnchor;
			set
			{
				bool changed = value != _activeAnchor;
				_activeAnchor = value;
				if (changed) 
					ActiveAnchorChange?.Invoke(_activeAnchor);
			}
		}

		[SerializeField] private NetworkedSpatialAnchor networkedAnchor;
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
			ActiveAnchor?.ColocateToAnchor();
		}

		private void OnValidate()
		{
			TryGetComponent(out networkedAnchor);
		}

		public override void OnDestroy()
		{
			base.OnDestroy();

			if(ActiveAnchor == this)
				ActiveAnchor = null;
		}

        private void LateUpdate()
        {
			if (ActiveAnchor == this)
				return;

			Vector3 camPosition = MainXROrigin.Instance.Camera.transform.position;
			float distanceFromOrigin = Vector3.Distance(networkedAnchor.transform.position, camPosition);

			if (distanceFromOrigin < colocateAtDistance || ActiveAnchor == null)
				MakeActiveAnchor();
		}

		public void MakeActiveAnchor()
		{
			if (!networkedAnchor.Anchor.Localized)
				return;

			ActiveAnchor = this;

			ColocateToAnchor();
		}

		public void ColocateToAnchor()
		{
			Pose toPose = networkedAnchor.OriginalPoseSync.Value.ToPose();
			Pose fromPose = new Pose(transform.position, transform.rotation);
			Colocation.TransformTrackingSpace(fromPose, toPose);
		}
	}
}