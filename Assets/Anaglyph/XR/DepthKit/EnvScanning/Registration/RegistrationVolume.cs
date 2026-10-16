using System;
using System.Collections.Generic;
using Anaglyph.GPU;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Anaglyph.XR.DepthKit.EnvScanning.Registration
{
	/// <summary>
	/// A registration TARGET: a dense copy of some chunks over their bounding box, plus
	/// a max-pooled pyramid whose coarse cells upper-bound the agreement any fine pose
	/// inside them could reach. Filled from the live atlas (a chunk subset of this
	/// headset's scan) or from chunks received whole (a peer's snapshot), so the same
	/// search serves loop closure and colocation.
	/// </summary>
	public sealed class RegistrationVolume : IDisposable
	{
		/// <summary>Largest dense grid allowed, in voxels per axis, to bound memory.</summary>
		public static readonly int3 MaxDims = new(320, 160, 320);

		public const int PyramidLevels = 4; // fine + 3 pooled: 1x, 2x, 4x, 8x voxels

		private readonly ComputeShader compute;
		private readonly EnvScanner scanner;

		private ComputeKernel clearKernel, fillAtlasKernel, fillUploadKernel, pyramidKernel;
		private ComputeBuffer chunkListBuffer;
		private ComputeBuffer uploadBuffer;

		public RenderTexture Fine { get; private set; }
		public RenderTexture[] Levels { get; private set; } = Array.Empty<RenderTexture>();
		public int3[] LevelDims { get; private set; } = Array.Empty<int3>();

		public int3 MinChunk { get; private set; }
		public int3 MaxChunk { get; private set; }
		public int3 Dims { get; private set; }
		public float3 Origin { get; private set; }
		public bool IsAllocated => Fine != null;
		public bool PyramidDirty { get; private set; }

		/// <summary>World bounds of the dense grid.</summary>
		public Bounds WorldBounds => new((Vector3)(Origin + (float3)Dims * scanner.VoxSize * 0.5f),
			(Vector3)((float3)Dims * scanner.VoxSize));

		private static readonly int denseMinChunkID = Shader.PropertyToID("denseMinChunk");
		private static readonly int denseDimsID = Shader.PropertyToID("denseDims");
		private static readonly int denseOriginID = Shader.PropertyToID("denseOrigin");
		private static readonly int denseFineID = Shader.PropertyToID("denseFine");
		private static readonly int chunkListID = Shader.PropertyToID("chunkList");
		private static readonly int chunkCountID = Shader.PropertyToID("chunkCount");
		private static readonly int uploadVoxelsID = Shader.PropertyToID("uploadVoxels");
		private static readonly int uploadChunkIndexID = Shader.PropertyToID("uploadChunkIndex");
		private static readonly int pyramidSrcID = Shader.PropertyToID("pyramidSrc");
		private static readonly int pyramidDstID = Shader.PropertyToID("pyramidDst");
		private static readonly int pyramidSrcDimsID = Shader.PropertyToID("pyramidSrcDims");
		private static readonly int pyramidDstDimsID = Shader.PropertyToID("pyramidDstDims");
		private static readonly int pyramidSrcIsFineID = Shader.PropertyToID("pyramidSrcIsFine");

		public RegistrationVolume(ComputeShader compute, EnvScanner scanner)
		{
			this.compute = compute;
			this.scanner = scanner;

			clearKernel = new ComputeKernel(compute, "ClearDense");
			fillAtlasKernel = new ComputeKernel(compute, "FillDenseFromAtlas");
			fillUploadKernel = new ComputeKernel(compute, "FillDenseFromUpload");
			pyramidKernel = new ComputeKernel(compute, "BuildPyramidLevel");

			int vpcd = scanner.VoxPerChunkDim;
			uploadBuffer = new ComputeBuffer(vpcd / 2 * vpcd * vpcd, sizeof(uint));
		}

		/// <summary>
		/// Sizes the dense grid to cover the given chunk coordinates and clears it. Returns
		/// false when the span exceeds <see cref="MaxDims"/>.
		/// </summary>
		public bool Allocate(int3 minChunk, int3 maxChunk)
		{
			int vpcd = scanner.VoxPerChunkDim;
			int3 span = maxChunk - minChunk + 1;
			int3 dims = span * (vpcd - 2) + 2;
			if (math.any(dims > MaxDims) || math.any(span < 1)) return false;

			if (Fine == null || math.any(Dims != dims))
			{
				Release();

				Fine = CreateVolume(dims, GraphicsFormat.R8G8_SNorm, "Registration Target");
				var levels = new List<RenderTexture>();
				var levelDims = new List<int3>();
				int3 d = dims;
				for (int i = 1; i < PyramidLevels; i++)
				{
					d = (d + 1) / 2;
					levels.Add(CreateVolume(d, GraphicsFormat.R16G16B16A16_SFloat, $"Registration Level {i}"));
					levelDims.Add(d);
				}

				Levels = levels.ToArray();
				LevelDims = levelDims.ToArray();
			}

			MinChunk = minChunk;
			MaxChunk = maxChunk;
			Dims = dims;
			Origin = scanner.ChunkCoordToCornerWorldPos(minChunk);

			BindDense(clearKernel);
			clearKernel.DispatchFit(dims.x, dims.y, dims.z);
			PyramidDirty = true;
			return true;
		}

		private static RenderTexture CreateVolume(int3 dims, GraphicsFormat format, string name)
		{
			RenderTexture tex = new(new RenderTextureDescriptor
			{
				width = dims.x, height = dims.y, volumeDepth = dims.z,
				msaaSamples = 1, useMipMap = false,
				graphicsFormat = format,
				dimension = TextureDimension.Tex3D,
				enableRandomWrite = true
			}) { name = name };
			tex.Create();
			return tex;
		}

		private void BindDense(ComputeKernel kernel)
		{
			compute.SetInts(denseMinChunkID, MinChunk.x, MinChunk.y, MinChunk.z);
			compute.SetInts(denseDimsID, Dims.x, Dims.y, Dims.z);
			compute.SetVector(denseOriginID, new Vector4(Origin.x, Origin.y, Origin.z, 0));
			kernel.Bind(denseFineID, Fine);
		}

		/// <summary>Sets the dense-grid uniforms for a kernel of the same shader that reads this volume.</summary>
		public void BindForReading(ComputeKernel kernel)
		{
			BindDense(kernel);
		}

		/// <summary>Copies the given chunks of the live scan atlas into the dense grid.</summary>
		public void FillFromAtlas(IReadOnlyList<int> chunkIndices)
		{
			if (!IsAllocated || chunkIndices.Count == 0) return;
			scanner.EnsureGpuResources();
			scanner.ConfigureAtlasReader(compute);

			EnsureChunkList(chunkIndices);
			BindDense(fillAtlasKernel);
			fillAtlasKernel.Bind("chunkTable", scanner.ChunkTable);
			fillAtlasKernel.Bind("chunkData", scanner.ChunkData);
			fillAtlasKernel.Bind(chunkListID, chunkListBuffer);
			compute.SetInt(chunkCountID, chunkIndices.Count);

			int vpcd = scanner.VoxPerChunkDim;
			fillAtlasKernel.DispatchFit(vpcd, vpcd, vpcd * chunkIndices.Count);
			PyramidDirty = true;
		}

		/// <summary>Writes one chunk's voxels (CPU layout) into the dense grid.</summary>
		public void FillFromVoxels(int chunkIndex, NativeArray<EnvScanner.Voxel> voxels)
		{
			if (!IsAllocated) return;
			scanner.ConfigureAtlasReader(compute);

			uploadBuffer.SetData(voxels.Reinterpret<uint>(EnvScanner.Voxel.stride));
			BindDense(fillUploadKernel);
			fillUploadKernel.Bind(uploadVoxelsID, uploadBuffer);
			compute.SetInt(uploadChunkIndexID, chunkIndex);

			int vpcd = scanner.VoxPerChunkDim;
			fillUploadKernel.DispatchFit(vpcd / 2, vpcd, vpcd);
			PyramidDirty = true;
		}

		/// <summary>Rebuilds the pooled levels from the fine grid.</summary>
		public void BuildPyramid()
		{
			if (!IsAllocated) return;

			Texture src = Fine;
			int3 srcDims = Dims;
			for (int i = 0; i < Levels.Length; i++)
			{
				pyramidKernel.Bind(pyramidSrcID, src);
				pyramidKernel.Bind(pyramidDstID, Levels[i]);
				compute.SetInts(pyramidSrcDimsID, srcDims.x, srcDims.y, srcDims.z);
				compute.SetInts(pyramidDstDimsID, LevelDims[i].x, LevelDims[i].y, LevelDims[i].z);
				compute.SetInt(pyramidSrcIsFineID, i == 0 ? 1 : 0);
				pyramidKernel.DispatchFit(LevelDims[i].x, LevelDims[i].y, LevelDims[i].z);
				src = Levels[i];
				srcDims = LevelDims[i];
			}

			PyramidDirty = false;
		}

		/// <summary>Texture, dims and cell size of a pyramid level; 0 is the fine grid.</summary>
		public (Texture tex, int3 dims, float cell, bool isFine) Level(int level)
		{
			if (level == 0) return (Fine, Dims, scanner.VoxSize, true);
			return (Levels[level - 1], LevelDims[level - 1], scanner.VoxSize * (1 << level), false);
		}

		public void EnsureChunkList(IReadOnlyList<int> chunkIndices)
		{
			if (chunkListBuffer == null || chunkListBuffer.count < chunkIndices.Count)
			{
				chunkListBuffer?.Release();
				chunkListBuffer = new ComputeBuffer(math.max(chunkIndices.Count, 16), sizeof(int));
			}

			int[] data = new int[chunkIndices.Count];
			for (int i = 0; i < data.Length; i++) data[i] = chunkIndices[i];
			chunkListBuffer.SetData(data);
		}

		public ComputeBuffer ChunkListBuffer => chunkListBuffer;

		private void Release()
		{
			if (Fine) Fine.Release();
			Fine = null;
			foreach (RenderTexture level in Levels) if (level) level.Release();
			Levels = Array.Empty<RenderTexture>();
			LevelDims = Array.Empty<int3>();
		}

		public void Dispose()
		{
			Release();
			chunkListBuffer?.Release();
			uploadBuffer?.Release();
			chunkListBuffer = null;
			uploadBuffer = null;
		}
	}
}
