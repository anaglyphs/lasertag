using System;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	/// <summary>
	/// How far a colocator currently trusts the alignment it has produced between tracking
	/// space and the canon (world) frame.
	/// </summary>
	public enum ColocationState
	{
		/// <summary>Not running.</summary>
		Stopped,

		/// <summary>
		/// Running, but has not yet found enough references to align. There is no meaningful
		/// world frame at all.
		/// </summary>
		Searching,

		/// <summary>Aligned. World space can be trusted.</summary>
		Localized,

		/// <summary>
		/// Was localized and isn't anymore — recenter, sleep/wake, tracking loss, or the
		/// references went out of view. The last alignment is still applied, so the world
		/// doesn't visibly jump, but it is stale: anything that writes durable world-space
		/// data (anchor canon poses, map object poses) must stop until this clears.
		///
		/// Distinct from <see cref="Searching"/> because a stale frame is still worth drawing
		/// and worth keeping a map loaded against; no frame at all is not.
		/// </summary>
		Lost
	}

	/// <summary>
	/// Interface for systems that align the coordinate spaces of all headsets connected to a multiplayer session
	/// so that their virtual coordinate spaces all map identically to the shared physical environment
	/// (or at least as close as possible).
	/// </summary>
	public interface IColocator
	{
		ColocationState State { get; }
		event Action<ColocationState> StateChanged;
		void StartColocation();
		void StopColocation();
	}
}
