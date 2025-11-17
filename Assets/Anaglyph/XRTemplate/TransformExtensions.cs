using UnityEngine;

namespace Anaglyph.XRTemplate
{
	public static class TransformExtensions
	{
		public static void RotateAroundPoint(this Transform t, Vector3 p, Quaternion q)
		{
			var toPivot = Matrix4x4.Translate(p);
			var fromPivot = toPivot.inverse;
			var rot = Matrix4x4.Rotate(q);
			var m = fromPivot * rot * toPivot;
			var final = t.localToWorldMatrix * m;

			t.position = final.GetPosition();
			t.rotation = final.rotation;
		}

		public static Matrix4x4 Flatten(this Matrix4x4 mat)
		{
			var rot = Flatten(mat.rotation);

			var pos = mat.GetPosition();
			var scl = mat.lossyScale;
			return Matrix4x4.TRS(pos, rot, scl);
		}

		public static Quaternion Flatten(this Quaternion r)
		{
			var forw = r * Vector3.forward;
			forw.y = 0;
			if (forw.magnitude == 0)
				forw = Vector3.forward;
			forw = forw.normalized;

			return Quaternion.LookRotation(forw, Vector3.up);
		}

		public static Quaternion Inverse(this Quaternion r)
		{
			return Quaternion.Inverse(r);
		}
	}
}