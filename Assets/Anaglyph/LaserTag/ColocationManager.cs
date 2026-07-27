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

		[SerializeField] private ReferenceColocator colocator;
		[SerializeField] private AnchorReferenceProvider anchorSource;
		[SerializeField] private TagReferenceSource tagSource;

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
			if (!colocator) colocator = FindFirstObjectByType<ReferenceColocator>();
			if (!anchorSource) anchorSource = AnchorReferenceProvider.Instance;
			if (!tagSource) tagSource = TagReferenceSource.Instance;

			tagSource.CanonTags = MapManager.Instance.CanonTags;
			tagSource.TagObserved += MapManager.Instance.OnTagObserved;

			MapManager.Instance.CurrentMapChanged += OnCurrentMapChanged;

			// Hosts only become discoverable once their world frame is trustworthy: joiners
			// who arrive earlier would download nothing they can align to.
			MetaSessionDiscovery discovery = MetaSessionDiscovery.Instance;
			if (discovery != null)
				discovery.AdvertisementGate = () =>
					MapManager.Instance != null && MapManager.Instance.WorldFrameTrusted;

			UpdateSources();
		}

		private void OnDestroy()
		{
			if (MapManager.Instance != null)
			{
				MapManager.Instance.CurrentMapChanged -= OnCurrentMapChanged;

				if (tagSource)
					tagSource.TagObserved -= MapManager.Instance.OnTagObserved;
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

			UpdateSources();
		}

		private void OnBusDeactivated()
		{
			sessionStarted = false;

			// Colocation does not end with the session — a loaded map keeps localizing.
			UpdateSources();

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
			UpdateSources();
		}

		/// <summary>
		/// Decides which reference sources feed the fit.
		///
		/// Anchors are always a source — they are what a map is pinned to in every mode, and
		/// what restores alignment after a recenter or sleep. Tags are a source whenever the
		/// loaded map has any: their corrections keep the anchors honest regardless of which
		/// colocation method a session was hosted with, so a tag map does not go blind just
		/// because it is being hosted over shared anchors.
		///
		/// The session's method decides what gets *shared*, which is the map system's
		/// business, not the colocator's.
		/// </summary>
		private void UpdateSources()
		{
			GameMap map = MapManager.Instance != null ? MapManager.Instance.CurrentMap : null;

			bool wantAnchors = map != null;
			bool wantTags = map != null &&
				(map.HasTags ||
				 // A session hosted in tag mode registers its canon tags as it goes, so the
				 // source has to be live before this peer's map has any of its own.
				 (sessionStarted && Method == ColocationMethod.AprilTag));

			colocator.ClearSources();

			if (wantAnchors && anchorSource)
				colocator.AddSource(anchorSource);

			if (wantTags && tagSource)
				colocator.AddSource(tagSource);

			// Tag detection is independent of colocation: the map editor's registration tool
			// turns it on with nothing loaded at all (see TagRegistrationTool).
			if (tagSource && !TagRegistrationTool.RegistrationMode)
				tagSource.SetRunning(wantTags);

			if (!wantAnchors && !wantTags)
			{
				colocator.StopColocation();
				SetColocated(false);
				return;
			}

			if (!MainXRRig.Instance) return;

			colocator.StateChanged -= OnColocatorStateChanged;
			colocator.StateChanged += OnColocatorStateChanged;

			colocator.StartColocation(); // no-op when already running
			OnColocatorStateChanged(colocator.State);
		}

		/// <summary>Lets the registration tool drive tag detection while authoring.</summary>
		public void SetTagDetectionOverride(bool on)
		{
			if (!tagSource) return;

			if (on)
				tagSource.SetRunning(true);
			else
				UpdateSources();
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
