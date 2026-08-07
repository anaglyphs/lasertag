using UnityEngine;

namespace Anaglyph.Lasertag
{
	[ExecuteAlways]
    public class WeaponPickupVisual : MonoBehaviour
    {
	    [SerializeField] private GameObject previewObject;
	    
	    [SerializeField] private float spinSpeed = 30;
	    [SerializeField] private float bobHeight = 0.05f;
	    [SerializeField] private float bobFrequency = 0.5f;
	    [SerializeField] private Vector3 basePosition;

	    private void OnValidate()
	    {
		    basePosition = transform.localPosition;
		    
		    WeaponPickup pickup = GetComponentInParent<WeaponPickup>();

		    if (pickup == null || pickup.weaponPrefab == null) return;
		    
		    IWeapon weapon = pickup.weaponPrefab.GetComponent<IWeapon>();

		    if (weapon == null) return;

		    previewObject = pickup.weaponPrefab.GetComponent<IWeapon>().VisualObject;
	    }

	    private void Start()
	    {
		    GameObject obj = Instantiate(previewObject, transform);
		    if (!obj) return;
		    obj.hideFlags = HideFlags.HideAndDontSave;
		    obj.transform.localPosition = Vector3.zero;
		    obj.transform.localRotation = Quaternion.identity;
	    }

        private void LateUpdate()
        {
	        Vector3 euler = Vector3.up * (spinSpeed * Time.time);
	        float bobOffset = Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2) * bobHeight;
	        
	        transform.localEulerAngles = euler;
	        transform.localPosition = basePosition + Vector3.up * bobOffset;
        }
    }
}
