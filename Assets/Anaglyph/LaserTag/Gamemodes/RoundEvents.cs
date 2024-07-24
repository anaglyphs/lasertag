using UnityEngine.Events;

namespace Anaglyph.Lasertag
{
    public class RoundEvents : SuperAwakeBehavior
    {
		public UnityEvent OnCountdown = new();
		public UnityEvent OnStart = new();
		public UnityEvent OnEnd = new();

		protected override void SuperAwake()
		{
			RoundManager.OnGameCountdownEveryone += OnCountdown.Invoke;
			RoundManager.OnGameStartEveryone += OnStart.Invoke;
			RoundManager.OnGameEndEveryone += OnEnd.Invoke;
		}

		private void OnDestroy()
		{
			RoundManager.OnGameCountdownEveryone -= OnCountdown.Invoke;
			RoundManager.OnGameStartEveryone -= OnStart.Invoke;
			RoundManager.OnGameEndEveryone -= OnEnd.Invoke;
		}
	}
}
