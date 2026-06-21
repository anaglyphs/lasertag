using UnityEngine;

namespace Anaglyph.Input
{
	[DefaultExecutionOrder(-9999)]
	public class HandMover : MonoBehaviour
	{
		[SerializeField] private HandSubject handSubject;

		private void Awake()
		{
			if (!handSubject)
				handSubject = GetComponentInParent<HandSubject>();
		}

		private void Update()
		{
			UpdatePosition();
		}

		private void LateUpdate()
		{
			UpdatePosition();
		}

		private void UpdatePosition()
		{
			transform.localPosition = handSubject.Position;
			transform.localRotation = handSubject.Rotation;
		}
	}
}