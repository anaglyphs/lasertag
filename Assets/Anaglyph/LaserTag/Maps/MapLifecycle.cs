namespace Anaglyph.LaserTag
{
	public enum MapPhase
	{
		Local,
		Hosting,
		SwitchingMap,
		AwaitingSessionMap,
		FollowingSession,
		RestoringLocalMap,
		Stopped
	}

	/// <summary>Owns session lifecycle and invalidates stale restoration continuations.</summary>
	public sealed class MapLifecycle
	{
		public MapPhase Phase { get; private set; } = MapPhase.Local;
		public int Operation { get; private set; }
		private float switchStartedAt;

		public void EnterSession(bool authority) =>
			Enter(authority ? MapPhase.Hosting : MapPhase.AwaitingSessionMap);

		public void BeginHosting(bool changingMap, float now)
		{
			if (changingMap)
				BeginSwitch(now);
			else
				Enter(MapPhase.Hosting);
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

		public void FollowSession() => Enter(MapPhase.FollowingSession);

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
		}
	}
}
