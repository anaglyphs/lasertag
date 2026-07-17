using AprilTag;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using JobsUtility = Unity.Jobs.LowLevel.Unsafe.JobsUtility;

//
// Small utilities and extension methods
//

namespace AprilTag {

static class SystemConfig
{
    public static int PreferredThreadCount
      => math.max(1, JobsUtility.JobWorkerCount);
}

static class NativeArrayExtensions
{
    public static void
      CopyTo<T>(this NativeArray<T> array, List<T> list) where T : unmanaged
    {
        list.Clear();
        list.Capacity = array.Length;
        for (var i = 0; i < array.Length; i++) list.Add(array[i]);
    }
}

static class MatdExtensions
{
    public static float3 AsFloat3(this ref Interop.Matd3x1 src)
      => math.float3((float)src.e0, (float)src.e1, (float)src.e2);

    public static float3x3 AsFloat3x3(this ref Interop.Matd3x3 src)
      => math.float3x3((float)src.e00, (float)src.e01, (float)src.e02,
                       (float)src.e10, (float)src.e11, (float)src.e12,
                       (float)src.e20, (float)src.e21, (float)src.e22);
}

} // namespace AprilTag
