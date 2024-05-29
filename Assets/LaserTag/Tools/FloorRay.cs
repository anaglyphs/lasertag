using UnityEngine;
using UnityEngine.Events;

namespace LaserTag
{
	[DefaultExecutionOrder(10)]
	public class FloorRay : MonoBehaviour
	{
		public float floorY;
		public UnityEvent<Vector3> FloorCast = new();
		public UnityEvent<Quaternion> FlatOrientation = new();

		private void Update()
		{
			FloorCast.Invoke(Cast());
			FlatOrientation.Invoke(Orient());
		}

		private Vector3 Cast()
		{
			Vector3 pos = transform.position - new Vector3(0, floorY, 0);
			Vector3 forw = transform.forward;

			if (forw.y == 0)
			{
				return new Vector3(pos.x, floorY, pos.z);
			}

			Vector2 slope = new Vector2(forw.x, forw.z) / forw.y;

			return new Vector3(slope.x * -pos.y + pos.x, floorY, slope.y * -pos.y + pos.z);
		}

		private Quaternion Orient()
		{
			Vector3 forw = transform.forward;

			if (forw.y == 1)
				return Quaternion.identity;

			forw.y = 0;
			return Quaternion.LookRotation(forw, Vector3.up);
		}
	}
}