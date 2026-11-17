using System.Collections.Generic;
using Anaglyph.XR.DepthKit;
using Anaglyph.XR.VisualLocalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// While visual alignment is the active method and not yet aligned, draws the features its
	/// query frames see as small view-facing dots on the surfaces they were seen on: green for
	/// those the map recognized, white for a sample of the rest. Placed by the depth sensor; a
	/// recognized one with no depth reading stands at the map's range for its landmark. Dots fade,
	/// so they show what is being seen now; once aligned they all fade out and drawing stops.
	/// </summary>
	[RequireComponent(typeof(VisualLocalizer), typeof(VisualColocationConstraintProvider))]
	public sealed class AlignmentFeatureDots : MonoBehaviour
	{
		[SerializeField] private Shader dotShader;
		[SerializeField] private Texture2D dotTexture;
		[SerializeField] private ComputeShader depthRayCast;
		[SerializeField] private int maxDots = 256;
		[Tooltip("Of maxDots, how many are kept for seen-but-unrecognized features, so they never push recognized ones out")]
		[SerializeField] private int seenDotSlots = 64;
		[SerializeField] private int seenDotsPerView = 12;
		[SerializeField] private Color recognizedColor = new(0.3f, 1f, 0.45f, 1f);
		[SerializeField] private Color seenColor = new(1f, 1f, 1f, 0.85f);
		[SerializeField] private float dotSeconds = 4f;
		[SerializeField] private float fadeInSeconds = 0.15f;
		[SerializeField] private float fadeOutOnAlignedSeconds = 0.5f;

		private VisualLocalizer localizer;
		private VisualColocationConstraintProvider provider;
		// 1 while unaligned, down to 0 once aligned
		private float shownWhileUnaligned;
		private DepthRayCaster depthRayCaster;
		private Material material;
		private Mesh mesh;

		private Vector3[] dotsInTrackingSpace;
		private float[] dotBornAt;
		private bool[] dotIsRecognized;
		private int nextRecognizedDot, nextSeenDot;
		private readonly List<int> viewFeatures = new(), unrecognizedFeatures = new();
		private bool hasDots;
		private Vector3[] vertices;
		private Color32[] colors;
		private bool casting;
		private readonly List<Ray> rays = new();

		private void Awake()
		{
			localizer = GetComponent<VisualLocalizer>();
			provider = GetComponent<VisualColocationConstraintProvider>();
			dotsInTrackingSpace = new Vector3[maxDots];
			dotBornAt = new float[maxDots];
			dotIsRecognized = new bool[maxDots];
			seenDotSlots = Mathf.Clamp(seenDotSlots, 0, maxDots - 1);
			nextSeenDot = RecognizedDotSlots;
			System.Array.Fill(dotBornAt, float.NegativeInfinity);
			vertices = new Vector3[maxDots * 4];
			colors = new Color32[maxDots * 4];
			mesh = CreateQuadMesh(maxDots);
			material = new Material(dotShader) { mainTexture = dotTexture };
			localizer.FeaturesSeen += OnFeaturesSeen;
			if (depthRayCast == null || !SystemInfo.supportsComputeShaders) return;
			try
			{
				depthRayCaster = new DepthRayCaster(depthRayCast, maxDots);
			}
			catch (System.Exception e)
			{
				// dots then stand at the map's range for each landmark
				Debug.LogException(e, this);
			}
		}

		private void OnDestroy()
		{
			if (localizer) localizer.FeaturesSeen -= OnFeaturesSeen;
			depthRayCaster?.Dispose();
			depthRayCaster = null;
			Destroy(mesh);
			Destroy(material);
		}

		private static Mesh CreateQuadMesh(int quads)
		{
			Mesh quadMesh = new() { name = "Alignment Feature Dots" };
			quadMesh.MarkDynamic();
			Vector2[] corners = { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };
			Vector3[] positions = new Vector3[quads * 4];
			Vector2[] uvs = new Vector2[quads * 4];
			int[] indices = new int[quads * 6];
			for (int q = 0; q < quads; q++)
			{
				for (int c = 0; c < 4; c++) uvs[q * 4 + c] = corners[c];
				int v = q * 4, i = q * 6;
				indices[i] = v; indices[i + 1] = v + 1; indices[i + 2] = v + 2;
				indices[i + 3] = v; indices[i + 4] = v + 2; indices[i + 5] = v + 3;
			}

			quadMesh.vertices = positions;
			quadMesh.uv = uvs;
			quadMesh.colors32 = new Color32[quads * 4];
			quadMesh.triangles = indices;
			// positions are dot centers; the shader spreads each quad to face the view
			quadMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
			return quadMesh;
		}

		private bool VisualAlignmentUnaligned =>
			VisualColocationConstraintProvider.Active == provider && provider.IsRunning &&
			provider.Alignment.CurrentPhase is CanonAlignment.Phase.Searching or CanonAlignment.Phase.Relocalizing;

		private int RecognizedDotSlots => maxDots - seenDotSlots;

		// every recognized feature, then an even sample of the rest
		private void PickViewFeatures(SeenFeature[] features)
		{
			viewFeatures.Clear();
			unrecognizedFeatures.Clear();
			for (int i = 0; i < features.Length; i++)
			{
				if (!features[i].recognized) unrecognizedFeatures.Add(i);
				else if (viewFeatures.Count < RecognizedDotSlots) viewFeatures.Add(i);
			}

			int seenCount = Mathf.Min(seenDotsPerView, seenDotSlots, unrecognizedFeatures.Count);
			if (seenCount == 0) return;
			float stride = unrecognizedFeatures.Count / (float)seenCount;
			float offset = Random.value * stride;
			for (int k = 0; k < seenCount; k++)
				viewFeatures.Add(unrecognizedFeatures[Mathf.Min((int)(offset + k * stride), unrecognizedFeatures.Count - 1)]);
		}

		private async void OnFeaturesSeen(Pose cameraInWorld, SeenFeature[] features)
		{
			Transform trackingSpace = MainXRRig.TrackingSpace;
			if (casting || trackingSpace == null || !VisualAlignmentUnaligned) return;

			PickViewFeatures(features);
			if (viewFeatures.Count == 0) return;
			int[] picked = viewFeatures.ToArray();

			// held in tracking space, which moves with the room, while the depth frame is fetched
			Pose cameraInTrackingSpace = new(trackingSpace.InverseTransformPoint(cameraInWorld.position), Quaternion.Inverse(trackingSpace.rotation) * cameraInWorld.rotation);
			rays.Clear();
			foreach (int i in picked)
				rays.Add(new Ray(cameraInWorld.position, cameraInWorld.rotation * features[i].ray));

			Vector4[] depthHits = null;
			if (depthRayCaster != null && DepthRayCaster.CanCast)
			{
				casting = true;
				try
				{
					depthHits = await depthRayCaster.CastAsync(rays);
				}
				finally
				{
					casting = false;
				}
			}

			trackingSpace = MainXRRig.TrackingSpace;
			if (!this || trackingSpace == null || !VisualAlignmentUnaligned) return;

			Matrix4x4 worldToTrackingSpace = trackingSpace.worldToLocalMatrix;
			for (int k = 0; k < picked.Length; k++)
			{
				SeenFeature feature = features[picked[k]];
				if (depthHits != null && k < depthHits.Length && depthHits[k].w > 0)
					AddDot(worldToTrackingSpace.MultiplyPoint3x4(depthHits[k]), feature.recognized);
				else if (feature.range > 0)
					AddDot(cameraInTrackingSpace.position + cameraInTrackingSpace.rotation * feature.ray * feature.range, feature.recognized);
			}
		}

		private void AddDot(Vector3 inTrackingSpace, bool recognized)
		{
			int slot;
			if (recognized)
			{
				slot = nextRecognizedDot;
				nextRecognizedDot = (nextRecognizedDot + 1) % RecognizedDotSlots;
			}
			else
			{
				if (seenDotSlots == 0) return;
				slot = nextSeenDot;
				nextSeenDot = RecognizedDotSlots + (nextSeenDot + 1 - RecognizedDotSlots) % seenDotSlots;
			}

			dotsInTrackingSpace[slot] = inTrackingSpace;
			dotBornAt[slot] = Time.unscaledTime;
			dotIsRecognized[slot] = recognized;
			hasDots = true;
		}

		private void ClearDots()
		{
			System.Array.Fill(dotBornAt, float.NegativeInfinity);
			hasDots = false;
		}

		private void LateUpdate()
		{
			bool unaligned = VisualAlignmentUnaligned;
			// a hitch as alignment lands would otherwise skip the fade
			const float longestFadeStepSeconds = 1f / 30f;
			float fadeStep = Mathf.Min(Time.unscaledDeltaTime, longestFadeStepSeconds) / fadeOutOnAlignedSeconds;
			shownWhileUnaligned = unaligned ? 1f : Mathf.MoveTowards(shownWhileUnaligned, 0f, fadeStep);
			Transform trackingSpace = MainXRRig.TrackingSpace;
			if (!hasDots || trackingSpace == null) return;
			if (shownWhileUnaligned <= 0f)
			{
				ClearDots();
				return;
			}

			float now = Time.unscaledTime;
			bool anyAlive = false;
			for (int d = 0; d < maxDots; d++)
			{
				float age = now - dotBornAt[d];
				bool alive = age < dotSeconds;
				anyAlive |= alive;
				Color tint = dotIsRecognized[d] ? recognizedColor : seenColor;
				tint.a *= alive ? Mathf.Clamp01(age / fadeInSeconds) * (1f - age / dotSeconds) * shownWhileUnaligned : 0f;
				Color32 color = tint;
				for (int c = 0; c < 4; c++)
				{
					vertices[d * 4 + c] = dotsInTrackingSpace[d];
					colors[d * 4 + c] = color;
				}
			}

			if (!anyAlive)
			{
				ClearDots();
				return;
			}

			mesh.SetVertices(vertices);
			mesh.SetColors(colors);
			RenderParams renderParams = new(material)
			{
				layer = gameObject.layer,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				worldBounds = new Bounds(trackingSpace.position, Vector3.one * 2000f),
			};
			Graphics.RenderMesh(renderParams, mesh, 0, trackingSpace.localToWorldMatrix);
		}
	}
}
