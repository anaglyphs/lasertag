using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(-1000)]
	[DisallowMultipleComponent]
	[RequireComponent(typeof(PanelInputConfiguration))]
	[RequireComponent(typeof(XRUIToolkitManager))]
	public sealed class UIToolkitMenuInputSetup : MonoBehaviour
	{
		private void Awake()
		{
			PanelInputConfiguration configuration =
				GetComponent<PanelInputConfiguration>();
			configuration.processWorldSpaceInput = true;
			configuration.panelInputRedirection =
				PanelInputConfiguration.PanelInputRedirection.Never;
			configuration.autoCreatePanelComponents = true;

			XRUIInputModule inputModule = FindFirstObjectByType<XRUIInputModule>();
			if (inputModule != null)
				inputModule.bypassUIToolkitEvents = false;
		}
	}
}
