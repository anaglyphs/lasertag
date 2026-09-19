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
using UnityEngine.Serialization;

namespace Anaglyph.LaserTag
{
	/// <summary>Requested session method and actual local alignment source have independent lifetimes.</summary>
	[DefaultExecutionOrder(-150)]
	public class ColocationManager : MonoBehaviour
	{
		public static ColocationManager Instance { get; private set; }
		[Serializable] public enum ColocationMethod { MetaSharedAnchor, AprilTag, SystemDetermined, TwoAprilTags }
		public static bool IsValidMethod(ColocationMethod method) => method is ColocationMethod.MetaSharedAnchor or ColocationMethod.AprilTag or ColocationMethod.SystemDetermined or ColocationMethod.TwoAprilTags;
		public static bool UsesSavedReferences(ColocationMethod method) => method is ColocationMethod.MetaSharedAnchor or ColocationMethod.AprilTag;
		public static bool IsColocated { get; private set; }
		public static Action<bool> Colocated = delegate { };
		[SerializeField] private Colocator colocator;
		[FormerlySerializedAs("spatialAnchorProvider")] [SerializeField] private SpatialAnchorColocationConstraintProvider spatialAnchorColocationProvider;
		[FormerlySerializedAs("tagProvider")] [SerializeField] private AprilTagColocationConstraintProvider aprilTagColocationProvider;
		private SystemDeterminedColocationConstraintProvider systemDeterminedProvider;
		private TwoAprilTagColocationConstraintProvider twoAprilTagProvider;
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
		public ColocationMethod PreferredAvailableMethod => space == null ? ColocationMethod.MetaSharedAnchor :
			CompatibleMethod(space.preferredColocationMethod, space.HasTags, true, space.anchors.Count > 0);
		public ColocationMethod PreferredSessionMethod => PreferredAvailableMethod;
		public static ColocationMethod CompatibleMethod(ColocationMethod requested, bool hasTags, bool hasContent,
			bool hasAnchors, bool systemFrameForTagSetup = false)
		{
			if (!UsesSavedReferences(requested)) return requested;
			if (systemFrameForTagSetup && requested == ColocationMethod.AprilTag && !hasTags) return ColocationMethod.SystemDetermined;
			if (!hasTags && hasAnchors) return ColocationMethod.MetaSharedAnchor;
			if (hasTags && !hasAnchors) return ColocationMethod.AprilTag;
			return requested;
		}
		public event Action MethodChanged = delegate { };
		public event Action SourceChanged = delegate { };
		public IColocationConstraintProvider ActiveProvider => colocator != null ? colocator.Provider : null;
		public bool UsingTagProvider => ActiveProvider != null && ActiveMethod == ColocationMethod.AprilTag;
		public bool UsingAnchorProvider => ActiveProvider != null && ActiveMethod == ColocationMethod.MetaSharedAnchor;
		public bool UsingSystemDeterminedProvider => ActiveProvider != null && ActiveMethod == ColocationMethod.SystemDetermined;
		public bool UsingTwoAprilTagProvider => ActiveProvider != null && ActiveMethod == ColocationMethod.TwoAprilTags;
		public SpatialAnchorColocationConstraintProvider AnchorProvider => spatialAnchorColocationProvider;
		public AprilTagColocationConstraintProvider TagProvider => aprilTagColocationProvider;
		public TwoAprilTagColocationConstraintProvider TwoTagProvider => twoAprilTagProvider;
		public FitAgreement Agreement => colocator != null ? colocator.Agreement : default;
		public ColocationAlignmentState AlignmentState => colocator != null ? colocator.AlignmentState : ColocationAlignmentState.Stopped;
		public int TrackingGeneration { get; private set; }
		public bool ReferenceAligned => UsesSavedReferences(ActiveMethod) && ActiveProvider != null && ActiveProvider.IsAvailable && IsColocated && Agreement.constraintCount > 0 && Agreement.meanAgrees && Agreement.agreeingCount >= Math.Min(Agreement.constraintCount, 2);
		public int CountRealizableReferences()
		{
			if (ActiveProvider == null || !ActiveProvider.IsAvailable) return 0;
			if (UsingSystemDeterminedProvider) return 1;
			scratch.Clear(); ActiveProvider.GetColocationConstraints(scratch); return scratch.Count;
		}
		public IColocationConstraintProvider GetProvider(ColocationMethod method) => method switch
		{
			ColocationMethod.MetaSharedAnchor => spatialAnchorColocationProvider,
			ColocationMethod.AprilTag => aprilTagColocationProvider,
			ColocationMethod.SystemDetermined => systemDeterminedProvider,
			ColocationMethod.TwoAprilTags => twoAprilTagProvider,
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
					space.initializationPending && !space.HasReferenceBasedData;
			}
			if (!SyncBus.Active && space != null) offlineMethod = space.preferredColocationMethod;
			if (!ManagedSelection) ActivateMethod(PreferredAvailableMethod);
		}
		internal void CommitMethod(ColocationMethod method)
		{
			if (!IsValidMethod(method)) return;
			if (SyncBus.Active) methodSync.Value = method;
			else { offlineMethod = method; MethodChanged.Invoke(); }
		}
		public void ActivateMethod(ColocationMethod method, bool preserveAlignment = false) =>
			ActivateSource(GetProvider(method), method, preserveAlignment);
		public void ActivateSource(IColocationConstraintProvider source, ColocationMethod method, bool preserveAlignment)
		{
			ActiveMethod = method;

			if (colocator != null)
			{
				if (preserveAlignment && source != null) colocator.Handoff(source);
				else { colocator.SetProvider(source); colocator.StartColocation(); }
				OnStateChanged(colocator.AlignmentState);
			}
			else source?.StartProviding();
			SourceChanged.Invoke();
		}
		public void TrackingOriginChanged()
		{
			TrackingGeneration++;
			twoAprilTagProvider?.ResetReferences();
			colocator?.InvalidateTracking();
		}
		public void InvalidateAlignment()
		{
			TrackingGeneration++;
			twoAprilTagProvider?.ResetReferences();
			colocator?.StopColocation(); SetColocated(false);
		}
		public void ClearSpace()
		{
			space = null; IsSettingUpTags = false;
			colocator?.SetProvider(null); SetColocated(false);
		}
		private void Awake()
		{
			Instance = this;
			if (!colocator) colocator = FindFirstObjectByType<Colocator>();
			if (!spatialAnchorColocationProvider) spatialAnchorColocationProvider = SpatialAnchorColocationConstraintProvider.Instance;
			if (!aprilTagColocationProvider) aprilTagColocationProvider = AprilTagColocationConstraintProvider.Instance;
			systemDeterminedProvider = new(MainXRRig.Instance != null ? MainXRRig.TrackingSpace : null);
			twoAprilTagProvider = new(aprilTagColocationProvider,
				MainXRRig.Instance != null ? MainXRRig.TrackingSpace : null,
				() => !applicationPaused && Application.isFocused && PlayerHeadsetStatus.HeadTrackingReady);
			if (colocator) colocator.StateChanged += OnStateChanged;
			methodSync.Register(); methodSync.Validate = (_, _) => false;
			methodSync.Synced += OnMethodSynced; methodSync.Changed += OnMethodChanged;
			SyncBus.Activated += OnBusActivated; SyncBus.Deactivated += OnBusDeactivated;
		}
		private void OnDestroy()
		{
			if (colocator) { colocator.StateChanged -= OnStateChanged; colocator.SetProvider(null); }
			twoAprilTagProvider?.StopProviding();
			methodSync.Synced -= OnMethodSynced; methodSync.Changed -= OnMethodChanged; methodSync.Unregister();
			SyncBus.Activated -= OnBusActivated; SyncBus.Deactivated -= OnBusDeactivated;
			if (Instance == this) { Instance = null; IsColocated = false; }
		}
		private void OnBusActivated()
		{
			if (SyncBus.IsAuthority) methodSync.Value = PreferredSessionMethod;
			// Preserve the local source while the coordinator resolves the incoming frame.
		}
		private void OnMethodSynced() { sessionStarted = true; OnMethodChanged(default, Method); }
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
		private void OnApplicationFocus(bool focused) { if (!focused) TrackingOriginChanged(); }
		private void OnApplicationPause(bool paused)
		{
			applicationPaused = paused;
			if (paused) TrackingOriginChanged();
		}
		private void OnStateChanged(ColocationAlignmentState state)
		{
			// ActivateSource also refreshes this status when the source is unchanged.
			// Such refreshes must not discard a partially scanned pair between maps.
			if (state != previousAlignmentState && state is ColocationAlignmentState.Lost or ColocationAlignmentState.Stopped)
				twoAprilTagProvider?.ResetReferences();
			previousAlignmentState = state;
			if (state != ColocationAlignmentState.Localized)
			{
				TrackingGeneration++; spatialAnchorColocationProvider?.InvalidateLocalFrame();
			}
			SetColocated(state == ColocationAlignmentState.Localized);
		}
		private void SetColocated(bool value) { if (IsColocated == value) return; IsColocated = value; Colocated.Invoke(value); }
		public void SetTagDetectionOverride(bool on) => aprilTagColocationProvider?.SetDetectionOverride(on);
	}
}
