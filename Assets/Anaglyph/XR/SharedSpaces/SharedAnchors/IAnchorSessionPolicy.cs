using System.Collections.Generic;

namespace Anaglyph.XR.SharedSpaces.SharedAnchors
{
	/// <summary>
	/// What the embedding game decides about shared anchors in a session: which headset mints
	/// and shares, when this device may mint, and whether a peer's proposal is in the frame
	/// the session is aligned in. The provider works without one: then the session authority
	/// mints, and every proposal in a current operation is accepted.
	/// </summary>
	public interface IAnchorSessionPolicy
	{
		/// <summary>The client that mints and shares anchors, or null while no headset is ready to.</summary>
		ulong? SelectMinter();

		/// <summary>This device is tracked and focused; without it, the local frame is untrusted.</summary>
		bool LocallyReady { get; }

		/// <summary>This device may mint a roaming anchor now (aligned, or defining a frame that has none yet).</summary>
		bool MayMint { get; }

		/// <summary>This device may upload its references for a preparation it was asked to run.</summary>
		bool MayPrepare { get; }

		/// <summary>A preparation with nothing to upload may mint one anchor to share.</summary>
		bool MayMintForPreparation { get; }

		/// <summary>The local tracking generation stamped on proposals, so a stale frame's anchors are refused.</summary>
		int TrackingGeneration { get; }

		bool ValidateRemoteMint(ulong sender);
		bool ValidateTrackingGeneration(ulong sender, int generation);
		bool ValidatePreparedAnchor(ulong sender, AnchorConstraintData anchor);

		/// <summary>The anchors this device would upload for a preparation, with their current canonical poses.</summary>
		void GetSharingCandidates(List<AnchorConstraintData> results);
	}
}
