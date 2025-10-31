using UnityEngine;

namespace Anaglyph.Menu
{
    public class OldDeviceWarning : MonoBehaviour
    {
	    private void Start()
        {
#if UNITY_EDITOR
	        gameObject.SetActive(false);
	        return;
#endif
	        
	        bool oldHeadset;
	        
	        using (AndroidJavaClass build = new AndroidJavaClass("android.os.Build"))
	        {
		        string device = build.GetStatic<string>("DEVICE");
		        oldHeadset = device.Equals("hollywood") || device.Equals("seacliff");
	        }
	        
	        if(!oldHeadset)
		        gameObject.SetActive(false);
        }
    }
}
