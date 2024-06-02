using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
	public class FrameCounter : MonoBehaviour
	{
		public string text;
		public Text fpsText;
		private float _deltaTime;

		void Update()
		{
			_deltaTime += (Time.deltaTime - _deltaTime) * 0.1f;
			float fps = 1.0f / _deltaTime;
			fpsText.text = text + Mathf.Ceil(fps).ToString();
		}
	}
}