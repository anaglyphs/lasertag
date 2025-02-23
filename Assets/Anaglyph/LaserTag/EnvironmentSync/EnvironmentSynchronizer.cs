#define SENDTOSELF

using Anaglyph.Netcode;
using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.DepthKit;
using System;
using System.Collections;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Anaglyph.Lasertag.EnvironmentSync
{
	public class EnvironmentSynchronizer : MonoBehaviour
	{
		public static EnvironmentSynchronizer Instance { get; private set; }
		private void Awake()
		{
			Instance = this;
		}

		[SerializeField] private float updateFrequency;

		[SerializeField] private Texture2D receivedDepthTex;
		[SerializeField] private RenderTexture convertedDepthTex;

		private NetworkManager manager => NetworkManager.Singleton;
		private EnvironmentMapper mapper => EnvironmentMapper.Instance;
		private DepthKitDriver depth => DepthKitDriver.Instance;

		private int viewID => DepthKitDriver.agDepthView_ID;
		private int projID => DepthKitDriver.agDepthProj_ID;
		private int depthTexID => DepthKitDriver.agDepthTex_ID;

		public UnityEvent<Texture2D> onGetDepth = new();

		private const int Mat4Size = sizeof(float) * 16;

		private Matrix4x4[] GetShaderMats(int id) => Shader.GetGlobalMatrixArray(id);

		private void Start()
		{
			manager.OnConnectionEvent += OnConnectionEvent;
		}

		private struct DepthUpdate : IDisposable
		{
			public NativeReference<Matrix4x4> view;
			public NativeReference<Matrix4x4> proj;
			public NativeArray<byte> depthTexRaw;

			public DepthUpdate(Matrix4x4 view, Matrix4x4 proj, byte[] depthTexRaw)
			{
				this.view = new(view, Allocator.TempJob);
				this.proj = new(proj, Allocator.TempJob);
				this.depthTexRaw = new(depthTexRaw, Allocator.TempJob);
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public void Serialize(FastBufferWriter writer)
			{
				writer.TryBeginWrite(Mat4Size);
				writer.WriteValue(view.Value);
				
				writer.TryBeginWrite(Mat4Size);
				writer.WriteValue(proj.Value);

				writer.TryBeginWrite(depthTexRaw.Length);
				writer.WriteBytes(depthTexRaw);
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public unsafe void Deserialize(FastBufferReader reader)
			{
				reader.TryBeginRead(Mat4Size);
				reader.ReadValue(out Matrix4x4 view);
				this.view.Value = view;

				reader.TryBeginRead(Mat4Size);
				reader.ReadValue(out Matrix4x4 proj);
				this.proj.Value = proj;

				reader.TryBeginRead(reader.Length - Mat4Size * 2);
				reader.ReadBytes((byte*)depthTexRaw.GetUnsafePtr(), reader.Length - reader.Position);
			}

			public void Dispose()
			{
				view.Dispose();
				proj.Dispose();
				depthTexRaw.Dispose();
			}
		}

		[BurstCompile]
		private struct SerializeJob : IJob
		{
			public DepthUpdate depthUpdate;
			[NativeDisableUnsafePtrRestriction]
			public FastBufferWriter writer;

			public SerializeJob(FastBufferWriter writer, DepthUpdate depthUpdate)
			{
				this.writer = writer;
				this.depthUpdate = depthUpdate;
			}

			public void Execute()
			{
				depthUpdate.Serialize(writer);
			}
		}

		[BurstCompile]
		private struct DeserializeJob : IJob
		{
			public DepthUpdate result;
			[NativeDisableUnsafePtrRestriction]
			public FastBufferReader reader;

			public DeserializeJob(FastBufferReader reader, int numDepthTexBytes)
			{
				this.reader = reader;
				this.result = new(default, default, new byte[numDepthTexBytes]);
			}

			public unsafe void Execute()
			{
				reader.Seek(0);

				reader.TryBeginRead(reader.Length);
				result.Deserialize(reader);
			}
		}

		private struct CopyWriterJob : IJob
		{
			[NativeDisableUnsafePtrRestriction]
			public FastBufferReader reader;
			[NativeDisableUnsafePtrRestriction]
			public FastBufferWriter writer;

			public CopyWriterJob(FastBufferReader reader, FastBufferWriter writer)
			{
				this.reader = reader;
				this.writer = writer;
			}

			public unsafe void Execute()
			{
				int numBytes = reader.Length;
				byte[] byteArray = new byte[numBytes];
				fixed (byte* bytePtr = byteArray)
				{
					reader.ReadBytesSafe(bytePtr, numBytes);
					writer.WriteBytesSafe(bytePtr, numBytes);
				}
			}
		}

		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		{
			if (NetcodeHelpers.ThisClientConnected(data))
			{
				EnvironmentMapper.Instance.Clear();
				manager.CustomMessagingManager.OnUnnamedMessage += OnUnnamedMessage;

				SyncLoop();
			}
		}

		private async void SyncLoop()
		{
			while (manager.IsConnectedClient && manager.IsListening)
			{
				await Awaitable.WaitForSecondsAsync(1f / updateFrequency);

				if (!DepthKitDriver.DepthAvailable)
					continue;

				byte[] depthTexRaw = depth.DepthTexCPU.EncodeToJPG(75);
				Matrix4x4 view = GetShaderMats(viewID)[0];
				Matrix4x4 proj = GetShaderMats(projID)[0];
				DepthUpdate depthUpdate = new(view, proj, depthTexRaw);

				int numBytes = depthTexRaw.Length + Mat4Size * 2;

				FastBufferWriter writer = new(numBytes, Allocator.TempJob);
				using (writer)
				{
					SerializeJob serializeJob = new(writer, depthUpdate);
					
					JobHandle handle = serializeJob.Schedule();
					while (!handle.IsCompleted) await Awaitable.NextFrameAsync();
					handle.Complete();

					depthUpdate.Dispose();
				}

#if SENDTOSELF
				OnUnnamedMessage(0, new FastBufferReader(writer, Allocator.TempJob));

#else
				if (manager.IsServer)
					SendToAllOtherClients(writer, NetworkManager.ServerClientId);
					
				else
					SendToServer(writer);
#endif
			}
		}

		private async void OnUnnamedMessage(ulong clientId, FastBufferReader reader)
		{
#if !SENDTOSELF
			if (clientId == manager.LocalClientId)
				throw new Exception("Client should not send a message to itself!");


			if (manager.IsServer)
			{
				reader.Seek(0);
				ForwardToAllOtherClients(reader, clientId);
			}
#endif

			if (manager.IsClient)
			{
				reader.Seek(0);

				int numDepthTexBytes = reader.Length - Mat4Size * 2;
				DeserializeJob deserializeJob = new(reader, numDepthTexBytes);

				var handle = deserializeJob.Schedule();
				while (!handle.IsCompleted) await Awaitable.NextFrameAsync();
				handle.Complete();

				var result = deserializeJob.result;

				byte[] rawImg = result.depthTexRaw.ToArray();
				Matrix4x4 view = result.view.Value;
				Matrix4x4 proj = result.proj.Value;
				result.Dispose();

				if(receivedDepthTex== null)
					receivedDepthTex = new(2, 2);

				receivedDepthTex.LoadImage(rawImg);

				//int w = receivedDepthTex.width;
				//int h = receivedDepthTex.height;

				//if (convertedDepthTex == null || 
				//	w != convertedDepthTex.width ||
				//	h != convertedDepthTex.height)
				//{
				//	RenderTextureDescriptor desc = new()
				//	{
				//		width = w,
				//		height = h,
				//		graphicsFormat = GraphicsFormat.R8_UNorm,
				//		depthStencilFormat = GraphicsFormat.None,
				//		dimension = TextureDimension.Tex2DArray,
				//		volumeDepth = 1,
				//		msaaSamples = 1,
				//	};
				//	convertedDepthTex = new RenderTexture(desc);
				//}
				
				//Graphics.Blit(receivedDepthTex, convertedDepthTex);

				// note:
				// this is where you were last.
				// I think you need to convert the texture via compute shader instead of blitting

				mapper.ApplyScan(receivedDepthTex, view, proj);
			}
		}

		private unsafe void ForwardToAllOtherClients(FastBufferReader reader, ulong sender)
		{
			int numBytes = reader.Length;

			StartCoroutine(CopyReader());
			IEnumerator CopyReader()
			{
				FastBufferWriter writer = new FastBufferWriter(numBytes, Allocator.TempJob);
				using (writer)
				{

					CopyWriterJob job = new(reader, writer);

					var handle = job.Schedule();
					while (!handle.IsCompleted) yield return null;
					handle.Complete();


					SendToAllOtherClients(writer, sender);
				}
			}
		}

		private void SendToAllOtherClients(FastBufferWriter writer, ulong sender)
		{
			foreach (ulong id in manager.ConnectedClientsIds)
			{
				if (id != sender && id != NetworkManager.ServerClientId)
					manager.CustomMessagingManager?.SendUnnamedMessage(
						id, writer, NetworkDelivery.ReliableFragmentedSequenced);
			}
		}

		private void SendToServer(FastBufferWriter writer) =>
			manager.CustomMessagingManager?.SendUnnamedMessage(
							NetworkManager.ServerClientId, writer,
							NetworkDelivery.ReliableFragmentedSequenced);
	}

	public static class SerializationMatrixExtensions
	{
		public static void WriteValue(this FastBufferWriter writer, in Matrix4x4 mat)
		{
			for (int i = 0; i < 4; i++)
			{
				writer.WriteValue(mat.GetRow(i));
			}
		}

		public static void ReadValue(this FastBufferReader reader, out Matrix4x4 mat)
		{
			mat = new();

			for (int i = 0; i < 4; i++)
			{
				reader.ReadValue(out Vector4 v);
				mat.SetRow(i, v);
			}
		}
	}
}