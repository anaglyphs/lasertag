using Anaglyph.Permissions;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>Startup policy keeps lack of sharing distinct from an unsuccessful room probe.</summary>
	public static class MapSpaceStartup
	{
		public static Method? InitialMethod(SpaceProbeOutcome probe, CapabilitySupport sharing, bool canProbe)
		{
			if (probe != SpaceProbeOutcome.NoMatches &&
				!(probe == SpaceProbeOutcome.Indeterminate && !canProbe && sharing == CapabilitySupport.Unsupported)) return null;
			return sharing == CapabilitySupport.Unsupported ? Method.AprilTag : Method.MetaSharedAnchor;
		}

		/// <summary>Retarget an unfinished automatic draft without replacing its frame or layout files.</summary>
		public static bool ConfigureDraft(MapSpace space, Method method)
		{
			if (space == null || !space.automaticallyCreated || !space.initializationPending || space.HasReferenceBasedData ||
				method is not (Method.MetaSharedAnchor or Method.AprilTag)) return false;
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
