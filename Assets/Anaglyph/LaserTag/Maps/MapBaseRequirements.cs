using Anaglyph.LaserTag.Objects.Gameplay.Base;
using Anaglyph.LaserTag.Player.Teams;

namespace Anaglyph.LaserTag.Maps
{
	public static class MapBaseRequirements
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
	}
}
