using System;
using Anaglyph.XRTemplate;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Anaglyph.Lasertag
{
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

		    if (node is XRNode.LeftHand or XRNode.RightHand)
		    {
			    Vector3 position = Vector3.zero;
			    controller.TryGetFeatureValue(CommonUsages.devicePosition, out position);
			    position = MainXRRig.TrackingSpace.TransformPoint(position);
			    
			    float offs = controllerOffset;
			    if (node == XRNode.LeftHand)
				    offs *= -1;
			    
			    Vector3 offsV = mainCamera.transform.right;
			    offsV.y = 0;
			    offsV = offsV.normalized * offs;

			    transform.position = position + offsV;
			    
			    var camTrans = mainCamera.transform;
			    var lookDir = (camTrans.position - transform.position).normalized;
			    Quaternion rotation = Quaternion.LookRotation(lookDir, Vector3.up);
			    transform.rotation = rotation;
		    }
	    }
    }
}
