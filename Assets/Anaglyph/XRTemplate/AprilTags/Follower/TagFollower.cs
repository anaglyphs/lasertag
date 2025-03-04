using UnityEngine;

namespace EnvisionCenter.XRTemplate.QuestCV
{
	public class TagFollower : MonoBehaviour
	{
		[SerializeField] private int followTagID = -1;

		public bool follow = true;

		//[Header("In Meters")]
		//public float minPositionChange = 0.02f;
		//[Header("In Degrees")]
		//public float minAngleChange = 2f;

		private void Awake()
		{
			if(followTagID != -1)
				FollowTag(followTagID);
		}

		private void OnDestroy()
		{
			TagFollowerMover.Instance.allFollowers.Remove(followTagID);
		}

		public void FollowTag(int id)
		{
			if (TagFollowerMover.Instance.allFollowers.ContainsKey(id))
				throw new System.Exception("Another tag follower is already following that ID!");

			
			TagFollowerMover.Instance.allFollowers.Remove(followTagID);
			TagFollowerMover.Instance.allFollowers.Add(id, this);
			followTagID = id;
		}

		public void OnDetect(Pose pose)
		{
			if (!follow)
				return;

			//bool inRoughlySamePosition = Vector3.Distance(transform.position, pose.position) < minPositionChange;

			//if (inRoughlySamePosition) return;

			//bool inRoughlySameRotation = Quaternion.Angle(transform.rotation, pose.rotation) < minAngleChange;

			//if (inRoughlySameRotation) return;
			
			transform.SetPositionAndRotation(pose.position, pose.rotation);
		}
	}
}