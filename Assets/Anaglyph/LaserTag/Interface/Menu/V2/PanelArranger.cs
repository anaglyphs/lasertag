using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class PanelArranger : MonoBehaviour
	{
		[SerializeField] private float radius = 1;
		[SerializeField] private RectTransform[] panels;
		[SerializeField] private RectTransform center;

		private float[] angles;

		private void Start()
		{
			PositionPanels();
		}

		private void OnEnable()
		{
			if (didStart)
				PositionPanels();
		}

		private void PositionPanels()
		{
			if (angles == null || angles.Length != panels.Length)
				angles = new float[panels.Length];

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
					offs = angle;

				angleSum += span;
			}

			for (int i = 0; i < panels.Length; i++)
			{
				float angle = angles[i] - offs;
				float x = Mathf.Sin(angle) * radius;
				float z = Mathf.Cos(angle) * radius;
				Vector3 pos = new(x, 0, z);

				RectTransform panel = panels[i];
				panel.localPosition = pos;
				panel.forward = transform.TransformDirection(pos);
			}
		}
	}
}