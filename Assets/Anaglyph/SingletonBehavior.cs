using UnityEngine;

[DefaultExecutionOrder(-32000)]
public abstract class SingletonBehavior<T> : MonoBehaviour where T : SingletonBehavior<T>
{
	public static T Instance { get; private set; }

	private void Init()
	{
		if (Instance == this)
			return;

		if (Instance == null)
		{
			Instance = (T)this;
			SingletonAwake();
		}
		else
		{
			Debug.LogWarning("More than one instance of " + GetType() + " created!");
			Destroy(this);
		}
	}

	private void Awake()
	{
		Init();
	}

	protected abstract void SingletonAwake();

	protected virtual void OnDestroy()
	{
		if (Instance == this)
			Instance = null;

		OnSingletonDestroy();
	}

	protected abstract void OnSingletonDestroy();
}