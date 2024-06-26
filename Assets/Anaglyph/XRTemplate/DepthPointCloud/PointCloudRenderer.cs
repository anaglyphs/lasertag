using System;
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
		private uint pointCount;

		private static readonly int PointTexID = Shader.PropertyToID("PointTex");
		private static readonly int PointTexWidthID = Shader.PropertyToID("PointTexWidth");
		private static readonly int PointTexHeightID = Shader.PropertyToID("PointTexHeight");
		private static readonly int PointCountID = Shader.PropertyToID("PointCount");

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

			effect.SetTexture(PointTexID, pointTexture);
			effect.SetInt(PointTexWidthID, pointTexture.width);
			effect.SetInt(PointTexHeightID, pointTexture.height);
			effect.SetUInt(PointCountID, (uint)(pointTexture.width * pointTexture.height));
		}

		public void UpdateAllPoints(List<Vector3> allPoints)
		{
			pointCount = (uint)Mathf.Min(allPoints.Count, pointTexture.width * pointTexture.height);

			for (uint i = 0; i < pointCount; i++)
			{
				uint strided = i * 4;
				Vector3 p = allPoints[(int)i];

				pointPositions[strided + 0] = p.x;
				pointPositions[strided + 1] = p.y;
				pointPositions[strided + 2] = p.z;
				pointPositions[strided + 3] = 1;

				//int x = (i % pointTexture.width);
				//int y = Mathf.FloorToInt(i / pointTexture.width);
				//pointTexture.SetPixel(x, y, new Color(p.x, p.y, p.z));
			}

			pointTexture.SetPixelData(pointPositions, 0);
			pointTexture.Apply();
			effect.SetTexture(PointTexID, pointTexture);

			effect.SetUInt(PointCountID, pointCount);

			effect.Reinit();
			effect.SendEvent(0);
		}

		private void OnDestroy()
		{
			Destroy(pointTexture);
		}
	}
}
