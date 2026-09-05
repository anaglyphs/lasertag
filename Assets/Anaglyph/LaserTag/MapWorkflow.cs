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
		private readonly bool frameAgrees;
		private readonly bool sessionHolding;
		private readonly bool roundInProgress;
		private readonly bool sessionUsesTags;

		public MapPolicy(MapPhase phase, bool hasMap, bool empty, bool hasTags,
			bool frameAgrees, bool sessionHolding, bool roundInProgress, bool sessionUsesTags)
		{
			this.phase = phase;
			this.hasMap = hasMap;
			this.empty = empty;
			this.hasTags = hasTags;
			this.frameAgrees = frameAgrees;
			this.sessionHolding = sessionHolding;
			this.roundInProgress = roundInProgress;
			this.sessionUsesTags = sessionUsesTags;
		}

		private string TransitionBlocker => phase switch
		{
			MapPhase.AwaitingSessionMap => "Waiting for the session's map",
			MapPhase.AdoptingSessionMap => "Saving the incoming map",
			MapPhase.RestoringLocalMap => "Waiting for the session to end",
			MapPhase.SwitchingMap => "Waiting for the map change",
			MapPhase.Stopped => "Map system is stopped",
			_ => sessionHolding ? "Waiting for the map change" : null
		};

		public bool CanCaptureScene => phase is MapPhase.Local or MapPhase.Hosting or MapPhase.SwitchingMap;
		public bool CanRecordReferences => CanCaptureScene || phase == MapPhase.FollowingSession;
		public bool CanCreateMap => phase is MapPhase.Local or MapPhase.Hosting or MapPhase.SwitchingMap;
		public bool PublishesContent => phase is MapPhase.Hosting or MapPhase.SwitchingMap;

		public bool FrameIsTrusted => TransitionBlocker == null && hasMap && frameAgrees;
		public bool CanProbe => phase == MapPhase.Local;
		public bool NeedsFirstTag => (phase is MapPhase.Hosting or MapPhase.FollowingSession) &&
			TransitionBlocker == null && hasMap && !hasTags && sessionUsesTags;

		public string EditBlocker => TransitionBlocker ??
			(hasMap && !frameAgrees ? "Waiting for alignment" : null);

		public string TagRegistrationBlocker => TransitionBlocker ??
			(hasTags && !frameAgrees ? "Align to this map's tags first" : EditBlocker);

		// Removing a moved tag must remain possible while alignment is lost.
		public string TagRemovalBlocker => TransitionBlocker;
		public string TagSizeBlocker => TransitionBlocker ??
			(hasTags ? "Unregister this map's tags to change their size" : null);

		public string RenameBlocker => TransitionBlocker ??
			(phase == MapPhase.FollowingSession ? "Only the host can rename the map" : null);

		private string ManageMapsBlocker => phase switch
		{
			MapPhase.Local => null,
			MapPhase.Hosting or MapPhase.SwitchingMap => roundInProgress ? "Not during a round" : null,
			MapPhase.AwaitingSessionMap or MapPhase.AdoptingSessionMap or MapPhase.FollowingSession =>
				"Only the host can change the map",
			_ => TransitionBlocker
		};

		public string NewMapBlocker
		{
			get
			{
				if (ManageMapsBlocker != null)
					return ManageMapsBlocker;
				if (roundInProgress)
					return "Not during a round";
				if (!hasMap || empty)
					return "Already a blank map";

				return null;
			}
		}

		public string DeleteMapBlocker(bool isCurrent)
		{
			if (phase == MapPhase.Stopped)
				return TransitionBlocker;
			if (isCurrent && phase != MapPhase.Local)
				return "Cannot delete the active session map";

			return null;
		}

		public string ChangeMapBlocker(GameMap target, bool alreadyLoaded, MapPresence presence)
		{
			if (ManageMapsBlocker != null)
				return ManageMapsBlocker;
			if (target == null)
				return "Map is missing";
			if (alreadyLoaded)
				return "Already loaded";
			if (presence == MapPresence.Elsewhere)
				return "Map belongs to another room";
			if (phase != MapPhase.Local && sessionUsesTags && !target.HasTags)
				return "Session uses tags; this map has none";

			return null;
		}
	}
}
