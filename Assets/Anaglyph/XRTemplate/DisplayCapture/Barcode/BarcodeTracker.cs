using Anaglyph.XRTemplate.DepthKit;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XRTemplate.DisplayCapture.Barcodes
{
	public class BarcodeTracker : MonoBehaviour
	{
		[SerializeField] private BarcodeReader barcodeReader;

		[SerializeField] private float horizontalFieldOfViewDegrees = 82f;
		public float Fov => horizontalFieldOfViewDegrees;
		private Matrix4x4 displayCaptureProjection;

		private List<Result> results = new();
		public IEnumerable<Result> Results => results;

		public event Action<IEnumerable<Result>> OnTrackBarcodes = delegate { };

		public struct Result
		{
			public string text;
			public Vector3[] corners; // 4 points
			// for zxing:
			// 0, bottom left
			// 1, top left
			// 2, top right
			// 3, bottom right

			public Pose pose;

			public Result(string text)
			{
				this.text = text;
				corners = new Vector3[4];
				pose = new Pose();
			}
		}

		private void Awake()
		{
			barcodeReader.OnReadBarcodes += OnReadBarcodes;

			Vector2Int size = DisplayCaptureManager.Instance.Size;
			float aspect = size.x / (float)size.y;

			displayCaptureProjection = Matrix4x4.Perspective(Fov, aspect, 1, 100f);
		}

		private void OnDestroy()
		{
			if(barcodeReader != null)
				barcodeReader.OnReadBarcodes -= OnReadBarcodes;
		}

		private void OnReadBarcodes(IEnumerable<BarcodeReader.Result> barcodeResults)
		{
			results.Clear();

			foreach (BarcodeReader.Result barcodeResult in barcodeResults)
			{
				Result trackResult = new Result(barcodeResult.text);

				float timestampInSeconds = barcodeResult.timestamp * 0.000000001f;
				OVRPlugin.PoseStatef headPoseState = OVRPlugin.GetNodePoseStateAtTime(timestampInSeconds, OVRPlugin.Node.Head);
				OVRPose headPose = headPoseState.Pose.ToOVRPose();
				Matrix4x4 headTransform = Matrix4x4.TRS(headPose.position, headPose.orientation, Vector3.one);

				Vector3[] worldPoints = new Vector3[4];

				for (int i = 0; i < 4; i++)
				{
					BarcodeReader.Point pixel = barcodeResult.points[i];

					Vector2Int size = DisplayCaptureManager.Instance.Size;

					Vector2 uv = new Vector2(pixel.x / size.x, 1f - pixel.y / size.y);
					Vector3 worldPos = Unproject(displayCaptureProjection, uv);
					worldPos.z = -worldPos.z;
					worldPos = headTransform.MultiplyPoint(worldPos);
					worldPoints[i] = worldPos;
				}

				DepthToWorld.SampleWorld(worldPoints, out trackResult.corners);

				var corners = trackResult.corners;

				Vector3 up = (corners[1] - corners[0]).normalized;
				Vector3 right = (corners[2] - corners[1]).normalized;
				Vector3 normal = -Vector3.Cross(up, right).normalized;

				Vector3 center = (corners[2] + corners[0]) / 2f;

				trackResult.pose = new Pose(center, Quaternion.LookRotation(normal, up));

				results.Add(trackResult);
			}

			OnTrackBarcodes.Invoke(results);
		}

		private static Vector3 Unproject(Matrix4x4 projection, Vector2 uv)
		{
			Vector2 v = 2f * uv - Vector2.one;
			var p = new Vector4(v.x, v.y, 0.1f, 1f);
			p = projection.inverse * p;
			return new Vector3(p.x, p.y, p.z) / p.w;
		}
	}
}