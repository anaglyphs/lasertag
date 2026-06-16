using System;
using StrikerLink.Shared.Utils;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class PanelArranger : MonoBehaviour
	{
		[SerializeField] private float radius = 1;
		[SerializeField] private RectTransform[] panels;
		[SerializeField] private RectTransform center;

		[SerializeField] private float transitionLength = 0.2f;
		[SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

		private float angleOffs;
		private float[] angles;

		private float transitionStartTime;

		private void Start()
		{
			angles = new float[panels.Length];

			PositionPanels();
		}

		private void OnEnable()
		{
			if (didStart)
				PositionPanels();

			transitionStartTime = Time.time;
		}

		private void Update()
		{
			float transitionTime = (Time.time - transitionStartTime) / transitionLength;

			float lerp = transitionCurve.Evaluate(transitionTime);

			for (int i = 0; i < panels.Length; i++)
			{
				float angle = Mathf.Lerp(0, angles[i] - angleOffs, lerp);
				float x = Mathf.Sin(angle) * radius;
				float z = Mathf.Cos(angle) * radius;
				Vector3 pos = new(x, 0, z);

				panels[i].localPosition = pos;
				panels[i].forward = transform.TransformDirection(pos);
			}
		}

		private void PositionPanels()
		{
			float angleSum = 0;
			float offs = 0;
			for (int i = 0; i < panels.Length; i++)
			{
				RectTransform panel = panels[i];
				if (!panel.gameObject.activeSelf)
					continue;

				float width = panel.rect.width * panel.localScale.x;

				float span = width / radius;
				float angle = angleSum + span / 2;
				angles[i] = angle;

				if (panel == center)
					angleOffs = angle;

				angleSum += span;
			}
		}
	}
}