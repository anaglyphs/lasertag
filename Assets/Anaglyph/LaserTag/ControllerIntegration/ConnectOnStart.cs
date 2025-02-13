using StrikerLink.Unity.Runtime.Core;
using UnityEngine;

namespace Anaglyph.Lasertag.ControllerIntegration
{
    public class ConnectOnStart : MonoBehaviour
    {
		private void Start()
		{
			GetComponent<StrikerController>().Connect();
		}
	}
}
