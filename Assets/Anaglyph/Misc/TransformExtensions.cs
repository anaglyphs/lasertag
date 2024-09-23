using UnityEngine;

namespace Anaglyph
{
	public static class TransformExtensions
	{
		public static void SetFrom(this Transform copyTo, Transform source)
		{
			copyTo.position = source.position;
			copyTo.rotation = source.rotation;
		}
	}
}
