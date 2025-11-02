using Anaglyph.XRTemplate;
using UnityEngine;
using Anaglyph.Menu;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using OVR.OpenVR;

namespace Anaglyph.Lasertag
{
	public class ToolPalette : MonoBehaviour
	{
		public static ToolPalette Left { get; private set; }
		public static ToolPalette Right { get; private set; }

		public SingleActiveChild toolSelector;

		private void Awake()
		{
			var handedness = GetComponentInParent<HandedHierarchy>().Handedness;

			if (handedness == InteractorHandedness.Right)
			{
				Right = this;
			}
			else if (handedness == InteractorHandedness.Left)
			{
				Left = this;
			}
		}
	}
}