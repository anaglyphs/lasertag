using Anaglyph.XRTemplate;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class EditorSimulatedAprilTags : MonoBehaviour
	{
		void Start()
		{
			transform.parent = MainXRRig.TrackingSpace;
		}
	}
}