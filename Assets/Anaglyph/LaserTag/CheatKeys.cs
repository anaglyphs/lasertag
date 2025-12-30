using Anaglyph.XRTemplate;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.Lasertag
{
	
    public class CheatKeys : MonoBehaviour
    {
	    
#if UNITY_EDITOR

	    [SerializeField] private GameObject redBase;
	    [SerializeField] private GameObject blueBase;
	    
	    private void Update()
	    {
		    if(Keyboard.current[Key.R].wasPressedThisFrame)
			    Spawn(redBase);
		    else if(Keyboard.current[Key.B].wasPressedThisFrame)
			    Spawn(blueBase);
	    }

	    private void Spawn(GameObject g)
	    {
		    var spawnPos = MainXRRig.Camera.transform.position;
		    spawnPos.y = 0;
		    Instantiate(g, spawnPos, Quaternion.identity).GetComponent<NetworkObject>().Spawn();
	    }
#endif
    }
}
