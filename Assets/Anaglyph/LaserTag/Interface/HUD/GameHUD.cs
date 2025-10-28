using System;
using Anaglyph.XRTemplate;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(9999)]
    public class GameHUD : MonoBehaviour
    {
	    [SerializeField] public float controllerOffset = 0.18f;
	    
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
			OnMatchStateChange(MatchReferee.State);

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

				default:
					timerGoalHUD.SetActive(false);
					scoreGoalHUD.SetActive(false);

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

			var camTrans = mainCamera.transform;

			if (node is XRNode.LeftHand or XRNode.RightHand)
		    {
			    Vector3 pos = Vector3.zero;
			    controller.TryGetFeatureValue(CommonUsages.devicePosition, out pos);
			    pos = MainXRRig.TrackingSpace.TransformPoint(pos);

				bool isRight = node == XRNode.RightHand;
				Vector3 offs = camTrans.right * (isRight ? -1 : 1);
			    offs.y = 0;
			    offs = offs.normalized * controllerOffset;
			    transform.position = pos + offs;

			    var lookDir = (transform.position - camTrans.position).normalized;
			    Quaternion rotation = Quaternion.LookRotation(lookDir, Vector3.up);
			    transform.rotation = rotation;
		    }
	    }
    }
}
