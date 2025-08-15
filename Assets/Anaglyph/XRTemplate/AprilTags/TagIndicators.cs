using AprilTag;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XRTemplate.AprilTags
{
	public class TagIndicators : MonoBehaviour
	{
		[SerializeField] private AprilTagTracker tracker;
		[SerializeField] private GameObject aprilTagIndicatorPrefab;

		private List<GameObject> indicators = new(3);

		private void Start()
		{
			tracker.OnDetectTags += OnDetectTags;
		}

		private void OnEnable()
		{
			if(didStart)
				tracker.OnDetectTags += OnDetectTags;
		}

		private void OnDisable()
		{
			tracker.OnDetectTags -= OnDetectTags;
		}

		private void OnDetectTags(IEnumerable<TagPose> poses)
		{
			int i = 0;
			foreach (TagPose pose in poses)
			{
				if (i >= indicators.Count)
					indicators.Add(Instantiate(aprilTagIndicatorPrefab, Vector3.zero, Quaternion.identity));

				indicators[i].transform.position = pose.Position;
				indicators[i].transform.rotation = pose.Rotation;

				i++;
			}
		}
	}
}