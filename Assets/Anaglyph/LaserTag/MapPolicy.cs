using Anaglyph.LaserTag.Maps;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag
{
	/// <summary>Command permissions and their localization keys. Null means an action is available.</summary>
	public static class MapPolicy
	{
		public static bool CanManageMaps(bool authority, MapPhase phase) =>
			authority && phase is MapPhase.Local or MapPhase.Hosting;

		public static bool CanCaptureScene(bool authority, MapPhase phase) =>
			authority && phase is MapPhase.Local or MapPhase.Hosting or MapPhase.SwitchingMap;

		public static bool CanEditMap(bool changingMap, bool frameReady, bool operatorCanEditSpace) =>
			!changingMap && (frameReady || operatorCanEditSpace);

		public static string ManageMapsBlocker(bool canManage, bool roundInProgress) =>
			!canManage ? "blocker.only-the-host-can-change-the-map" :
			roundInProgress ? "blocker.not-during-a-round" : null;

		public static string ChangeMapBlocker(bool canManage, bool roundInProgress, bool alreadyLoaded, bool exists) =>
			ManageMapsBlocker(canManage, roundInProgress) ??
			(alreadyLoaded ? "blocker.already-loaded" : !exists ? "blocker.map-is-missing" : null);

		public static string NewMapBlocker(bool canManage, bool roundInProgress, bool hasSpace) =>
			ManageMapsBlocker(canManage, roundInProgress) ?? (!hasSpace ? "space.load-first" : null);

		public static string RenameBlocker(bool authority) =>
			!authority ? "blocker.only-the-host-can-rename-the-map" : null;

		public static string DeleteMapBlocker(string id, string currentId, bool sessionActive) =>
			id == null ? "blocker.no-map-selected" :
			sessionActive && currentId == id ? "blocker.cannot-delete-the-active-session-map" : null;

		public static string SpaceChangeBlocker(bool canManage, bool roundInProgress,
			bool changingAlignment, bool isOperator, MapPresence presence) =>
			!canManage || roundInProgress || changingAlignment ? "space.busy" :
			!isOperator && presence == MapPresence.Elsewhere ? "blocker.map-belongs-to-another-room" : null;

		public static string DeleteSpaceBlocker(bool canManage, bool roundInProgress,
			bool changingAlignment, bool activeSessionSpace) =>
			!canManage || roundInProgress || changingAlignment || activeSessionSpace ? "space.busy" : null;

		public static string ColocationPreferenceBlocker(bool hasSpace, bool roundInProgress,
			bool operatorManagedSession, bool operatorRequest) =>
			!hasSpace ? "space.load-first" :
			roundInProgress ? "blocker.wait-until-the-round-ends" :
			operatorManagedSession && !operatorRequest ? "blocker.the-operator-sets-the-alignment-method" : null;

		public static string ColocationMethodBlocker(Method method, bool hasSpace, bool roundInProgress,
			bool operatorManagedSession, bool operatorRequest) =>
			!ColocationManager.IsValidMethod(method) ? "blocker.unknown-colocation-method" :
			ColocationPreferenceBlocker(hasSpace, roundInProgress, operatorManagedSession, operatorRequest);

		public static bool CanAuthorReferences(bool hasReferences, Method activeMethod,
			bool aligned, bool initializationGrant) =>
			hasReferences ? aligned && ColocationManager.UsesSavedReferences(activeMethod) : initializationGrant;

		public static bool CanInitializeTags(bool inSession, bool isAuthority,
			bool transitionActive, bool isTransitionAuthor) =>
			!inSession || (transitionActive ? isTransitionAuthor : isAuthority);

		public static string TagRegistrationBlocker(bool canAuthorTags) =>
			canAuthorTags ? null : "alignment.align-before-reference-setup";

		public static string TagRemovalBlocker(bool hasSpace, bool changingAlignment) =>
			!hasSpace ? "space.load-first" : changingAlignment ? "space.busy" : null;

		public static string TagSizeBlocker(bool hasSpace, bool hasTags) =>
			!hasSpace ? "space.load-first" :
			hasTags ? "blocker.unregister-this-map-s-tags-to-change-their-size" : null;
	}
}
