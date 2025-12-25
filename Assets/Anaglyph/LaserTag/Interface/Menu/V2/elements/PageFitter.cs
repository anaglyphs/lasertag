using Anaglyph.Menu;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class PageFitter : MonoBehaviour
	{
		[SerializeField] private NavPagesParent pages;
		[SerializeField] private float padding = 40;

		private RectTransform rt;

		private void Start()
		{
			TryGetComponent(out rt);
			pages.Changed += OnPageChanged;

			OnPageChanged(pages.CurrentPage);
		}

		private Vector2 targetLerpFrom;
		private Vector2 targetLerpTo;
		private float changeTime;

		private void OnPageChanged(NavPage page)
		{
			Rect pageRect = page.RectTransform.rect;

			targetLerpFrom = targetLerpTo;
			targetLerpTo = new Vector2(pageRect.width + padding, pageRect.height + padding);
			changeTime = Time.time;
		}

		private void Update()
		{
			AnimationCurve curve = pages.normalizedTransitionCurve;
			float transitionLength = pages.transitionLengthSeconds;
			float timeSince = Time.time - changeTime;

			float t = curve.Evaluate(timeSince / transitionLength);

			rt.sizeDelta = Vector2.Lerp(targetLerpFrom, targetLerpTo, t);
		}
	}
}