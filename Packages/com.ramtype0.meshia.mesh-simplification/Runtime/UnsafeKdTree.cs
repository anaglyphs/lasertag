using System;
using System.Collections.Generic;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Meshia.MeshSimplification
{
    struct UnsafeKdTree : INativeDisposable
    {
        struct Node
        {
            public int PointIndex;
            public int Component;
            public int2 ChildNodeIndices;
        }
        UnsafeList<Node> Nodes;
        public UnsafeKdTree(AllocatorManager.AllocatorHandle allocator)
        {
            Nodes = new(0, allocator);
        }
        public void Initialize(ReadOnlySpan<float3> points)
        {
            Nodes.Clear();

            if (Nodes.Capacity < points.Length)
            {
                Nodes.Capacity = points.Length;
            }

            UnsafeList<int> indices = new(points.Length, Allocator.Temp);
            indices.Resize(points.Length);
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            var rootNodeIndex = AllocateNode();
            InitializeNode(points, rootNodeIndex, indices, 0);
            indices.Dispose();
        }
        unsafe static void Split<T>(UnsafeList<T> list, int mid, out UnsafeList<T> first, out UnsafeList<T> second)
            where T : unmanaged
        {
            if (mid == 0)
            {
                first = new(null, 0);
                second = new(null, 0);

            }
            else
            {
                first = new(list.Ptr, mid - 1);

                second = new(list.Ptr + mid + 1, list.Length - mid - 1);
            }
        }
        [return: AssumeRange(0, int.MaxValue - 1)]
        int AllocateNode() => Nodes.Length++;
        void InitializeNode(ReadOnlySpan<float3> points, int nodeIndex, UnsafeList<int> indices, int nodeDepth)
        {
            if (nodeIndex == -1)
            {
                return;
            }
            var component = nodeDepth % 3;

            var mid = indices.Length >> 1;
            unsafe
            {
                fixed (float3* pointsPtr = points)
                {
                    indices.Sort(new PointsIndexComparer
                    {
                        List = new(pointsPtr, points.Length),
                        Component = component,
                    });
                }
            }


            Split(indices, mid, out var first, out var second);

            var firstChildNodeIndex = first.IsEmpty ? -1 : AllocateNode();

            var secondChiildNodeIndex = second.IsEmpty ? -1 : AllocateNode();
            Nodes[nodeIndex] = new()
            {
                PointIndex = indices[mid],
                Component = component,
                ChildNodeIndices = new(firstChildNodeIndex, secondChiildNodeIndex),
            };

            InitializeNode(points, firstChildNodeIndex, first, nodeDepth + 1);
            InitializeNode(points, secondChiildNodeIndex, second, nodeDepth + 1);



        }

        public void QueryPointsInSphere(ReadOnlySpan<float3> points, float3 center, float radius, ref UnsafeList<int> results)
        {
            QueryPointsInSphere(points, 0, center, radius, ref results);
        }
        void QueryPointsInSphere(ReadOnlySpan<float3> points, int nodeIndex, float3 center, float radius, ref UnsafeList<int> results)
        {
            if (nodeIndex == -1)
            {
                return;
            }
            var node = Nodes[nodeIndex];
            var nodePoint = points[node.PointIndex];
            var squaredDistance = math.distancesq(center, nodePoint);

            if (squaredDistance < radius * radius)
            {
                results.Add(node.PointIndex);
            }

            var component = node.Component;
            var componentDifference = center[component] - nodePoint[component];
            var searchDirection = math.select(new int2(0, 1), new int2(1, 0), componentDifference > 0);
            QueryPointsInSphere(points, node.ChildNodeIndices[searchDirection.x], center, radius, ref results);
            if (math.abs(componentDifference) < radius)
            {
                QueryPointsInSphere(points, node.ChildNodeIndices[searchDirection.y], center, radius, ref results);
            }
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            return Nodes.Dispose(inputDeps);
        }

        public void Dispose()
        {
            Nodes.Dispose();
        }
        struct PointsIndexComparer : IComparer<int>

        {
            public UnsafeList<float3> List;

            public int Component;

            public int Compare(int x, int y) => List[x][Component].CompareTo(List[y][Component]);
        }
    }



}
