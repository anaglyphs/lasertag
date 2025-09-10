using UnityEngine;

namespace Anaglyph.Lasertag
{
    public class DepthLight : MonoBehaviour
    {
		public Color color = Color.white;
		public float epsilon = 0.01f;

		private MeshRenderer meshRenderer = null;

		private void Awake()
		{
			meshRenderer = GetComponent<MeshRenderer>();
		}

		private void OnEnable()
		{
			DepthLightDriver.AllLights.Add(this);
			meshRenderer.enabled = true;
		}

		private void OnDisable()
		{
			DepthLightDriver.AllLights.Remove(this);
			meshRenderer.enabled = false;
		}

		public float GetIntensity() 
		{
			float radius = transform.lossyScale.x;
			return epsilon * radius * radius;
		}
	}
}
