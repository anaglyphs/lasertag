using Anaglyph.Lasertag.Weapons;
using StrikerLink.Shared.Utils;
using UnityEngine;
using UnityEngine.Serialization;

namespace Anaglyph.Lasertag
{
	public class AutomaticAnimator : MonoBehaviour
	{
		[SerializeField] private Transform cylinder;
		[SerializeField] private int numChambers;
		[SerializeField] private Vector3 rotationAxis = Vector3.forward;
		[SerializeField] private float falloff;
		[SerializeField] private Automatic automatic;
		[SerializeField] private AudioSource spinSFX;

		[SerializeField] private AnimationCurve spinSFXPitch = new(new Keyframe(0, 0), new Keyframe(1, 1));

		private float targetRotSpeed;
		private float rotSpeed;

		private float fireEventStartTime;

		private void OnEnable()
		{
			rotationAxis.Normalize();

			automatic.IsFiringChanged += OnIsFiringChanged;
		}

		private void OnDisable()
		{
			automatic.IsFiringChanged -= OnIsFiringChanged;
		}

		private void OnIsFiringChanged(bool firing)
		{
			if (firing)
			{
				cylinder.localEulerAngles = new Vector3(0, 0, 0);
				fireEventStartTime = Time.time;
				spinSFX.loop = true;
				spinSFX.Play();
			}
		}

		private void Update()
		{
			float maxRotSpeed = numChambers / automatic.FireFrequency * 360;

			if (automatic.IsFiring)
			{
				rotSpeed = maxRotSpeed;

				cylinder.localEulerAngles = rotationAxis * ((fireEventStartTime - Time.time) * rotSpeed);
			}
			else
			{
				rotSpeed = Mathf.Max(0, rotSpeed - Time.deltaTime * falloff * 360);
				cylinder.localEulerAngles += rotationAxis * (rotSpeed * Time.deltaTime);
			}

			float l = rotSpeed / maxRotSpeed;

			spinSFX.pitch = spinSFXPitch.Evaluate(l);
			spinSFX.volume = l;

			if (rotSpeed == 0) spinSFX.Stop();
		}
	}
}