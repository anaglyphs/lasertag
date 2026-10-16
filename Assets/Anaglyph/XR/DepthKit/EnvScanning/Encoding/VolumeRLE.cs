using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Anaglyph.XR.DepthKit.EnvScanning.Encoding
{
	/// <summary>
	/// Run-length coding of a chunk's voxels for whole-chunk transfer (snapshots).
	/// Never-integrated voxels (count 0) are the runs; everything else travels as
	/// raw (value, count) pairs so the receiver can fuse by weight.
	/// </summary>
	public static class VolumeRLE
	{
		public static async Task EncodeChunk(NativeArray<EnvScanner.Voxel> volume, NativeList<byte> compressedData)
		{
			compressedData.Clear();

			CompressJob job = new()
			{
				Encoding = compressedData,
				Volume = volume
			};

			JobHandle jobHandle = job.Schedule();
			while (!jobHandle.IsCompleted) await Awaitable.NextFrameAsync();
			jobHandle.Complete();
		}

		public static async Task<bool> DecodeChunk(NativeArray<byte> encoding, NativeArray<EnvScanner.Voxel> volume)
		{
			NativeReference<bool> successResult = new(false, Allocator.TempJob);

			try
			{
				DecodeJob job = new()
				{
					Encoding = encoding,
					Volume = volume,
					Success = successResult
				};

				JobHandle jobHandle = job.Schedule();
				while (!jobHandle.IsCompleted) await Awaitable.NextFrameAsync();
				jobHandle.Complete();

				return successResult.Value;
			}
			finally
			{
				successResult.Dispose();
			}
		}

		/// <summary>Synchronous decode for callers already off the main thread or in a job.</summary>
		public static bool DecodeChunkNow(NativeArray<byte> encoding, NativeArray<EnvScanner.Voxel> volume)
		{
			NativeReference<bool> successResult = new(false, Allocator.TempJob);

			try
			{
				new DecodeJob { Encoding = encoding, Volume = volume, Success = successResult }.Run();
				return successResult.Value;
			}
			finally
			{
				successResult.Dispose();
			}
		}

		// control byte: 0x80 | n  = run of n+1 empty voxels (n in 0..127)
		//               n         = next n+1 voxels are raw (value, count) pairs
		private const int MaxRun = 128;

		[BurstCompile]
		private struct CompressJob : IJob
		{
			[ReadOnly] public NativeArray<EnvScanner.Voxel> Volume;
			public NativeList<byte> Encoding;

			public void Execute()
			{
				int readHead = 0;

				int numEmpty = 0;
				int numRaw = 0;
				int rawRunStartIndex = 0;

				while (readHead < Volume.Length)
				{
					EnvScanner.Voxel voxel = Volume[readHead];

					if (voxel.IsEmpty)
					{
						if (numRaw > 0)
						{
							Encoding[rawRunStartIndex] = (byte)(numRaw - 1);
							numRaw = 0;
						}

						numEmpty++;

						if (numEmpty == MaxRun)
						{
							Encoding.Add((byte)(0x80 | (numEmpty - 1)));
							numEmpty = 0;
						}
					}
					else
					{
						if (numEmpty > 0)
						{
							Encoding.Add((byte)(0x80 | (numEmpty - 1)));
							numEmpty = 0;
						}

						if (numRaw == 0)
						{
							rawRunStartIndex = Encoding.Length;
							Encoding.Add(0);
						}

						numRaw++;

						Encoding.Add((byte)voxel.value);
						Encoding.Add(voxel.count);

						if (numRaw == MaxRun)
						{
							Encoding[rawRunStartIndex] = (byte)(numRaw - 1);
							numRaw = 0;
						}
					}

					readHead++;
				}

				if (numEmpty > 0)
					Encoding.Add((byte)(0x80 | (numEmpty - 1)));
				else if (numRaw > 0) Encoding[rawRunStartIndex] = (byte)(numRaw - 1);
			}
		}

		[BurstCompile]
		private struct DecodeJob : IJob
		{
			public NativeArray<EnvScanner.Voxel> Volume;
			[ReadOnly] public NativeArray<byte> Encoding;
			public NativeReference<bool> Success;

			public void Execute()
			{
				Success.Value = false;

				int writeHead = 0;
				int readHead = 0;

				while (readHead < Encoding.Length)
				{
					byte control = Encoding[readHead];

					if ((control & 0x80) != 0)
					{
						// run of empty values
						int count = (control & 0x7F) + 1;
						if (writeHead + count > Volume.Length) return;
						for (int i = 0; i < count; i++)
						{
							Volume[writeHead] = new EnvScanner.Voxel(sbyte.MaxValue, 0);
							writeHead++;
						}

						readHead++;
					}
					else
					{
						// run of raw values
						int count = control + 1;
						if (readHead + 1 + count * 2 > Encoding.Length) return;
						if (writeHead + count > Volume.Length) return;

						readHead++;
						for (int i = 0; i < count; i++)
						{
							sbyte value = (sbyte)Encoding[readHead++];
							byte integCount = Encoding[readHead++];
							Volume[writeHead] = new EnvScanner.Voxel(value, integCount);
							writeHead++;
						}
					}
				}

				Success.Value = writeHead == Volume.Length;
			}
		}
	}
}
