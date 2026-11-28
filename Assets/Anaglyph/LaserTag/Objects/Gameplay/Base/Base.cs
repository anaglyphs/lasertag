using System.Collections.Generic;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player;
using Anaglyph.LaserTag.Player.Teams;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag.Objects.Gameplay.Base
{
	[DefaultExecutionOrder(500)]
	public class Base : NetworkBehaviour
	{
		[SerializeField] private TeamOwner teamOwner;
		public TeamOwner TeamOwner => teamOwner;
		public byte Team => teamOwner.Team;

		[SerializeField] private MeshRenderer meshRenderer;
		[SerializeField] private Collider volume;
		[SerializeField] private RisingGlowRing[] glowRings;
		[SerializeField] private Transform weaponRack;
		[SerializeField, Min(0)] private float weaponRackSecondsIntoRound = 20;
		[SerializeField, Min(0.01f)] private float loweredMeshHeight = 0.5f;
		[SerializeField, Min(0.01f)] private float transitionSeconds = 1f;

		private float raisedMeshHeight;
		private Vector3 raisedWeaponRackPosition;
		private float weaponRackSinkDepth;
		private float meshLoweredFraction;
		private float weaponRackSunkFraction;

		public const string Tag = "Base";

		public static List<Base> AllBases { get; private set; } = new();

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Init() => AllBases = new List<Base>();

		private void OnValidate()
		{
			TryGetComponent(out teamOwner);
			TryGetComponent(out volume);
		}

		private void Awake()
		{
			gameObject.tag = Tag;
			AllBases.Add(this);

			raisedMeshHeight = meshRenderer.transform.localScale.y;
			raisedWeaponRackPosition = weaponRack.localPosition;
		}

		private void Update()
		{
			bool roundOnlySpawn = MatchReferee.State == MatchState.Playing
			                      && MatchReferee.Settings.respawnCondition == RespawnCondition.NextRound;
			bool weaponRackTimeOver = roundOnlySpawn
			                          && MatchReferee.Instance.GetTimeElapsed() >= weaponRackSecondsIntoRound;

			AnimateMeshAndGlowRings(roundOnlySpawn);
			AnimateWeaponRack(weaponRackTimeOver);

			volume.isTrigger = roundOnlySpawn || LocalPlayerInside();
		}

		private void AnimateMeshAndGlowRings(bool lowered)
		{
			float step = Time.deltaTime / transitionSeconds;
			meshLoweredFraction = Mathf.MoveTowards(meshLoweredFraction, lowered ? 1 : 0, step);
			float eased = Mathf.SmoothStep(0, 1, meshLoweredFraction);

			Transform mesh = meshRenderer.transform;
			Vector3 scale = mesh.localScale;
			scale.y = Mathf.Lerp(raisedMeshHeight, loweredMeshHeight, eased);
			mesh.localScale = scale;

			foreach (RisingGlowRing ring in glowRings)
				ring.Visibility = 1 - eased;
		}

		private void AnimateWeaponRack(bool sunk)
		{
			if (sunk && weaponRackSunkFraction == 0)
				weaponRackSinkDepth = MeasureHeightAboveBase(weaponRack);

			float step = Time.deltaTime / transitionSeconds;
			weaponRackSunkFraction = Mathf.MoveTowards(weaponRackSunkFraction, sunk ? 1 : 0, step);
			float eased = weaponRackSunkFraction * weaponRackSunkFraction;

			weaponRack.localPosition = raisedWeaponRackPosition + Vector3.down * (weaponRackSinkDepth * eased);
			weaponRack.gameObject.SetActive(weaponRackSunkFraction < 1);
		}

		private float MeasureHeightAboveBase(Transform root)
		{
			float top = transform.position.y;
			foreach (Renderer childRenderer in root.GetComponentsInChildren<Renderer>())
				top = Mathf.Max(top, childRenderer.bounds.max.y);

			return (top - transform.position.y) / transform.lossyScale.y;
		}

		// Only shooters collide their own bullets and grenades, so each peer's bases need only
		// be solid against its own player: open while their head is inside, so they can fire
		// out, and closed otherwise, so they can't fire in
		private bool LocalPlayerInside()
		{
			PlayerAvatar local = PlayerAvatar.Local;
			return local != null && local.HasSpatialPresence && Contains(local.HeadTransform.position);
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
			AllBases.Remove(this);
		}

		/// <summary>Membership is tested against the volume rather than by contact - a dead
		/// player has no hitbox but still has to be able to walk into one to respawn.</summary>
		public bool Contains(Vector3 point)
		{
			// ClosestPoint returns the point itself when it's inside
			return volume.ClosestPoint(point) == point;
		}
	}
}
