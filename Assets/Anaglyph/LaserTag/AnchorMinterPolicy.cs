using System;
using System.Collections.Generic;
using Anaglyph.LaserTag.Player;

namespace Anaglyph.LaserTag
{
	/// <summary>Selects one capable headset without granting it map or session ownership.</summary>
	public static class AnchorMinterPolicy
	{
		public static ulong? Select(ulong? current, ulong sessionOwner, bool operatorManaged,
			Guid mapId, ColocationManager.ColocationMethod method,
			IEnumerable<KeyValuePair<ulong, HeadsetReadiness>> headsets)
		{
			ulong? first = null;
			ulong? aligned = null;
			bool currentReady = false;
			bool currentAligned = false;
			foreach (var pair in headsets)
			{
				HeadsetReadiness readiness = pair.Value;
				if (!readiness.CanMintSharedAnchors || mapId == Guid.Empty ||
					readiness.mapId != mapId || readiness.method != method) continue;
				if (!operatorManaged)
				{
					if (pair.Key == sessionOwner) return sessionOwner;
					continue;
				}
				if (!first.HasValue || pair.Key < first.Value) first = pair.Key;
				if (readiness.referenceFrameTrusted && (!aligned.HasValue || pair.Key < aligned.Value)) aligned = pair.Key;
				if (current == pair.Key)
				{
					currentReady = true;
					currentAligned = readiness.referenceFrameTrusted;
				}
			}
			if (currentReady && (currentAligned || !aligned.HasValue)) return current;
			return aligned ?? first;
		}
	}
}
