using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.Audio
{
	public struct SoundReflection
	{
		/// <summary>Where the echo seems to come from: along the bounce's arrival direction, as far as its path is long.</summary>
		public Vector3 apparentSource;

		/// <summary>How much later than the direct sound it arrives.</summary>
		public float delaySeconds;

		public float gain;

		/// <summary>0 clear, 1 fully muffled.</summary>
		public float muffle;
	}

	/// <summary>Echoes of a sound off nearby surfaces.</summary>
	public interface ISoundReflections
	{
		void FindReflections(Vector3 source, List<SoundReflection> reflections);
	}
}
