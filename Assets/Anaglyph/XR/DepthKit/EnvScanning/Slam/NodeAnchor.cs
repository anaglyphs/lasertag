using System;
using System.Collections.Generic;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.DepthKit.EnvScanning.Slam
{
	/// <summary>
	/// Moves a transform with the shared pose graph, the way the environment scan moves:
	/// it is anchored to the node that owns the chunk it sits in, holds its pose relative
	/// to that node, and is put back there whenever a solve moves the node. Moving the
	/// transform by any other means (a grab, a drop, an editor) re-anchors it where it
	/// ends up.
	///
	/// For a networked object only the peer with authority over it should move it; set
	/// <see cref="HasAuthority"/> and every other peer leaves the transform to replication.
	/// </summary>
	[DisallowMultipleComponent]
	public class NodeAnchor : MonoBehaviour
	{
		[Tooltip("Seconds between re-anchoring to the chunk underneath while left alone, so the object follows its surface's anchor as that changes")]
		[SerializeField] private float reanchorInterval = 2f;

		[Tooltip("Movement (meters) by something else that counts as the object having been moved, not jitter")]
		[SerializeField] private float externalMoveTolerance = 0.002f;

		/// <summary>Whether this peer moves the transform; null means always (local-only objects).</summary>
		public Func<bool> HasAuthority { get; set; }

		/// <summary>The node this transform moves with; 0 while the graph is empty.</summary>
		public ulong Node { get; private set; }

		private float3 relativePosition;
		private quaternion relativeRotation = quaternion.identity;
		private Vector3 lastPosition;
		private Quaternion lastRotation;
		private float nextReanchor;

		private static readonly List<NodeAnchor> all = new();
		public static IReadOnlyList<NodeAnchor> All => all;

		private bool Authoritative => HasAuthority == null || HasAuthority();

		private void OnEnable()
		{
			all.Add(this);
			PoseGraph.Shared.Solved += OnSolved;
			PoseGraph.Shared.Cleared += OnCleared;
			Reanchor();
		}

		private void OnDisable()
		{
			all.Remove(this);
			PoseGraph.Shared.Solved -= OnSolved;
			PoseGraph.Shared.Cleared -= OnCleared;
		}

		private void OnCleared() => Node = 0;

		private void LateUpdate()
		{
			if (!Authoritative)
			{
				Remember();
				return;
			}

			bool movedExternally = Vector3.Distance(transform.position, lastPosition) > externalMoveTolerance ||
				Quaternion.Angle(transform.rotation, lastRotation) > 0.2f;
			bool lost = Node == 0 || !PoseGraph.Shared.Contains(Node);

			if (movedExternally || (lost && PoseGraph.Shared.NodeCount > 0) || Time.unscaledTime >= nextReanchor)
				Reanchor();
		}

		/// <summary>Anchors to the node owning the chunk underneath (or the nearest node), keeping the current pose.</summary>
		public void Reanchor()
		{
			nextReanchor = Time.unscaledTime + reanchorInterval;
			Remember();

			Node = PoseGraph.Shared.AnchorFor(transform.position, EnvScanner.Instance);
			if (Node == 0 || !PoseGraph.Shared.TryGetNode(Node, out PoseNode node)) return;

			relativePosition = node.world.ApplyInverse(transform.position);
			relativeRotation = math.mul(math.inverse(node.world.Rotation), transform.rotation);
		}

		private void OnSolved(IReadOnlyDictionary<ulong, Pose4> before)
		{
			if (Node == 0 || !before.ContainsKey(Node) || !Authoritative) return;
			if (!PoseGraph.Shared.TryGetNode(Node, out PoseNode node)) return;

			transform.SetPositionAndRotation(node.world.Apply(relativePosition), math.mul(node.world.Rotation, relativeRotation));
			Remember();
		}

		private void Remember()
		{
			lastPosition = transform.position;
			lastRotation = transform.rotation;
		}
	}
}
