using System;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.SharedSpaces.AprilTags;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Selects which colocation strategy this device runs, and fronts the XRTemplate colocation
	/// stack for the rest of the game — so nothing else has to hold its own references to the
	/// providers or the colocator, or reason about which of them is live.
	/// </summary>
	// Before MapManager (-100), so the providers it hands out are resolved by the time the map
	// layer builds against them. After the providers themselves (-200), which set their instances.
	[DefaultExecutionOrder(-150)]
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

		[FormerlySerializedAs("spatialAnchorProvider")] [SerializeField] private SpatialAnchorColocationConstraintProvider spatialAnchorColocationProvider;
		[FormerlySerializedAs("tagProvider")] [SerializeField] private AprilTagColocationConstraintProvider aprilTagColocationProvider;
		
		/// <summary>
		/// The provider actually driving colocation, or null when none is selected. This is the
		/// only trustworthy answer to "which strategy is live": <see cref="Method"/> says what the
		/// session asked for, and <see cref="UpdateProvider"/> selects nothing at all when that
		/// method cannot serve the current map.
		/// </summary>
		public IColocationConstraintProvider ActiveProvider =>
			colocator != null ? colocator.Provider : null;

		public bool UsingTagProvider =>
			aprilTagColocationProvider != null && ReferenceEquals(ActiveProvider, aprilTagColocationProvider);

		public bool UsingAnchorProvider =>
			spatialAnchorColocationProvider != null && ReferenceEquals(ActiveProvider, spatialAnchorColocationProvider);

		public SpatialAnchorColocationConstraintProvider AnchorProvider => spatialAnchorColocationProvider;
		public AprilTagColocationConstraintProvider TagProvider => aprilTagColocationProvider;

		/// <summary>How well the applied alignment currently fits the references being observed.</summary>
		public FitAgreement Agreement => colocator != null ? colocator.Agreement : default;

		/// <summary>
		/// How many of a map's references the active provider could currently produce a constraint
		/// from. Zero means there is nothing to verify the world frame against, so the frame the
		/// device is standing in is that map's frame by definition — the same reasoning
		/// <see cref="Colocator"/> applies to a session with no reference runtime at all.
		/// </summary>
		public int CountRealizableReferences(GameMap map)
		{
			if (map == null)
				return 0;

			IColocationConstraintProvider active = ActiveProvider;
			if (active == null || !active.IsAvailable)
				return 0;

			// Tag mode can only realize this device's tag-backed anchors. Roaming anchors stay in
			// the map for shared-anchor mode, but must not raise the bar for a provider that can
			// never expose them.
			bool tagMode = UsingTagProvider;

			int count = 0;
			foreach (MapAnchorEntry anchor in map.anchors)
				if (!tagMode || anchor.tagId >= 0)
					count++;

			return count;
		}

		// True from method-sync (session fully known) until the session ends.
		private bool sessionStarted;

		private bool advertiseGateOpen;

		private void Awake()
		{
			Instance = this;

			// Resolved here rather than in Start: the map layer builds against these references
			// while it wakes, so they have to be settled before anything downstream runs.
			if (!colocator) colocator = FindFirstObjectByType<Colocator>();
			if (!spatialAnchorColocationProvider) spatialAnchorColocationProvider = SpatialAnchorColocationConstraintProvider.Instance;
			if (!aprilTagColocationProvider) aprilTagColocationProvider = AprilTagColocationConstraintProvider.Instance;

			methodSync.Register();
			methodSync.Synced += OnMethodSynced;
			SyncBus.Activated += OnBusActivated;
			SyncBus.Deactivated += OnBusDeactivated;
		}

		private void Start()
		{
			if (spatialAnchorColocationProvider)
				spatialAnchorColocationProvider.MintingGate = () =>
					MapManager.Instance == null || MapManager.Instance.CheckWorldFrameIsTrusted();

			MapManager.CurrentMapChanged += OnCurrentMapChanged;

			// Hosts only become discoverable once their world frame is trustworthy: joiners
			// who arrive earlier would download nothing they can align to.
			MetaSessionDiscovery discovery = MetaSessionDiscovery.Instance;
			if (discovery != null)
				discovery.AdvertisementGate = CanAdvertiseSession;

			UpdateProvider();
		}

		private void OnDestroy()
		{
			MapManager.CurrentMapChanged -= OnCurrentMapChanged;

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

			if (spatialAnchorColocationProvider)
				spatialAnchorColocationProvider.RoamingMintEnabled = map != null && !map.HasTags;

			if (map != null)
			{
				if (SelectedMethod == ColocationMethod.AprilTag)
				{
					if (map.HasTags)
						next = aprilTagColocationProvider;
				}
				else
				{
					next = spatialAnchorColocationProvider;
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
			if (!aprilTagColocationProvider) return;

			aprilTagColocationProvider.SetDetectionOverride(on);
		}

		// Only Localized counts as colocated. Lost keeps the stale alignment applied but stops
		// anything downstream from treating world space as trustworthy — which means
		// IsColocated can go false mid-session rather than only when the session ends.
		private void OnColocatorStateChanged(ColocationState state)
		{
#if UNITY_EDITOR
			if (!XRSettings.isDeviceActive)
			{
				SetColocated(true);
				return;
			}
#endif
			
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
