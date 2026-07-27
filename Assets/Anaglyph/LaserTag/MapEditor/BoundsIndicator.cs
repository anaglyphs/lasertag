using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// Drives a skinned box mesh — eight bones, one per corner, every vertex weighted rigidly
	/// to the corner it belongs to — so that the box wraps the visible geometry of a target
	/// hierarchy. Because the bones only ever translate, corner ornaments keep the size they
	/// were authored at no matter how large or lopsided the box gets.
	///
	/// Rig requirements: exactly eight bones, sitting at the corners of a box in the rest pose
	/// the prefab ships with. Which bone drives which corner is read from that rest pose, so
	/// the bone order in the SkinnedMeshRenderer doesn't matter. The bones' pivots are what
	/// land on the bounds, so put each pivot on its corner and model the ornament around it.
	/// </summary>
	public class BoundsIndicator : MonoBehaviour
	{
		private const int CornerCount = 8;

		private static readonly Vector3[] CornerDirections =
		{
			new(-1, -1, -1), new(1, -1, -1), new(-1, 1, -1), new(1, 1, -1),
			new(-1, -1,  1), new(1, -1,  1), new(-1, 1,  1), new(1, 1,  1),
		};

		[SerializeField] private SkinnedMeshRenderer skinnedMesh;

		[Tooltip("Object to wrap. Can be left empty and set at runtime instead.")]
		[SerializeField] private Transform target;

		[Tooltip("Grown outward from the visual bounds on every side, in meters.")]
		[SerializeField] private float padding = 0.02f;

		[Tooltip("Keeps corners from overlapping on flat or tiny objects, in meters.")]
		[SerializeField] private float minSize = 0.05f;

		[Tooltip("Align the box with the target's own axes instead of the world's.")]
		[SerializeField] private bool orientToTarget = true;

		[Tooltip("Roughly how long the box takes to catch up to the target. 0 snaps.")]
		[SerializeField] private float smoothTime = 0.05f;

		[Tooltip("Also measure renderers on deactivated GameObjects.")]
		[SerializeField] private bool includeInactive = false;

		public Transform Target => target;

		// Rest pose, in the renderer's space, captured before anything moves.
		private Transform[] bones;
		private Vector3[] cornerDirections;
		private Quaternion[] restRotations;
		// How far the mesh sticks out past the corner bones. Only used to pad culling bounds.
		private Vector3 overhang;

		// Where the box is right now, as opposed to where the target wants it.
		private Vector3 center;
		private Quaternion rotation = Quaternion.identity;
		private Vector3 halfExtents;
		private bool isPosed;

		private void Reset()
		{
			skinnedMesh = GetComponentInChildren<SkinnedMeshRenderer>();
		}

		private void OnEnable()
		{
			// Don't sweep across the room from wherever we were left.
			isPosed = false;
		}

		private void OnDisable()
		{
			if (skinnedMesh != null)
				skinnedMesh.enabled = false;
		}

		/// <summary>
		/// Wraps a new object, or nothing at all when passed null. The box jumps straight to
		/// the new target rather than sweeping across the room to get there.
		/// </summary>
		public void SetTarget(Transform target)
		{
			this.target = target;
			isPosed = false;
		}

		/// <summary>Skips smoothing on the next update — use it after the target teleports.</summary>
		public void Snap()
		{
			isPosed = false;
		}

		// LateUpdate so we measure the target after anything that moves or animates it.
		private void LateUpdate()
		{
			// Read the rig the first time we're asked to draw rather than in Awake — the
			// editor runs Awake the moment the component is added, before the renderer
			// field has been filled in, and complaining then would be noise.
			if (bones == null && !ReadRig())
			{
				enabled = false;
				return;
			}

			if (target == null)
			{
				skinnedMesh.enabled = false;
				return;
			}

			Quaternion targetRotation = orientToTarget ? target.rotation : Quaternion.identity;

			// A rotation-only frame anchored on the target: extents stay in world units, and
			// the numbers stay small however far from the world origin the target is.
			Matrix4x4 boxToWorld = Matrix4x4.TRS(target.position, targetRotation, Vector3.one);

			if (!TryGetVisualBounds(target, boxToWorld.inverse, out Bounds bounds, includeInactive, transform))
			{
				skinnedMesh.enabled = false;
				return;
			}

			Vector3 targetCenter = boxToWorld.MultiplyPoint3x4(bounds.center);
			Vector3 targetHalfExtents = Vector3.Max(bounds.extents + Vector3.one * padding,
				Vector3.one * (minSize / 2));

			if (isPosed && smoothTime > 0)
			{
				float t = 1 - Mathf.Exp(-Time.deltaTime / smoothTime);
				center = Vector3.Lerp(center, targetCenter, t);
				rotation = Quaternion.Slerp(rotation, targetRotation, t);
				halfExtents = Vector3.Lerp(halfExtents, targetHalfExtents, t);
			}
			else
			{
				center = targetCenter;
				rotation = targetRotation;
				halfExtents = targetHalfExtents;
				isPosed = true;
			}

			// Skinning ignores this transform, but keeping it on the box makes the object
			// sensible to look at in the hierarchy and to parent things to.
			transform.SetPositionAndRotation(center, rotation);

			for (int i = 0; i < CornerCount; i++)
				bones[i].SetPositionAndRotation(Corner(i, halfExtents), rotation * restRotations[i]);

			UpdateCullingBounds();

			skinnedMesh.enabled = true;
		}

		private Vector3 Corner(int i, Vector3 extents)
			=> center + rotation * Vector3.Scale(cornerDirections[i], extents);

		/// <summary>
		/// A skinned mesh is culled against bounds that don't follow the bones, so the
		/// authored ones would blink the box out of existence once it grows past them.
		/// </summary>
		private void UpdateCullingBounds()
		{
			Transform space = skinnedMesh.rootBone != null ? skinnedMesh.rootBone : skinnedMesh.transform;
			Vector3 extents = halfExtents + overhang;

			Bounds bounds = new(space.InverseTransformPoint(Corner(0, extents)), Vector3.zero);
			for (int i = 1; i < CornerCount; i++)
				bounds.Encapsulate(space.InverseTransformPoint(Corner(i, extents)));

			skinnedMesh.localBounds = bounds;
		}

		// The rest pose the prefab ships with is the reference the box deforms away from.
		// Read here rather than from Mesh.bindposes, which model importers strip unless the
		// mesh is marked read/write.
		private bool ReadRig()
		{
			if (skinnedMesh == null)
			{
				Debug.LogError($"{nameof(BoundsIndicator)} needs a {nameof(SkinnedMeshRenderer)}", this);
				return false;
			}

			bones = skinnedMesh.bones;

			if (bones.Length != CornerCount)
			{
				Debug.LogError($"{nameof(BoundsIndicator)} needs a rig with exactly {CornerCount} " +
					$"corner bones; {skinnedMesh.name} has {bones.Length}", this);
				return false;
			}

			cornerDirections = new Vector3[CornerCount];
			restRotations = new Quaternion[CornerCount];

			Matrix4x4 worldToRig = skinnedMesh.transform.worldToLocalMatrix;
			Vector3[] restPositions = new Vector3[CornerCount];
			Bounds restBox = new();

			for (int i = 0; i < CornerCount; i++)
			{
				Matrix4x4 rest = worldToRig * bones[i].localToWorldMatrix;
				restPositions[i] = rest.GetPosition();
				restRotations[i] = rest.rotation;

				if (i == 0)
					restBox = new Bounds(restPositions[0], Vector3.zero);
				else
					restBox.Encapsulate(restPositions[i]);
			}

			int cornersSeen = 0;

			for (int i = 0; i < CornerCount; i++)
			{
				Vector3 offset = restPositions[i] - restBox.center;
				cornerDirections[i] = new Vector3(Mathf.Sign(offset.x), Mathf.Sign(offset.y), Mathf.Sign(offset.z));

				int corner = (offset.x >= 0 ? 1 : 0) | (offset.y >= 0 ? 2 : 0) | (offset.z >= 0 ? 4 : 0);
				cornersSeen |= 1 << corner;
			}

			if (cornersSeen != 0xFF)
			{
				Debug.LogError($"{nameof(BoundsIndicator)} could not tell the corner bones of " +
					$"{skinnedMesh.name} apart. In the rest pose they must sit at the eight " +
					"distinct corners of a box, none of them on its center planes.", this);
				return false;
			}

			// Mesh bounds are the rest pose's, in the same space as the bones we just read.
			Bounds meshBounds = skinnedMesh.sharedMesh != null ? skinnedMesh.sharedMesh.bounds : restBox;
			overhang = Vector3.Max(Vector3.Max(meshBounds.max - restBox.max, restBox.min - meshBounds.min),
				Vector3.zero);

			return true;
		}

		private static readonly List<Renderer> renderers = new();

		/// <summary>
		/// Bounds of everything visible under <paramref name="root"/>, in the space of
		/// <paramref name="worldToBox"/>. False when there is nothing to draw.
		/// </summary>
		/// <param name="exclude">Hierarchy to leave out — the indicator itself, usually.</param>
		public static bool TryGetVisualBounds(Transform root, Matrix4x4 worldToBox, out Bounds bounds,
			bool includeInactive = false, Transform exclude = null)
		{
			bounds = default;
			bool any = false;

			root.GetComponentsInChildren(includeInactive, renderers);

			foreach (Renderer renderer in renderers)
			{
				if (!includeInactive && !renderer.enabled)
					continue;

				if (exclude != null && renderer.transform.IsChildOf(exclude))
					continue;

				// A skinned mesh keeps its local bounds in root bone space, not its own.
				Transform space = renderer is SkinnedMeshRenderer skinned && skinned.rootBone != null
					? skinned.rootBone
					: renderer.transform;

				Matrix4x4 toBox = worldToBox * space.localToWorldMatrix;
				Bounds local = renderer.localBounds;

				for (int i = 0; i < CornerCount; i++)
				{
					Vector3 corner = toBox.MultiplyPoint3x4(
						local.center + Vector3.Scale(local.extents, CornerDirections[i]));

					if (any)
					{
						bounds.Encapsulate(corner);
					}
					else
					{
						bounds = new Bounds(corner, Vector3.zero);
						any = true;
					}
				}
			}

			renderers.Clear();
			return any;
		}

		/// <summary>World-axis-aligned bounds of everything visible under <paramref name="root"/>.</summary>
		public static bool TryGetVisualBounds(Transform root, out Bounds bounds, bool includeInactive = false)
			=> TryGetVisualBounds(root, Matrix4x4.identity, out bounds, includeInactive);
	}
}
