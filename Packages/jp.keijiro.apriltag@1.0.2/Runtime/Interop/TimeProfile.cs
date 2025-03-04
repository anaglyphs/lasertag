using System;
using System.Runtime.InteropServices;

namespace AprilTag.Interop {

[StructLayoutAttribute(LayoutKind.Sequential)]
public unsafe struct TimeProfileEntry
{
    #region Internal data structure

    fixed byte name[32];
    long utime;

    #endregion

    #region Public accessors

    public string Name => ConvertName();
    public long UTime => utime;

    #endregion

    #region Internal method

    unsafe string ConvertName()
    {
        fixed (byte* p = name) return Marshal.PtrToStringAnsi((IntPtr)p);
    }

    #endregion
}

[StructLayoutAttribute(LayoutKind.Sequential)]
public struct TimeProfile
{
    #region Internal data structure

    long utime;
    IntPtr stamps;

    #endregion

    #region Public accessors

    public long UTime => utime;

    public unsafe Span<TimeProfileEntry> Stamps
      => ((ZArray<TimeProfileEntry>*)stamps)->AsSpan;

    #endregion
}

} // namespace AprilTag.Interop
