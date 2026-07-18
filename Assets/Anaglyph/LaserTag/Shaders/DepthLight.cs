using UnityEngine;
using System;
using UnityEngine.Serialization;

namespace Anaglyph.Lasertag
{
	public class DepthLight : MonoBehaviour
	{
		public Color color = Color.white;
		[FormerlySerializedAs("epsilon")]
		[Min(0)]
		[Tooltip("Inverse-square intensity multiplier. The sphere's size controls only the light's range.")]
		public float intensity = 0.16f;

		private MaterialPropertyBlock propertyBlock = null;
		private MeshRenderer meshRenderer = null;

		private const string ColorProp = "_Color";
		private const string IntensityProp = "_Intensity";
		private const string InvSqrRadiusProp = "_InvSqrRadius";

		private static readonly int ColorID = Shader.PropertyToID(ColorProp);
		private static readonly int IntensityID = Shader.PropertyToID(IntensityProp);
		private static readonly int InvSqrRadiusID = Shader.PropertyToID(InvSqrRadiusProp);

		private void Awake()
		{
			meshRenderer = GetComponent<MeshRenderer>();
		}

		private void Start()
		{
			propertyBlock = new();
		}

		private static bool globallyEnabled = true;
		private static Action globallyEnabledChanged = delegate { };
		public static void SetGloballyEnabled(bool value)
		{
			globallyEnabled = value;
			globallyEnabledChanged?.Invoke();
		}

		private void OnEnable()
		{
			OnGloballyEnabledChanged();
			globallyEnabledChanged += OnGloballyEnabledChanged;
		}

		private void OnGloballyEnabledChanged()
		{
			meshRenderer.enabled = globallyEnabled;
		}

		private void OnDisable()
		{
			meshRenderer.enabled = false;
			globallyEnabledChanged -= OnGloballyEnabledChanged;
		}

		private void LateUpdate()
		{
			// Unity's built-in sphere has a radius of 0.5, so use its actual
			// bounds instead of treating the transform scale as the radius.
			float radius = meshRenderer.localBounds.extents.x * Mathf.Abs(transform.lossyScale.x);
			float radiusSqr = radius * radius;

			propertyBlock.SetColor(ColorID, color);
			propertyBlock.SetFloat(IntensityID, Mathf.Max(0, intensity));
			propertyBlock.SetFloat(InvSqrRadiusID, radiusSqr > 0 ? 1.0f / radiusSqr : 0);

			meshRenderer.SetPropertyBlock(propertyBlock);
		}
	}
}
