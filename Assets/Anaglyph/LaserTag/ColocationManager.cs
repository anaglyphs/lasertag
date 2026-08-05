using Anaglyph.Netcode;
using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using UnityEngine;
using UnityEngine.Serialization;

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
		public ColocationMethod SelectedMethod => SyncBus.Active ? Method : methodHostSetting;

		[SerializeField] private Colocator colocator;

		[SerializeField] private SpatialAnchorConstraintProvider spatialAnchorProvider;
		[SerializeField] private TagConstraintProvider tagProvider;

		public bool UsingTagProvider => colocator != null &&
			ReferenceEquals(colocator.Provider, tagProvider);

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
			if (!colocator) colocator = FindFirstObjectByType<Colocator>();
			if (!spatialAnchorProvider) spatialAnchorProvider = SpatialAnchorConstraintProvider.Instance;
			if (!tagProvider) tagProvider = TagConstraintProvider.Instance;

			if (spatialAnchorProvider)
				spatialAnchorProvider.MintingGate = () =>
					MapManager.Instance == null || MapManager.Instance.CheckWorldFrameIsTrusted();

			if (MapManager.Instance != null)
				MapManager.Instance.CurrentMapChanged += OnCurrentMapChanged;

			// Hosts only become discoverable once their world frame is trustworthy: joiners
			// who arrive earlier would download nothing they can align to.
			MetaSessionDiscovery discovery = MetaSessionDiscovery.Instance;
			if (discovery != null)
				discovery.AdvertisementGate = CanAdvertiseSession;

			UpdateProvider();
		}

		private void OnDestroy()
		{
			if (MapManager.Instance != null)
			{
				MapManager.Instance.CurrentMapChanged -= OnCurrentMapChanged;
			}

			if (colocator)
				colocator.SetProvider(null);

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

			bool open = CanAdvertiseSession();
			if (open == advertiseGateOpen) return;

			advertiseGateOpen = open;
			discovery.RefreshState();
		}

		private bool CanAdvertiseSession()
		{
			MapManager mapManager = MapManager.Instance;
			if (mapManager == null || !mapManager.CheckWorldFrameIsTrusted())
				return false;

			ColocationMethod hostMethod = sessionStarted ? Method : methodHostSetting;
			if (hostMethod != ColocationMethod.AprilTag)
				return true;

			// Tag mode is only meaningful when the provider has at least one registered tag
			// from which each peer can create its own private anchor.
			GameMap map = mapManager.CurrentMap;
			return map != null && map.HasTags;
		}

		private void OnBusActivated()
		{
			// Written before any endpoint's Synced fires, so joiner and authority
			// alike see the session's method in OnMethodSynced.
			if (SyncBus.IsAuthority)
			{
				methodSync.Value = methodHostSetting;
				UpdateProvider();
			}
			else
			{
				// Do not align even briefly against the previous offline map while the
				// authority's combined provider/map snapshot is being applied.
				colocator?.StopColocation();
				SetColocated(false);
			}
		}

		// Full session state is in (authority: right after activation; joiners: after
		// the combined snapshot). Also re-fires after an authority change re-sync,
		// hence the guard.
		private void OnMethodSynced()
		{
			if (sessionStarted) return;
			sessionStarted = true;

			UpdateProvider();
		}

		private void OnBusDeactivated()
		{
			sessionStarted = false;

			// Colocation does not end with the session — a loaded map keeps localizing.
			UpdateProvider();

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
			UpdateProvider();
		}

		/// <summary>
		/// Selects exactly one self-contained provider. Tag mode is valid only for a map that
		/// contains registered tags. A tag-enabled map may instead use shared-anchor mode, but
		/// roaming minting remains disabled so every saved anchor keeps a parent tag.
		/// </summary>
		private void UpdateProvider()
		{
			GameMap map = MapManager.Instance != null ? MapManager.Instance.CurrentMap : null;
			IColocationConstraintProvider next = null;

			if (spatialAnchorProvider)
				spatialAnchorProvider.RoamingMintEnabled = map != null && !map.HasTags;

			if (map != null)
			{
				if (SelectedMethod == ColocationMethod.AprilTag)
				{
					if (map.HasTags)
						next = tagProvider;
				}
				else
				{
					next = spatialAnchorProvider;
				}
			}

			colocator.SetProvider(next);

			if (next == null)
			{
				colocator.StopColocation();
				SetColocated(false);
				return;
			}

			if (!MainXRRig.Instance) return;

			colocator.StateChanged -= OnColocatorStateChanged;
			colocator.StateChanged += OnColocatorStateChanged;

			colocator.StartColocation();
			OnColocatorStateChanged(colocator.State);
		}

		/// <summary>Lets the registration tool drive tag detection while authoring.</summary>
		public void SetTagDetectionOverride(bool on)
		{
			if (!tagProvider) return;

			tagProvider.SetDetectionOverride(on);
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
