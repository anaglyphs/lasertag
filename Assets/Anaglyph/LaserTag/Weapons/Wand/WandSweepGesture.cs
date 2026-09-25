using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	public sealed class WandSweepGesture
	{
		private Vector3 previousPosition;
		private Vector3 sweepStart;
		private bool hasPreviousPosition;
		public float Speed { get; private set; }

		public bool Sample(Vector3 position, float deltaTime, float minimumSpeed, float minimumDistance)
		{
			if (!hasPreviousPosition || deltaTime <= 0 || deltaTime > 0.1f)
			{
				previousPosition = sweepStart = position;
				hasPreviousPosition = true;
				Speed = 0;
				return false;
			}

			Speed = Vector3.Distance(previousPosition, position) / deltaTime;
			previousPosition = position;
			if (Speed < minimumSpeed || Speed > 12f)
			{
				sweepStart = position;
				return false;
			}

			return Vector3.Distance(sweepStart, position) >= minimumDistance;
		}

		public void Reset()
		{
			hasPreviousPosition = false;
			Speed = 0;
		}
	}
}
