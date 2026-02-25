using System;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
namespace Meshia.MeshSimplification
{
    internal static class UnityCollectionsHelpers
    {
        public static unsafe ref T ElementAt<T>(this NativeArray<T> array, int i)
            where T : unmanaged
        {
            if (Hint.Likely((uint)i < (uint)array.Length))
            {

                return ref ((T*)array.GetUnsafePtr())[i];
            }
            else
            {
                throw new IndexOutOfRangeException();
            }
        }

        public static unsafe Span<T> AsSpan<T>(this UnsafeList<T> list)
            where T : unmanaged
        {
            return new Span<T>(list.Ptr, list.Length);
        }

    }
}


