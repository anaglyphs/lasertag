using System;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.SharedSpaces.AprilTags;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using UnityEngine;
using UnityEngine.Serialization;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Selects which colocation strategy this device runs, and fronts the XRTemplate colocation
	/// stack for the rest of the game — so nothing else has to hold its own references to the
	/// providers or the colocator, or reason about which of them is live.
	/// </summary>
	// Before LaserTagMapCoordinator (-100), so the providers it hands out are resolved by the time the map
	// layer builds against them. After the providers themselves (-200), which set their instances.
	[DefaultExecutionOrder(-150)]
	public class ColocationManager : MonoBehaviour
	{
		public static ColocationManager Instance { get; private set; }

		[Serializable]
		public enum ColocationMethod
		{
			MetaSharedAnchor = 0,
			AprilTag = 1,
			SystemDetermined = 2,
			TwoAprilTags = 3
		}

		public static bool IsValidMethod(ColocationMethod method) =>
			method is ColocationMethod.MetaSharedAnchor or ColocationMethod.AprilTag or
				ColocationMethod.SystemDetermined or ColocationMethod.TwoAprilTags;

		public static bool UsesSavedReferences(ColocationMethod method) =>
			method is ColocationMethod.MetaSharedAnchor or ColocationMethod.AprilTag;

		public static bool IsColocated { get; private set; }
		public static Action<bool> Colocated = delegate { };

		private ColocationMethod mapPreference;
		private ColocationMethod offlineMethod;
		private readonly SyncVariable<ColocationMethod> methodSync = new("colo.method");
		public ColocationMethod Method => methodSync.Value;
		public ColocationMethod SelectedMethod => SyncBus.Active ? Method : offlineMethod;
		public ColocationMethod PreferredAvailableMethod => CompatibleMethod(mapPreference, mapHasTags,
			mapHasContent, mapHasAnchors, systemFrameForTagSetup);
		public bool IsSettingUpTags => mapLoaded && systemFrameForTagSetup &&
			mapPreference == ColocationMethod.AprilTag && !mapHasTags &&
			SelectedMethod == ColocationMethod.SystemDetermined;

		public static bool RequiresTagSetup(ColocationMethod current, ColocationMethod requested, bool hasTags) =>
			current == ColocationMethod.SystemDetermined && requested == ColocationMethod.AprilTag && !hasTags;
		public ColocationMethod PreferredSessionMethod => !UsesSavedReferences(PreferredAvailableMethod)
			? PreferredAvailableMethod : (mapHasTags || !mapHasAnchors) && spatialAnchorColocationProvider != null &&
			!HeadsetConfiguration.SessionIsOperatorManaged && !spatialAnchorColocationProvider.CanShareAnchors
			? ColocationMethod.AprilTag : PreferredAvailableMethod;

		public static ColocationMethod CompatibleMethod(ColocationMethod requested, bool hasTags,
			bool hasContent, bool hasAnchors, bool systemFrameForTagSetup = false)
		{
			if (systemFrameForTagSetup && requested == ColocationMethod.AprilTag && !hasTags)
				return ColocationMethod.SystemDetermined;
			if (!UsesSavedReferences(requested)) return requested;
			if (!hasTags && hasAnchors) return ColocationMethod.MetaSharedAnchor;
			if (hasTags && !hasAnchors) return ColocationMethod.AprilTag;
			return requested;
		}
		public event Action MethodChanged = delegate { };

		// The map coordinator prepares reference state before committing a live change.
		internal void CommitMethod(ColocationMethod method)
		{
			if (methodSync.Value == method) return;
			spatialAnchorColocationProvider?.InvalidateReferenceContext();
			methodSync.Value = method;
		}

		[SerializeField] private Colocator colocator;
		private SystemDeterminedColocationConstraintProvider systemDeterminedProvider;
		private TwoAprilTagColocationConstraintProvider twoAprilTagProvider;

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

		public bool UsingSystemDeterminedProvider =>
			systemDeterminedProvider != null && ReferenceEquals(ActiveProvider, systemDeterminedProvider);

		public bool UsingTwoAprilTagProvider =>
			twoAprilTagProvider != null && ReferenceEquals(ActiveProvider, twoAprilTagProvider);

		public SpatialAnchorColocationConstraintProvider AnchorProvider => spatialAnchorColocationProvider;
		public AprilTagColocationConstraintProvider TagProvider => aprilTagColocationProvider;
		public TwoAprilTagColocationConstraintProvider TwoTagProvider => twoAprilTagProvider;

		/// <summary>How well the applied alignment currently fits the references being observed.</summary>
		public FitAgreement Agreement => colocator != null ? colocator.Agreement : default;

		/// <summary>
		/// How far this device currently trusts its own alignment. Finer grained than
		/// <see cref="IsColocated"/>, which collapses everything but Localized into false.
		/// </summary>
		public ColocationAlignmentState AlignmentState =>
			colocator != null ? colocator.AlignmentState : ColocationAlignmentState.Stopped;

		/// <summary>
		/// How many references the active provider can try to realize. Count its own set rather
		/// than the map's union of private and shared UUIDs for the same physical references.
		/// System-determined alignment contributes one origin; two-tag alignment always waits
		/// for two physical points, including on an empty map before either has been seen.
		/// The map coordinator decides whether zero references allows a blank-world bootstrap.
		/// </summary>
		public int CountRealizableReferences()
		{
			if (ActiveProvider == null || !ActiveProvider.IsAvailable) return 0;
			if (UsingSystemDeterminedProvider) return 1;
			if (UsingTwoAprilTagProvider) return 2;
			return UsingTagProvider ? aprilTagColocationProvider.LocalAnchorCount
				: spatialAnchorColocationProvider.Constraints.Count;
		}

		private bool mapLoaded;
		private bool mapHasTags;
		private bool mapHasContent;
		private bool mapHasAnchors;
		private bool systemFrameForTagSetup;

		public void ConfigureMap(bool loaded, bool hasTags, bool hasContent, bool hasAnchors,
			ColocationMethod preference, Func<bool> mintingGate, bool systemFrameForTagSetup = false)
		{
			mapLoaded = loaded;
			mapHasTags = hasTags;
			mapHasContent = hasContent;
			mapHasAnchors = hasAnchors;
			mapPreference = preference;
			this.systemFrameForTagSetup = systemFrameForTagSetup;
			if (!SyncBus.Active) offlineMethod = PreferredAvailableMethod;
			if (spatialAnchorColocationProvider) spatialAnchorColocationProvider.MintingGate = mintingGate;
			UpdateProvider();
		}

		// True from method-sync (session fully known) until the session ends.
		private bool sessionStarted;

		private void Awake()
		{
			Instance = this;

			// Resolved here rather than in Start: the map layer builds against these references
			// while it wakes, so they have to be settled before anything downstream runs.
			if (!colocator) colocator = FindFirstObjectByType<Colocator>();
			if (!spatialAnchorColocationProvider) spatialAnchorColocationProvider = SpatialAnchorColocationConstraintProvider.Instance;
			if (!aprilTagColocationProvider) aprilTagColocationProvider = AprilTagColocationConstraintProvider.Instance;
			systemDeterminedProvider = new SystemDeterminedColocationConstraintProvider(
				MainXRRig.Instance != null ? MainXRRig.TrackingSpace : null);
			twoAprilTagProvider = new TwoAprilTagColocationConstraintProvider(aprilTagColocationProvider,
				AnchorRegistry.Instance, MainXRRig.Instance != null ? MainXRRig.TrackingSpace : null);

			methodSync.Register();
			methodSync.Synced += OnMethodSynced;
			methodSync.Changed += OnMethodChanged;
			// Clients must use the coordinator's preparation command, not a raw enum write.
			methodSync.Validate = (_, _) => false;
			SyncBus.Activated += OnBusActivated;
			SyncBus.Deactivated += OnBusDeactivated;
		}

		private void Start() => UpdateProvider();

		private void OnDestroy()
		{

			if (colocator)
			{
				colocator.Provider?.StopProviding();
				colocator.SetProvider(null);
			}
			twoAprilTagProvider?.StopProviding();

			SyncBus.Activated -= OnBusActivated;
			SyncBus.Deactivated -= OnBusDeactivated;
			methodSync.Synced -= OnMethodSynced;
			methodSync.Changed -= OnMethodChanged;
			methodSync.Unregister();
			if (Instance == this) Instance = null;
		}

		private void OnBusActivated()
		{
			// Written before any endpoint's Synced fires, so joiner and authority
			// alike see the session's method in OnMethodSynced.
			if (SyncBus.IsAuthority)
			{
				// An existing tagless map must first be aligned through its anchors before a
				// first tag can be registered in that frame. Blank maps can bootstrap with tags.
				// An operator delegates native anchor work to a headset; its own runtime
				// does not restrict the session method.
				methodSync.Value = PreferredSessionMethod;
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
			MethodChanged.Invoke();
		}

		private void OnMethodChanged(ColocationMethod _, ColocationMethod __)
		{
			// Join snapshots are applied before map adoption. Wait for Synced on that path.
			if (!SyncBus.Active || !sessionStarted) return;
			UpdateProvider();
			MethodChanged.Invoke();
		}

		private void OnBusDeactivated()
		{
			sessionStarted = false;

			// Colocation does not end with the session — a loaded map keeps localizing.
			UpdateProvider();
			MethodChanged.Invoke();
		}

		/// <summary>
		/// Selects exactly one self-contained provider. System and two-tag modes ignore retained
		/// map references. Offline, registered-tag mode is valid only for a map
		/// that contains registered tags; in a session it is selected regardless, so an empty map
		/// can still receive its first registration. A tag-enabled map may instead use
		/// shared-anchor mode, but roaming minting remains disabled so every saved anchor keeps a
		/// parent tag.
		/// </summary>
		private void UpdateProvider()
		{
			IColocationConstraintProvider next = null;

			if (spatialAnchorColocationProvider)
				spatialAnchorColocationProvider.RoamingMintEnabled = mapLoaded && !mapHasTags;

			if (mapLoaded)
			{
				if (SelectedMethod == ColocationMethod.SystemDetermined)
				{
					next = systemDeterminedProvider;
				}
				else if (SelectedMethod == ColocationMethod.TwoAprilTags)
				{
					next = twoAprilTagProvider;
				}
				else if (SelectedMethod == ColocationMethod.AprilTag)
				{
					// In a session the method is the session's contract, so tag mode selects the
					// tag provider even for a map with no tags yet. That is what lets a peer
					// register the first one into an empty map: the provider has to be the one
					// holding tag state for anybody's registration to reach the map.
					if (mapHasTags || SyncBus.Active)
						next = aprilTagColocationProvider;
				}
				else if (SelectedMethod == ColocationMethod.MetaSharedAnchor)
				{
					next = spatialAnchorColocationProvider;
				}
			}

			if (!ReferenceEquals(colocator.Provider, next)) colocator.Provider?.StopProviding();
			colocator.SetProvider(next);

			if (next == null)
			{
				colocator.StopColocation();
				SetColocated(false);
				return;
			}

			if (!MainXRRig.Instance)
			{
				// The operator still receives and validates provider state without a local XR rig.
				next.StartProviding();
				return;
			}

			colocator.StateChanged -= OnColocatorStateChanged;
			colocator.StateChanged += OnColocatorStateChanged;

			colocator.StartColocation();
			OnColocatorStateChanged(colocator.AlignmentState);
			
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

		/// <summary>Lets the registration tool drive tag detection while authoring.</summary>
		public void SetTagDetectionOverride(bool on)
		{
			if (!aprilTagColocationProvider) return;

			aprilTagColocationProvider.SetDetectionOverride(on);
		}

		// Only Localized counts as colocated. Lost keeps the stale alignment applied but stops
		// anything downstream from treating world space as trustworthy — which means
		// IsColocated can go false mid-session rather than only when the session ends.
		private void OnColocatorStateChanged(ColocationAlignmentState alignmentState)
		{
			if (alignmentState != ColocationAlignmentState.Localized)
				spatialAnchorColocationProvider?.InvalidateLocalFrame();
			// The solver already handles runtimes without reference support. Simulated XR
			// must satisfy the same alignment state that telemetry reports to the operator.
			SetColocated(alignmentState == ColocationAlignmentState.Localized);
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
