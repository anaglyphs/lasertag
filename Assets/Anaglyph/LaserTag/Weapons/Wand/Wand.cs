using Anaglyph.Lasertag.Logistics;
using Anaglyph.XRTemplate;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SpatialTracking;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using static OVRPlugin;

namespace Anaglyph.Lasertag.Weapons
{
	public class Wand : MonoBehaviour
	{
		[SerializeField] private GameObject boltPrefab;
		[SerializeField] private Transform emitFromTransform;
		[SerializeField] private double firePosePredictionSecs = 0.1f;


		public float sampleFrequency = 1f / 240f;

		public float peakThreshold = 20f;
		public float stopThreshold = 3f;

		public float minFireVecDotWithViewDir = 0.4f;

		private Camera mainCamera;
		private HandedHierarchy handedHierarchy;
		private Node node;
		
		private double timeLastFrame;
		private double lastFireTime;

		private TrackedPoseDriver trackedPoseDriver;

		private bool peaked = false;
		private double peakedTime = 0;

		private void Awake()
		{
			handedHierarchy = GetComponentInParent<HandedHierarchy>();
			trackedPoseDriver = GetComponentInParent<TrackedPoseDriver>(true);
			mainCamera = Camera.main;

		}

		private void OnEnable()
		{
			timeLastFrame = GetTimeInSeconds();
		}

		private void Start()
		{
			var handedness = handedHierarchy.Handedness;

			if (handedness == InteractorHandedness.None)
				return;

			node = handedness == InteractorHandedness.Left ?
				Node.HandLeft : Node.HandRight;
		}

		private void LateUpdate()
		{
			// sample
			double timeThisFrame = GetTimeInSeconds();

			double deltaTime = timeThisFrame - timeLastFrame;

			int numSamples = (int)(deltaTime / sampleFrequency);
			double sampleTimeStep = deltaTime / numSamples;

			for(int i = 0; i < numSamples; i++)
			{
				double time = timeLastFrame + i * sampleTimeStep;

				var poseState = GetNodePoseStateAtTime(time, node);
				var av = poseState.AngularVelocity;
				Vector3 angVel = new(av.x, av.y, av.z);

				var headPoseState = GetNodePoseStateAtTime(time, Node.Head);
				var hav = headPoseState.AngularVelocity;
				Vector3 headAngVel = new(hav.x, hav.y, hav.z);

				// relative to head
				angVel -= headAngVel;
				
				float angSpeed = Vector3.Magnitude(angVel);

				if(!peaked && angSpeed > peakThreshold)
				{
					peaked = true;
					peakedTime = time;

				} else if (peaked && time > peakedTime && angSpeed < stopThreshold)
				{
					peaked = false;
					
					double fireTime = time + firePosePredictionSecs;
					poseState = GetNodePoseStateAtTime(fireTime, node);
					var pose = poseState.Pose.ToOVRPose();

					Vector3 fireForward = pose.orientation * Vector3.forward;
					Vector3 camForward = mainCamera.transform.forward;
					float dot = Vector3.Dot(camForward, fireForward);

					if (dot > minFireVecDotWithViewDir)
					{
						lastFireTime = fireTime;
						Fire(new Pose(pose.position, pose.orientation));
					}

					break;
				}
			}

			timeLastFrame = timeThisFrame;
		}


		public void Fire(Pose firePose)
		{
			if (!NetworkManager.Singleton.IsConnectedClient || !WeaponsManagement.canFire)
				return;



			// jank
			trackedPoseDriver.transform.localPosition = firePose.position;
			trackedPoseDriver.transform.localRotation = firePose.rotation;

			NetworkObject n = NetworkObjectPool.Instance.GetNetworkObject(
				boltPrefab, emitFromTransform.position, emitFromTransform.rotation);

			n.SpawnWithOwnership(NetworkManager.Singleton.LocalClientId);
		}

#if UNITY_EDITOR
		private void OnFire(InputAction.CallbackContext context)
		{
			if (context.performed && context.ReadValueAsButton())
				Fire(new Pose(transform.position, transform.rotation));
		}
#endif
	}
}
