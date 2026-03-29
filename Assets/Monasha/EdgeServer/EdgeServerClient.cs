using System;
using UnityEngine;
using System.Net.Sockets;
using Anaglyph;
using Anaglyph.XRTemplate.DepthKit;
using Unity.Collections;
using UnityEngine.Rendering;


namespace Monasha.EdgeServer
{
	public class EdgeServerClient : MonoBehaviour
	{
		[SerializeField] private string serverIP = "127.0.0.1";
		[SerializeField] private int serverPort = 9876;

		private TcpClient client;
		private NetworkStream stream;
		private bool readbackPending;

		[SerializeField] private ComputeShader depthCopyCompute;
		private ComputeKernel depthCopyKernel;
		private RenderTexture depthCopy;


		private void Start()
		{
			depthCopyKernel = new ComputeKernel(depthCopyCompute, "CopyDepth");

			try
			{
				client = new TcpClient(serverIP, serverPort);
				stream = client.GetStream();
				Debug.Log($"[EdgeServerClient] Connected to edge server at {serverIP}:{serverPort}");
			}
			catch (Exception e)
			{
				Debug.LogError($"[EdgeServerClient] Failed to connect: {e.Message}");
			}

			// subscribe to depth updates
			DepthKitDriver.Instance.Updated += OnDepthUpdated;
		}

		private void Update()
		{
			if (stream == null) return;

			// Check if ack bytes arrived
			if (!stream.DataAvailable) return;

			var buffer = new byte[256];
			var bytesRead = stream.Read(buffer, 0, buffer.Length);
			Debug.Log($"[EdgeServerClient] Received ack: {bytesRead} bytes, type=0x{buffer[0]:X2}");
		}

		private void OnDepthUpdated()
		{
			if (stream == null || readbackPending) return;

			var depthTex = DepthKitDriver.Instance.DepthTex;
			if (depthTex == null) return;

			if (depthCopy == null || depthCopy.width != depthTex.width || depthCopy.height != depthTex.height)
			{
				if (depthCopy != null) depthCopy.Release();
				depthCopy = new RenderTexture(depthTex.width, depthTex.height, 0, RenderTextureFormat.RFloat)
				{
					enableRandomWrite = true
				};
				depthCopy.Create();
			}

			depthCopyKernel.Set("_InputDepth", depthTex);
			depthCopyKernel.Set("_OutputDepth", depthCopy);
			depthCopyKernel.DispatchFit(depthCopy);

			readbackPending = true;
			AsyncGPUReadback.Request(depthCopy, 0, request =>
			{
				readbackPending = false;

				if (request.hasError)
				{
					Debug.LogError("GPU readback failed");
					return;
				}

				var data = request.GetData<byte>();
				var bytes = data.ToArray();

				var matrices = SerializeMatrices();
				var payload = new byte[matrices.Length + bytes.Length];
				Buffer.BlockCopy(matrices, 0, payload, 0, matrices.Length);
				Buffer.BlockCopy(bytes, 0, payload, matrices.Length, bytes.Length);

				var header = new byte[5];
				header[0] = 0x01;
				var len = payload.Length;
				header[1] = (byte)(len >> 24);
				header[2] = (byte)(len >> 16);
				header[3] = (byte)(len >> 8);
				header[4] = (byte)(len);

				stream.Write(header, 0, 5);
				stream.Write(payload, 0, payload.Length);
				Debug.Log($"Sent depth frame: {payload.Length} bytes");
			});
		}


		private byte[] SerializeMatrices()
		{
			var dkd = DepthKitDriver.Instance;

			// 4 matrix arrays x 2 eyes x 64 bytes each = 512 bytes
			var data = new byte[512];
			var offset = 0;

			for (var eye = 0; eye < 2; eye++) WriteMatrix(dkd.View[eye]);
			for (var eye = 0; eye < 2; eye++) WriteMatrix(dkd.Proj[eye]);
			for (var eye = 0; eye < 2; eye++) WriteMatrix(dkd.ViewInv[eye]);
			for (var eye = 0; eye < 2; eye++) WriteMatrix(dkd.ProjInv[eye]);

			return data;

			void WriteMatrix(Matrix4x4 m)
			{
				for (var i = 0; i < 16; i++)
				{
					var floatBytes = BitConverter.GetBytes(m[i]);
					Buffer.BlockCopy(floatBytes, 0, data, offset, 4);
					offset += 4;
				}
			}
		}

		private void OnDestroy()
		{
			stream?.Close();
			client?.Close();
		}
	}
}