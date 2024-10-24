using Anaglyph;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.MenuXR
{
    public class ImageColorer : MonoBehaviour
    {
        private Image image;
        public ColorObject colorObject;

		private void Awake()
		{
			this.SetComponent(ref image);
		}

		private void OnEnable()
		{
			colorObject.AddChangeListenerAndCheck(SetColor);
		}

		private void OnDisable()
		{
			colorObject.onChange -= SetColor;
		}

		private void OnValidate()
		{
			if (colorObject == null) return;

			this.SetComponent(ref image);
			SetColor(colorObject.Value);
		}

		private void SetColor(Color color) => image.color = color;
	}
}
