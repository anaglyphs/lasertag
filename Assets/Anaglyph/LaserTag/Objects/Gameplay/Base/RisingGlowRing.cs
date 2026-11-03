using UnityEngine;

namespace Anaglyph.LaserTag.Objects.Gameplay.Base
{
	[RequireComponent(typeof(Renderer))]
	public class RisingGlowRing : MonoBehaviour
	{
		private static readonly int IntensityID = Shader.PropertyToID("_Intensity");

		[SerializeField] private float riseHeight = 2f;
		[SerializeField, Min(0.01f)] private float riseSeconds = 4f;
		[SerializeField, Range(0f, 1f)] private float cycleOffset = 0f;
		[SerializeField, Range(0.001f, 1f)] private float fadeInShare = 0.1f;
		[SerializeField] private float peakIntensity = 1f;

		private new Renderer renderer;
		private MaterialPropertyBlock propertyBlock;

		private void Awake()
		{
			renderer = GetComponent<Renderer>();
			propertyBlock = new MaterialPropertyBlock();
		}

		private void LateUpdate()
		{
			float cycle = Mathf.Repeat(Time.time / riseSeconds + cycleOffset, 1f);

			Vector3 position = transform.localPosition;
			position.y = cycle * riseHeight;
			transform.localPosition = position;

			float fadeOut = 1f - cycle;
			float intensity = peakIntensity * Mathf.Clamp01(cycle / fadeInShare) * fadeOut * fadeOut;

			// the block also holds the colour from TeamColorer
			renderer.GetPropertyBlock(propertyBlock);
			propertyBlock.SetFloat(IntensityID, intensity);
			renderer.SetPropertyBlock(propertyBlock);
		}
	}
}
