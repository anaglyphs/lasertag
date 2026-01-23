// using System;
// using Anaglyph.Menu;
// using UnityEngine;
//
// namespace Anaglyph.Lasertag
// {
// 	public class PageFitter : MonoBehaviour
// 	{
// 		[SerializeField] private NavPagesParent pages;
// 		[SerializeField] private float padding = 40;
//
// 		private RectTransform rt;
//
// 		private void Awake()
// 		{
// 			TryGetComponent(out rt);
// 			pages.Changed += OnPageChanged;
// 		}
//
// 		private void Start()
// 		{
// 			OnPageChanged(pages.CurrentPage);
// 		}
//
// 		private Vector2 targetLerpFrom;
// 		private Vector2 targetLerpTo;
// 		private float changeTime;
//
// 		private void OnPageChanged(NavPage page)
// 		{
// 			targetLerpFrom = targetLerpTo;
// 			changeTime = Time.time;
// 		}
//
// 		private void Update()
// 		{
// 			NavPage page = pages.CurrentPage;
//
// 			if (!page)
// 				return;
// 			
// 			Rect pageRect = page.RectTransform.rect;
// 			targetLerpTo = new Vector2(pageRect.width + padding, pageRect.height + padding);
// 			
// 			AnimationCurve curve = pages.normalizedTransitionCurve;
// 			float transitionLength = pages.transitionLengthSeconds;
// 			float timeSince = Time.time - changeTime;
//
// 			float t = curve.Evaluate(timeSince / transitionLength);
//
// 			rt.sizeDelta = Vector2.Lerp(targetLerpFrom, targetLerpTo, t);
// 		}
// 	}
// }

