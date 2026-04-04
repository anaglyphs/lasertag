using System;
using UnityEngine;
using System.Net.Sockets;
using Anaglyph;
using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.DepthKit;
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

		private byte[] recvHeaderBuf = new byte[5];
		private byte[] recvPayloadBuf;
		private int recvHeaderOffset;
		private int recvPayloadOffset;
		private int recvPayloadLength;
		private bool readingPayload;
		private byte recvMessageType;

		[SerializeField] private ComputeShader voxelWriterCompute;
		private ComputeKernel writeVoxelsKernel;
		private ComputeBuffer voxelBuffer;

		[SerializeField] private MeshFilter meshFilter;
		private Mesh receivedMesh;
		
		private bool waitingForMesh;
		
		private Vector3 sentHeadPos;
		private Quaternion sentHeadRot;

		private void Start()
		{
			depthCopyKernel = new ComputeKernel(depthCopyCompute, "CopyDepth");
			writeVoxelsKernel = new ComputeKernel(voxelWriterCompute, "WriteVoxels");

			try
			{
				client = new TcpClient(serverIP, serverPort);
				stream = client.GetStream();

				EnvironmentMapper.UseEdgeServer = true;

				Debug.Log($"[EdgeServerClient] Connected to edge server at {serverIP}:{serverPort}");
			}
			catch (Exception e)
			{
				Debug.LogError($"[EdgeServerClient] Failed to connect: {e.Message}");
			}

			// subscribe to depth updates
			DepthKitDriver.Instance.Updated += OnDepthUpdated;

			var mapper = EnvironmentMapper.Instance;
			if (mapper != null)
				Debug.Log(
					$"Volume: {mapper.VoxelCount}, voxelSize: {mapper.VoxelSize}, voxelDist: {mapper.VoxelDistance}");
		}


		private void Update()
		{
			if (stream == null || !stream.DataAvailable) return;

			if (!readingPayload)
			{
				// Read header
				var needed = 5 - recvHeaderOffset;
				var read = stream.Read(recvHeaderBuf, recvHeaderOffset, needed);
				recvHeaderOffset += read;

				if (recvHeaderOffset < 5) return;

				// Parse header
				var type = recvHeaderBuf[0];
				recvMessageType = type;
				recvPayloadLength = (recvHeaderBuf[1] << 24) | (recvHeaderBuf[2] << 16) |
				                    (recvHeaderBuf[3] << 8) | recvHeaderBuf[4];

				recvPayloadBuf = new byte[recvPayloadLength];
				recvPayloadOffset = 0;
				readingPayload = true;
				recvHeaderOffset = 0;
			}

			if (readingPayload)
			{
				var needed = recvPayloadLength - recvPayloadOffset;
				var read = stream.Read(recvPayloadBuf, recvPayloadOffset, needed);
				recvPayloadOffset += read;

				if (recvPayloadOffset < recvPayloadLength) return;

				// Full payload received
				readingPayload = false;
				if (recvMessageType == 0x03)
				{
					ApplyMeshData(recvPayloadBuf);
				}
				else
				{
					ApplyVoxelData(recvPayloadBuf);
				}
			}
		}

		private struct VoxelData
		{
			public uint coordX, coordY, coordZ;
			public float value;
		}


		private void ApplyVoxelData(byte[] data)
		{
			var offset = 0;
			var voxelCount = (int)BitConverter.ToUInt32(data, offset);
			offset += 4;

			if (voxelCount == 0) return;

			// Parse voxels
			var voxels = new VoxelData[voxelCount];
			for (var i = 0; i < voxelCount; i++)
			{
				voxels[i].coordX = BitConverter.ToUInt32(data, offset);
				offset += 4;
				voxels[i].coordY = BitConverter.ToUInt32(data, offset);
				offset += 4;
				voxels[i].coordZ = BitConverter.ToUInt32(data, offset);
				offset += 4;
				voxels[i].value = BitConverter.ToSingle(data, offset);
				offset += 4;
			}

			// Upload to GPU
			if (voxelBuffer == null || voxelBuffer.count < voxelCount)
			{
				voxelBuffer?.Release();
				voxelBuffer = new ComputeBuffer(voxelCount, 16); // 4x uint/float = 16 bytes
			}

			voxelBuffer.SetData(voxels);

			// Write into volume
			var volume = EnvironmentMapper.Instance.Volume;
			voxelWriterCompute.SetInt("voxelCount", voxelCount);
			writeVoxelsKernel.Set("volume", volume);
			writeVoxelsKernel.Set("voxels", voxelBuffer);
			writeVoxelsKernel.DispatchFit(voxelCount, 1, 1);

			Debug.Log($"Wrote {voxelCount} voxels to volume");
		}

		private void ApplyMeshData(byte[] data)
		{
			waitingForMesh = false;
			float posDelta = Vector3.Distance(Camera.main.transform.position, sentHeadPos);
			float rotDelta = Quaternion.Angle(Camera.main.transform.rotation, sentHeadRot);
			if (posDelta > 0.15f || rotDelta > 15f) return;

			int offset = 0;
			int vertCount = (int)BitConverter.ToUInt32(data, offset);
			offset += 4;
			int idxCount = (int)BitConverter.ToUInt32(data, offset);
			offset += 4;

			if (vertCount == 0) return;

			var positions = new Vector3[vertCount];
			var normals = new Vector3[vertCount];
			for (int i = 0; i < vertCount; i++)
			{
				positions[i] = new Vector3(
					BitConverter.ToSingle(data, offset), // x
					BitConverter.ToSingle(data, offset + 4), // y
					BitConverter.ToSingle(data, offset + 8) // z
				);
				normals[i] = new Vector3(
					BitConverter.ToSingle(data, offset + 12),
					BitConverter.ToSingle(data, offset + 16),
					BitConverter.ToSingle(data, offset + 20)
				);
				offset += 24;
			}

			var indices = new int[idxCount];
			for (int i = 0; i < idxCount; i++)
			{
				indices[i] = (int)BitConverter.ToUInt32(data, offset);
				offset += 4;
			}

			if (receivedMesh == null)
				receivedMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
			else
				receivedMesh.Clear();

			receivedMesh.SetVertices(positions);
			receivedMesh.SetNormals(normals);
			receivedMesh.SetTriangles(indices, 0);

			if (meshFilter != null)
				meshFilter.sharedMesh = receivedMesh;

			Debug.Log($"[EdgeServerClient] Applied mesh: {vertCount} verts, {idxCount / 3} tris");
		}

		private void OnDepthUpdated()
		{
			if (stream == null || readbackPending || waitingForMesh) return;

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

				var matrices = SerializeFrameData();
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

				sentHeadPos = Camera.main.transform.position;
				sentHeadRot = Camera.main.transform.rotation;
				waitingForMesh = true;
				stream.Write(header, 0, 5);
				stream.Write(payload, 0, payload.Length);
				Debug.Log($"Sent depth frame: {payload.Length} bytes");
			});
		}


		private byte[] SerializeFrameData()
		{
			var dkd = DepthKitDriver.Instance;
			var mapper = EnvironmentMapper.Instance;

			// 512 bytes matrices + 28 bytes volume config = 540 bytes
			var data = new byte[540];
			var offset = 0;

			void WriteFloat(float f)
			{
				var b = BitConverter.GetBytes(f);
				Buffer.BlockCopy(b, 0, data, offset, 4);
				offset += 4;
			}

			void WriteInt(int i)
			{
				var b = BitConverter.GetBytes(i);
				Buffer.BlockCopy(b, 0, data, offset, 4);
				offset += 4;
			}

			void WriteMatrix(Matrix4x4 m)
			{
				for (var i = 0; i < 16; i++)
					WriteFloat(m[i]);
			}

			// Matrices (512 bytes)
			for (var eye = 0; eye < 2; eye++) WriteMatrix(dkd.View[eye]);
			for (var eye = 0; eye < 2; eye++) WriteMatrix(dkd.Proj[eye]);
			for (var eye = 0; eye < 2; eye++) WriteMatrix(dkd.ViewInv[eye]);
			for (var eye = 0; eye < 2; eye++) WriteMatrix(dkd.ProjInv[eye]);

			// Volume config (28 bytes)
			WriteInt(mapper.VoxelCount.x);
			WriteInt(mapper.VoxelCount.y);
			WriteInt(mapper.VoxelCount.z);
			WriteFloat(mapper.VoxelSize);
			WriteFloat(mapper.VoxelDistance);
			WriteFloat(mapper.MaxUpdateDist);
			WriteInt(mapper.PlayerHeads.Count);

			return data;
		}

		private void OnDestroy()
		{
			stream?.Close();
			client?.Close();
			voxelBuffer?.Release();
			EnvironmentMapper.UseEdgeServer = false;
		}
	}
}