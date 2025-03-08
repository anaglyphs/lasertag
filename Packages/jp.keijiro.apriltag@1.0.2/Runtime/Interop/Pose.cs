using System;
using System.Runtime.InteropServices;

namespace AprilTag.Interop {

[StructLayoutAttribute(LayoutKind.Sequential)]
public struct Pose : IDisposable
{
    #region Internal data structure

    IntPtr matd_r;
    IntPtr matd_t;

    #endregion

    #region Public properties and methods

    unsafe public ref Matd3x3 R => ref Util.AsRef<Matd3x3>((void*)matd_r);
    unsafe public ref Matd3x1 t => ref Util.AsRef<Matd3x1>((void*)matd_t);

    public Pose(ref DetectionInfo info)
    {
        matd_r = matd_t = IntPtr.Zero;
        _Estimate(ref info, ref this);
    }

    public void Dispose()
    {
        if (matd_r != IntPtr.Zero) _MatdDestroy(matd_r);
        if (matd_t != IntPtr.Zero) _MatdDestroy(matd_t);
        matd_r = matd_t = IntPtr.Zero;
    }

    #endregion

    #region Unmanaged interface

    [DllImport(Config.DllName, EntryPoint="matd_destroy")]
    static extern void _MatdDestroy(IntPtr matd);

    [DllImport(Config.DllName, EntryPoint="estimate_tag_pose")]
    static extern double _Estimate(ref DetectionInfo info, ref Pose pose);

    #endregion
}

} // namespace AprilTag.Interop
