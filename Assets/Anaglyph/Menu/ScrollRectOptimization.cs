using UnityEngine;
using UnityEngine.UI;

namespace EnvisionCenter.LabUI
{
	public class ScrollRectOptimization : MonoBehaviour
	{
		private ScrollRect scrollRect;
		private Vector2 lastPos;
		private bool wasScrolling;

		ICanvasElement[] canvasElements;

		private void Awake()
		{
			scrollRect = GetComponent<ScrollRect>();
		}



		private void OnEnable()
		{
			Canvas.preWillRenderCanvases += OnCanvasPreRender;
		}

		private void OnDisable()
		{
			Canvas.preWillRenderCanvases -= OnCanvasPreRender;
		}

		private void OnCanvasPreRender()
		{
			bool scrolling = lastPos != scrollRect.normalizedPosition;

			if (scrolling && !wasScrolling)
			{
				canvasElements = scrollRect.content.GetComponentsInChildren<ICanvasElement>(true);
				SetLayoutElementsEnabled(false);
			} else if (!scrolling && wasScrolling)
			{
				SetLayoutElementsEnabled(true);
				LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
			}

			if(scrolling)
			{
				foreach (ICanvasElement element in canvasElements)
					CanvasUpdateRegistry.DisableCanvasElementForRebuild(element);
			}

			lastPos = scrollRect.normalizedPosition;
			wasScrolling = scrolling;
		}

		private void SetLayoutElementsEnabled(bool b)
		{
			Behaviour[] behaviors;

			behaviors = scrollRect.content.GetComponentsInChildren<ContentSizeFitter>(true);
			foreach (var behavior in behaviors)
				behavior.enabled = b;

			behaviors = scrollRect.content.GetComponentsInChildren<LayoutGroup>(true);
			foreach (var behavior in behaviors)
				behavior.enabled = b;
		}
	}
}
