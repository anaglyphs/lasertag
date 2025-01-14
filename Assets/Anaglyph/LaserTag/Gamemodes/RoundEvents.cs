//using UnityEngine.Events;

//namespace Anaglyph.Lasertag
//{
//	public class RoundEvents : SuperAwakeBehavior
//	{
//		public UnityEvent OnNotPlaying = new();
//		public UnityEvent OnQueued = new();
//		public UnityEvent OnCountdown = new();
//		public UnityEvent OnPlaying = new();
//		public UnityEvent OnPlayEnd = new();

//		protected override void SuperAwake()
//		{
//			RoundManager.OnNotPlaying += OnNotPlaying.Invoke;
//			RoundManager.OnQueued += OnQueued.Invoke;
//			RoundManager.OnCountdown += OnCountdown.Invoke;
//			RoundManager.OnPlaying += OnPlaying.Invoke;
//			RoundManager.OnPlayEnd += OnPlayEnd.Invoke;
//		}

//		private void OnDestroy()
//		{
//			RoundManager.OnNotPlaying -= OnNotPlaying.Invoke;
//			RoundManager.OnQueued -= OnQueued.Invoke;
//			RoundManager.OnCountdown -= OnCountdown.Invoke;
//			RoundManager.OnPlaying -= OnPlaying.Invoke;
//			RoundManager.OnPlayEnd -= OnPlayEnd.Invoke;
//		}
//	}
//}
