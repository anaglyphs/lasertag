using UnityEngine;
using UnityEngine.Serialization;

namespace Anaglyph.LaserTag.Weapons.Automatic
{
	public class AutomaticAnimator : MonoBehaviour
	{
		[SerializeField] private Transform cylinder;
		[SerializeField] private Vector3 rotationAxis = Vector3.forward;
		[SerializeField] private float maxRotationSpeed = 10800;
		[SerializeField] private float falloff;
		[FormerlySerializedAs("view")] [SerializeField] private WeaponVisual visual;
		[SerializeField] private AudioSource spinSFX;

		[SerializeField] private AnimationCurve spinSFXPitch = new(new Keyframe(0, 0), new Keyframe(1, 1));

		private float rotSpeed;

		private void OnEnable()
		{
			rotationAxis.Normalize();

			visual.Fired += OnFired;
			visual.IsFiringChanged += OnIsFiringChanged;
		}

		private void OnDisable()
		{
			visual.Fired -= OnFired;
			visual.IsFiringChanged -= OnIsFiringChanged;
			spinSFX.Stop();
		}

		private void OnFired()
		{
			rotSpeed = maxRotationSpeed;
			PlaySpinSFX();
		}

		private void OnIsFiringChanged(bool firing)
		{
			if (firing)
			{
				cylinder.localEulerAngles = Vector3.zero;
				rotSpeed = maxRotationSpeed;
				PlaySpinSFX();
			}
		}

		private void Update()
		{
			if (visual.IsFiring)
				rotSpeed = maxRotationSpeed;
			else
				rotSpeed = Mathf.Max(0, rotSpeed - Time.deltaTime * falloff * 360);

			cylinder.localEulerAngles -= rotationAxis * (rotSpeed * Time.deltaTime);

			float l = rotSpeed / maxRotationSpeed;

			spinSFX.pitch = spinSFXPitch.Evaluate(l);
			spinSFX.volume = l;

			if (rotSpeed == 0)
				spinSFX.Stop();
		}

		private void PlaySpinSFX()
		{
			if (spinSFX.isPlaying)
				return;

			spinSFX.loop = true;
			spinSFX.Play();
		}
	}
}
