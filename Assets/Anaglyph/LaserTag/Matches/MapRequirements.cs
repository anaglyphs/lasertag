using Anaglyph.LaserTag.Objects.Gameplay.Base;
using Anaglyph.LaserTag.Objects.Gameplay.Flag;
using Anaglyph.LaserTag.Player.Teams;
using UnityEngine;

namespace Anaglyph.LaserTag.Matches
{
	public static class MapRequirements
	{
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

		public static bool HasAnyFlag => Object.FindAnyObjectByType<Flag>() != null;
	}
}
