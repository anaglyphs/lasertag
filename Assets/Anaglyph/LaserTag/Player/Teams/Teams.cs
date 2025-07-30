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
			new Color(216 / 255f, 27  / 255f, 96  / 255f),
			new Color(30  / 255f, 136 / 255f, 229 / 255f),
		};

		public static readonly string[] TeamNames = new string[]
		{
			"None",
			"Red",
			"Blue",
		};
	}
}
