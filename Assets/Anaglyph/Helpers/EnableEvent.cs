using UnityEngine;
using UnityEngine.Events;

public class EnableEvent : MonoBehaviour
{
	[SerializeField] private UnityEvent onEnable = new();

	private void OnEnable()
	{
		onEnable.Invoke();
	}
}
