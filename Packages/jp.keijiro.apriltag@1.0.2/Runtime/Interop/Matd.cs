using System;
using System.Runtime.InteropServices;

namespace AprilTag.Interop {

[StructLayoutAttribute(LayoutKind.Sequential)]
public struct Matd3x1
{
    public readonly uint nrows, ncols;
    public readonly double e0, e1, e2;
}

[StructLayoutAttribute(LayoutKind.Sequential)]
public struct Matd3x3
{
    public readonly uint nrows, ncols;
    public readonly double e00, e01, e02;
    public readonly double e10, e11, e12;
    public readonly double e20, e21, e22;
}

} // namespace AprilTag.Interop
