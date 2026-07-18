using Anaglyph.Netcode;
using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class ColocationManager : MonoBehaviour
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
		private readonly SyncVariable<ColocationMethod> methodSync = new("colo.method");
		public ColocationMethod Method => methodSync.Value;

		[SerializeField] private MetaAnchorColocator metaAnchorColocator;
		[SerializeField] private TagColocator tagColocator;
		private IColocator activeColocator;

		// True from method-sync (session fully known) until the session ends.
		private bool sessionStarted;

		private void Awake()
		{
			Instance = this;

			methodSync.Register();
			methodSync.Synced += OnMethodSynced;
			SyncBus.Activated += OnBusActivated;
			SyncBus.Deactivated += OnBusDeactivated;
		}

		private void OnDestroy()
		{
			SyncBus.Activated -= OnBusActivated;
			SyncBus.Deactivated -= OnBusDeactivated;
			methodSync.Synced -= OnMethodSynced;
			methodSync.Unregister();
		}

		private void OnBusActivated()
		{
			// Written before any endpoint's Synced fires, so joiner and authority
			// alike see the session's method in OnMethodSynced.
			if (SyncBus.IsAuthority)
				methodSync.Value = methodHostSetting;
		}

		// Full session state is in (authority: right after activation; joiners: after
		// the combined snapshot) — the replacement for OnNetworkSessionSynchronized.
		// Also re-fires after an authority change re-sync, hence the guard.
		private void OnMethodSynced()
		{
			if (sessionStarted) return;
			sessionStarted = true;

			switch (Method)
			{
				case ColocationMethod.MetaSharedAnchor:
					SetActiveColocator(metaAnchorColocator);
					break;

				case ColocationMethod.AprilTag:
					SetActiveColocator(tagColocator);
					break;
			}

			if (!MainXRRig.Instance) return;

			activeColocator.StartColocation();
		}

		private void OnBusDeactivated()
		{
			sessionStarted = false;

			if (activeColocator == null) return;

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