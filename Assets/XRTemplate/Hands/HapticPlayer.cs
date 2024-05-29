using UnityEngine;

namespace XRTemplate
{
    public class HapticPlayer : MonoBehaviour
    {
        [SerializeField] private Oculus.Haptics.HapticClip clip;
        private HandSide side;
        private Oculus.Haptics.Controller controller;

        private Oculus.Haptics.HapticClipPlayer player;

		private void Awake()
		{
			side = GetComponentInParent<HandSide>();
            player = new(clip);

            controller = side.isRight ? Oculus.Haptics.Controller.Right : Oculus.Haptics.Controller.Left;
		}


        public void PlayClip()
        {
            player.Play(controller);
        }
    }
}