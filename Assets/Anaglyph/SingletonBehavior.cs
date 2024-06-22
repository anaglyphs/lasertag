using UnityEngine;

[DefaultExecutionOrder(-32000)]
public abstract class SingletonBehavior<T> : SuperAwakeBehavior where T : SingletonBehavior<T>
{
	public static T Instance { get; private set; }

	protected override void SuperAwake()
	{
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

	protected abstract void SingletonAwake();

	protected virtual void OnDestroy()
	{
		if (Instance == this)
			Instance = null;

		OnSingletonDestroy();
	}

	protected abstract void OnSingletonDestroy();
}