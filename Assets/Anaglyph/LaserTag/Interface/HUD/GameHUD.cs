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
		private InputDevice follow = default;

		private void Awake()
		{
			MatchReferee.StateChanged += OnMatchStateChange;
			OnMatchStateChange(MatchReferee.State);
		}

		private void OnEnable()
	    {
		    mainCamera = Camera.main;

			InputDevices.deviceConnected += OnDeviceEvent;
			InputDevices.deviceDisconnected += OnDeviceEvent;
			FindController();
		}

		private void OnDisable()
		{
			InputDevices.deviceConnected -= OnDeviceEvent;
			InputDevices.deviceDisconnected -= OnDeviceEvent;
		}

		private void OnDestroy()
		{
			MatchReferee.StateChanged -= OnMatchStateChange;
		}

		private void OnDeviceEvent(InputDevice obj) => FindController();

	    private void OnMatchStateChange(MatchState state)
	    {
		    bool playing = state == MatchState.Playing;
		    
		    gameObject.SetActive(playing);

			if (state == MatchState.NotPlaying)
			{
				timerGoalHUD.SetActive(false);
				scoreGoalHUD.SetActive(false);
			}
			else
			{
				MatchSettings settings = MatchReferee.Settings;

				timerGoalHUD.SetActive(settings.winCondition == WinCondition.Timer);
				scoreGoalHUD.SetActive(settings.winCondition == WinCondition.ReachScore);

				switch (settings.winCondition)
				{
					case WinCondition.ReachScore:
						scoreTarget.text = settings.scoreTarget.ToString();
						break;
				}
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

		private void FindController()
		{
			InputDevice controller = default;
			foreach (var potentialNode in nodesToCheck)
			{
				controller = InputDevices.GetDeviceAtXRNode(potentialNode);
				if (controller.isValid)
				{
					follow = controller;
					break;
				}
			}
		}

	    private void LateUpdate()
	    {
		    if (!follow.isValid) return;

			var camTrans = mainCamera.transform;
			var camPos = camTrans.position;

			var chars = follow.characteristics;

			if (chars.HasFlag(InputDeviceCharacteristics.HeldInHand & InputDeviceCharacteristics.TrackedDevice))
		    {
			    Vector3 pos = Vector3.zero;
			    follow.TryGetFeatureValue(CommonUsages.devicePosition, out pos);
			    pos = MainXRRig.TrackingSpace.TransformPoint(pos);

				bool isRight = chars.HasFlag(InputDeviceCharacteristics.Right);
				Vector3 handToCam = (camPos - pos).normalized * (isRight ? -1 : 1);
				Vector3 offs = Vector3.Cross(handToCam, camTrans.up);
			    offs.y = 0;
			    offs = offs.normalized * controllerOffset;
			    transform.position = pos + offs;

			    var lookDir = (transform.position - camPos).normalized;
			    Quaternion rotation = Quaternion.LookRotation(lookDir, Vector3.up);
			    transform.rotation = rotation;
		    }
	    }
    }
}
