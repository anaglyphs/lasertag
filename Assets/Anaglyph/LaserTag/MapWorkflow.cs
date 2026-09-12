using System;
using Anaglyph.LaserTag.Maps;

namespace Anaglyph.LaserTag
{
	/// <summary>The device's map workflow. Alignment remains an independent observed fact.</summary>
	public enum MapPhase
	{
		Local,
		Hosting,
		SwitchingMap,
		AwaitingSessionMap,
		AdoptingSessionMap,
		FollowingSession,
		RestoringLocalMap,
		Stopped
	}

	/// <summary>
	/// Owns lifecycle transitions and their payloads. Scene helpers do not keep a second copy
	/// of this state. The operation number prevents an old disconnect continuation from
	/// restoring its world after a newer load or session has taken over.
	/// </summary>
	public sealed class MapWorkflow
	{
		public MapPhase Phase { get; private set; } = MapPhase.Local;
		public int Operation { get; private set; }
		public GameMap IncomingMap { get; private set; }
		public bool AdoptionChangesFrame { get; private set; }
		private float switchStartedAt;

		// Retrying a content update does not discard a frame adopted earlier in this session.
		public bool HasSessionFrame => Phase == MapPhase.FollowingSession ||
			(Phase == MapPhase.AdoptingSessionMap && !AdoptionChangesFrame);

		public void EnterSession(bool authority)
		{
			Enter(authority ? MapPhase.Hosting : MapPhase.AwaitingSessionMap);
		}

		public void BeginHosting(bool changingMap, float now)
		{
			Enter(MapPhase.Hosting);
			if (changingMap)
				BeginSwitch(now);
		}

		public void BeginSwitch(float now)
		{
			Enter(MapPhase.SwitchingMap);
			switchStartedAt = now;
		}

		public bool SwitchTimedOut(float now, float timeout) =>
			Phase == MapPhase.SwitchingMap && now - switchStartedAt >= timeout;

		public void FinishSwitch()
		{
			if (Phase == MapPhase.SwitchingMap)
				Enter(MapPhase.Hosting);
		}

		public void AwaitSessionMap()
		{
			Enter(MapPhase.AwaitingSessionMap);
		}

		public void BeginAdoption(GameMap incoming, bool changesFrame)
		{
			if (incoming == null)
				throw new ArgumentNullException(nameof(incoming));

			Enter(MapPhase.AdoptingSessionMap);
			IncomingMap = incoming;
			AdoptionChangesFrame = changesFrame;
		}

		public void FinishAdoption()
		{
			if (Phase != MapPhase.AdoptingSessionMap)
				throw new InvalidOperationException("No map adoption is in progress.");

			Enter(MapPhase.FollowingSession);
		}

		public int BeginRestore()
		{
			Enter(MapPhase.RestoringLocalMap);
			return Operation;
		}

		public bool IsCurrentRestore(int operation) =>
			Phase == MapPhase.RestoringLocalMap && Operation == operation;

		public void EnterLocal() => Enter(MapPhase.Local);
		public void Stop() => Enter(MapPhase.Stopped);

		private void Enter(MapPhase phase)
		{
			Phase = phase;
			Operation++;
			IncomingMap = null;
			AdoptionChangesFrame = false;
		}
	}

