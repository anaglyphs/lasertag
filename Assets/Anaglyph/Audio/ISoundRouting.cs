using UnityEngine;

namespace Anaglyph.Audio
{
	/// <summary>How a sound reaches the listener around what stands between them.</summary>
	public interface ISoundRouting
	{
		/// <summary>
		/// Where the sound should seem to come from, and how muffled it is (0 clear, 1 fully
		/// muffled). False when nothing is known: the sound plays where it is, unmuffled
		/// </summary>
		bool TryRoute(Vector3 source, out Vector3 apparentSource, out float muffle);
	}
}
