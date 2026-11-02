using System;
using System.Collections.Generic;
using System.Threading;
using Anaglyph.Audio;
using Anaglyph.LaserTag.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons.Grenades
{
	/// <summary>
	/// A launched grenade that bursts shortly after it first lands. The owner simulates it
	/// and NetworkTransform carries it to everyone else; the burst is one RPC.
	/// </summary>
	[RequireComponent(typeof(Rigidbody))]
	public class SmokeGrenade : NetworkBehaviour
	{
		[SerializeField, Min(0)] private float launchSpeed = 8f;
		[SerializeField, Min(0), Tooltip("Seconds after the first impact before it bursts.")]
		private float impactFuse = 0.75f;
		[SerializeField, Min(0), Tooltip("Seconds after launch it bursts even if it never lands.")]
		private float maxFuse = 3f;
		[SerializeField, Min(0), Tooltip("Linear damping from the first impact on, so it settles near where it lands instead of skidding across the room.")]
		private float landedDamping = 4f;

		[SerializeField] private AudioClip burstSFX;
		[SerializeField] private int despawnDelay = 1;

		private Rigidbody body;
		private NetworkTransform networkTransform;
		private Renderer[] renderers;
		private Collider[] colliders;
		private readonly List<Collider> ownerColliders = new();

		private bool launchPending;
		private Pose launchPose;
		private bool burst;
		private bool landed;
		private float flightDamping;
		private float burstTime;
		private CancellationTokenSource despawnCancelSrc;

		private void Awake()
		{
			body = GetComponent<Rigidbody>();
			networkTransform = GetComponent<NetworkTransform>();
			renderers = GetComponentsInChildren<Renderer>(true);
			colliders = GetComponentsInChildren<Collider>(true);
			flightDamping = body.linearDamping;
		}

		public override void OnNetworkSpawn()
		{
			burst = false;
			SetVisible(true);

			if (!IsOwner)
				return;

			landed = false;
			body.linearDamping = flightDamping;
			burstTime = Time.time + maxFuse;
			// NetworkRigidbody settles kinematic-ness during spawn, so the push waits a step.
			// A pooled grenade's body still holds where it last came to rest, and would put
			// the transform back there by then
			launchPose = new Pose(transform.position, transform.rotation);
			launchPending = true;
			IgnoreShooter();
		}

		public override void OnNetworkDespawn()
		{
			launchPending = false;
			despawnCancelSrc?.Cancel();
		}

		// launched from the hand, so it would otherwise bounce off its own shooter's head
		private void IgnoreShooter()
		{
			PlayerAvatar shooter = PlayerAvatar.Local;
			if (shooter == null)
				return;

			ownerColliders.Clear();
			shooter.GetComponentsInChildren(true, ownerColliders);
			foreach (Collider own in colliders)
			foreach (Collider other in ownerColliders)
				Physics.IgnoreCollision(own, other);
		}

		private void FixedUpdate()
		{
			if (!launchPending || body.isKinematic)
				return;

			launchPending = false;
			body.position = launchPose.position;
			body.rotation = launchPose.rotation;
			// teleport, so nobody watches it streak over from where it last landed
			networkTransform.Teleport(launchPose.position, launchPose.rotation, transform.localScale);
			body.linearVelocity = launchPose.forward * launchSpeed;
			body.angularVelocity = Vector3.zero;
		}

		private void OnCollisionEnter(Collision collision)
		{
			if (!IsSpawned || !IsOwner || launchPending || landed)
				return;

			landed = true;
			body.linearDamping = landedDamping;
			if (!burst)
				burstTime = Mathf.Min(burstTime, Time.time + impactFuse);
		}

		private void Update()
		{
			if (IsSpawned && IsOwner && !burst && Time.time >= burstTime)
				BurstRpc(transform.position);
		}

		[Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
		private void BurstRpc(Vector3 position)
		{
			burst = true;
			SetVisible(false);
			if (burstSFX != null)
				AudioPool.Play(burstSFX, position);

			if (IsOwner)
			{
				body.linearVelocity = Vector3.zero;
				body.angularVelocity = Vector3.zero;
				DelayedDespawn();
			}
		}

		private void SetVisible(bool visible)
		{
			foreach (Renderer r in renderers)
				r.enabled = visible;
		}

		private async void DelayedDespawn()
		{
			despawnCancelSrc = new CancellationTokenSource();
			CancellationToken ctn = despawnCancelSrc.Token;

			try
			{
				await Awaitable.WaitForSecondsAsync(despawnDelay, ctn);
				ctn.ThrowIfCancellationRequested();
				NetworkObject.Despawn(true);
			}
			catch (OperationCanceledException)
			{
			}
		}
	}
}
