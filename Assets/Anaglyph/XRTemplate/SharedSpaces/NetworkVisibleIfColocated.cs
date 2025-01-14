using Anaglyph.SharedSpaces;
using Unity.Netcode;

namespace Anaglyph.XRTemplate.SharedSpaces
{
    public class NetworkVisibleIfColocated : NetworkBehaviour
    {
		//private void Awake()
		//{
		//	NetworkObject.CheckObjectVisibility = Check;
		//	NetworkObject.SpawnWithObservers = false;
		//}

		//private bool Check(ulong clientID)
		//{
		//	return AnchorColocator.IsColocated.Value;
		//}
	}
}
