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
			TryGetComponent(out image);
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

			TryGetComponent(out image);
			SetColor(colorObject.Value);
		}

		private void SetColor(Color color) => image.color = color;
	}
}
