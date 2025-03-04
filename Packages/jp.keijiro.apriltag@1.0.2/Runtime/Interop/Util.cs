namespace AprilTag.Interop {

static class Util
{
    public unsafe static ref T AsRef<T>(void* source) where T : unmanaged
#if UNITY_2019_1_OR_NEWER
      => ref Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AsRef<T>(source);
#else
      => ref System.Runtime.CompilerServices.Unsafe.AsRef<T>(source);
#endif

    public unsafe static void* AsPointer<T>(ref T value) where T : unmanaged
#if UNITY_2019_1_OR_NEWER
      => Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AddressOf(ref value);
#else
      => System.Runtime.CompilerServices.Unsafe.AsPointer(ref value);
#endif
}

} // namespace AprilTag.Interop
