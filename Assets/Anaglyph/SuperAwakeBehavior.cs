using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// SuperAwakeBehaviors call SuperAwake() even when disabled. 
/// </summary>
public abstract class SuperAwakeBehavior : MonoBehaviour
{
	public SuperAwakeBehavior() : base() {
		MainThreadActionCaller.QueueActionOnMainThread(CallAwakeIfDisabled);
	}

	private bool superAwakeCalled = false;

	private void CallAwakeIfDisabled()
	{
		if (this == null || !Application.isPlaying || isActiveAndEnabled || superAwakeCalled)
			return;

#if UNITY_EDITOR
		if (PrefabUtility.IsPartOfPrefabAsset(this))
			return;
#endif

		superAwakeCalled = true;
		SuperAwake();
	}

	private void Awake()
	{
		if (!superAwakeCalled)
		{
			superAwakeCalled = true;
			SuperAwake();
		}
	}

	protected abstract void SuperAwake();
}