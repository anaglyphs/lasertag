using UnityEngine;

namespace Anaglyph.Menu
{
    public class Throbber : MonoBehaviour
    {
	    [SerializeField] private float secPerStep = 0.1f;
	    [SerializeField] private float angle = 360 / 12f;

	    private void Update()
	    {
		    float z = Mathf.Floor(Time.time / secPerStep) * angle;
		    transform.localEulerAngles = new Vector3(0, 0, z);
	    }
    }
}
