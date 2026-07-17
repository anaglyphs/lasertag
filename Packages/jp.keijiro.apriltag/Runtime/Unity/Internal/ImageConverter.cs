using System;
using Unity.Burst;
using Unity.Collections;
using Color32 = UnityEngine.Color32;
using ImageU8 = AprilTag.Interop.ImageU8;

namespace AprilTag
{
	//
	// Burst-accelerated image convertion functions
	//
	[BurstCompile]
	public static class ImageConverter
	{
		public static unsafe void Convert(NativeArray<Color32> data, NativeArray<byte> output, int width, int height)
		{
			fixed (Color32* src = &data.AsReadOnlySpan().GetPinnableReference())
			fixed (byte* dst = &output.AsSpan().GetPinnableReference())
			{
				BurstConvert(src, dst, width, height, width);
			}
		}

		[BurstCompile]
		private static unsafe void BurstConvert
			(Color32* src, byte* dst, int width, int height, int stride, bool flip = true)
		{
			int offs_src = 0;
			int offs_dst = flip ? stride * (height - 1) : 0;

			int strideSigned = stride * (flip ? -1 : 1);

			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
					dst[offs_dst + x] = src[offs_src + x].g;

				offs_src += width;
				offs_dst += strideSigned;
			}
		}
	}
} // namespace AprilTag