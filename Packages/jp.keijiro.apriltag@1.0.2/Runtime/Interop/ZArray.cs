using System;
using System.Runtime.InteropServices;

namespace AprilTag.Interop {

[StructLayoutAttribute(LayoutKind.Sequential)]
public struct ZArray<T> where T : unmanaged
{
    #region Internal data structure

    ulong el_sz;
    int size;
    int alloc;
    IntPtr data;

    #endregion

    #region Public accessors

    public unsafe Span<T> AsSpan
      => new Span<T>((void*)data, size);

    #endregion
}

} // namespace AprilTag.Interop
