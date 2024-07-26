using System;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public static class TeamManagement
	{
		public const byte NumTeams = 4;

		public static readonly Color[] TeamColors = new Color[]
		{
			Color.white, // blank team
			new Color(216 / 255f, 27  / 255f, 96  / 255f),
			new Color(30  / 255f, 136 / 255f, 229 / 255f),
			new Color(255 / 255f, 193 / 255f, 7   / 255f),
		};

		public static readonly String[] TeamNames = new string[]
		{
			"None",
			"Red",
			"Blue",
			"Yellow",
		};
	}
}
