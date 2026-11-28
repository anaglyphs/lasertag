using Anaglyph.LaserTag.Objects.Gameplay.Base;
using Anaglyph.LaserTag.Objects.Gameplay.Flag;
using Anaglyph.LaserTag.Player.Teams;
using UnityEngine;

namespace Anaglyph.LaserTag.Matches
{
	[System.Flags]
	public enum MissingMatchObjects { None = 0, BothTeamBases = 1, AnyFlag = 2, BothTeamFlags = 4 }

	public static class MapRequirements
	{
		/// <summary>What must still be placed before a match with these settings can start.</summary>
		public static MissingMatchObjects MissingFor(MatchSettings settings)
		{
			MissingMatchObjects missing = HasBothTeamBases ? MissingMatchObjects.None : MissingMatchObjects.BothTeamBases;
			if (settings.pointsPerFlagCapture <= 0) return missing;
			if (OneTeamsFlagsAreEnoughFor(settings)) return HasAnyFlag ? missing : missing | MissingMatchObjects.AnyFlag;
			return HasBothTeamFlags ? missing : missing | MissingMatchObjects.BothTeamFlags;
		}

		public static bool HasBothTeamBases
		{
			get
			{
				bool hasRed = false, hasBlue = false;
				foreach (Base homeBase in Base.AllBases)
				{
					if (!homeBase || !homeBase.isActiveAndEnabled || !homeBase.TeamOwner) continue;
					hasRed |= homeBase.Team == Teams.Red;
					hasBlue |= homeBase.Team == Teams.Blue;
					if (hasRed && hasBlue) return true;
				}
				return false;
			}
		}

		public static bool HasAnyFlag
		{
			get
			{
				FindTeamsWithFlags(out bool hasRed, out bool hasBlue);
				return hasRed || hasBlue;
			}
		}

		public static bool HasBothTeamFlags
		{
			get
			{
				FindTeamsWithFlags(out bool hasRed, out bool hasBlue);
				return hasRed && hasBlue;
			}
		}

		/// <summary>The team whose flags are the only ones in the map. Zero when both or neither team has one.</summary>
		public static byte TeamDefendingTheOnlyFlags
		{
			get
			{
				FindTeamsWithFlags(out bool hasRed, out bool hasBlue);
				if (hasRed == hasBlue) return 0;
				return hasRed ? Teams.Red : Teams.Blue;
			}
		}

		/// <summary>Whether these settings can be played with one team's flags, the other team only defending.</summary>
		public static bool OneTeamsFlagsAreEnoughFor(MatchSettings settings) =>
			settings.respawnCondition == RespawnCondition.NextRound;

		private static void FindTeamsWithFlags(out bool hasRed, out bool hasBlue)
		{
			hasRed = false;
			hasBlue = false;
			foreach (Flag flag in Object.FindObjectsByType<Flag>())
			{
				hasRed |= flag.Team == Teams.Red;
				hasBlue |= flag.Team == Teams.Blue;
			}
		}
	}
}
