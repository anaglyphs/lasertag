using System.Collections.Generic;
using Anaglyph.Netcode;
using Anaglyph.XR.Input;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Anaglyph.LaserTag.Weapons
{
	[RequireComponent(typeof(HandSubject))]
	public class Wand : MonoBehaviour, IWeapon
	{
		public enum Spell { Fireball, Lightning, Shield }

		[SerializeField] private Transform emitFromTransform;
		[SerializeField] private WeaponVisual visual;
		[SerializeField] private GameObject fireballPrefab;
		[SerializeField] private GameObject lightningPrefab;
		[SerializeField] private GameObject shieldPrefab;
		[SerializeField] private WandSweepTrail sweepTrail;
		[SerializeField, Min(0.1f)] private float minimumShieldSpeed = 1.5f;
		[SerializeField, Min(0.01f)] private float minimumShieldDistance = 0.25f;
		[SerializeField, Min(0.01f)] private float minimumTrailWidth = 0.025f;
		[SerializeField, Min(0.01f)] private float maximumTrailWidth = 0.6f;
		[SerializeField, Min(0.1f)] private float fullWidthSpeed = 4f;
		public UnityEvent onFire = new();

		private readonly WandSweepGesture sweepGesture = new();
		private HandSubject hand;
		private float previousSampleTime;
		private Vector3 previousTip, previousWidthDirection;
		private float previousWidth;
		private bool hasTrailSample;

		private void Awake() => hand = GetComponent<HandSubject>();

		private void LateUpdate()
		{
			const UnityEngine.XR.InputTrackingState requiredTracking = UnityEngine.XR.InputTrackingState.Position |
				UnityEngine.XR.InputTrackingState.Rotation;
			if (sweepTrail == null || hand.Current == null || (hand.Current.TrackingState & requiredTracking) != requiredTracking)
			{
				ResetSweep();
				return;
			}
			float now = Time.time;
			float elapsed = now - previousSampleTime;
			if (hasTrailSample && elapsed < 1f / 30f) return;
			bool blocking = sweepGesture.Sample(hand.Position, elapsed, minimumShieldSpeed, minimumShieldDistance)
				&& WeaponsManagement.CanFire && !hand.Current.InputBlocked;
			Vector3 tip = emitFromTransform.position;
			Vector3 widthDirection = emitFromTransform.forward;
			float width = Mathf.Lerp(minimumTrailWidth, maximumTrailWidth, sweepGesture.Speed / fullWidthSpeed);
			if (hasTrailSample && elapsed <= 0.1f && Vector3.Distance(previousTip, tip) < 1f)
			{
				Vector3 travel = tip - previousTip;
				if (travel.sqrMagnitude > 0.000001f)
				{
					widthDirection = Vector3.ProjectOnPlane(widthDirection, travel.normalized).normalized;
					if (widthDirection.sqrMagnitude < 0.01f)
						widthDirection = Vector3.ProjectOnPlane(emitFromTransform.up, travel.normalized).normalized;
					if (Vector3.Dot(widthDirection, previousWidthDirection) < 0) widthDirection = -widthDirection;
					sweepTrail.Emit(new WandSweepTrail.Segment
					{
						startLeft = previousTip - previousWidthDirection * previousWidth * 0.5f,
						startRight = previousTip + previousWidthDirection * previousWidth * 0.5f,
						endLeft = tip - widthDirection * width * 0.5f,
						endRight = tip + widthDirection * width * 0.5f,
						time = SharedNetworkTime.TimeAsFloat,
						blocking = blocking
					});
				}
			}
			previousTip = tip;
			previousWidthDirection = widthDirection;
			previousWidth = width;
			previousSampleTime = now;
			hasTrailSample = true;
		}

		private void ResetSweep()
		{
			sweepGesture.Reset();
			hasTrailSample = false;
		}

		private static readonly List<Wand> activeWands = new();
		public static IReadOnlyList<Wand> ActiveWands => activeWands;
		public GameObject VisualObject => visual.gameObject;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics() => activeWands.Clear();

		private void OnEnable() => activeWands.Add(this);
		private void OnDisable()
		{
			activeWands.Remove(this);
			ResetSweep();
		}
		public void OnFire(InputAction.CallbackContext context) { }

		public void Cast(Spell spell)
		{
			NetworkManager manager = NetworkManager.Singleton;
			if (!isActiveAndEnabled || manager == null || !manager.IsConnectedClient ||
				manager.ShutdownInProgress || !WeaponsManagement.CanFire)
				return;

			GameObject prefab = spell switch
			{
				Spell.Fireball => fireballPrefab,
				Spell.Lightning => lightningPrefab,
				Spell.Shield => shieldPrefab,
				_ => null
			};

			if (prefab == null || NetworkObjectPool.Instance == null)
				return;

			NetworkObject projectile = NetworkObjectPool.Instance.GetNetworkObject(
				prefab, emitFromTransform.position, emitFromTransform.rotation);
			projectile.SpawnWithOwnership(manager.LocalClientId);
			visual.PlayFire();
			onFire.Invoke();
		}
	}
}
