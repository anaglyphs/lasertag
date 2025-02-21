using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Anaglyph.XRTemplate
{
    public class HandednessEvents : MonoBehaviour
    {
		private HandedHierarchy hierarchyHandedness;

		private void Awake()
		{
			hierarchyHandedness = GetComponentInParent<HandedHierarchy>(true);
			if (hierarchyHandedness == null)
				return;

			hierarchyHandedness.OnHandednessChange += CallEvents;
		}

		public UnityEvent OnNone = new();
		public UnityEvent OnRight = new();
		public UnityEvent OnLeft = new();

		private void Start()
		{
			CallEvents(hierarchyHandedness.Handedness);
		}

		private void CallEvents(InteractorHandedness handedness)
		{
			switch (handedness)
			{
				case InteractorHandedness.None:
					OnNone.Invoke(); break;
				case InteractorHandedness.Left:
					OnLeft.Invoke(); break;
				case InteractorHandedness.Right:
					OnRight.Invoke(); break;
			}
		}
	}
}
