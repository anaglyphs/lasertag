using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Anaglyph.LaserTag.NPCs
{
	public class Navigator : MonoBehaviour
	{
		private const GraphicsFormat Format = GraphicsFormat.R8G8B8A8_SNorm;
		private const GraphicsFormat DepthForm = GraphicsFormat.D16_UNorm;
		
		[SerializeField] private int texSize = 64;
		
		[SerializeField] private Camera cam;
		[SerializeField] private LayerMask mask;

		private RenderTexture tex;
		
		private void Awake()
		{
			int s = texSize;
			tex = new RenderTexture(s, s, Format, DepthForm, 1);
			cam.targetTexture = tex;
			cam.cullingMask = mask;
			cam.enabled = false;
			cam.backgroundColor = Color.black;
		}

		private void Update()
		{
			//cam.Render();
		}
	}
}
