using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.XRTemplate
{
    public class ScanRoomButton : MonoBehaviour
    {
		private void Awake()
		{
			GetComponent<Button>()?.onClick.AddListener(ScanRoomMesh);
		}

		public void ScanRoomMesh() => RoomMeshManager.ScanRoomMesh();
	}
}
