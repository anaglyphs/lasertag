//#define SENDTOSELF

//using Anaglyph;
//using Anaglyph.Netcode;
//using Anaglyph.XRTemplate;
//using Anaglyph.XRTemplate.DepthKit;
//using System;
//using System.Collections;
//using System.IO.Compression;
//using System.IO;
//using System.Runtime.CompilerServices;
//using Unity.Burst;
//using Unity.Collections;
//using Unity.Collections.LowLevel.Unsafe;
//using Unity.Jobs;
//using Unity.Netcode;
//using UnityEngine;
//using UnityEngine.Rendering;
//using UnityEngine.Experimental.Rendering;

//namespace Anaglyph.Lasertag.EnvironmentSync
//{
//	public class EnvironmentSynchronizer : MonoBehaviour
//	{
//		public static EnvironmentSynchronizer Instance { get; private set; }
//		private void Awake()
//		{
//			Instance = this;
//		}

//		[SerializeField] private float updateFrequency;

//		private NetworkManager manager => NetworkManager.Singleton;
//		private EnvironmentMapper mapper => EnvironmentMapper.Instance;

//		private int viewID => DepthKitDriver.agDepthView_ID;
//		private int projID => DepthKitDriver.agDepthProj_ID;

//		private const int Mat4Size = sizeof(float) * 16;

//		private Matrix4x4[] GetShaderMats(int id) => Shader.GetGlobalMatrixArray(id);

//		[SerializeField] private ComputeShader shader;
//		private ComputeKernel kernel;

//		private void Start()
//		{
//			manager.OnConnectionEvent += OnConnectionEvent;
//			kernel = new(shader, "ConvertDepth");
//		}

//		public static byte[] Compress(byte[] data)
//		{
//			MemoryStream output = new MemoryStream();
//			using (DeflateStream dstream = new DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal))
//			{
//				dstream.Write(data, 0, data.Length);
//			}
//			return output.ToArray();
//		}

//		public static byte[] Decompress(byte[] data)
//		{
//			MemoryStream input = new MemoryStream(data);
//			MemoryStream output = new MemoryStream();
//			using (DeflateStream dstream = new DeflateStream(input, CompressionMode.Decompress))
//			{
//				dstream.CopyTo(output);
//			}
//			return output.ToArray();
//		}

//		private struct VolumeUpdate : IDisposable
//		{
//			public NativeReference<Matrix4x4> view;
//			public NativeReference<Matrix4x4> proj;
//			public NativeArray<byte> values;

//			public VolumeUpdate(Matrix4x4 view, Matrix4x4 proj, NativeArray<byte> values)
//			{
//				this.view = new(view, Allocator.TempJob);
//				this.proj = new(proj, Allocator.TempJob);
//				this.values = values;
//			}

//			public VolumeUpdate(Matrix4x4 view, Matrix4x4 proj, byte[] values)
//			{
//				this.view = new(view, Allocator.TempJob);
//				this.proj = new(proj, Allocator.TempJob);
//				this.values = new(values, Allocator.TempJob);
//			}

//			[MethodImpl(MethodImplOptions.AggressiveInlining)]
//			public void Serialize(FastBufferWriter writer)
//			{
//				writer.TryBeginWrite(Mat4Size);
//				writer.WriteValue(view.Value);

//				writer.TryBeginWrite(Mat4Size);
//				writer.WriteValue(proj.Value);

//				writer.TryBeginWrite(values.Length);
//				writer.WriteBytes(values, values.Length);
//			}

//			[MethodImpl(MethodImplOptions.AggressiveInlining)]
//			public unsafe void Deserialize(FastBufferReader reader)
//			{
//				reader.TryBeginRead(Mat4Size);
//				reader.ReadValue(out Matrix4x4 view);
//				this.view.Value = view;

//				reader.TryBeginRead(Mat4Size);
//				reader.ReadValue(out Matrix4x4 proj);
//				this.proj.Value = proj;

//				reader.TryBeginRead(reader.Length - Mat4Size * 2);
//				reader.ReadBytes((byte*)values.GetUnsafePtr(), reader.Length - reader.Position);
//			}

