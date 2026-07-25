using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.XRTemplate.AprilTags
{
	/// <summary>
	/// XRCpuImage.Convert() won't go from a color format to single-channel R8,
	/// which is exactly what AR Foundation's XR Simulation hands us in the editor
	/// (a BGRA32 readback of the sim camera). This is a Burst stand-in for that
	/// one conversion.
	/// </summary>
	public static class CpuImageGrayscale
	{
		// Rec.601 luma weights in 8.8 fixed point (0.299, 0.587, 0.114). They sum
		// to exactly 256, so a white pixel lands on 255 and no clamp is needed.
		private const uint RWeight = 77;
		private const uint GWeight = 150;
		private const uint BWeight = 29;

		private const int RowsPerBatch = 16;

		/// <summary>Bytes the output array needs for <see cref="ConvertToGrayscale"/>.</summary>
		public static int GetGrayscaleDataSize(this XRCpuImage image) => image.width * image.height;

		public static bool SupportsGrayscaleConversion(this XRCpuImage image) =>
			image.format is XRCpuImage.Format.BGRA32 or XRCpuImage.Format.RGBA32;

		/// <summary>
		/// Converts a BGRA32/RGBA32 image to tightly packed 8-bit luminance.
		/// Blocks until the conversion is done, so the image stays valid throughout.
		/// </summary>
		/// <param name="mirrorY">
		/// Flips vertically, matching XRCpuImage.Transformation.MirrorY. Needed for
		/// GPU readbacks, whose first row is the bottom of the image.
		/// </param>
		public static void ConvertToGrayscale(this XRCpuImage image, NativeArray<byte> output, bool mirrorY) =>
			image.ScheduleGrayscaleConversion(output, mirrorY).Complete();

		/// <summary>
		/// Job-scheduling form of <see cref="ConvertToGrayscale"/>. The caller must
		/// complete the returned handle before disposing <paramref name="image"/>.
		/// </summary>
		public static JobHandle ScheduleGrayscaleConversion(this XRCpuImage image, NativeArray<byte> output,
			bool mirrorY, JobHandle dependsOn = default)
		{
			// Byte offset of red within a pixel. Green is always at 1, and blue sits
			// opposite red, so blue's offset is 2 - red's.
			int redOffset = image.format switch
			{
				XRCpuImage.Format.BGRA32 => 2,
				XRCpuImage.Format.RGBA32 => 0,
				_ => throw new NotSupportedException(
					$"Grayscale conversion only handles BGRA32 and RGBA32, got {image.format}")
			};

			int width = image.width;
			int height = image.height;
			int required = width * height;

			if (output.Length < required)
				throw new ArgumentException($"Output needs {required} bytes but holds {output.Length}", nameof(output));

			XRCpuImage.Plane plane = image.GetPlane(0);
			NativeArray<byte> src = plane.data;

			int firstRow = mirrorY ? height - 1 : 0;
			int rowStep = mirrorY ? -1 : 1;

			// Fast path: pixels sit on 4-byte boundaries, so each one is a single
			// 32-bit load and Burst can chew through four at a time.
			bool packed = plane.pixelStride == 4 && plane.rowStride % 4 == 0 && src.Length % 4 == 0;

			if (packed)
			{
				PackedGrayscaleJob job = new()
				{
					Src = src.Reinterpret<byte, uint>(),
					Dst = output,
					Width = width,
					SrcFirstRow = firstRow,
					SrcRowStep = rowStep,
					SrcRowStride = plane.rowStride / 4,
					RedShift = redOffset * 8,
				};

				return job.Schedule(height, RowsPerBatch, dependsOn);
			}
			else
			{
				GrayscaleJob job = new()
				{
					Src = src,
					Dst = output,
					Width = width,
					SrcFirstRow = firstRow,
					SrcRowStep = rowStep,
					SrcRowStride = plane.rowStride,
					SrcPixelStride = plane.pixelStride,
					RedOffset = redOffset,
				};

				return job.Schedule(height, RowsPerBatch, dependsOn);
			}
		}

		private static uint4 Luma(uint4 pixels, int redShift, int blueShift) =>
			(((pixels >> redShift) & 0xFFu) * RWeight +
			 ((pixels >> 8) & 0xFFu) * GWeight +
			 ((pixels >> blueShift) & 0xFFu) * BWeight) >> 8;

		private static uint Luma(uint pixel, int redShift, int blueShift) =>
			(((pixel >> redShift) & 0xFFu) * RWeight +
			 ((pixel >> 8) & 0xFFu) * GWeight +
			 ((pixel >> blueShift) & 0xFFu) * BWeight) >> 8;

		// One job index per output row. Dst writes stay inside [y * Width, y * Width
		// + Width), which the parallel-for range check can't see, hence the disable.
		[BurstCompile(OptimizeFor = OptimizeFor.Performance)]
		private struct PackedGrayscaleJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<uint> Src;
			[WriteOnly, NativeDisableParallelForRestriction] public NativeArray<byte> Dst;

			public int Width;
			public int SrcFirstRow;
			public int SrcRowStep;
			public int SrcRowStride; // in pixels
			public int RedShift; // 16 for BGRA32, 0 for RGBA32

			public void Execute(int y)
			{
				// Little endian, so BGRA bytes read back as 0xAARRGGBB.
				int redShift = RedShift;
				int blueShift = 16 - RedShift;

				int s = (SrcFirstRow + y * SrcRowStep) * SrcRowStride;
				int d = y * Width;

				int x = 0;
				for (; x <= Width - 4; x += 4)
				{
					uint4 pixels = new(Src[s + x], Src[s + x + 1], Src[s + x + 2], Src[s + x + 3]);
					uint4 luma = Luma(pixels, redShift, blueShift);

					Dst[d + x] = (byte)luma.x;
					Dst[d + x + 1] = (byte)luma.y;
					Dst[d + x + 2] = (byte)luma.z;
					Dst[d + x + 3] = (byte)luma.w;
				}

				for (; x < Width; x++)
					Dst[d + x] = (byte)Luma(Src[s + x], redShift, blueShift);
			}
		}

		// Fallback for planes with padding or a non-4-byte pixel stride.
		[BurstCompile(OptimizeFor = OptimizeFor.Performance)]
		private struct GrayscaleJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<byte> Src;
			[WriteOnly, NativeDisableParallelForRestriction] public NativeArray<byte> Dst;

			public int Width;
			public int SrcFirstRow;
			public int SrcRowStep;
			public int SrcRowStride; // in bytes
			public int SrcPixelStride; // in bytes
			public int RedOffset; // 2 for BGRA32, 0 for RGBA32

			public void Execute(int y)
			{
				int redOffset = RedOffset;
				int blueOffset = 2 - RedOffset;

				int s = (SrcFirstRow + y * SrcRowStep) * SrcRowStride;
				int d = y * Width;

				for (int x = 0; x < Width; x++)
				{
					int p = s + x * SrcPixelStride;

					uint luma = ((uint)Src[p + redOffset] * RWeight +
					             (uint)Src[p + 1] * GWeight +
					             (uint)Src[p + blueOffset] * BWeight) >> 8;

					Dst[d + x] = (byte)luma;
				}
			}
		}
	}
}
