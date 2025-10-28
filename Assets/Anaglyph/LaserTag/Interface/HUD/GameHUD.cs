using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Anaglyph.Lasertag
{
    public class GameHUD : MonoBehaviour
    {
	    [SerializeField] private Vector3 offset;
	    
	    [SerializeField] private GameObject scoreGoalHUD;
	    [SerializeField] private GameObject timerGoalHUD;
	    
	    [SerializeField] private Text timerTarget;
	    [SerializeField] private Text scoreTarget;
	    
	    private Camera mainCamera;

	    private void OnEnable()
	    {
		    mainCamera = Camera.main;
	    }

	    private void Start()
	    {
		    MatchReferee.StateChanged += OnMatchStateChange;
	    }

	    private void OnDestroy()
	    {
		    MatchReferee.StateChanged -= OnMatchStateChange;
	    }

	    private void OnMatchStateChange(MatchState state)
	    {
		    bool playing = state == MatchState.Playing;
		    
		    gameObject.SetActive(playing);

		    switch (state)
		    {
			    case MatchState.Playing:
				    MatchSettings settings = MatchReferee.Settings;
			    
				    timerGoalHUD.SetActive(settings.winCondition == WinCondition.Timer);
				    scoreGoalHUD.SetActive(settings.winCondition == WinCondition.ReachScore);
			    
				    switch (settings.winCondition)
				    {
					    case WinCondition.ReachScore:
						    scoreTarget.text = settings.scoreTarget.ToString();
						    break;
				    }

				    break;
		    }
	    }

	    private void Update()
	    {
		    bool playing = MatchReferee.State == MatchState.Playing;

		    if (!playing) return;

		    switch (MatchReferee.Settings.winCondition)
		    {
			    case WinCondition.Timer:
				    TimeSpan time = TimeSpan.FromSeconds(MatchReferee.Instance.GetTimeLeft());
					timerTarget.text = time.ToString(@"m\:ss");
				    break;
			    
			    case WinCondition.ReachScore:
				    
				    break;
		    }
	    }

	    private XRNode preferredNode = XRNode.RightHand;
	    private XRNode[] nodesToCheck = { XRNode.RightHand, XRNode.LeftHand };

	    private void LateUpdate()
	    {
		    XRNode node = default;
		    InputDevice controller = default;
		    foreach (var potentialNode in nodesToCheck)
		    {
			    controller = InputDevices.GetDeviceAtXRNode(potentialNode);
			    if (controller.isValid)
			    {
				    node = potentialNode;
				    break;
			    }
		    }

		    if (!controller.isValid) return;
		    
		    Vector3 position = Vector3.zero;
		    controller.TryGetFeatureValue(CommonUsages.devicePosition, out position);

		    Vector3 offs = offset;
		    if (node == XRNode.LeftHand)
			    offs.x *= -1;
		    
		    transform.localPosition = position + offs;
		    transform.LookAt(mainCamera.transform, Vector3.up);
	    }
    }
}
