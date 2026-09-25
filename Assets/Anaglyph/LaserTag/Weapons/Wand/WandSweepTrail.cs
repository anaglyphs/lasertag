using System;
using System.Runtime.InteropServices;
using Anaglyph.LaserTag.Weapons.Bullets;
using Anaglyph.Netcode;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	public class WandSweepTrail : MonoBehaviour
	{
		[Serializable, StructLayout(LayoutKind.Sequential)]
		public struct Segment : INetworkSerializeByMemcpy
		{
			public Vector3 startLeft, startRight, endLeft, endRight;
			public float time;
			public bool blocking;
		}

		[SerializeField] private TrailRenderer trailRenderer;
		[SerializeField, Min(0.01f)] private float lifetime = 0.25f;
		[SerializeField] private Color trailColor = new(0.35f, 0.4f, 1f, 0.35f);
		[SerializeField] private Color shieldColor = new(0.15f, 1f, 0.8f, 0.8f);
		public event Action<Segment> Emitted;
		public ulong OwnerClientId { get; set; }

		private const int CapsuleCapacity = 32;
		private readonly CapsuleCollider[] capsules = new CapsuleCollider[CapsuleCapacity];
		private readonly float[] expiresAt = new float[CapsuleCapacity];
		private bool shieldColorApplied;
		private GameObject trailObject;

		public void Emit(Segment segment)
		{
			Add(segment);
			if (segment.blocking && isActiveAndEnabled)
				ProjectileBarrierHistory.Local.Record(OwnerClientId,
					(segment.startLeft + segment.startRight) * 0.5f,
					(segment.endLeft + segment.endRight) * 0.5f,
					Mathf.Max(Vector3.Distance(segment.startLeft, segment.startRight),
						Vector3.Distance(segment.endLeft, segment.endRight)) * 0.5f,
					segment.time, segment.time + lifetime);
			Emitted?.Invoke(segment);
		}

		public void Add(Segment segment)
		{
			if (!isActiveAndEnabled || SharedNetworkTime.TimeAsFloat - segment.time >= lifetime)
				return;
			if (trailRenderer != null)
			{
				trailRenderer.widthMultiplier = Vector3.Distance(segment.endLeft, segment.endRight);
				SetTrailColor(segment.blocking);
			}
			if (!segment.blocking) return;
			int slot = AcquireCapsule();
			CapsuleCollider capsule = capsules[slot];
			Vector3 start = (segment.startLeft + segment.startRight) * 0.5f;
			Vector3 end = (segment.endLeft + segment.endRight) * 0.5f;
			Vector3 travel = end - start;
			float radius = Mathf.Max(Vector3.Distance(segment.startLeft, segment.startRight),
				Vector3.Distance(segment.endLeft, segment.endRight)) * 0.5f;
			capsule.transform.SetPositionAndRotation((start + end) * 0.5f,
				travel.sqrMagnitude > 0.000001f ? Quaternion.FromToRotation(Vector3.up, travel) : Quaternion.identity);
			capsule.radius = Mathf.Max(radius, 0.001f);
			capsule.height = travel.magnitude + capsule.radius * 2;
			capsule.GetComponent<ProjectileBarrier>().OwnerClientId = OwnerClientId;
			expiresAt[slot] = segment.time + lifetime;
			capsule.enabled = true;
		}

		private void SetTrailColor(bool blocking)
		{
			if (shieldColorApplied == blocking) return;
			shieldColorApplied = blocking;
			Color color = blocking ? shieldColor : trailColor;
			trailRenderer.startColor = color;
			trailRenderer.endColor = Color.clear;
		}

		private int AcquireCapsule()
		{
			float now = SharedNetworkTime.TimeAsFloat;
			int oldest = 0;
			for (int i = 0; i < capsules.Length; i++)
			{
				if (capsules[i] == null)
				{
					if (trailObject == null) trailObject = new GameObject("Wand Sweep Colliders");
					var capsuleObject = new GameObject("Sweep Capsule", typeof(CapsuleCollider), typeof(ProjectileBarrier));
					capsuleObject.transform.SetParent(trailObject.transform, false);
					capsuleObject.layer = Physics.IgnoreRaycastLayer;
					capsules[i] = capsuleObject.GetComponent<CapsuleCollider>();
					capsules[i].direction = 1;
					capsules[i].enabled = false;
					return i;
				}
				if (!capsules[i].enabled || expiresAt[i] <= now) return i;
				if (expiresAt[i] < expiresAt[oldest]) oldest = i;
			}
			return oldest;
		}

		private void LateUpdate()
		{
			float now = SharedNetworkTime.TimeAsFloat;
			for (int i = 0; i < capsules.Length; i++)
				if (capsules[i] != null && expiresAt[i] <= now)
					capsules[i].enabled = false;
		}

		private void OnDisable()
		{
			if (trailRenderer != null) trailRenderer.Clear();
			foreach (CapsuleCollider capsule in capsules)
				if (capsule != null) capsule.enabled = false;
			if (trailObject != null) trailObject.SetActive(false);
		}

		private void OnEnable()
		{
			if (trailObject != null) trailObject.SetActive(true);
			if (trailRenderer != null)
			{
				trailRenderer.Clear();
				trailRenderer.time = lifetime;
				shieldColorApplied = true;
				SetTrailColor(false);
			}
		}

		private void OnDestroy()
		{
			if (trailObject != null) Destroy(trailObject);
		}
	}
}
