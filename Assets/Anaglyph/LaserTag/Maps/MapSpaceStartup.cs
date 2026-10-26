using Anaglyph.Permissions;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>Startup policy keeps lack of sharing distinct from an unsuccessful room probe.</summary>
	public static class MapSpaceStartup
	{
		/// <summary>The method a new space starts with: visual, which needs no references authored and keeps its own.</summary>
		public const Method DefaultMethod = Method.Visual;

		/// <summary>The method for a space made at startup once no saved space is found here; null while that is undecided.</summary>
		public static Method? InitialMethod(SpaceProbeOutcome probe, CapabilitySupport sharing, bool canProbe)
		{
			if (probe != SpaceProbeOutcome.NoMatches &&
				!(probe == SpaceProbeOutcome.Indeterminate && !canProbe && sharing == CapabilitySupport.Unsupported)) return null;
			return DefaultMethod;
		}

		/// <summary>Retarget an unfinished automatic draft without replacing its frame or layout files.</summary>
		public static bool ConfigureDraft(MapSpace space, Method method)
		{
			if (space == null || !space.automaticallyCreated || !space.initializationPending || space.HasReferenceBasedData ||
				method is not (Method.MetaSharedAnchor or Method.AprilTag or Method.Visual)) return false;
			if (space.preferredColocationMethod == method && space.hasPendingSetup == (method == Method.AprilTag) &&
				space.pendingSetupMethod == method && space.pendingSetupIntent == ReferenceTransitionIntent.ActivateTarget) return true;
			space.preferredColocationMethod = method;
			space.hasPendingSetup = method == Method.AprilTag;
			space.pendingSetupMethod = method;
			space.pendingSetupIntent = ReferenceTransitionIntent.ActivateTarget;
			space.MarkChanged();
			return true;
		}
	}
}
