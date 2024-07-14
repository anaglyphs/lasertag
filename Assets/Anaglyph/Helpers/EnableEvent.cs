using UnityEngine;
using UnityEngine.Events;

public class EnableEvent : MonoBehaviour
{
	public UnityEvent onEnable = new();

	private void OnEnable()
	{
		onEnable.Invoke();
	}
}
