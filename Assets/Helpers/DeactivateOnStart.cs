using UnityEngine;

[DefaultExecutionOrder(32000)]
public class DeactivateOnStart : MonoBehaviour
{
	private void Start()
	{
		gameObject.SetActive(false);
	}
}