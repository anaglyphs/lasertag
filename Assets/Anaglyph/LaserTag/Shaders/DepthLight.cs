using UnityEngine;
using System;

namespace Anaglyph.Lasertag
{
	public class DepthLight : MonoBehaviour
	{
		public Color color = Color.white;
		public float epsilon = 0.01f;

		private MaterialPropertyBlock propertyBlock = null;
		private MeshRenderer meshRenderer = null;

		private const string ColorProp = "_Color";
		private const string IntensityProp = "_Intensity";

		private static readonly int ColorID = Shader.PropertyToID(ColorProp);
		private static readonly int IntensityID = Shader.PropertyToID(IntensityProp);

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
			float radius = transform.lossyScale.x;
			float intensity = epsilon * radius * radius;

			propertyBlock.SetColor(ColorID, color);
			propertyBlock.SetFloat(IntensityID, intensity);

			meshRenderer.SetPropertyBlock(propertyBlock);
		}
	}
}
