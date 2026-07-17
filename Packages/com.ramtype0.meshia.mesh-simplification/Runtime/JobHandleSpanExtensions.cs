using System;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
namespace Meshia.MeshSimplification
{
    internal static class JobHandleSpanExtensions
    {
        public static unsafe JobHandle CombineDependencies(this ReadOnlySpan<JobHandle> jobHandles)
        {
            fixed (JobHandle* jobHandlesPtr = jobHandles)
            {
                return JobHandleUnsafeUtility.CombineDependencies(jobHandlesPtr, jobHandles.Length);
            }
        }
        public static unsafe JobHandle CombineDependencies(this Span<JobHandle> jobHandles) => ((ReadOnlySpan<JobHandle>)jobHandles).CombineDependencies();
    }
}