	/// <summary>
	/// A single decision table for game commands and their UI explanations. Inputs are facts
	/// captured by the coordinator; evaluating permissions has no side effects or singletons.
	/// </summary>
	public readonly struct MapPolicy
	{
		private readonly MapPhase phase;
		private readonly bool hasMap;
		private readonly bool empty;
		private readonly bool hasTags;
		private readonly bool hasAnchors;
		private readonly bool systemDeterminedFrame;
		private readonly bool twoTagFrame;
		private readonly bool frameAgrees;
		private readonly bool sessionHolding;
		private readonly bool roundInProgress;
		private readonly bool sessionUsesTags;
		private readonly bool operatorManagedSession;

		public MapPolicy(MapPhase phase, bool hasMap, bool empty, bool hasTags,
			bool frameAgrees, bool sessionHolding, bool roundInProgress, bool sessionUsesTags,
			bool operatorManagedSession = false, bool hasAnchors = true, bool systemDeterminedFrame = false,
			bool twoTagFrame = false)
		{
			this.phase = phase;
			this.hasMap = hasMap;
			this.empty = empty;
			this.hasTags = hasTags;
			this.hasAnchors = hasAnchors && !empty;
			this.systemDeterminedFrame = systemDeterminedFrame && !empty;
			this.twoTagFrame = twoTagFrame;
			this.frameAgrees = frameAgrees;
			this.sessionHolding = sessionHolding;
			this.roundInProgress = roundInProgress;
			this.sessionUsesTags = sessionUsesTags;
			this.operatorManagedSession = operatorManagedSession;
		}

		// Blockers are stable copy keys; null means the action is available.
		private string TransitionBlocker => phase switch
		{
			MapPhase.AwaitingSessionMap => "blocker.waiting-for-the-session-s-map",
			MapPhase.AdoptingSessionMap => "blocker.saving-the-incoming-map",
			MapPhase.RestoringLocalMap => "blocker.waiting-for-the-session-to-end",
			MapPhase.SwitchingMap => "blocker.waiting-for-the-map-change",
			MapPhase.Stopped => "blocker.map-system-is-stopped",
			_ => sessionHolding ? "blocker.waiting-for-the-map-change" : null
		};

		public bool CanCaptureScene => phase is MapPhase.Local or MapPhase.Hosting or MapPhase.SwitchingMap;
		public bool CanRecordReferences => CanCaptureScene || phase == MapPhase.FollowingSession;
		public bool CanCreateMap => phase is MapPhase.Local or MapPhase.Hosting or MapPhase.SwitchingMap;
		public bool PublishesContent => phase is MapPhase.Hosting or MapPhase.SwitchingMap;

		public bool FrameIsTrusted => TransitionBlocker == null && hasMap && frameAgrees;

		public static bool CanBootstrapFrame(bool hasTags, int anchorCount, bool empty,
			bool inSession, bool isAuthority) => !hasTags && anchorCount == 0 &&
			(!inSession || isAuthority || empty);

		public bool HasAlignmentReferences => hasTags || hasAnchors || systemDeterminedFrame || twoTagFrame;

		public string ReferenceSetupBlocker => TransitionBlocker ??
			(hasMap && HasAlignmentReferences && !frameAgrees ? "alignment.align-before-reference-setup" : null);

		public string ColocationMethodBlocker(bool targetHasReferences) => TransitionBlocker ??
			(!hasMap ? "blocker.load-a-map-first" : roundInProgress ? "blocker.wait-until-the-round-ends" :
			 HasAlignmentReferences && !targetHasReferences
				? ReferenceSetupBlocker ?? "alignment.target-needs-reference" : null);

		public string ColocationPreferenceBlocker => ColocationPreferenceBlockerFor(operatorRequest: false);

		// The operator owns this choice in managed sessions. The authority checks the actual
		// requester too, so receiving a headset's request does not give it operator privileges.
		public string ColocationPreferenceBlockerFor(bool operatorRequest) => TransitionBlocker ??
			(operatorManagedSession && !operatorRequest ? "blocker.the-operator-sets-the-alignment-method" :
			 roundInProgress ? "blocker.wait-until-the-round-ends" : null);
		public bool CanProbe => phase == MapPhase.Local;
		public bool NeedsFirstTag => (phase is MapPhase.Hosting or MapPhase.FollowingSession) &&
			TransitionBlocker == null && hasMap && !hasTags && sessionUsesTags;

		public string EditBlocker => TransitionBlocker ??
			(hasMap && !frameAgrees ? "blocker.waiting-for-alignment" : null);

		public string TagRegistrationBlocker => TransitionBlocker ??
			(hasTags && !frameAgrees ? "blocker.align-to-this-map-s-tags-first" : EditBlocker);

		// Removing a moved tag must remain possible while alignment is lost.
		public string TagRemovalBlocker => TransitionBlocker;
		public string TagSizeBlocker => TransitionBlocker ??
			(hasTags ? "blocker.unregister-this-map-s-tags-to-change-their-size" : null);

		public string RenameBlocker => TransitionBlocker ??
			(phase == MapPhase.FollowingSession ? "blocker.only-the-host-can-rename-the-map" : null);

		private string ManageMapsBlocker => phase switch
		{
			MapPhase.Local => null,
			MapPhase.Hosting or MapPhase.SwitchingMap => roundInProgress ? "blocker.not-during-a-round" : null,
			MapPhase.AwaitingSessionMap or MapPhase.AdoptingSessionMap or MapPhase.FollowingSession =>
				"blocker.only-the-host-can-change-the-map",
			_ => TransitionBlocker
		};

		public string NewMapBlocker
		{
			get
			{
				if (ManageMapsBlocker != null)
					return ManageMapsBlocker;
				if (roundInProgress)
					return "blocker.not-during-a-round";
				if (!hasMap || empty)
					return "blocker.already-a-blank-map";

				return null;
			}
		}

		public string DeleteMapBlocker(bool isCurrent)
		{
			if (phase == MapPhase.Stopped)
				return TransitionBlocker;
			if (isCurrent && phase != MapPhase.Local)
				return "blocker.cannot-delete-the-active-session-map";

			return null;
		}

		public string ChangeMapBlocker(GameMap target, bool alreadyLoaded, MapPresence presence)
		{
			if (ManageMapsBlocker != null)
				return ManageMapsBlocker;
			if (target == null)
				return "blocker.map-is-missing";
			if (alreadyLoaded)
				return "blocker.already-loaded";
			if (presence == MapPresence.Elsewhere)
				return "blocker.map-belongs-to-another-room";

			return null;
		}
	}
}
