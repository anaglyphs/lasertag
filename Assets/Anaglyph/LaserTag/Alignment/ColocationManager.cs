using System;
using System.Collections.Generic;
using Anaglyph.LaserTag.Player;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.SharedSpaces.AprilTags;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using UnityEngine;
using Space = Anaglyph.LaserTag.Maps.Space;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Runs one colocation provider at a time through the <see cref="Colocator"/>. Which
	/// method runs is the map layer's decision (<see cref="Maps.SpaceAlignment"/>); this
	/// component switches sources, tracks the alignment state and the tracking generation.
	/// </summary>
	/// <remarks>Awake touches only this component's own fields, so dependents can wire up in Start.</remarks>
	public class ColocationManager : MonoBehaviour
	{
		public static ColocationManager Instance { get; private set; }

		[Serializable]
		public enum ColocationMethod
		{
			MetaSharedAnchor = 0,
			AprilTag = 1,
			// 2 and 3 were the system frame and the separate two-tag method, 4 was scan
			// registration; saved spaces migrate them (see MigrateLegacyMethod)
			Visual = 5
		}

		public static bool IsValidMethod(ColocationMethod method) => method is ColocationMethod.MetaSharedAnchor
			or ColocationMethod.AprilTag or ColocationMethod.Visual;

		/// <summary>Maps the retired values onto their replacements: system frame and two-tag onto AprilTag, scan registration onto visual.</summary>
		public static ColocationMethod MigrateLegacyMethod(ColocationMethod method) => (int)method switch
		{
			2 or 3 => ColocationMethod.AprilTag,
			4 => ColocationMethod.Visual,
			_ => method
		};

		/// <summary>AprilTag alignment without registered tags runs in two-tag mode, which saves no references.</summary>
		public static bool IsTwoTagMode(ColocationMethod method, Space space) =>
			method == ColocationMethod.AprilTag && (space == null || !space.HasTags);

		/// <summary><see cref="UsesSavedReferences(ColocationMethod)"/>, less AprilTag's two-tag mode.</summary>
		public static bool UsesSavedReferences(ColocationMethod method, Space space) =>
			UsesSavedReferences(method) && !IsTwoTagMode(method, space);

		/// <summary>
		/// Aligns to references a space keeps: anchors, tags, or its saved visual map. The
		/// space's frame is then the same from one session to the next, and references of
		/// different kinds share it.
		/// </summary>
		public static bool UsesSavedReferences(ColocationMethod method) =>
			UsesNativeReferences(method) || method == ColocationMethod.Visual;

		/// <summary><see cref="UsesNativeReferences(ColocationMethod)"/>, less AprilTag's two-tag mode.</summary>
		public static bool UsesNativeReferences(ColocationMethod method, Space space) =>
			UsesNativeReferences(method) && !IsTwoTagMode(method, space);

		/// <summary>
		/// Aligns to references the headset observes (anchors, tags), which the map layer
		/// authors and hands over between methods. A visual map is the provider's own.
		/// </summary>
		public static bool UsesNativeReferences(ColocationMethod method) =>
			method is ColocationMethod.MetaSharedAnchor or ColocationMethod.AprilTag;

		/// <summary>A native-reference method the space has no references for falls back to the kind it has.</summary>
		public static ColocationMethod CompatibleMethod(ColocationMethod requested, bool hasTags, bool hasAnchors)
		{
			if (!UsesNativeReferences(requested)) return requested;
			if (!hasTags && hasAnchors) return ColocationMethod.MetaSharedAnchor;
			if (hasTags && !hasAnchors) return ColocationMethod.AprilTag;
			return requested;
		}

		public static bool IsColocated { get; private set; }
		public static Action<bool> Colocated = delegate { };
		[SerializeField] private Colocator colocator;
		[SerializeField] private SpatialAnchorColocationConstraintProvider spatialAnchorColocationProvider;
		[SerializeField] private AprilTagColocationConstraintProvider aprilTagColocationProvider;
		[SerializeField] private VisualColocationConstraintProvider visualProvider;
		private bool applicationPaused;
		private ColocationAlignmentState previousAlignmentState;
		private readonly List<ColocationConstraint> scratch = new();

		/// <summary>The method whose provider is the colocator's source.</summary>
		public ColocationMethod ActiveMethod { get; private set; }

		public event Action SourceChanged = delegate { };
		public IColocationConstraintProvider ActiveProvider => colocator.Provider;
		public bool UsingTagProvider => ActiveProvider != null && ActiveMethod == ColocationMethod.AprilTag;
		public bool UsingAnchorProvider => ActiveProvider != null && ActiveMethod == ColocationMethod.MetaSharedAnchor;

		/// <summary>The tag provider itself is the source, which it only is in two-tag mode.</summary>
		public bool UsingTwoTagMode => ActiveMethod == ColocationMethod.AprilTag &&
			ReferenceEquals(ActiveProvider, aprilTagColocationProvider) && aprilTagColocationProvider.TwoTagMode;

		public bool UsingVisualProvider => ActiveProvider != null && ActiveMethod == ColocationMethod.Visual;

		public VisualColocationConstraintProvider VisualProvider => visualProvider;
		public SpatialAnchorColocationConstraintProvider AnchorProvider => spatialAnchorColocationProvider;
		public AprilTagColocationConstraintProvider TagProvider => aprilTagColocationProvider;
		public FitAgreement Agreement => colocator.Agreement;
		public ColocationAlignmentState AlignmentState => colocator.AlignmentState;

		/// <summary>Counts up whenever poses measured before stop meaning the same place: recenter, pause, alignment lost.</summary>
		public int TrackingGeneration { get; private set; }

		// two-tag mode aligns to a pair of tags, not to the space's saved references. The
		// visual provider's constraints are its own alignment: colocated is aligned
		public bool ReferenceAligned => UsesSavedReferences(ActiveMethod) && !UsingTwoTagMode && ActiveProvider != null &&
		                                ActiveProvider.IsAvailable && IsColocated &&
		                                (ActiveMethod == ColocationMethod.Visual ||
		                                 Agreement.constraintCount > 0 && Agreement.meanAgrees &&
		                                 Agreement.agreeingCount >= Math.Min(Agreement.constraintCount, 2));

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
			ColocationMethod.Visual => visualProvider,
			_ => null
		};

		public void ActivateMethod(ColocationMethod method, bool preserveAlignment = false) =>
			ActivateSource(GetProvider(method), method, preserveAlignment);

		public void ActivateSource(IColocationConstraintProvider source, ColocationMethod method, bool preserveAlignment)
		{
			ActiveMethod = method;
			// The map layer realizes registered tags through the tag provider's own anchors, so
			// the provider is the source itself only when there are none: two-tag mode. Decided
			// here, not per frame, so tags registered while it aligns don't swap its frame
			aprilTagColocationProvider.TwoTagMode = ReferenceEquals(source, aprilTagColocationProvider) &&
				aprilTagColocationProvider.RegisteredTags.Count == 0;

			if (preserveAlignment && source != null) colocator.Handoff(source);
			else
			{
				colocator.SetProvider(source);
				colocator.StartColocation();
			}

			OnStateChanged(colocator.AlignmentState);
			SourceChanged.Invoke();
		}

		public void TrackingOriginChanged()
		{
			TrackingGeneration++;
			aprilTagColocationProvider.ResetPairReferences();
			colocator.InvalidateTracking();
		}

		public void InvalidateAlignment()
		{
			TrackingGeneration++;
			aprilTagColocationProvider.ResetPairReferences();
			colocator.StopColocation();
			SetColocated(false);
		}

		/// <summary>No space is shown: nothing to align to.</summary>
		public void ClearProvider()
		{
			colocator.SetProvider(null);
			SetColocated(false);
		}

		private void Awake()
		{
			Instance = this;
			aprilTagColocationProvider.TrackingReady = () =>
				!applicationPaused && AppFocus.IsFocused && PlayerHeadsetStatus.HeadTrackingReady;
			colocator.StateChanged += OnStateChanged;
		}

		private void OnDestroy()
		{
			colocator.StateChanged -= OnStateChanged;
			colocator.SetProvider(null);
			if (Instance == this)
			{
				Instance = null;
				IsColocated = false;
			}
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
				aprilTagColocationProvider.ResetPairReferences();
			previousAlignmentState = state;
			if (state != ColocationAlignmentState.Localized)
			{
				TrackingGeneration++;
				spatialAnchorColocationProvider.InvalidateLocalFrame();
			}

			SetColocated(state == ColocationAlignmentState.Localized);
		}

		private void SetColocated(bool value)
		{
			if (IsColocated == value) return;
			IsColocated = value;
			Colocated.Invoke(value);
		}

		public void SetTagDetectionOverride(bool on) => aprilTagColocationProvider.SetDetectionOverride(on);
	}
}
