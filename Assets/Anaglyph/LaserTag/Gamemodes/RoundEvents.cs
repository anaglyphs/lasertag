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
			RoundManager.OnCountdown += OnCountdown.Invoke;
			RoundManager.OnRoundStart += OnStart.Invoke;
			RoundManager.OnRoundEnd += OnEnd.Invoke;
		}

		private void OnDestroy()
		{
			RoundManager.OnCountdown -= OnCountdown.Invoke;
			RoundManager.OnRoundStart -= OnStart.Invoke;
			RoundManager.OnRoundEnd -= OnEnd.Invoke;
		}
	}
}
