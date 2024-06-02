using UnityEngine;

[DefaultExecutionOrder(-32000)]
public abstract class SingletonBehavior<T> : SuperAwakeBehavior where T : SingletonBehavior<T>
{
	public static T Instance { get; private set; }

	protected override void SuperAwake()
	{
		if (ReferenceEquals(Instance, null))
		{
			Instance = (T)this;
		}
		else
		{
			Debug.LogWarning("More than one instance of " + GetType() + " created!");
			Destroy(this);
		}
	}

	protected virtual void OnDestroy()
	{
		if (Instance == this)
			Instance = null;
	}
}