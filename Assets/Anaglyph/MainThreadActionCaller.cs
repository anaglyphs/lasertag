using System.Collections.Generic;
using System;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine;

public static class MainThreadActionCaller
{
	private static List<Action> actionQueue = new List<Action>();
	private static List<Action> actionQueueCopy = new List<Action>();
	private volatile static bool actionsQueued = false;

	public static void QueueActionOnMainThread(Action action)
	{
		lock (actionQueue)
		{
			if (!actionQueue.Contains(action))
			{
				actionQueue.Add(action);
				actionsQueued = true;
			}
		}
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void AddMainThreadActionCallerToCurrentPlayerLoop()
	{
		var currentPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();

		var callQueuedMethodLoop = new PlayerLoopSystem
		{
			subSystemList = null,
			updateDelegate = CallQueuedActions,
			type = typeof(CallQueuedActionsUpdate)
		};

		int updateSystemIndex = -1;

		for (int i = 0; i < currentPlayerLoop.subSystemList.Length; i++)
		{
			if (currentPlayerLoop.subSystemList[i].type == typeof(Initialization))
			{
				// make sure CallQueuedMethods isn't already added to the current loop system
				foreach (var updateSubsystem in currentPlayerLoop.subSystemList[i].subSystemList)
					if (updateSubsystem.type == typeof(CallQueuedActionsUpdate))
						return;

				updateSystemIndex = i;
				break;
			}
		}

		var initSubsystemList = currentPlayerLoop.subSystemList[updateSystemIndex].subSystemList;
		var modifiedInitSubsystemList = new PlayerLoopSystem[initSubsystemList.Length + 1];
		Array.Copy(initSubsystemList, 0, modifiedInitSubsystemList, 1, initSubsystemList.Length);
		modifiedInitSubsystemList[0] = callQueuedMethodLoop;
		currentPlayerLoop.subSystemList[updateSystemIndex].subSystemList = modifiedInitSubsystemList;

		PlayerLoop.SetPlayerLoop(currentPlayerLoop);
	}

	private struct CallQueuedActionsUpdate { }

	private static void CallQueuedActions()
	{
		if (!actionsQueued)
			return;

		actionQueueCopy.Clear();
		lock (actionQueue)
		{
			actionQueueCopy.AddRange(actionQueue);

			actionQueue.Clear();
			actionsQueued = false;
		}

		for (int i = 0; i < actionQueueCopy.Count; i++)
		{
			try
			{
				actionQueueCopy[i].Invoke();
			} catch (Exception e)
			{
				Debug.LogException(e);
			}
		}
	}
}