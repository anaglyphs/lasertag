using UnityEngine;

namespace Anaglyph.LaserTag.Player.Teams
{
	public static class Teams
	{
		public const byte NumTeams = 3;

		public const byte Red = 1;
		public const byte Blue = 2;

		public static readonly Color[] Colors = new Color[]
		{
			Color.white, // blank team	
			new Color32(0xFF, 0x1F, 0x00, 0xFF),
			new Color32(0x00,0x5F, 0xFF, 0xFF),
		};

		public static readonly string[] TeamNames = new string[]
		{
			"None",
			"Red",
			"Blue",
		};
	}
}
