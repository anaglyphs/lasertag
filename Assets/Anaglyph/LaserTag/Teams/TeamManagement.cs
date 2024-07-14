using UnityEngine;

namespace Anaglyph.Lasertag
{
    public static class TeamManagement
    {
        public const int NumTeams = 3;

        public static readonly Color[] TeamColors = new Color[]
        {
            Color.white,
            new Color(216, 27, 96),
            new Color(30, 136, 229),
			new Color(255, 193, 7),
		};
    }
}
