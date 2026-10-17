using System.Collections.Generic;
using Anaglyph.XR;
using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.SharedSpaces;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Part of the mesh draw mode: while a relocalization solves, the live environment
	/// mesh is hidden (it stands where the stale frame put it) and ghost copies of the
	/// chunks nearest the player show where the map would sit in the room if the
	/// search's current best hypothesis were applied. Every pyramid level's winner and
	/// the refined pose snap the ghosts there the moment the registration reports it,
	/// so the search is watched as it happens. Once the alignment is applied the ghosts
	/// go and the live mesh returns, now on the room.
	///
	/// A hypothesis T maps the fresh scan (physical surfaces, in the current frame)
	/// onto the map, so the map pulled into the current frame sits at T⁻¹. Ghost poses
	/// are kept relative to the tracking space, the room's frame, so a hypothesis still
	/// means the same place once the colocator moves the rig.
	/// </summary>
	public class ScanRegistrationDebugView : MonoBehaviour
	{
		public static int MaxGhostChunks = 48;

		// opacity over passthrough is EnvMesher.DebugMeshOpacity, shared with the live mesh
		private static readonly Color staleColor = new(1f, 0.55f, 0.15f);
		private static readonly Color coarseColor = new(1f, 0.3f, 0.3f);
		private static readonly Color fineColor = new(1f, 0.9f, 0.3f);
		private static readonly Color refinedColor = new(0.4f, 0.9f, 1f);
		private static readonly int colorID = Shader.PropertyToID("_Color");

		private static ScanRegistrationDebugView instance;

		private Material material;
		private ScanRegistrationColocationConstraintProvider provider;
		private EnvMesher mesher;

		private bool relocalizing;
		private Transform ghostRoot;
		private readonly List<Mesh> ghostMeshes = new();
		private readonly List<MeshRenderer> hiddenRenderers = new();

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Create()
		{
			if (instance != null) return;
			GameObject go = new(nameof(ScanRegistrationDebugView));
			DontDestroyOnLoad(go);
			instance = go.AddComponent<ScanRegistrationDebugView>();
		}

		private void OnEnable()
		{
			material = new Material(Shader.Find("Anaglyph/Scan Registration Ghost")) { color = staleColor };
		}

		private void OnDisable()
		{
			Unhook();
			if (material) Destroy(material);
		}

		// the provider and mesher come and go with scenes and sessions
		private void Update()
		{
			if (provider == null || mesher == null)
			{
				if (provider != null || mesher != null) Unhook();
				Hook();
				return;
			}

			if (!relocalizing) return;

			// the alignment is applied once the colocator has fitted the new constraint
			if (provider.CurrentPhase == ScanRegistrationColocationConstraintProvider.Phase.Aligned &&
			    ColocationManager.Instance != null &&
			    ColocationManager.Instance.AlignmentState == ColocationAlignmentState.Localized)
				EndRelocalization();
		}

		private void Hook()
		{
			provider = ScanRegistrationColocationConstraintProvider.Instance;
			mesher = EnvMesher.Instance;
			if (provider == null || mesher == null) return;

			provider.PhaseChanged += OnPhaseChanged;
			provider.SearchStep += OnSearchStep;
			mesher.ChunksVisibleChanged += OnChunksVisibleChanged;
			if (provider.CurrentPhase == ScanRegistrationColocationConstraintProvider.Phase.Relocalizing) BeginRelocalization();
		}

		private void Unhook()
		{
			if (provider != null)
			{
				provider.PhaseChanged -= OnPhaseChanged;
				provider.SearchStep -= OnSearchStep;
			}
			if (mesher != null) mesher.ChunksVisibleChanged -= OnChunksVisibleChanged;
			provider = null;
			mesher = null;
			EndRelocalization();
		}

		private void OnPhaseChanged(ScanRegistrationColocationConstraintProvider.Phase phase)
		{
			if (phase == ScanRegistrationColocationConstraintProvider.Phase.Relocalizing) BeginRelocalization();
			else if (phase != ScanRegistrationColocationConstraintProvider.Phase.Aligned) EndRelocalization();
			// Aligned ends it from Update, once the colocator has moved the rig
		}

		// draw mode toggled mid-solve: the ghosts and the hidden mesh follow it
		private void OnChunksVisibleChanged(bool visible)
		{
			if (!relocalizing) return;
			if (visible) ShowGhosts();
			else HideGhosts();
		}

		private void BeginRelocalization()
		{
			if (relocalizing) return;
			relocalizing = true;
			if (mesher != null && mesher.ChunksVisible) ShowGhosts();
		}

		private void EndRelocalization()
		{
			relocalizing = false;
			HideGhosts();
		}

		/// <summary>
		/// Hides the live mesh and copies the nearest populated chunks under a root that
		/// rides with the tracking space, at identity in the world right now: the map as
		/// the stale frame has it
		/// </summary>
		private void ShowGhosts()
		{
			HideGhosts();
			if (mesher == null || MainXRRig.Instance == null) return;

			Transform space = MainXRRig.TrackingSpace;
			Vector3 head = MainXRRig.Camera ? MainXRRig.Camera.transform.position : space.position;

			List<Chunk> candidates = new();
			foreach (Chunk chunk in mesher.ChunksList)
			{
				if (!chunk) continue;
				if (chunk.TryGetComponent(out MeshRenderer renderer) && renderer.enabled)
				{
					renderer.enabled = false;
					hiddenRenderers.Add(renderer);
				}
				if (chunk.meshIsPopulated && chunk.mesh) candidates.Add(chunk);
			}
			candidates.Sort((a, b) =>
				(a.transform.position + EnvMesher.ChunkWorldSizeHalf - head).sqrMagnitude
				.CompareTo((b.transform.position + EnvMesher.ChunkWorldSizeHalf - head).sqrMagnitude));

			ghostRoot = new GameObject("Alignment Ghosts").transform;
			ghostRoot.SetParent(space, false);
			ghostRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

			int count = Mathf.Min(candidates.Count, MaxGhostChunks);
			for (int i = 0; i < count; i++)
			{
				Chunk chunk = candidates[i];
				// a wireframe copy: the live chunk re-meshes and may be destroyed while the ghost lingers
				Mesh copy = WireframeMesh.Create(chunk.mesh);
				ghostMeshes.Add(copy);

				GameObject g = new($"Ghost {chunk.chunkIndex}");
				g.transform.SetParent(ghostRoot, false);
				g.transform.SetPositionAndRotation(chunk.transform.position, chunk.transform.rotation);
				g.AddComponent<MeshFilter>().sharedMesh = copy;
				MeshRenderer ghost = g.AddComponent<MeshRenderer>();
				ghost.sharedMaterial = material;
				ghost.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				ghost.receiveShadows = false;
			}

			material.SetColor(colorID, staleColor);
		}

		private void HideGhosts()
		{
			if (ghostRoot) Destroy(ghostRoot.gameObject);
			ghostRoot = null;
			foreach (Mesh mesh in ghostMeshes) if (mesh) Destroy(mesh);
			ghostMeshes.Clear();
			foreach (MeshRenderer renderer in hiddenRenderers) if (renderer) renderer.enabled = true;
			hiddenRenderers.Clear();
		}

		// snaps the ghosts to where this hypothesis would put the map, the moment it is known
		private void OnSearchStep(SearchStep step)
		{
			if (ghostRoot == null || MainXRRig.Instance == null) return;

			Quaternion rotation = Quaternion.Euler(0f, -math.degrees(step.pose.yaw), 0f);
			Vector3 position = rotation * -(Vector3)step.pose.translation;
			Transform space = MainXRRig.TrackingSpace;
			ghostRoot.SetLocalPositionAndRotation(
				space.InverseTransformPoint(position),
				Quaternion.Inverse(space.rotation) * rotation);

			material.SetColor(colorID, step.refined ? refinedColor
				: Color.Lerp(fineColor, coarseColor, step.level / (float)(RegistrationVolume.PyramidLevels - 1)));
		}
	}
}
