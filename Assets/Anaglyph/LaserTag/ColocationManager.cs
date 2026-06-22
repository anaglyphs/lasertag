using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.Lasertag
{
	public class ColocationManager : NetworkBehaviour
	{
		public static ColocationManager Instance { get; private set; }

		[Serializable]
		public enum ColocationMethod
		{
			MetaSharedAnchor = 0,
			AprilTag = 1
		}

		public static bool IsColocated { get; private set; }
		public static Action<bool> Colocated = delegate { };

		public ColocationMethod methodHostSetting;
		private readonly NetworkVariable<ColocationMethod> methodSync = new(0);
		public ColocationMethod Method => methodSync.Value;

		[SerializeField] private MetaAnchorColocator metaAnchorColocator;
		[SerializeField] private TagColocator tagColocator;
		private IColocator activeColocator;

		private void Awake()
		{
			Instance = this;
		}

		public override void OnNetworkSpawn()
		{
			if (IsOwner) methodSync.Value = methodHostSetting;
		}

		protected override void OnNetworkSessionSynchronized()
			//protected override void OnNetworkPostSpawn()
		{
			switch (Method)
			{
				case ColocationMethod.MetaSharedAnchor:
					activeColocator = metaAnchorColocator;
					break;

				case ColocationMethod.AprilTag:
					activeColocator = tagColocator;
					break;
			}

			SetActiveColocator(activeColocator);

			if (!MainXRRig.Instance) return;

			activeColocator.StartColocation();
		}

		public override void OnNetworkDespawn()
		{
			activeColocator.Colocated -= OnColocated;

			if (!MainXRRig.Instance) return;

			activeColocator.StopColocation();
			SetColocated(false);

			Vector3 p = MainXRRig.TrackingSpace.position;

			if (p.magnitude > 10000f ||
			    float.IsNaN(p.x) || float.IsInfinity(p.x) ||
			    float.IsNaN(p.y) || float.IsInfinity(p.y) ||
			    float.IsNaN(p.z) || float.IsInfinity(p.z))
			{
				MainXRRig.TrackingSpace.position = Vector3.zero;
				MainXRRig.TrackingSpace.rotation = Quaternion.identity;
			}
		}

		private void SetActiveColocator(IColocator colocator)
		{
			if (activeColocator != null)
			{
				activeColocator.StopColocation();
				activeColocator.Colocated -= OnColocated;
			}

			activeColocator = colocator;
			activeColocator.Colocated += OnColocated;
		}

		public void RealignEveryone()
		{
			ulong localID = NetworkManager.Singleton.LocalClientId;
			if (OwnerClientId != localID)
				NetworkObject.ChangeOwnership(localID);

			activeColocator.RealignEveryone();
		}

		private void OnColocated()
		{
			SetColocated(true);
		}

		private void SetColocated(bool b)
		{
			if (b == IsColocated)
				return;

			IsColocated = b;
			Colocated?.Invoke(IsColocated);
		}
	}
}