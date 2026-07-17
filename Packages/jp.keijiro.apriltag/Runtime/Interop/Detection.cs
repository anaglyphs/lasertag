using System;
using System.Runtime.InteropServices;

namespace AprilTag.Interop {

[StructLayoutAttribute(LayoutKind.Sequential)]
public struct Detection
{
    #region Internal data structure

    IntPtr family;
    int id;
    int hamming;
    float decision_margin;
    IntPtr H;
    double c0, c1;
    double p00, p01;
    double p10, p11;
    double p20, p21;
    double p30, p31;

    #endregion

    #region Public accessors

    public int ID => id;
    public int Hamming => hamming;
    public float DecisionMargin => decision_margin;
    public (double x, double y) Center => (c0, c1);
    public (double x, double y) Corner1 => (p00, p01);
    public (double x, double y) Corner2 => (p10, p11);
    public (double x, double y) Corner3 => (p20, p21);
    public (double x, double y) Corner4 => (p30, p31);

    #endregion
}

} // namespace AprilTag.Interop
