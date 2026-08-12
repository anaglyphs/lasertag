using Anaglyph.XR;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	public class EditorSimulatedAprilTags : MonoBehaviour
	{
		void Start()
		{
			transform.parent = MainXRRig.TrackingSpace;
		}
	}
}