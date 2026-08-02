// using System;
//
// namespace Anaglyph.XRTemplate.SharedSpaces
// {
//
//
// 	/// <summary>
// 	/// Interface for systems that align the coordinate spaces of all headsets connected to a multiplayer session
// 	/// so that their virtual coordinate spaces all map identically to the shared physical environment
// 	/// (or at least as close as possible).
// 	/// </summary>
// 	public interface IColocator
// 	{
// 		ColocationState State { get; }
// 		event Action<ColocationState> StateChanged;
// 		void StartColocation();
// 		void StopColocation();
// 	}
// }
