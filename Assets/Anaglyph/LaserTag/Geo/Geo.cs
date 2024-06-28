using UnityEngine;

namespace Anaglyph.LaserTag
{
    public static class Geo
    {
		public static bool PointIsInCylinder(Vector3 cylPos, float cylRad, float cylHeight, Vector3 point)
		{
			if (point.y > cylPos.y + cylHeight || point.y < cylPos.y)
				return false;

			Vector2 cylPosXY = new Vector2(cylPos.x, cylPos.z);
			Vector2 pointXY = new Vector2(point.x, point.z);

			float distFlat = Vector2.Distance(cylPosXY, pointXY);
			if (distFlat > cylRad)
				return false;

			return true;
		}
	}
}
