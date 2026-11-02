using Unity.Mathematics;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// How fast this headset's tracking drifts: like a random walk, so its uncertainty grows
	/// with the square root of the distance walked. The one model everything that reasons about
	/// drift goes by: the colocator's filter, the track's trust window and the gate on revisits.
	/// Measured on device at about 1.4 cm/√m and 0.05°/√m.
	/// </summary>
	public readonly struct DriftModel
	{
		public static readonly DriftModel Default = new(0.014f, math.radians(0.05f));

		/// <summary>How many sigmas of drift a measurement is allowed to have moved by.</summary>
		private const float AllowanceSigmas = 3f;

		/// <summary>Position σ gained after one meter walked, meters per √meter.</summary>
		public readonly float positionRate;
		/// <summary>Yaw σ gained after one meter walked, radians per √meter.</summary>
		public readonly float yawRate;

		public DriftModel(float positionRate, float yawRate)
		{
			this.positionRate = positionRate;
			this.yawRate = yawRate;
		}

		/// <summary>Position σ after <paramref name="walked"/> meters, meters.</summary>
		public float PositionSigma(float walked) => positionRate * math.sqrt(math.max(0, walked));

		/// <summary>Yaw σ after <paramref name="walked"/> meters, radians.</summary>
		public float YawSigma(float walked) => yawRate * math.sqrt(math.max(0, walked));

		/// <summary>How far drift may plausibly have moved things in <paramref name="walked"/> meters, meters.</summary>
		public float PositionAllowance(float walked) => AllowanceSigmas * PositionSigma(walked);

		/// <summary>How far drift may plausibly have turned things in <paramref name="walked"/> meters, radians.</summary>
		public float YawAllowance(float walked) => AllowanceSigmas * YawSigma(walked);
	}
}
