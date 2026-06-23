using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// Bakes the RectTransform's size (width, height) into UV channel 1 of every
	/// vertex of the UI Graphic's mesh, so a shader can read the element's
	/// dimensions via TEXCOORD1.
	///
	/// UGUI has no per-instance material data (no MaterialPropertyBlock for UI), so
	/// stuffing the size into a spare vertex channel is the standard way to feed
	/// per-Image data to a shared material without breaking batching.
	///
	/// Put this on the same GameObject as the Image. It re-bakes automatically when
	/// the rect dimensions change, in both play and edit mode.
	/// </summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public class UIRect : BaseMeshEffect
	{
		protected override void OnEnable()
		{
			base.OnEnable();
			EnsureCanvasChannel();
			if (graphic != null)
				graphic.SetVerticesDirty();
		}

		public override void ModifyMesh(VertexHelper vh)
		{
			if (!IsActive() || graphic == null)
				return;

			Vector2 size = graphic.rectTransform.rect.size;

			UIVertex v = default;
			int count = vh.currentVertCount;
			for (int i = 0; i < count; i++)
			{
				vh.PopulateUIVertex(ref v, i);
				v.uv1 = size; // -> TEXCOORD1 in the shader
				vh.SetUIVertex(v, i);
			}
		}

		protected override void OnRectTransformDimensionsChange()
		{
			base.OnRectTransformDimensionsChange();
			if (graphic != null)
				graphic.SetVerticesDirty();
		}

		/// <summary>
		/// Makes sure the Canvas that renders this Image actually transmits the
		/// TexCoord1 stream; otherwise the baked rect size never reaches the shader.
		/// </summary>
		private void EnsureCanvasChannel()
		{
			Canvas canvas = graphic != null ? graphic.canvas : GetComponentInParent<Canvas>();
			if (canvas == null)
				return;

			const AdditionalCanvasShaderChannels need = AdditionalCanvasShaderChannels.TexCoord1;
			if ((canvas.additionalShaderChannels & need) != need)
				canvas.additionalShaderChannels |= need;
		}
	}
}