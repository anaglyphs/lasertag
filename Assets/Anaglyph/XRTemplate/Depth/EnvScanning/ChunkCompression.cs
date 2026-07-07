using System.Threading.Tasks;
using Anaglyph.DepthKit.EnvScanning;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Anaglyph.DepthKit.EnvScanningV2
{
	public static class ChunkCompression
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
				int i = 0;
				int numEmpty = 0;
				int numRaw = 0;
				int rawRunStartIndex = 0;

				while (i < Volume.Length)
				{
					if (numEmpty == 128)
					{
						Encoding.Add(sbyte.MinValue);
						const byte a = 0b10000000;
						numEmpty = 0;
					}

					if (numRaw == 128)
					{
						Encoding.Add(sbyte.MaxValue);
						numRaw = 0;
					}

					sbyte val = Volume[i].value;

					if (val == sbyte.MaxValue)
					{
						// 
						if (numRaw > 0 && numEmpty > 1)
						{
							Encoding[rawRunStartIndex] = (sbyte)(numRaw - 1);
							numRaw = 0;
						}

						numEmpty++;
					}
					else
					{
						numRaw++;

						//	if (numEmpty)
					}
				}

				// literalLen = 0;
				//
				// int i = 0;
				// while (i < Volume.Length)
				// {
				// 	sbyte val = Volume[i].value;
				//
				// 	// count repeats of val, capped at max run length
				// 	int repeats = 1;
				// 	while (repeats < MaxTokenLen && i + repeats < Volume.Length
				// 	                             && Volume[i + repeats].value == val)
				// 		repeats++;
				//
				// 	if (repeats >= 3)
				// 	{
				// 		FlushLiteral();
				// 		Encoding.Add((sbyte)(1 - repeats)); // -2..-127 -> 3..128 copies
				// 		Encoding.Add(val);
				// 	}
				// 	else
				// 	{
				// 		// 1s and 2s are absorbed into the literal.
				// 		// a 2-run not preceded by a literal would be 1 byte cheaper
				// 		// as a run token; skipped for simplicity
				// 		for (int j = 0; j < repeats; j++)
				// 		{
				// 			if (literalLen == 0)
				// 			{
				// 				literalControlIdx = Encoding.Length;
				// 				Encoding.Add(0); // control placeholder
				// 			}
				//
				// 			Encoding.Add(val);
				// 			literalLen++;
				//
				// 			if (literalLen == MaxTokenLen)
				// 				FlushLiteral();
				// 		}
				// 	}
				//
				// 	i += repeats;
				// }
				//
				// FlushLiteral();
			}
			//
			// private void FlushLiteral()
			// {
			// 	if (literalLen == 0) return;
			// 	Encoding[literalControlIdx] = (sbyte)(literalLen - 1);
			// 	literalLen = 0;
			// }
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
					// sbyte control = Encoding[readHead++];
					//
					// // never emitted by the encoder; reject as malformed
					// if (control == sbyte.MinValue) return;
					//
					// if (control >= 0)
					// {
					// 	// literal: control + 1 verbatim values
					// 	int count = control + 1;
					// 	if (readHead + count > Encoding.Length) return;
					// 	if (writeHead + count > Volume.Length) return;
					//
					// 	for (int j = 0; j < count; j++)
					// 		Volume[writeHead++] = new EnvScanner.Voxel(Encoding[readHead++]);
					// }
					// else
					// {
					// 	// run: next value repeated 1 - control times
					// 	int count = 1 - control;
					// 	if (readHead >= Encoding.Length) return;
					// 	if (writeHead + count > Volume.Length) return;
					//
					// 	sbyte value = Encoding[readHead++];
					// 	for (int j = 0; j < count; j++)
					// 		Volume[writeHead++] = new EnvScanner.Voxel(value);
					// }
				}

				Success.Value = writeHead == Volume.Length;
			}
		}
	}
}