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
			new Color32(0xFF, 0x00, 0x44, 0xFF),
			new Color32(0x00, 0xFF, 0xF8, 0xFF),
		};

		public static readonly string[] TeamNames = new string[]
		{
			"None",
			"Red",
			"Blue",
		};
	}
}
