using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.Menu
{
	public class DisableUIMaterialZTest : MonoBehaviour
	{
		[SerializeField] private Material[] materials;

		private static readonly int ztestModeID = Shader.PropertyToID("unity_GUIZTestMode");

		private void Start()
		{
			int always = (int)CompareFunction.Always;

			Canvas.GetDefaultCanvasMaterial().SetInt(ztestModeID, always);

			// setting global int does not work
			// must be set per material
			foreach (Material material in materials)
				material.SetInt(ztestModeID, always);
		}
	}
}