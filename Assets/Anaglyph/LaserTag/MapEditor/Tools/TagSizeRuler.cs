using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.LaserTag.MapEditor.Tools
{
	/// <summary>Local ruler visuals; points follow the tracking origin when colocation moves the rig.</summary>
	public sealed class TagSizeRuler : IDisposable
	{
		private readonly GameObject root;
		private readonly LineRenderer tip;
		private readonly LineRenderer start;
		private readonly LineRenderer edge;
		private readonly TextMesh label;
		private readonly Material labelMaterial;
		private Vector3 localStart;
		public bool HasStart { get; private set; }

		public TagSizeRuler(Transform trackingSpace, int layer)
		{
			Material material = Resources.Load<Material>("TagSizeRuler");
			root = new GameObject("Tag size ruler");
			root.transform.SetParent(trackingSpace, false);
			root.layer = layer;
			tip = MakeLine("Controller tip", material, 0.004f);
			start = MakeLine("First corner", material, 0.004f);
			edge = MakeLine("Measured edge", material, 0.0015f);

			var textObject = new GameObject("Length", typeof(TextMesh));
			textObject.layer = layer;
			textObject.transform.SetParent(root.transform, false);
			label = textObject.GetComponent<TextMesh>();
			label.font = Resources.Load<Font>("Fonts/DINish/TTF/DINish-Regular");
			label.fontSize = 64;
			label.characterSize = 0.002f;
			label.anchor = TextAnchor.MiddleCenter;
			label.alignment = TextAlignment.Center;
			label.color = Color.white;
			var renderer = label.GetComponent<MeshRenderer>();
			labelMaterial = new Material(material);
			labelMaterial.mainTexture = label.font.material.mainTexture;
			renderer.sharedMaterial = labelMaterial;
			Font.textureRebuilt += OnFontTextureRebuilt;
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			Reset();
		}

		private void OnFontTextureRebuilt(Font font)
		{
			if (font == label.font)
				labelMaterial.mainTexture = font.material.mainTexture;
		}

		private LineRenderer MakeLine(string name, Material material, float width)
		{
			var obj = new GameObject(name, typeof(LineRenderer));
			obj.layer = root.layer;
			obj.transform.SetParent(root.transform, false);
			var line = obj.GetComponent<LineRenderer>();
			line.sharedMaterial = material;
			line.useWorldSpace = true;
			line.positionCount = 2;
			line.startWidth = line.endWidth = width;
			line.startColor = line.endColor = Color.white;
			line.numCapVertices = 8;
			line.alignment = LineAlignment.View;
			line.shadowCastingMode = ShadowCastingMode.Off;
			line.receiveShadows = false;
			return line;
		}

		public void SetStart(Vector3 worldPoint)
		{
			localStart = root.transform.InverseTransformPoint(worldPoint);
			HasStart = true;
		}

		public float LengthCm(Vector3 worldPoint) =>
			Vector3.Distance(root.transform.TransformPoint(localStart), worldPoint) * 100f;

		public void Reset()
		{
			HasStart = false;
			root.SetActive(false);
		}

		public void Update(Vector3 worldTip, Camera camera, bool showTip)
		{
			root.SetActive(true);
			tip.enabled = showTip;
			DrawDot(tip, worldTip, camera.transform.up);
			start.enabled = edge.enabled = HasStart;
			label.gameObject.SetActive(HasStart);
			if (!HasStart) return;

			Vector3 worldStart = root.transform.TransformPoint(localStart);
			DrawDot(start, worldStart, camera.transform.up);
			edge.SetPosition(0, worldStart);
			edge.SetPosition(1, worldTip);
			Vector3 midpoint = (worldStart + worldTip) * 0.5f;
			if (!TryGetLabelRotation(worldTip - worldStart, midpoint, camera.transform.position,
				camera.transform.right, camera.transform.up, out Quaternion rotation))
			{
				label.gameObject.SetActive(false);
				return;
			}
			label.transform.SetPositionAndRotation(midpoint + rotation * Vector3.up * 0.015f, rotation);
			label.text = MenuCopy.Format("Game", "ruler.length", LengthCm(worldTip));
		}

		private static void DrawDot(LineRenderer dot, Vector3 point, Vector3 up)
		{
			// A very short round-capped line draws a dot without a collider or a generated mesh asset.
			dot.SetPosition(0, point - up * 0.00001f);
			dot.SetPosition(1, point + up * 0.00001f);
		}

		public static bool TryGetLabelRotation(Vector3 edge, Vector3 midpoint, Vector3 viewer,
			Vector3 viewerRight, Vector3 viewerUp, out Quaternion rotation)
		{
			rotation = Quaternion.identity;
			if (edge.sqrMagnitude < 0.000001f) return false;
			Vector3 right = edge.normalized;
			float horizontal = Vector3.Dot(right, viewerRight);
			if (horizontal < -0.01f || (Mathf.Abs(horizontal) <= 0.01f && Vector3.Dot(right, viewerUp) < 0f))
				right = -right;
			// Text's baseline stays parallel to the edge; rotate around it to face the viewer.
			Vector3 forward = Vector3.ProjectOnPlane(midpoint - viewer, right);
			if (forward.sqrMagnitude < 0.000001f) return false;
			rotation = Quaternion.LookRotation(forward, Vector3.Cross(forward, right));
			return true;
		}

		public void Dispose()
		{
			Font.textureRebuilt -= OnFontTextureRebuilt;
			if (root == null) return;
			if (Application.isPlaying)
			{
				UnityEngine.Object.Destroy(root);
				UnityEngine.Object.Destroy(labelMaterial);
			}
			else
			{
				UnityEngine.Object.DestroyImmediate(root);
				UnityEngine.Object.DestroyImmediate(labelMaterial);
			}
		}
	}
}
