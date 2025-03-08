using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace AprilTag.Interop {

public sealed class ImageU8 : SafeHandleZeroOrMinusOneIsInvalid
{
    #region SafeHandle implementation

    ImageU8() : base(true) {}

    protected override bool ReleaseHandle()
    {
        _Destroy(handle);
        return true;
    }

    #endregion

    #region image_u8 struct representation

    [StructLayout(LayoutKind.Sequential)]
    internal struct InternalData
    {
        internal int width;
        internal int height;
        internal int stride;
        internal IntPtr buf;
    }

    unsafe ref InternalData Data
      => ref Util.AsRef<InternalData>((void*)handle);

    #endregion

    #region Public properties and methods

    public int Width => Data.width;
    public int Height => Data.height;
    public int Stride => Data.stride;

    unsafe public Span<byte> Buffer
      => new Span<byte>((void*)Data.buf, Stride * Height);

    public static ImageU8 Create(int width, int height)
      => _Create((uint)width, (uint)height);

    #endregion

    #region Unmanaged interface

    [DllImport(Config.DllName, EntryPoint="image_u8_create_stride")]
    static extern ImageU8 _CreateStride(uint width, uint height, uint stride);

    [DllImport(Config.DllName, EntryPoint="image_u8_create")]
    static extern ImageU8 _Create(uint width, uint height);

    [DllImport(Config.DllName, EntryPoint="image_u8_destroy")]
    static extern void _Destroy(IntPtr image);

    #endregion
}

} // namespace AprilTag.Interop
