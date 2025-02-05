using UnityEngine;

namespace Anaglyph.Lasertag
{
    public static class Geo
	{
		public static bool PointIsInCylinder(Vector3 cylPos, float radius, float height, Vector3 point)
		{
			// y check
			if (point.y > cylPos.y + height || point.y < cylPos.y)
				return false;

			cylPos.y = 0;
			point.y = 0;

			// radius check
			float distFlat = Vector3.Distance(cylPos, point);
			if (distFlat > radius)
				return false;

			return true;
		}
	}
}
