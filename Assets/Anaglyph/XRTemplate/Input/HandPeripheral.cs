using UnityEngine;

namespace Anaglyph.Input
{
	/// <summary>
	/// A physical attachment the player mounts a tracked controller into. Poses are relative
	/// to the controller's grip pose, since that is the frame the cradle holds it in.
	/// </summary>
	[CreateAssetMenu(fileName = "Hand Peripheral", menuName = "Anaglyph/Hand Peripheral")]
	public class HandPeripheral : ScriptableObject
	{
		[SerializeField] private Handedness mountedHand;

		[Header("Barrel, relative to the mounted controller")]
		[SerializeField] private Vector3 barrelPosition;
		[SerializeField] private Vector3 barrelEulerAngles;

		public Handedness MountedHand => mountedHand;

		/// <summary>Where shots leave, and where the interaction ray points from.</summary>
		public Pose BarrelFromController =>
			new(barrelPosition, Quaternion.Euler(barrelEulerAngles));
	}
}