//			public void Dispose()
//			{
//				view.Dispose();
//				proj.Dispose();
//				values.Dispose();
//			}
//		}

//		[BurstCompile]
//		private struct SerializeJob : IJob
//		{
//			public VolumeUpdate depthUpdate;
//			[NativeDisableUnsafePtrRestriction]
//			public FastBufferWriter writer;

//			public SerializeJob(FastBufferWriter writer, VolumeUpdate depthUpdate)
//			{
//				this.writer = writer;
//				this.depthUpdate = depthUpdate;
//			}

//			public void Execute()
//			{
//				depthUpdate.Serialize(writer);
//			}
//		}

//		[BurstCompile]
//		private struct DeserializeJob : IJob
//		{
//			public VolumeUpdate result;
//			[NativeDisableUnsafePtrRestriction]
//			public FastBufferReader reader;

//			public DeserializeJob(FastBufferReader reader, int numDepthTexBytes)
//			{
//				this.reader = reader;
//				this.result = new(default, default, new byte[numDepthTexBytes]);
//			}

//			public unsafe void Execute()
//			{
//				reader.Seek(0);

//				reader.TryBeginRead(reader.Length);
//				result.Deserialize(reader);
//			}
//		}

//		private struct CopyWriterJob : IJob
//		{
//			[NativeDisableUnsafePtrRestriction]
//			public FastBufferReader reader;
//			[NativeDisableUnsafePtrRestriction]
//			public FastBufferWriter writer;

//			public CopyWriterJob(FastBufferReader reader, FastBufferWriter writer)
//			{
//				this.reader = reader;
//				this.writer = writer;
//			}

//			public unsafe void Execute()
//			{
//				int numBytes = reader.Length;
//				byte[] byteArray = new byte[numBytes];
//				fixed (byte* bytePtr = byteArray)
//				{
//					reader.ReadBytesSafe(bytePtr, numBytes);
//					writer.WriteBytesSafe(bytePtr, numBytes);
//				}
//			}
//		}

//		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
//		{
//			if (NetcodeHelpers.ThisClientConnected(data))
//			{
//				EnvironmentMapper.Instance.Clear();
//				manager.CustomMessagingManager.OnUnnamedMessage += OnUnnamedMessage;

//				SyncLoop();
//			}
//		}

//		private async void SyncLoop()
//		{
//			while (manager.IsConnectedClient && manager.IsListening)
//			{
//				await Awaitable.WaitForSecondsAsync(1f / updateFrequency);

//				if (!DepthKitDriver.DepthAvailable)
//					continue;

//				Matrix4x4 view = GetShaderMats(viewID)[0];
//				Matrix4x4 proj = GetShaderMats(projID)[0];

//				Texture depthTex = Shader.GetGlobalTexture(DepthKitDriver.agDepthTex_ID);

//				kernel.Set("agDepthNormalTexRW", depthTex);

//				int w = depthTex.width;
//				int h = depthTex.height;
//				var form = GraphicsFormat.R16_UNorm;

//				RenderTexture depthTexCPU = new(w, h, form, GraphicsFormat.None);
//				depthTexCPU.enableRandomWrite = true;

//				kernel.Set("agDepthReadback", depthTexCPU);
//				kernel.DispatchGroups(w, h, 1);

//				var readback = await AsyncGPUReadback.RequestAsync(depthTexCPU);
//				if (readback.hasError) continue;
//				var bytes = readback.GetData<byte>();
//				bytes = ImageConversion.EncodeNativeArrayToEXR(bytes, form, (uint)w, (uint)h, 0, Texture2D.EXRFlags.CompressZIP);

//				VolumeUpdate depthUpdate = new(view, proj, bytes);

//				int numBytes = bytes.Length + Mat4Size * 2;

//				FastBufferWriter writer = new(numBytes, Allocator.TempJob);
//				using (writer)
//				{
//					SerializeJob serializeJob = new(writer, depthUpdate);

