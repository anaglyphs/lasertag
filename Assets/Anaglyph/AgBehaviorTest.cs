using UnityEngine;

public class AgBehaviorTest : SuperAwakeBehavior
{
	private int numTimesOnCreateCalled = 0;

	protected override void SuperAwake()
	{
		numTimesOnCreateCalled++;
		Debug.Log("AgBehaviorTest SuperAwake called " + numTimesOnCreateCalled + " time(s)");
	}

	private void Start()
	{
		Debug.Log("AgBehaviorTest Start called!");
	}
}
