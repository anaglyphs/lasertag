using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Anaglyph.DepthKit.EnvScanning.Encoding
{
	public static class VolumeRLE
	{
		public static async Task EncodeChunk(NativeArray<EnvScanner.Voxel> volume, NativeList<sbyte> compressedData)
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

		public static async Task<bool> DecodeChunk(NativeArray<sbyte> encoding, NativeArray<EnvScanner.Voxel> volume)
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

		[BurstCompile]
		private struct CompressJob : IJob
		{
			[ReadOnly] public NativeArray<EnvScanner.Voxel> Volume;
			public NativeList<sbyte> Encoding;

			// negative value = run of empty voxels
			// positive value = next n bytes are raw voxel data

			public void Execute()
			{
				int readHead = 0;

				int numEmpty = 0;
				int numRaw = 0;
				int rawRunStartIndex = 0;

				while (readHead < Volume.Length)
				{
					sbyte val = Volume[readHead].value;

					if (val == sbyte.MaxValue)
					{
						if (numRaw > 0)
						{
							Encoding[rawRunStartIndex] = (sbyte)(numRaw - 1);
							numRaw = 0;
						}

						numEmpty++;

						if (numEmpty == 128)
						{
							Encoding.Add((sbyte)-numEmpty);
							numEmpty = 0;
						}
					}
					else
					{
						if (numEmpty > 0)
						{
							Encoding.Add((sbyte)-numEmpty);
							numEmpty = 0;
						}

						if (numRaw == 0)
						{
							rawRunStartIndex = Encoding.Length;
							Encoding.Add(0);
						}

						numRaw++;

						Encoding.Add(val);

						if (numRaw == 128)
						{
							Encoding[rawRunStartIndex] = (sbyte)(numRaw - 1);
							numRaw = 0;
						}
					}

					readHead++;
				}

				if (numEmpty > 0)
					Encoding.Add((sbyte)-numEmpty);
				else if (numRaw > 0) Encoding[rawRunStartIndex] = (sbyte)(numRaw - 1);
			}
		}

		[BurstCompile]
		private struct DecodeJob : IJob
		{
			public NativeArray<EnvScanner.Voxel> Volume;
			[ReadOnly] public NativeArray<sbyte> Encoding;
			public NativeReference<bool> Success;

			public void Execute()
			{
				Success.Value = false;

				int writeHead = 0;
				int readHead = 0;

				while (readHead < Encoding.Length)
				{
					sbyte control = Encoding[readHead];

					if (control < 0)
					{
						// run of empty values
						int count = -control; // 1..128
						if (writeHead + count > Volume.Length) return;
						for (int i = 0; i < -control; i++)
						{
							Volume[writeHead] = new EnvScanner.Voxel(sbyte.MaxValue);
							writeHead++;
						}
					}
					else
					{
						// run of raw values

						int count = control + 1;
						if (readHead + count > Encoding.Length) return;
						if (writeHead + count > Volume.Length) return;

						for (int i = 0; i < control + 1; i++)
						{
							readHead++;
							sbyte rawVal = Encoding[readHead];

							Volume[writeHead] = new EnvScanner.Voxel(rawVal);
							writeHead++;
						}
					}

					readHead++;
				}

				Success.Value = writeHead == Volume.Length;
			}
		}
	}
}