//					JobHandle handle = serializeJob.Schedule();
//					while (!handle.IsCompleted) await Awaitable.NextFrameAsync();
//					handle.Complete();

//					depthUpdate.Dispose();
//				}

//#if SENDTOSELF
//				OnUnnamedMessage(0, new FastBufferReader(writer, Allocator.TempJob));

//#else
//				if (manager.IsServer)
//					SendToAllOtherClients(writer, NetworkManager.ServerClientId);
					
//				else
//					SendToServer(writer);
//#endif
//			}
//		}

//		private async void OnUnnamedMessage(ulong clientId, FastBufferReader reader)
//		{
//#if !SENDTOSELF
//			if (clientId == manager.LocalClientId)
//				throw new Exception("Client should not send a message to itself!");


//			if (manager.IsServer)
//			{
//				reader.Seek(0);
//				ForwardToAllOtherClients(reader, clientId);
//			}
//#endif

//			if (manager.IsClient)
//			{
//				reader.Seek(0);

//				int numDepthTexBytes = reader.Length - Mat4Size * 2;
//				DeserializeJob deserializeJob = new(reader, numDepthTexBytes);

//				var handle = deserializeJob.Schedule();
//				while (!handle.IsCompleted) await Awaitable.NextFrameAsync();
//				handle.Complete();

//				var result = deserializeJob.result;

//				byte[] bytes = result.values.ToArray();
//				Matrix4x4 view = result.view.Value;
//				Matrix4x4 proj = result.proj.Value;
//				result.Dispose();

//				var form = GraphicsFormat.R16_UNorm;
//				Texture2D depthTexLoad = new(2, 2, form, TextureCreationFlags.None);
//				depthTexLoad.LoadImage(bytes);

//				RenderTextureDescriptor desc = new()
//				{
//					width = depthTexLoad.width,
//					height = depthTexLoad.height,
//					volumeDepth = 1,
//					dimension = TextureDimension.Tex2DArray,
//					graphicsFormat = form,
//					msaaSamples = 1,
//				};

//				RenderTexture updateDepthTex = new(desc);

//				Graphics.Blit(depthTexLoad, updateDepthTex, 0, 0);

//				mapper.ApplyScan(updateDepthTex, view, proj);
//			}
//		}

//		private unsafe void ForwardToAllOtherClients(FastBufferReader reader, ulong sender)
//		{
//			int numBytes = reader.Length;

//			StartCoroutine(CopyReader());
//			IEnumerator CopyReader()
//			{
//				FastBufferWriter writer = new FastBufferWriter(numBytes, Allocator.TempJob);
//				using (writer)
//				{

//					CopyWriterJob job = new(reader, writer);

//					var handle = job.Schedule();
//					while (!handle.IsCompleted) yield return null;
//					handle.Complete();


//					SendToAllOtherClients(writer, sender);
//				}
//			}
//		}

//		private void SendToAllOtherClients(FastBufferWriter writer, ulong sender)
//		{
//			foreach (ulong id in manager.ConnectedClientsIds)
//			{
//				if (id != sender && id != NetworkManager.ServerClientId)
//					manager.CustomMessagingManager?.SendUnnamedMessage(
//						id, writer, NetworkDelivery.ReliableFragmentedSequenced);
//			}
//		}

//		private void SendToServer(FastBufferWriter writer) =>
//			manager.CustomMessagingManager?.SendUnnamedMessage(
//							NetworkManager.ServerClientId, writer,
//							NetworkDelivery.ReliableFragmentedSequenced);
//	}

//	public static class SerializationMatrixExtensions
//	{
//		public static void WriteValue(this FastBufferWriter writer, in Matrix4x4 mat)
//		{
//			for (int i = 0; i < 4; i++)
//			{
//				writer.WriteValue(mat.GetRow(i));
//			}
//		}

//		public static void ReadValue(this FastBufferReader reader, out Matrix4x4 mat)
//		{
//			mat = new();

//			for (int i = 0; i < 4; i++)
//			{
//				reader.ReadValue(out Vector4 v);
//				mat.SetRow(i, v);
//			}
//		}
//	}
//}