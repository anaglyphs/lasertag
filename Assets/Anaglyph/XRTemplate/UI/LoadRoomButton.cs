using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.XRTemplate
{
    public class LoadRoomButton : MonoBehaviour
    {
		private void Awake()
		{
			GetComponent<Button>()?.onClick.AddListener(LoadRoomMesh);
		}

		public void LoadRoomMesh() => RoomMeshManager.LoadRoomMesh();
	}
}
