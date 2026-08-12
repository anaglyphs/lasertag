using Anaglyph.XR.Input;
using UnityEngine;

namespace Anaglyph.LaserTag.MapEditor
{
	public class Palette : MonoBehaviour
	{
		public static Palette Instance { get; private set; }

		private HandSubject handSubject;

		private void Awake()
		{
			Instance = this;

			TryGetComponent(out handSubject);
		}

		private void Start()
		{
			MapEditor.ActiveChanged += gameObject.SetActive;
			gameObject.SetActive(MapEditor.IsActive);

			if (handSubject.Current == null)
				handSubject.Assign(HandInput.Get(Handedness.Left));
		}

		private void OnDestroy()
		{
			MapEditor.ActiveChanged -= gameObject.SetActive;
		}

		public void SetHandSide(Handedness handedness)
		{
			handSubject.Assign(HandInput.Get(handedness));
		}
	}
}