using System;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	[RequireComponent(typeof(OVRSpatialAnchor), typeof(NetworkedAnchor), typeof(WorldLockAnchor))]
	public class ColocationAnchor : NetworkBehaviour
	{
		public static ColocationAnchor Instance { get; private set; }

		private readonly NetworkVariable<Pose> targetPoseSync = new();

		public static event Action Aligned = delegate { };
		private static event Action AnchorInstantiated = delegate { };

		private OVRSpatialAnchor anchor;
		private WorldLockAnchor worldLockAnchor;

		private void Awake()
		{
			anchor = GetComponent<OVRSpatialAnchor>();
			worldLockAnchor = GetComponent<WorldLockAnchor>();
			worldLockAnchor.Aligned += Aligned.Invoke;
		}

		public override void OnNetworkSpawn()
		{
			Instance = this;

			if (!XRSettings.enabled) return;

			if (!anchor.Localized)
				MainXRRig.TrackingSpace.position = new Vector3(0, 1000, 0);

			if (IsOwner)
			{
				targetPoseSync.OnValueChanged += delegate { SetTargetPose(); };
				targetPoseSync.Value = transform.GetWorldPose();
				SetTargetPose();
			}

			AnchorInstantiated.Invoke();

			if (IsOwner)
				AnchorInstantiated += OnAnchorInstantiated;
		}

		private void SetTargetPose()
		{
			var t = targetPoseSync.Value;
			var mat = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
			worldLockAnchor.SetTargetAndAlign(mat);
		}

		private void OnAnchorInstantiated()
		{
			if (IsOwner && Instance != this)
				NetworkObject.Despawn(true);
		}

		public override void OnNetworkDespawn()
		{
			AnchorInstantiated -= OnAnchorInstantiated;
		}
	}
}