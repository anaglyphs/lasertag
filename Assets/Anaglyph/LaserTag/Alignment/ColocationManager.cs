using System;
using System.Collections.Generic;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Player;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.SharedSpaces.AprilTags;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>Requested session method and actual local alignment source have independent lifetimes.</summary>
	/// <remarks>Awake builds every provider and touches no other component's Awake state, so dependents can wire up in Start.</remarks>
	public class ColocationManager : MonoBehaviour
	{
		public static ColocationManager Instance { get; private set; }

		[Serializable]
		public enum ColocationMethod
		{
			MetaSharedAnchor = 0,
			AprilTag = 1,
			// 2 and 3 were the system frame and the separate two-tag method; saved spaces
			// migrate both to AprilTag (see MigrateLegacyMethod)
			ScanRegistration = 4,
			Visual = 5
		}

		public static bool IsValidMethod(ColocationMethod method) => method is ColocationMethod.MetaSharedAnchor
			or ColocationMethod.AprilTag or ColocationMethod.ScanRegistration or ColocationMethod.Visual;

		/// <summary>Maps the retired system-frame and two-tag values onto AprilTag, whose two-tag mode replaces both.</summary>
		public static ColocationMethod MigrateLegacyMethod(ColocationMethod method) =>
			(int)method is 2 or 3 ? ColocationMethod.AprilTag : method;

		/// <summary>AprilTag alignment without registered tags runs in two-tag mode, which saves no references.</summary>
		public static bool IsTwoTagMode(ColocationMethod method, MapSpace space) =>
			method == ColocationMethod.AprilTag && (space == null || !space.HasTags);

		/// <summary><see cref="UsesSavedReferences(ColocationMethod)"/>, less AprilTag's two-tag mode.</summary>
		public static bool UsesSavedReferences(ColocationMethod method, MapSpace space) =>
			UsesSavedReferences(method) && !IsTwoTagMode(method, space);

		// Visual relocalization takes over colocation for now: anchors, tags and scan
		// registration stay in the code but are never selected. Flip
		// PreferredAvailableMethod to return control to the per-space preference
		public static bool UsesSavedReferences(ColocationMethod method) =>
			method is ColocationMethod.MetaSharedAnchor or ColocationMethod.AprilTag;

		public static bool IsColocated { get; private set; }
		public static Action<bool> Colocated = delegate { };
		[SerializeField] private Colocator colocator;
		[SerializeField] private SpatialAnchorColocationConstraintProvider spatialAnchorColocationProvider;
		[SerializeField] private AprilTagColocationConstraintProvider aprilTagColocationProvider;
		[SerializeField] private ScanRegistrationColocationConstraintProvider scanRegistrationProvider;
		[SerializeField] private VisualColocationConstraintProvider visualProvider;
		private readonly SyncVariable<ColocationMethod> methodSync = new("colo.method");
		private ColocationMethod offlineMethod;
		private MapSpace space;
		private bool sessionStarted;
		private bool applicationPaused;
		private ColocationAlignmentState previousAlignmentState;

		private readonly List<ColocationConstraint> scratch = new();
		public bool ManagedSelection { get; set; }
		public bool IsSettingUpTags { get; internal set; }
		public ColocationMethod Method => methodSync.Value;
		public ColocationMethod SelectedMethod => SyncBus.Active ? Method : offlineMethod;
		public ColocationMethod ActiveMethod { get; private set; }
		public ColocationMethod PreferredAvailableMethod => ColocationMethod.Visual;
		public ColocationMethod PreferredSessionMethod => PreferredAvailableMethod;

		public static ColocationMethod CompatibleMethod(ColocationMethod requested, bool hasTags, bool hasContent,
			bool hasAnchors)
		{
			if (!UsesSavedReferences(requested)) return requested;
			if (!hasTags && hasAnchors) return ColocationMethod.MetaSharedAnchor;
			if (hasTags && !hasAnchors) return ColocationMethod.AprilTag;
			return requested;
		}

		public event Action MethodChanged = delegate { };
		public event Action SourceChanged = delegate { };
		public IColocationConstraintProvider ActiveProvider => colocator != null ? colocator.Provider : null;
		public bool UsingTagProvider => ActiveProvider != null && ActiveMethod == ColocationMethod.AprilTag;
		public bool UsingAnchorProvider => ActiveProvider != null && ActiveMethod == ColocationMethod.MetaSharedAnchor;

		/// <summary>The tag provider itself is the source, which it only is in two-tag mode.</summary>
		public bool UsingTwoTagMode => ActiveProvider != null && ActiveMethod == ColocationMethod.AprilTag &&
			aprilTagColocationProvider != null && ReferenceEquals(ActiveProvider, aprilTagColocationProvider) &&
			aprilTagColocationProvider.TwoTagMode;

		public bool UsingScanRegistrationProvider =>
			ActiveProvider != null && ActiveMethod == ColocationMethod.ScanRegistration;

		public bool UsingVisualProvider => ActiveProvider != null && ActiveMethod == ColocationMethod.Visual;

		public ScanRegistrationColocationConstraintProvider ScanRegistrationProvider => scanRegistrationProvider;
		public VisualColocationConstraintProvider VisualProvider => visualProvider;
		public SpatialAnchorColocationConstraintProvider AnchorProvider => spatialAnchorColocationProvider;
		public AprilTagColocationConstraintProvider TagProvider => aprilTagColocationProvider;
		public FitAgreement Agreement => colocator != null ? colocator.Agreement : default;

		public ColocationAlignmentState AlignmentState =>
			colocator != null ? colocator.AlignmentState : ColocationAlignmentState.Stopped;

		public int TrackingGeneration { get; private set; }

		// two-tag mode aligns to a pair of tags, not to the space's saved references
		public bool ReferenceAligned => UsesSavedReferences(ActiveMethod) && !UsingTwoTagMode && ActiveProvider != null &&
		                                ActiveProvider.IsAvailable && IsColocated && Agreement.constraintCount > 0 &&
		                                Agreement.meanAgrees && Agreement.agreeingCount >=
		                                Math.Min(Agreement.constraintCount, 2);

		public int CountRealizableReferences()
		{
			if (ActiveProvider == null || !ActiveProvider.IsAvailable) return 0;
			if (ActiveProvider is CanonColocationProvider) return 1;
			scratch.Clear();
			ActiveProvider.GetColocationConstraints(scratch);
			return scratch.Count;
		}

		public IColocationConstraintProvider GetProvider(ColocationMethod method) => method switch
		{
			ColocationMethod.MetaSharedAnchor => spatialAnchorColocationProvider,
			ColocationMethod.AprilTag => aprilTagColocationProvider,
			ColocationMethod.ScanRegistration => scanRegistrationProvider,
			ColocationMethod.Visual => visualProvider,
			_ => null
		};

		public void ConfigureSpace(MapSpace value, Func<bool> mintingGate)
		{
			space = value?.Clone();
			if (spatialAnchorColocationProvider)
			{
				spatialAnchorColocationProvider.MintingGate = mintingGate;
				spatialAnchorColocationProvider.RoamingMintEnabled = space != null;
				spatialAnchorColocationProvider.RequireSharingForMint = space != null && space.automaticallyCreated &&
				                                                        space.initializationPending &&
				                                                        !space.HasReferenceBasedData;
			}

			if (!SyncBus.Active && space != null) offlineMethod = space.preferredColocationMethod;
			if (!ManagedSelection) ActivateMethod(PreferredAvailableMethod);
		}

		internal void CommitMethod(ColocationMethod method)
		{
			if (!IsValidMethod(method)) return;
			if (SyncBus.Active) methodSync.Value = method;
			else
			{
				offlineMethod = method;
				MethodChanged.Invoke();
			}
		}

		public void ActivateMethod(ColocationMethod method, bool preserveAlignment = false) =>
			ActivateSource(GetProvider(method), method, preserveAlignment);

		public void ActivateSource(IColocationConstraintProvider source, ColocationMethod method,
			bool preserveAlignment)
		{
			ActiveMethod = method;
			// The map layer realizes registered tags through its own observation, so the tag
			// provider is the source itself only when there are none: two-tag mode. Decided
			// here, not per frame, so tags registered while it aligns don't swap its frame
			if (aprilTagColocationProvider)
				aprilTagColocationProvider.TwoTagMode = ReferenceEquals(source, aprilTagColocationProvider) &&
					aprilTagColocationProvider.RegisteredTags.Count == 0;

			if (colocator != null)
			{
				if (preserveAlignment && source != null) colocator.Handoff(source);
				else
				{
					colocator.SetProvider(source);
					colocator.StartColocation();
				}

				OnStateChanged(colocator.AlignmentState);
			}
			else source?.StartProviding();

			SourceChanged.Invoke();
		}

		public void TrackingOriginChanged()
		{
			TrackingGeneration++;
			if (aprilTagColocationProvider) aprilTagColocationProvider.ResetPairReferences();
			colocator?.InvalidateTracking();
		}

		public void InvalidateAlignment()
		{
			TrackingGeneration++;
			if (aprilTagColocationProvider) aprilTagColocationProvider.ResetPairReferences();
			colocator?.StopColocation();
			SetColocated(false);
		}

		public void ClearSpace()
		{
			space = null;
			IsSettingUpTags = false;
			colocator?.SetProvider(null);
			SetColocated(false);
		}

		private void Awake()
		{
			Instance = this;
			// Colocator.Instance may not be set yet; another component's Awake has no guaranteed order
			if (!colocator) colocator = FindFirstObjectByType<Colocator>();

			if (aprilTagColocationProvider)
				aprilTagColocationProvider.TrackingReady = () =>
					!applicationPaused && AppFocus.IsFocused && PlayerHeadsetStatus.HeadTrackingReady;

			if (!visualProvider) visualProvider = GetComponent<VisualColocationConstraintProvider>();

			if (colocator) colocator.StateChanged += OnStateChanged;
			methodSync.Register();
			methodSync.Validate = (_, _) => false;
			methodSync.Synced += OnMethodSynced;
			methodSync.Changed += OnMethodChanged;
			SyncBus.Activated += OnBusActivated;
			SyncBus.Deactivated += OnBusDeactivated;
		}

		private void OnDestroy()
		{
			if (colocator)
			{
				colocator.StateChanged -= OnStateChanged;
				colocator.SetProvider(null);
			}

			methodSync.Synced -= OnMethodSynced;
			methodSync.Changed -= OnMethodChanged;
			methodSync.Unregister();
			SyncBus.Activated -= OnBusActivated;
			SyncBus.Deactivated -= OnBusDeactivated;
			if (Instance == this)
			{
				Instance = null;
				IsColocated = false;
			}
		}

		private void OnBusActivated()
		{
			if (SyncBus.IsAuthority) methodSync.Value = PreferredSessionMethod;
			// Preserve the local source while the coordinator resolves the incoming frame.
		}

		private void OnMethodSynced()
		{
			sessionStarted = true;
			OnMethodChanged(default, Method);
		}

		private void OnMethodChanged(ColocationMethod _, ColocationMethod method)
		{
			if (!sessionStarted && SyncBus.Active) return;
			if (!ManagedSelection) ActivateMethod(method);
			MethodChanged.Invoke();
		}

		private void OnBusDeactivated()
		{
			sessionStarted = false;
			if (!ManagedSelection) ActivateMethod(PreferredAvailableMethod);
			MethodChanged.Invoke();
		}

		private void OnApplicationFocus(bool focused)
		{
			if (AppFocus.IsLoss(focused)) TrackingOriginChanged();
		}

		private void OnApplicationPause(bool paused)
		{
			applicationPaused = paused;
			if (paused) TrackingOriginChanged();
		}

		private void OnStateChanged(ColocationAlignmentState state)
		{
			// ActivateSource also refreshes this status when the source is unchanged.
			// Such refreshes must not discard a partially scanned pair between maps.
			if (state != previousAlignmentState &&
			    state is ColocationAlignmentState.Lost or ColocationAlignmentState.Stopped)
				if (aprilTagColocationProvider) aprilTagColocationProvider.ResetPairReferences();
			previousAlignmentState = state;
			if (state != ColocationAlignmentState.Localized)
			{
				TrackingGeneration++;
				spatialAnchorColocationProvider?.InvalidateLocalFrame();
			}

			SetColocated(state == ColocationAlignmentState.Localized);
		}

		private void SetColocated(bool value)
		{
			if (IsColocated == value) return;
			IsColocated = value;
			Colocated.Invoke(value);
		}

		public void SetTagDetectionOverride(bool on) => aprilTagColocationProvider?.SetDetectionOverride(on);
	}
}
