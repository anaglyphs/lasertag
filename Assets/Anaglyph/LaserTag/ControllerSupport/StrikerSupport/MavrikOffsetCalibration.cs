using Anaglyph.StrikerSupport;
using Anaglyph.XR.Input;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Hold the peripheral so its barrel lines up with desiredBarrel and squeeze the trigger.
/// The pose is averaged for as long as the trigger is held, and the result stays on screen
/// afterwards - copy the numbers onto the Striker Mavrik asset. Each new squeeze starts over.
/// </summary>
public class MavrikOffsetCalibration : MonoBehaviour
{
	private const float TriggerThreshold = 0.5f;

	private HandInput handInput;
	[SerializeField] private TextMesh textMesh;
	[SerializeField] private Transform desiredBarrel;

	private Vector3 positionSum;
	private Vector4 rotationSum;
	private int sampleCount;
	private bool wasTriggerHeld;

	private void Start()
	{
		handInput = HandInput.Get(Handedness.Left);
	}

	private void Update()
	{
		// the hand registers itself in Awake, which may land after this component's Start
		if (handInput == null)
			handInput = HandInput.Get(Handedness.Left);

		MavrikDevice mavrik = InputSystem.GetDevice<MavrikDevice>();

		if (handInput == null || mavrik == null)
		{
			textMesh.text = handInput == null ? "No left hand" : "No Mavrik device";
			return;
		}

		bool triggerHeld = mavrik.trigger.ReadValue() > TriggerThreshold;

		if (triggerHeld && !wasTriggerHeld)
			sampleCount = 0;

		wasTriggerHeld = triggerHeld;

		if (triggerHeld && handInput.IsTracking)
			AddSample(BarrelFromController());

		textMesh.text = sampleCount == 0
			? "Line the barrel up\nand hold the trigger"
			: Report();
	}

	private void AddSample(Pose barrel)
	{
		Vector4 rotation = new(barrel.rotation.x, barrel.rotation.y, barrel.rotation.z, barrel.rotation.w);

		// a quaternion and its negation are the same rotation, so samples that drifted into
		// the opposite hemisphere would cancel the sum out instead of reinforcing it
		if (Vector4.Dot(rotationSum, rotation) < 0f)
			rotation = -rotation;

		if (sampleCount == 0)
		{
			positionSum = Vector3.zero;
			rotationSum = Vector4.zero;
		}

		positionSum += barrel.position;
		rotationSum += rotation;
		sampleCount++;
	}

	private string Report()
	{
		Vector3 position = positionSum / sampleCount;
		Vector4 r = rotationSum.normalized;
		Vector3 euler = new Quaternion(r.x, r.y, r.z, r.w).eulerAngles;

		return $"{sampleCount} samples\n" +
			"barrelPosition\n" +
			$"  X {position.x:F4}\n" +
			$"  Y {position.y:F4}\n" +
			$"  Z {position.z:F4}\n" +
			"barrelEulerAngles\n" +
			$"  X {Signed(euler.x):F2}\n" +
			$"  Y {Signed(euler.y):F2}\n" +
			$"  Z {Signed(euler.z):F2}";
	}

	/// <summary>
	/// Hand poses are reported in the rig's tracking space, so the target is converted into
	/// that space rather than the controller out to the world - a scaled rig would otherwise
	/// stretch the offset.
	/// </summary>
	private Pose BarrelFromController()
	{
		Transform trackingSpace = handInput.transform.parent;

		Vector3 targetPosition = trackingSpace.InverseTransformPoint(desiredBarrel.position);
		Quaternion targetRotation = Quaternion.Inverse(trackingSpace.rotation) * desiredBarrel.rotation;

		Quaternion controllerInverse = Quaternion.Inverse(handInput.Rotation);

		return new Pose(
			controllerInverse * (targetPosition - handInput.Position),
			controllerInverse * targetRotation);
	}

	// eulerAngles wraps to 0-360, which reads badly for small negative angles
	private static float Signed(float degrees) => degrees > 180f ? degrees - 360f : degrees;
}
