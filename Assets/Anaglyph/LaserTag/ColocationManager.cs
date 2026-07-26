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

		private bool advertiseGateOpen;

		private void Awake()
		{
			Instance = this;

			methodSync.Register();
			methodSync.Synced += OnMethodSynced;
			SyncBus.Activated += OnBusActivated;
			SyncBus.Deactivated += OnBusDeactivated;
		}

		private void Start()
		{
			// Colocators consume the map system's references; the wiring points this way
			// because their assembly cannot see the game layer.
			metaAnchorColocator.ReferenceSource = MapManager.Instance;
			tagColocator.AnchorReferenceSource = MapManager.Instance;
			tagColocator.CanonTags = MapManager.Instance.CanonTags;
			tagColocator.TagObserved += MapManager.Instance.OnTagObserved;

			MapManager.Instance.CurrentMapChanged += OnCurrentMapChanged;

			// Hosts only become discoverable once their world frame is trustworthy: joiners
			// who arrive earlier would download nothing they can align to.
			MetaSessionDiscovery discovery = MetaSessionDiscovery.Instance;
			if (discovery != null)
				discovery.AdvertisementGate = () =>
					MapManager.Instance != null && MapManager.Instance.WorldFrameTrusted;

			UpdateActiveColocator();
		}

		private void OnDestroy()
		{
			if (MapManager.Instance != null)
			{
				MapManager.Instance.CurrentMapChanged -= OnCurrentMapChanged;
				tagColocator.TagObserved -= MapManager.Instance.OnTagObserved;
			}

			SyncBus.Activated -= OnBusActivated;
			SyncBus.Deactivated -= OnBusDeactivated;
			methodSync.Synced -= OnMethodSynced;
			methodSync.Unregister();
		}

		private void Update()
		{
			// The advertisement gate has no event of its own (agreement shifts every frame);
			// poke the discovery only on transitions.
			MetaSessionDiscovery discovery = MetaSessionDiscovery.Instance;
			if (discovery == null) return;

			bool open = MapManager.Instance != null && MapManager.Instance.WorldFrameTrusted;
			if (open == advertiseGateOpen) return;

			advertiseGateOpen = open;
			discovery.RefreshState();
		}

		private void OnBusActivated()
		{
			// Written before any endpoint's Synced fires, so joiner and authority
			// alike see the session's method in OnMethodSynced.
			if (SyncBus.IsAuthority)
				methodSync.Value = methodHostSetting;
		}

		// Full session state is in (authority: right after activation; joiners: after
		// the combined snapshot). Also re-fires after an authority change re-sync,
		// hence the guard.
		private void OnMethodSynced()
		{
			if (sessionStarted) return;
			sessionStarted = true;

			UpdateActiveColocator();
		}

		private void OnBusDeactivated()
		{
			sessionStarted = false;

			// Colocation does not end with the session — a loaded map keeps localizing.
			UpdateActiveColocator();

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

		private void OnCurrentMapChanged(GameMap map)
		{
			// In a session the method sync dictates the colocator regardless of the map.
			if (!sessionStarted)
				UpdateActiveColocator();
		}

		/// <summary>
		/// In a session, the synced method decides. Outside one, a loaded map localizes with
		/// whatever it has: tag maps go through the tag colocator (which also consumes the
		/// map's anchors), plain maps through the anchor colocator. No map and no session
		/// means nothing to localize against.
		/// </summary>
		private void UpdateActiveColocator()
		{
			IColocator target = null;

			if (sessionStarted)
			{
				target = Method == ColocationMethod.AprilTag
					? tagColocator
					: (IColocator)metaAnchorColocator;
			}
			else
			{
				GameMap map = MapManager.Instance != null ? MapManager.Instance.CurrentMap : null;

				if (map != null)
					target = map.HasTags ? tagColocator : (IColocator)metaAnchorColocator;
			}

			SetActiveColocator(target);
		}

		private void SetActiveColocator(IColocator colocator)
		{
			if (activeColocator != colocator)
			{
				if (activeColocator != null)
				{
					activeColocator.StopColocation();
					activeColocator.StateChanged -= OnColocatorStateChanged;
				}

				activeColocator = colocator;

				if (activeColocator != null)
					activeColocator.StateChanged += OnColocatorStateChanged;
			}

			if (activeColocator == null)
			{
				SetColocated(false);
				return;
			}

			if (!MainXRRig.Instance) return;

			activeColocator.StartColocation(); // no-op when already running
			OnColocatorStateChanged(activeColocator.State);
		}

		// Only Localized counts as colocated. Lost keeps the stale alignment applied but stops
		// anything downstream from treating world space as trustworthy — which means
		// IsColocated can go false mid-session rather than only when the session ends.
		private void OnColocatorStateChanged(ColocationState state)
		{
			SetColocated(state == ColocationState.Localized);
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
