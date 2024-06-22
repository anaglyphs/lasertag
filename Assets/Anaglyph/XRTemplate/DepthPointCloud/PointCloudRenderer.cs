using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace Anaglyph.XRTemplate
{
	[RequireComponent(typeof(VisualEffect))]
	public class PointCloudRenderer : MonoBehaviour
	{
		[SerializeField] private Vector2Int pointTexSize = new Vector2Int(1024, 1024);
		[SerializeField] private VisualEffect effect;
		private Texture2D pointTexture;
		private float[] pointPositions;

		private void OnValidate()
		{
			this.SetDefaultComponent(ref effect);
		}

		private void Awake()
		{
			pointTexture = new Texture2D(pointTexSize.x, pointTexSize.y, TextureFormat.RGBAFloat, 1, false);
			pointTexture.wrapMode = TextureWrapMode.Clamp;
			pointTexture.filterMode = FilterMode.Point;
			pointTexture.Apply();

			pointPositions = new float[pointTexture.width * pointTexture.height * 4];

			effect.SetTexture("PointTex", pointTexture);
			effect.SetInt("PointTexSizeX", pointTexture.width);
			effect.SetInt("PointTexSizeY", pointTexture.height);
		}

		public void UpdatePoints(List<Vector3> allPoints)
		{
			int count = Mathf.Min(allPoints.Count, pointTexture.width * pointTexture.height);

			for (int i = 0; i < count; i++)
			{
				int strided = i * 4;
				Vector3 p = allPoints[i];

				pointPositions[strided + 0] = p.x;
				pointPositions[strided + 1] = p.y;
				pointPositions[strided + 2] = p.z;
				pointPositions[strided + 3] = 1;

				//int x = (i % pointTexture.width);
				//int y = Mathf.FloorToInt(i / pointTexture.width);
				//pointTexture.SetPixel(x, y, new Color(p.x, p.y, p.z));
			}

			pointTexture.SetPixelData<float>(pointPositions, 0);

			pointTexture.Apply();
			effect.SetTexture("PointTex", pointTexture);
		}

		private void OnDestroy()
		{
			Destroy(pointTexture);
		}
	}
}
