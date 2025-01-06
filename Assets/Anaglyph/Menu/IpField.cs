using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
    public class IpField : MonoBehaviour
    {
        [SerializeField] private InputField field;

		private void OnValidate()
		{
			TryGetComponent(out field);
		}

		private void Start()
		{
			string ip = IpText.GetLocalIPAddress();
			int length = Mathf.Min(ip.Length, ip.LastIndexOf('.') + 1);
			field.text = ip.Substring(0, length);
		}
	}
}
