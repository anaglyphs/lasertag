using UnityEngine.Events;

namespace Anaglyph.Lasertag
{
    public class RoundEvents : SuperAwakeBehavior
    {
		public UnityEvent OnCountdown;
		public UnityEvent OnStart;
		public UnityEvent OnEnd;

		protected override void SuperAwake()
		{
			RoundManager.OnGameCountdown += OnCountdown.Invoke;
			RoundManager.OnGameStart += OnStart.Invoke;
			RoundManager.OnGameEnd += OnEnd.Invoke;
		}

		private void OnDestroy()
		{
			RoundManager.OnGameCountdown -= OnCountdown.Invoke;
			RoundManager.OnGameStart -= OnStart.Invoke;
			RoundManager.OnGameEnd -= OnEnd.Invoke;
		}
	}
}
