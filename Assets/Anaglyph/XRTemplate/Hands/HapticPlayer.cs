using UnityEngine;
using Oculus.Haptics;

namespace Anaglyph.XRTemplate
{
    public class HapticPlayer : MonoBehaviour
    {
		[SerializeField] private HapticClip clip = null;
        private HandSide side;
		private Controller controller;

		private HapticClipPlayer player;

		private void Awake()
		{
			side = GetComponentInParent<HandSide>();
            player = new(clip);

			controller = side.isRight ? Controller.Right : Controller.Left;
		}


        public void PlayClip()
        {
            player.Play(controller);
        }
    }
}