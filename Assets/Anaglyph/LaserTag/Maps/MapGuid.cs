using System;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// How maps write guids. One format, "N", used for anchor ids, map ids and content versions
	/// alike — the anchor guids are simultaneously trackable ids, local-storage save ids and
	/// shared group ids, so the string in a map file has to round-trip exactly.
	/// </summary>
	internal static class MapGuid
	{
		public static string ToString(Guid guid) => guid.ToString("N");

		/// <summary>
		/// Parses a persisted guid. Map files can be hand-edited or half-written, and MapStore
		/// goes to some trouble to keep a damaged one loadable — throwing here mid-import would
		/// undo that and leave the providers holding half a map.
		/// </summary>
		public static bool TryParse(string value, out Guid guid)
		{
			if (Guid.TryParseExact(value, "N", out guid))
				return true;

			Debug.LogWarning($"Ignoring malformed guid '{value}' in a saved map.");
			return false;
		}
	}
}
