using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AprilTag.Interop {

public sealed class Detector : SafeHandleZeroOrMinusOneIsInvalid
{
    #region SafeHandle implementation

    Detector() : base(true) {}

    protected override bool ReleaseHandle()
    {
        _Destroy(handle);
        return true;
    }

    #endregion

    #region apriltag_detector struct representation

    [StructLayout(LayoutKind.Sequential)]
    internal struct InternalData
    {
        internal int nthreads;
        internal float quad_decimate;
        internal float quad_sigma;
        internal int refine_edges;
        internal double decode_sharpening;
        internal int debug;
        internal int min_cluster_pixels;
        internal int max_nmaxima;
        internal float critical_rad;
        internal float cos_critical_rad;
        internal float max_line_fit_mse;
        internal int min_white_black_diff;
        internal int deglitch;
        internal IntPtr tp;
        internal uint nedges;
        internal uint nsegments;
        internal uint nquads;
        internal IntPtr tag_families;
        internal IntPtr wp;
        // pthread_mutex_t mutex;
    }

    unsafe ref InternalData Data
      => ref Util.AsRef<InternalData>((void*)handle);

    #endregion

    #region Public properties and methods

    public int ThreadCount
      { get => Data.nthreads; set => Data.nthreads = value; }

    public float QuadDecimate
      { get => Data.quad_decimate; set => Data.quad_decimate = value; }

    public float QuadSigma
      { get => Data.quad_sigma; set => Data.quad_sigma = value; }

    public int RefineEdges
      { get => Data.refine_edges; set => Data.refine_edges = value; }

    public double DecodeSharpening
      { get => Data.decode_sharpening; set => Data.decode_sharpening = value; }

    public bool Debug
      { get => Data.debug != 0; set => Data.debug = value ? 1 : 0; }

    public unsafe ref TimeProfile TimeProfile
      => ref Util.AsRef<TimeProfile>((void*)Data.tp);

    public static Detector Create()
      => _Create();

    public void AddFamily(Family family)
      => _AddFamilyBits(this, family, 2);

    public void RemoveFamily(Family family)
      => _RemoveFamily(this, family);

    public DetectionArray Detect(ImageU8 image)
      => _Detect(this, image);

    #endregion

    #region Unmanaged interface

    [DllImport(Config.DllName, EntryPoint="apriltag_detector_create")]
    static extern Detector _Create();

    [DllImport(Config.DllName, EntryPoint="apriltag_detector_destroy")]
    static extern void _Destroy(IntPtr detector);

    [DllImport(Config.DllName, EntryPoint="apriltag_detector_add_family_bits")]
    static extern void _AddFamilyBits
      (Detector detector, Family family, int correctedBits);

    [DllImport(Config.DllName, EntryPoint="apriltag_detector_remove_family")]
    static extern void _RemoveFamily(Detector detector, Family family);

    [DllImport(Config.DllName, EntryPoint="apriltag_detector_detect")]
    static extern DetectionArray _Detect(Detector detector, ImageU8 image);

    #endregion
}

} // namespace AprilTag.Interop
