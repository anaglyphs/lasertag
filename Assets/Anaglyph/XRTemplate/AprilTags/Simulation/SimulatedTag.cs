using AprilTag;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XRTemplate.AprilTags
{
	[SelectionBase]
    public class SimulatedTag : MonoBehaviour
    {
        public int tagId = 0;

		public bool isInView { get; private set; }

		public static List<SimulatedTag> Visible = new();

		private Camera mainCamera;

		private void OnEnable()
		{
			mainCamera = Camera.main;
			Visible.Add(this);
		}

		private void Start()
		{
#if !UNITY_EDITOR
			gameObject.SetActive(false);
#endif
		}

		private void OnDisable()
		{
			Visible.Remove(this);
		}

		private void FixedUpdate()
		{
			Vector2 viewportPoint = mainCamera.WorldToViewportPoint(transform.position);
			float x = viewportPoint.x;
			float y = viewportPoint.y;

			isInView = x > 0f && x < 1f && y > 0f && y < 1f;
		}

		public TagPose GetTagPoseInWorldSpace()
		{
			return new(tagId, transform.position, transform.rotation);
		}
	}
}
