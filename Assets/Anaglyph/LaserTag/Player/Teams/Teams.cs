using System;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public static class Teams
	{
		public const byte NumTeams = 3;

		public static readonly Color[] Colors = new Color[]
		{
			Color.white, // blank team
			new Color(255 / 255f, 25  / 255f, 0   / 255f),
			new Color(0   / 255f, 140 / 255f, 255 / 255f),
		};

		public static readonly string[] TeamNames = new string[]
		{
			"None",
			"Red",
			"Blue",
		};
	}
}
