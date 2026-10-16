using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
	/// <summary>
	/// Renderer drawn into the scanner's depth mask. Nothing behind it, as seen from the
	/// depth sensor, is integrated into the scan. Moved onto the <see cref="LayerName"/>
	/// layer, which cameras exclude, so it's never drawn to the player. Skinned renderers
	/// work, e.g. the local player's arms.
	/// </summary>
	[RequireComponent(typeof(Renderer))]
	public class EnvScanMask : MonoBehaviour
	{
		public const string LayerName = "EnvScanMask";

		public static List<EnvScanMask> All { get; } = new();

		public Renderer Renderer { get; private set; }

		/// <summary>Cached, since <see cref="Renderer.sharedMaterials"/> allocates</summary>
		public int SubMeshCount { get; private set; }

		private void Awake()
		{
			Renderer = GetComponent<Renderer>();
			SubMeshCount = Renderer.sharedMaterials.Length;

			int layer = LayerMask.NameToLayer(LayerName);
			if (layer < 0)
				Debug.LogWarning($"[{nameof(EnvScanMask)}] no '{LayerName}' layer; mask will be visible", this);
			else
				gameObject.layer = layer;

			// skinning is skipped for renderers no camera sees, and no camera sees this layer
			if (Renderer is SkinnedMeshRenderer skinned)
				skinned.updateWhenOffscreen = true;
		}

		private void OnEnable()
		{
			All.Add(this);
		}

		private void OnDisable()
		{
			All.Remove(this);
		}
	}
}
